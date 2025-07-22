using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SendBulk.Models;
using System.Data.SqlClient;
using System.Text;
using System.Xml.Linq;

namespace SendBulk.Services
{
    public class BulkSmsStatusChecker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BulkSmsStatusChecker> _logger;
        private readonly string _connectionString;
        private readonly FarapayamakSettings _settings;
        private readonly HttpClient _httpClient;

        public BulkSmsStatusChecker(
            IServiceScopeFactory scopeFactory,
            ILogger<BulkSmsStatusChecker> logger,
            IConfiguration configuration,
            IOptions<FarapayamakSettings> options)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _settings = options.Value;
            _httpClient = new HttpClient();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // منتظر شروع سرویس
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("شروع چک کردن وضعیت پیامک‌های انبوه");
                    await CheckPendingBulkSms();
                    _logger.LogInformation("پایان چک کردن وضعیت پیامک‌های انبوه");

                    // چک هر 3 دقیقه
                    await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در چک کردن وضعیت پیامک‌های انبوه");
                    // در صورت خطا، 5 دقیقه صبر کن
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }

        private async Task CheckPendingBulkSms()
        {
            using var scope = _scopeFactory.CreateScope();
            var pendingBulks = await GetPendingBulkSms();

            _logger.LogInformation($"تعداد bulk های در انتظار: {pendingBulks.Count}");

            foreach (var bulk in pendingBulks)
            {
                try
                {
                    _logger.LogInformation($"چک کردن وضعیت bulk ID: {bulk.BulkId}");

                    var status = await GetBulkStatusFromFarapayamak(bulk.BulkId);

                    if (status != null)
                    {
                        _logger.LogInformation($"وضعیت bulk {bulk.BulkId}: {status.Status}");

                        // چک وضعیت‌های نهایی
                        if (status.Status == 3 || status.Status == 4 || status.Status == 7)
                        {
                            await ProcessBulkResult(bulk, status);
                        }
                        else
                        {
                            _logger.LogInformation($"Bulk {bulk.BulkId} هنوز در حال پردازش است. وضعیت: {status.Status}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"نتوانست وضعیت bulk {bulk.BulkId} را دریافت کند");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"خطا در چک کردن وضعیت bulk ID: {bulk.BulkId}");
                }

                // تأخیر بین هر چک برای عدم اسپم سرور
                await Task.Delay(1000);
            }
        }

        private async Task<List<PendingBulkSms>> GetPendingBulkSms()
        {
            var pendingBulks = new List<PendingBulkSms>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
            SELECT BulkId, UserId, TotalCreditsUsed, MessageCount, AdminPhone, 
                   Title, MessageText, DateSent
            FROM [toranjdata_crm_2018].[dbo].[BulkSmsTracking]
            WHERE Status = 0
            AND DateSent >= DATEADD(hour, -24, GETDATE())
            ORDER BY DateSent ASC";

                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (reader.Read())
                {
                    pendingBulks.Add(new PendingBulkSms
                    {
                        BulkId = reader["BulkId"].ToString(),
                        UserId = Convert.ToInt32(reader["UserId"]),
                        TotalCreditsUsed = Convert.ToDecimal(reader["TotalCreditsUsed"]),
                        MessageCount = Convert.ToInt32(reader["MessageCount"]),
                        AdminPhone = reader["AdminPhone"].ToString(),
                        Title = reader["Title"]?.ToString() ?? "",
                        MessageText = reader["MessageText"]?.ToString() ?? "",
                        DateSent = Convert.ToDateTime(reader["DateSent"])
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در دریافت لیست bulk های در انتظار");
            }

            return pendingBulks;
        }


        private async Task<BulkStatusResponse> GetBulkStatusFromFarapayamak(string bulkId)
        {
            try
            {
                var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
               xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
               xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <GetBulkDetails xmlns=""http://tempuri.org/"">
      <username>{System.Security.SecurityElement.Escape(_settings.Username)}</username>
      <password>{System.Security.SecurityElement.Escape(_settings.Password)}</password>
      <bulkdId>{bulkId}</bulkdId>
    </GetBulkDetails>
  </soap:Body>
</soap:Envelope>";

                var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
                content.Headers.Add("SOAPAction", "\"http://tempuri.org/GetBulkDetails\"");

                // URL صحیح با HTTPS
                var response = await _httpClient.PostAsync("https://api.payamak-panel.com/post/numberbulk.asmx", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"پاسخ چک وضعیت bulk {bulkId}: {responseContent}");

                return ParseBulkStatusResponse(responseContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"خطا در دریافت وضعیت bulk ID: {bulkId}");
                return null;
            }
        }

        private BulkStatusResponse ParseBulkStatusResponse(string soapResponse)
        {
            try
            {
                const string startTag = "<GetBulkDetailsResult>";
                const string endTag = "</GetBulkDetailsResult>";

                var startIndex = soapResponse.IndexOf(startTag);
                if (startIndex == -1) return null;

                startIndex += startTag.Length;
                var endIndex = soapResponse.IndexOf(endTag, startIndex);
                if (endIndex == -1) return null;

                var xmlContent = soapResponse.Substring(startIndex, endIndex - startIndex).Trim();
                var xDoc = XDocument.Parse(xmlContent);
                var bulk = xDoc.Descendants("BulkDetails").FirstOrDefault();
                if (bulk == null) return null;

                var sentCount = (int?)bulk.Element("SentCount") ?? 0;
                var failedCount = (int?)bulk.Element("FailedCount") ?? 0;
                var status = (int?)bulk.Element("SendStatus") ?? -1;
                var resultMessage = bulk.Element("Descriptions")?.Value ?? "";
                var sentDateString = bulk.Element("SentDate")?.Value;

                // تبدیل تاریخ شمسی به میلادی
                DateTime? sentDate = null;
                if (!string.IsNullOrWhiteSpace(sentDateString))
                {
                    try
                    {
                        sentDate = PersianToGregorian(sentDateString);
                    }
                    catch { }
                }

                return new BulkStatusResponse
                {
                    Status = status,
                    SentCount = sentCount,
                    FailedCount = failedCount,
                    SentDate = sentDate,
                    ResultMessage = resultMessage,
                    OriginalXml = xmlContent
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در پارس کردن پاسخ وضعیت");
                return null;
            }
        }

        private DateTime PersianToGregorian(string persianDateTime)
        {
            // مثال: ۱۴۰۴/۰۴/۳۱ ۱۸:۴۶
            var parts = persianDateTime.Split(' ');
            var date = parts[0].Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3')
                                .Replace('۴', '4').Replace('۵', '5').Replace('۶', '6').Replace('۷', '7')
                                .Replace('۸', '8').Replace('۹', '9');
            var time = parts.Length > 1 ? parts[1] : "00:00";

            var dateParts = date.Split('/');
            var timeParts = time.Split(':');

            var persianCalendar = new System.Globalization.PersianCalendar();
            return persianCalendar.ToDateTime(
                int.Parse(dateParts[0]),
                int.Parse(dateParts[1]),
                int.Parse(dateParts[2]),
                int.Parse(timeParts[0]),
                int.Parse(timeParts[1]),
                0, 0
            );
        }


        private async Task ProcessBulkResult(PendingBulkSms bulk, BulkStatusResponse status)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                int finalStatusCode;
                string resultMessage;
                bool shouldRefund = false;

                switch (status.Status)
                {
                    case 3:
                        finalStatusCode = (int)BulkSmsStatus.Success;
                        resultMessage = "ارسال انبوه پیامک با موفقیت تایید و ارسال شد";
                        break;
                    case 4:
                        finalStatusCode = (int)BulkSmsStatus.Failed;
                        shouldRefund = true;
                        resultMessage = "ارسال انبوه پیامک ارسال نشد - اعتبار برگشت داده شد";
                        break;
                    case 7:
                        finalStatusCode = (int)BulkSmsStatus.Cancelled;
                        shouldRefund = true;
                        resultMessage = "ارسال انبوه پیامک تایید نشد - اعتبار برگشت داده شد";
                        break;
                    default:
                        _logger.LogWarning($"وضعیت نامشخص برای bulk {bulk.BulkId}: {status.Status}");
                        return;
                }

                if (shouldRefund)
                {
                    await RefundUserCredit(bulk.UserId, bulk.TotalCreditsUsed, bulk.MessageCount, connection, transaction);
                }

                await UpdateBulkTrackingStatus(bulk.BulkId, finalStatusCode, resultMessage, status.SentCount,
                    status.FailedCount, status.SentDate, status.OriginalXml, connection, transaction);

                transaction.Commit();

                var notificationMessage = shouldRefund
                    ? $"❌ ارسال ناموفق\nعنوان: {bulk.Title}\nتعداد: {bulk.MessageCount}\nاعتبار برگشت: {bulk.TotalCreditsUsed}\nتاریخ: {DateTime.Now:yyyy/MM/dd HH:mm}"
                    : $"✅ ارسال موفق\nعنوان: {bulk.Title}\nتعداد: {bulk.MessageCount}\nتاریخ: {DateTime.Now:yyyy/MM/dd HH:mm}";

                await SendAdminNotification(bulk.AdminPhone, notificationMessage);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, $"خطا در پردازش نتیجه bulk ID: {bulk.BulkId}");
                throw;
            }
        }


        private async Task RefundUserCredit(int userId, decimal refundAmount, int messageCount,
            SqlConnection connection, SqlTransaction transaction)
        {
            // دریافت موجودی فعلی
            var currentCredit = await GetUserCurrentCredit(userId, connection, transaction);
            var newCredit = currentCredit + refundAmount;

            var query = @"
                INSERT INTO [toranjdata_crm_2018].[dbo].[apiMessaging] 
                (UserID, Type, CreditChanges, RemainingCredit, Description, Date, Status, ip)
                VALUES 
                (@UserId, @Type, @CreditChanges, @RemainingCredit, @Description, @Date, @Status, @IP)";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Type", 1);
            command.Parameters.AddWithValue("@CreditChanges", refundAmount);
            command.Parameters.AddWithValue("@RemainingCredit", newCredit);
            command.Parameters.AddWithValue("@Description", $"برگشت اعتبار - عدم تایید ارسال انبوه - تعداد: {messageCount}");
            command.Parameters.AddWithValue("@Date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@Status", 1);
            command.Parameters.AddWithValue("@IP", "127.0.0.1");

            await command.ExecuteNonQueryAsync();
        }

        private async Task<decimal> GetUserCurrentCredit(int userId, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
                SELECT TOP 1 RemainingCredit
                FROM [toranjdata_crm_2018].[dbo].[apiMessaging]
                WHERE UserID = @UserId AND ISDATE([Date]) = 1
                ORDER BY TRY_CAST([Date] AS DATETIME) DESC";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@UserId", userId);

            var result = await command.ExecuteScalarAsync();
            if (result != null && decimal.TryParse(result.ToString(), out decimal credit))
                return credit;

            return 0;
        }

        private async Task UpdateBulkTrackingStatus(string bulkId, int status, string resultMessage,
    int sentCount, int failedCount, DateTime? dateProcessed, string originalXml,
    SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
        UPDATE [toranjdata_crm_2018].[dbo].[BulkSmsTracking]
        SET Status = @Status, 
            ResultMessage = @ResultMessage, 
            DateProcessed = @DateProcessed,
            SentCount = @SentCount,
            FailedCount = @FailedCount,
            OriginalXml = @OriginalXml,
            UpdatedAt = @UpdatedAt
        WHERE BulkId = @BulkId";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@BulkId", bulkId);
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@ResultMessage", resultMessage ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@DateProcessed", dateProcessed ?? DateTime.Now);
            command.Parameters.AddWithValue("@SentCount", sentCount);
            command.Parameters.AddWithValue("@FailedCount", failedCount);
            command.Parameters.AddWithValue("@OriginalXml", originalXml ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

            await command.ExecuteNonQueryAsync();
        }


        private async Task SendAdminNotification(string adminPhone, string message)
        {
            var payload = new
            {
                username = "9122973712",
                password = "Alireza@1455",
                to = adminPhone,
                from = "5000400055",
                text = message,
                isFlash = false
            };

            try
            {
                using var httpClient = new HttpClient();
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync("https://rest.payamak-panel.com/api/SendSMS/SendSMS", content);

                var responseContent = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"ارسال پیامک به ادمین ({adminPhone}) ناموفق بود. کد: {response.StatusCode}, پاسخ: {responseContent}");
                }
                else
                {
                    _logger.LogInformation($"✅ پیامک به ادمین ({adminPhone}) با موفقیت ارسال شد.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ خطا در ارسال پیامک به ادمین: {adminPhone}");
            }
        }

        public class PendingBulkSms
        {
            public string BulkId { get; set; }
            public int UserId { get; set; }
            public decimal TotalCreditsUsed { get; set; }
            public int MessageCount { get; set; }
            public string AdminPhone { get; set; }
            public string Title { get; set; }
            public string MessageText { get; set; }
            public DateTime DateSent { get; set; }
        }

        public class BulkStatusResponse
        {
            public int Status { get; set; }
            public int SentCount { get; set; }
            public int FailedCount { get; set; }
            public DateTime? SentDate { get; set; }
            public string ResultMessage { get; set; }
            public string OriginalXml { get; set; }
        }


        public enum BulkSmsStatus
        {
            Pending = 0,
            Success = 1,
            Failed = 2,
            Cancelled = 3
        }

    }
}
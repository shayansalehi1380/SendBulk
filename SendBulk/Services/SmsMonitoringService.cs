using Microsoft.Extensions.Options;
using SendBulk.Models;
using SendBulk.Services;
using System.Data.SqlClient;
using System.Text.Json;

namespace SendBulk.Services
{
    public class SmsMonitoringService : BackgroundService
    {
        private readonly ILogger<SmsMonitoringService> _logger;
        private readonly SmsService _smsService;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _connectionString;
        private readonly FarapayamakSettings _settings;
        private readonly HttpClient _httpClient;

        public SmsMonitoringService(
            ILogger<SmsMonitoringService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            IOptions<FarapayamakSettings> farapayamakOptions)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _settings = farapayamakOptions.Value;
            _httpClient = new HttpClient();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SMS Monitoring Service شروع شد");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckPendingBulkMessages();

                    // هر 30 ثانیه چک کن
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در سرویس نظارت بر پیامک‌ها");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        private async Task CheckPendingBulkMessages()
        {
            try
            {
                var pendingMessages = await GetPendingBulkMessagesAsync();

                foreach (var message in pendingMessages)
                {
                    await ProcessBulkMessage(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در چک کردن پیامک‌های در انتظار");
            }
        }

        private async Task<List<PendingBulkMessage>> GetPendingBulkMessagesAsync()
        {
            var messages = new List<PendingBulkMessage>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // پیدا کردن پیامک‌هایی که در 10 دقیقه گذشته ارسال شده‌اند و هنوز تایید نشده‌اند
                var query = @"
                    SELECT DISTINCT 
                        am.UserID,
                        am.Description,
                        am.CreditChanges,
                        am.Date,
                        am.RemainingCredit
                    FROM [toranjdata_crm_2018].[dbo].[apiMessaging] am
                    WHERE am.Description LIKE '%ارسال انبوه پیامک - در حال پردازش%'
                    AND DATEDIFF(MINUTE, TRY_CAST(am.Date AS DATETIME), GETDATE()) BETWEEN 2 AND 30
                    AND NOT EXISTS (
                        SELECT 1 FROM [toranjdata_crm_2018].[dbo].[apiMessaging] am2 
                        WHERE am2.UserID = am.UserID 
                        AND am2.Date > am.Date 
                        AND (am2.Description LIKE '%ارسال انبوه پیامک - موفق%' 
                             OR am2.Description LIKE '%برگشت اعتبار%')
                    )
                ";

                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (reader.Read())
                {
                    // استخراج ResponseId از Description
                    var description = reader["Description"]?.ToString() ?? "";
                    var responseId = ExtractResponseIdFromDescription(description);

                    if (!string.IsNullOrEmpty(responseId))
                    {
                        messages.Add(new PendingBulkMessage
                        {
                            UserId = Convert.ToInt32(reader["UserID"]),
                            ResponseId = responseId,
                            CreditAmount = Math.Abs(Convert.ToDecimal(reader["CreditChanges"])),
                            Date = reader["Date"]?.ToString() ?? "",
                            Description = description
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در دریافت پیامک‌های در انتظار از دیتابیس");
            }

            return messages;
        }

        private async Task ProcessBulkMessage(PendingBulkMessage message)
        {
            try
            {
                _logger.LogInformation($"چک کردن وضعیت پیامک برای کاربر {message.UserId} با ResponseId: {message.ResponseId}");

                var bulkDetails = await GetBulkDetailsFromFarapayamak(message.ResponseId);

                if (bulkDetails != null)
                {
                    // بررسی وضعیت ارسال
                    switch (bulkDetails.SendStatus)
                    {
                        case 3: // ارسال شده - موفق
                            await MarkAsSuccessful(message);
                            _logger.LogInformation($"پیامک کاربر {message.UserId} با موفقیت ارسال شد");
                            break;

                        case 4: // ارسال نشده - ناموفق
                        case 7: // تایید نشده
                            await ProcessFailedMessage(message, bulkDetails);
                            _logger.LogWarning($"پیامک کاربر {message.UserId} ارسال نشد. وضعیت: {bulkDetails.SendStatus}");
                            break;

                        case 0: // منتظر تایید
                        case 1: // منتظر ارسال  
                        case 2: // در حال ارسال
                            // هنوز در حال پردازش - صبر کن
                            _logger.LogInformation($"پیامک کاربر {message.UserId} هنوز در حال پردازش است. وضعیت: {bulkDetails.SendStatus}");
                            break;
                    }
                }
                else
                {
                    _logger.LogWarning($"نتوانست جزئیات پیامک {message.ResponseId} را از فراپیامک دریافت کند");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"خطا در پردازش پیامک کاربر {message.UserId}");
            }
        }

        private async Task<BulkDetailsResponse?> GetBulkDetailsFromFarapayamak(string responseId)
        {
            try
            {
                var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
               xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
               xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <GetBulkDetails xmlns=""http://tempuri.org/"">
      <username>{_settings.Username}</username>
      <password>{_settings.Password}</password>
      <bulkdId>{responseId}</bulkdId>
    </GetBulkDetails>
  </soap:Body>
</soap:Envelope>";

                var content = new StringContent(soapEnvelope, System.Text.Encoding.UTF8, "text/xml");
                content.Headers.Add("SOAPAction", "\"http://tempuri.org/GetBulkDetails\"");

                var response = await _httpClient.PostAsync("http://api.payamak-panel.com/post/numberbulk.asmx", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                return ParseBulkDetailsResponse(responseContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"خطا در دریافت جزئیات پیامک از فراپیامک برای ResponseId: {responseId}");
                return null;
            }
        }

        private BulkDetailsResponse? ParseBulkDetailsResponse(string soapResponse)
        {
            try
            {
                // استخراج SendStatus از XML response
                var sendStatusMatch = System.Text.RegularExpressions.Regex.Match(soapResponse, @"<SendStatus>(\d+)</SendStatus>");
                var titleMatch = System.Text.RegularExpressions.Regex.Match(soapResponse, @"<Title>(.*?)</Title>");
                var requestCountMatch = System.Text.RegularExpressions.Regex.Match(soapResponse, @"<RequestCount>(\d+)</RequestCount>");
                var sentCountMatch = System.Text.RegularExpressions.Regex.Match(soapResponse, @"<SentCount>(\d+)</SentCount>");

                if (sendStatusMatch.Success)
                {
                    return new BulkDetailsResponse
                    {
                        SendStatus = int.Parse(sendStatusMatch.Groups[1].Value),
                        Title = titleMatch.Success ? titleMatch.Groups[1].Value : "",
                        RequestCount = requestCountMatch.Success ? int.Parse(requestCountMatch.Groups[1].Value) : 0,
                        SentCount = sentCountMatch.Success ? int.Parse(sentCountMatch.Groups[1].Value) : 0
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در پارس کردن پاسخ فراپیامک");
                return null;
            }
        }

        private async Task MarkAsSuccessful(PendingBulkMessage message)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE [toranjdata_crm_2018].[dbo].[apiMessaging] 
                    SET Description = 'ارسال انبوه پیامک - موفق'
                    WHERE UserID = @UserId 
                    AND Description LIKE '%ارسال انبوه پیامک - در حال پردازش%'
                    AND Date = @Date
                ";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserId", message.UserId);
                command.Parameters.AddWithValue("@Date", message.Date);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"خطا در به‌روزرسانی وضعیت موفق برای کاربر {message.UserId}");
            }
        }

        private async Task ProcessFailedMessage(PendingBulkMessage message, BulkDetailsResponse bulkDetails)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var smsService = scope.ServiceProvider.GetRequiredService<SmsService>();

                // برگشت اعتبار
                await smsService.UpdateUserCreditAsync(
                    message.UserId,
                    message.CreditAmount,
                    "برگشت اعتبار - ارسال ناموفق",
                    1
                );

                // دریافت اطلاعات کاربر برای ارسال پیامک اطلاع‌رسانی
                var userInfo = await GetUserInfoAsync(message.UserId);

                if (!string.IsNullOrEmpty(userInfo?.CellPhone))
                {
                    var notificationMessage = $"کاربر گرامی، پیامک انبوه شما به دلیل عدم تایید اپراتور ارسال نشد. مبلغ {message.CreditAmount:N0} ریال به موجودی شما برگشت داده شد. لطفاً مجدداً ارسال نمایید. لغو11";

                    // ارسال پیامک اطلاع‌رسانی
                    await smsService.SendToNumbersAsync("اطلاع‌رسانی", notificationMessage, userInfo.CellPhone);

                    _logger.LogInformation($"پیامک اطلاع‌رسانی برای کاربر {message.UserId} ارسال شد");
                }

                _logger.LogInformation($"اعتبار {message.CreditAmount} برای کاربر {message.UserId} برگشت داده شد");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"خطا در پردازش پیامک ناموفق برای کاربر {message.UserId}");
            }
        }

        private async Task<UserInfo?> GetUserInfoAsync(int userId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP 1 
                        COALESCE(FirstName + ' ' + LastName, FirstName, LastName, Username) as Name,
                        CellPhone
                    FROM [toranjdata_crm_2018].[dbo].[User] 
                    WHERE UserID = @UserId
                ";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserId", userId);

                using var reader = await command.ExecuteReaderAsync();

                if (reader.Read())
                {
                    return new UserInfo
                    {
                        Name = reader["Name"]?.ToString() ?? "",
                        CellPhone = reader["CellPhone"]?.ToString() ?? ""
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"خطا در دریافت اطلاعات کاربر {userId}");
                return null;
            }
        }

        private string ExtractResponseIdFromDescription(string description)
        {
            // فرض می‌کنیم ResponseId در توضیحات ذخیره شده
            // باید این متد را بر اساس نحوه ذخیره ResponseId در توضیحات تنظیم کنید
            var match = System.Text.RegularExpressions.Regex.Match(description, @"ResponseId:(\d+)");
            return match.Success ? match.Groups[1].Value : "";
        }

        public override void Dispose()
        {
            _httpClient?.Dispose();
            base.Dispose();
        }
    }

    // کلاس‌های مدل
    public class PendingBulkMessage
    {
        public int UserId { get; set; }
        public string ResponseId { get; set; } = "";
        public decimal CreditAmount { get; set; }
        public string Date { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class BulkDetailsResponse
    {
        public int SendStatus { get; set; }
        public string Title { get; set; } = "";
        public int RequestCount { get; set; }
        public int SentCount { get; set; }
    }

    public class UserInfo
    {
        public string Name { get; set; } = "";
        public string CellPhone { get; set; } = "";
    }
}
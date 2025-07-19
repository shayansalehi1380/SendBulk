using Microsoft.Extensions.Options;
using SendBulk.Models;
using SendBulk.Models.Request;
using SendBulk.Models.Response;
using System.Data;
using System.Data.SqlClient;
using System.Runtime;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace SendBulk.Services
{
    public class SmsService
    {
        private readonly FarapayamakSettings _settings;
        private readonly HttpClient _httpClient;
        private readonly string _connectionString;

        public SmsService(IOptions<FarapayamakSettings> options, IConfiguration configuration)
        {
            _settings = options.Value;
            _httpClient = new HttpClient();
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // Decrypt method from legacy code
        private string Decrypt(string cipherString, bool useHashing)
        {
            byte[] keyArray;
            byte[] toEncryptArray = Convert.FromBase64String(cipherString);

            string key = "Toranj";

            if (useHashing)
            {
                using (MD5CryptoServiceProvider hashmd5 = new MD5CryptoServiceProvider())
                {
                    keyArray = hashmd5.ComputeHash(Encoding.UTF8.GetBytes(key));
                }
            }
            else
                keyArray = Encoding.UTF8.GetBytes(key);

            using (TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider())
            {
                tdes.Key = keyArray;
                tdes.Mode = CipherMode.ECB;
                tdes.Padding = PaddingMode.PKCS7;

                ICryptoTransform cTransform = tdes.CreateDecryptor();
                byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);

                return Encoding.UTF8.GetString(resultArray);
            }
        }

        // Get UserID from token
        private async Task<int> GetUserIdFromTokenAsync(string token)
        {
            try
            {
                string decodedToken = HttpUtility.UrlDecode(token).Replace(" ", "+");
                string decryptedUserId = Decrypt(decodedToken, true);

                if (int.TryParse(decryptedUserId, out int userId))
                {
                    return userId;
                }

                throw new UnauthorizedAccessException("توکن معتبر نمی‌باشد");
            }
            catch (Exception)
            {
                throw new UnauthorizedAccessException("خطا در احراز هویت کاربر");
            }
        }

        // Get user credit from database
        private async Task<decimal> GetUserCreditFromDatabaseAsync(int userId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = @"
                        SELECT TOP 1 RemainingCredit
                        FROM [toranjdata_crm_2018].[dbo].[apiMessaging]
                        WHERE UserID = @UserId AND ISDATE([Date]) = 1
                        ORDER BY TRY_CAST([Date] AS DATETIME) DESC;
                                                                    ";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);

                        var result = await command.ExecuteScalarAsync();

                        if (result != null && decimal.TryParse(result.ToString(), out decimal credit))
                        {
                            return credit;
                        }

                        return 0;
                    }
                }
            }
            catch (Exception)
            {
                throw new Exception("خطا در دریافت موجودی کیف پول");
            }
        }

        // Get SMS Plan Info (equivalent to getSMSPlanInfo from legacy code)
        public async Task<object> GetSMSPlanInfoAsync(string token)
        {
            try
            {
                var userId = await GetUserIdFromTokenAsync(token);

                // Products p = new Products(3); - equivalent logic
                // Since we don't have Products class, returning mock data based on legacy comment
                var planInfo = new
                {
                    status = "success",
                    msg = "بسته شارژ خط خدماتی",
                    data = new
                    {
                        title = "بسته 1000 تایی پیامک",
                        price = "50000", // Mock price - should come from Products table
                        packageId = 3
                    }
                };

                return planInfo;
            }
            catch (UnauthorizedAccessException ex)
            {
                return new { status = "error", msg = ex.Message, data = "" };
            }
            catch (Exception ex)
            {
                return new { status = "error", msg = "خطا در دریافت اطلاعات", data = "" };
            }
        }

        // Get Credit with token authentication
        public async Task<object> GetCreditAsync(string token)
        {
            try
            {
                var userId = await GetUserIdFromTokenAsync(token);
                var credit = await GetUserCreditFromDatabaseAsync(userId);

                return new
                {
                    status = "success",
                    msg = "اتصال با موفقیت برقرار شد",
                    data = credit.ToString("0.00")
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new { status = "error", msg = ex.Message, data = "" };
            }
            catch (Exception ex)
            {
                return new { status = "error", msg = ex.Message, data = "" };
            }
        }

        // Legacy Get Credit method (for backward compatibility)
        public async Task<SmsCreditResponse> GetCreditLegacyAsync()
        {
            var request = new SmsCreditRequest
            {
                Username = _settings.Username,
                Password = _settings.Password
            };

            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://rest.payamak-panel.com/api/SendSMS/GetCredit", content);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("دریافت موجودی با خطا مواجه شد.");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SmsCreditResponse>(responseContent);

            if (result != null && decimal.TryParse(result.Value, out var decimalValue))
            {
                result.Value = Math.Round(decimalValue, 2).ToString("0.00");
            }

            return result ?? new SmsCreditResponse { RetStatus = -1, StrRetStatus = "خطا در تبدیل پاسخ" };
        }

        // Get User Info with token
        public async Task<object> GetUserInfoAsync(string token)
        {
            try
            {
                var userId = await GetUserIdFromTokenAsync(token);
                var credit = await GetUserCreditFromDatabaseAsync(userId);

                // Get user name from database (you might need to adjust this query)
                string userName = await GetUserNameAsync(userId);

                return new
                {
                    status = "success",
                    data = new
                    {
                        name = userName,
                        balance = credit.ToString("0.00"),
                        userId = userId
                    }
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new { status = "error", msg = ex.Message, data = (object)null };
            }
            catch (Exception ex)
            {
                return new { status = "error", msg = ex.Message, data = (object)null };
            }
        }

        // Get user name from database
        private async Task<string> GetUserNameAsync(int userId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Adjust this query based on your user table structure
                    var query = "SELECT TOP 1 Name FROM Users WHERE UserID = @UserId";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);

                        var result = await command.ExecuteScalarAsync();
                        return result?.ToString() ?? "کاربر";
                    }
                }
            }
            catch
            {
                return "کاربر";
            }
        }

        // Send SMS with token authentication
        public async Task<object> SendSMSAsync(string token, string title, string message, string numbersRaw)
        {
            try
            {
                var userId = await GetUserIdFromTokenAsync(token);
                var userCredit = await GetUserCreditFromDatabaseAsync(userId);

                // Check if user has enough credit
                var numbers = numbersRaw
                    .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Trim())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .ToArray();

                if (numbers.Length == 0)
                    return new { status = "error", msg = "هیچ شماره‌ای برای ارسال یافت نشد", data = "" };

                // Calculate required credit (assuming 1 credit per SMS)
                decimal requiredCredit = numbers.Length;

                if (userCredit < requiredCredit)
                    return new { status = "error", msg = "موجودی کیف پول کافی نمی‌باشد", data = "" };

                // Send SMS using existing method
                var (response, cleanedNumbers) = await SendToNumbersAsync(title, message, numbersRaw);

                // Update user credit in database
                await UpdateUserCreditAsync(userId, -requiredCredit, "ارسال پیامک", cleanedNumbers.Length);

                return new
                {
                    status = "success",
                    msg = "پیامک با موفقیت ارسال شد",
                    data = new
                    {
                        sentTo = cleanedNumbers,
                        count = cleanedNumbers.Length,
                        remainingCredit = (userCredit - requiredCredit).ToString("0.00")
                    }
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new { status = "error", msg = ex.Message, data = "" };
            }
            catch (Exception ex)
            {
                return new { status = "error", msg = ex.Message, data = "" };
            }
        }

        // Update user credit in database
        private async Task UpdateUserCreditAsync(int userId, decimal creditChange, string description, int messageCount)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Get current credit
                    var currentCredit = await GetUserCreditFromDatabaseAsync(userId);
                    var newCredit = currentCredit + creditChange;

                    var query = @"
                        INSERT INTO [toranjdata_crm_2018].[dbo].[apiMessaging] 
                        (UserID, Type, CreditChanges, RemainingCredit, Description, Date, Status, ip)
                        VALUES 
                        (@UserId, @Type, @CreditChanges, @RemainingCredit, @Description, @Date, @Status, @IP)";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        command.Parameters.AddWithValue("@Type", 1); // SMS type
                        command.Parameters.AddWithValue("@CreditChanges", creditChange);
                        command.Parameters.AddWithValue("@RemainingCredit", newCredit);
                        command.Parameters.AddWithValue("@Description", $"{description} - تعداد: {messageCount}");
                        command.Parameters.AddWithValue("@Date", DateTime.Now);
                        command.Parameters.AddWithValue("@Status", 1); // Success
                        command.Parameters.AddWithValue("@IP", ""); // You might want to pass actual IP

                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception)
            {
                // Log error but don't throw to avoid breaking SMS send process
            }
        }

        // Legacy methods (keeping for backward compatibility)
        public async Task<(string response, string[] cleanedNumbers)> SendToNumbersAsync(string title, string message, string numbersRaw)
        {
            var numbers = numbersRaw
                .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToArray();

            if (numbers.Length == 0)
                throw new ArgumentException("هیچ شماره‌ای برای ارسال یافت نشد.");

            var messages = numbers.Select(_ => message).ToArray();

            var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
                                <soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
                                xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
                                xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
                            <soap:Body>
                            <SendMultipleSMS3 xmlns=""http://tempuri.org/"">
                            <username>{_settings.Username}</username>
                            <password>{_settings.Password}</password>
                            <to>
                                {string.Join("", numbers.Select(n => $"<string>{System.Security.SecurityElement.Escape(n)}</string>"))}
                            </to>
                            <from>{System.Security.SecurityElement.Escape(_settings.From)}</from>
                            <text>
                            {string.Join("", messages.Select(m => $"<string>{System.Security.SecurityElement.Escape(m)}</string>"))}
                            </text>
                            </SendMultipleSMS3>
                            </soap:Body>
                        </soap:Envelope>";

            var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", "\"http://tempuri.org/SendMultipleSMS3\"");

            var response = await _httpClient.PostAsync(_settings.ServiceUrl, content);
            var resultContent = await response.Content.ReadAsStringAsync();

            return (resultContent, numbers);
        }

        public async Task<string> AddNumberBulkAsync(AddNumberBulkRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                throw new ArgumentException("عنوان پیام نباید خالی باشد.");

            if (string.IsNullOrWhiteSpace(request.Message))
                throw new ArgumentException("متن پیام نباید خالی باشد.");

            if (!Regex.IsMatch(request.Message, @"لغو ?11$"))
                throw new ArgumentException("عبارت 'لغو11' یا 'لغو 11' باید در انتهای پیام باشد.");

            int charCount = request.Message.Length;
            int pages = charCount <= 70 ? 1 : (int)Math.Ceiling((charCount - 70) / 67.0) + 1;
            if (pages > 8)
                throw new ArgumentException("تعداد صفحات پیام بیش از حد مجاز (8 صفحه) است.");

            if (string.IsNullOrWhiteSpace(request.Receivers))
                throw new ArgumentException("لیست شماره‌ها نباید خالی باشد.");

            var numbers = request.Receivers.Split(',').Select(n => n.Trim()).Where(n => !string.IsNullOrEmpty(n)).ToList();

            if (numbers.Count == 0)
                throw new ArgumentException("هیچ شماره‌ای یافت نشد.");

            var validNumbers = numbers.Where(n => n.Length == 11 && n.StartsWith("09") && Regex.IsMatch(n, @"^\d{11}$")).ToList();

            if (validNumbers.Count < 10)
                throw new ArgumentException($"حداقل 10 شماره صحیح مورد نیاز است. تعداد شماره‌های صحیح وارد شده: {validNumbers.Count}");

            validNumbers = validNumbers.Distinct().ToList();
            var receivers = string.Join(",", validNumbers);

            var dateToSend = string.IsNullOrWhiteSpace(request.DateToSend)
                ? DateTime.Now.AddMinutes(1).ToString("yyyy/MM/dd HH:mm")
                : request.DateToSend;

            var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
               xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
               xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <AddNumberBulk xmlns=""http://tempuri.org/"">
      <username>{System.Security.SecurityElement.Escape(_settings.Username)}</username>
      <password>{System.Security.SecurityElement.Escape(_settings.Password)}</password>
      <from>{System.Security.SecurityElement.Escape(_settings.From)}</from>
      <title>{System.Security.SecurityElement.Escape(request.Title)}</title>
      <message>{System.Security.SecurityElement.Escape(request.Message)}</message>
      <receivers>{System.Security.SecurityElement.Escape(receivers)}</receivers>
      <DateToSend>{System.Security.SecurityElement.Escape(dateToSend)}</DateToSend>
    </AddNumberBulk>
  </soap:Body>
</soap:Envelope>";

            var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", "\"http://tempuri.org/AddNumberBulk\"");

            var url = "https://api.payamak-panel.com/post/newbulks.asmx";

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                var match = Regex.Match(responseContent, @"<AddNumberBulkResult>(\d+)</AddNumberBulkResult>");
                var resultCode = match.Success ? match.Groups[1].Value : "NoMatch";

                Console.WriteLine("===== SMS BULK SEND LOG =====");
                Console.WriteLine($"Title: {request.Title}");
                Console.WriteLine($"Message: {request.Message}");
                Console.WriteLine($"Total Numbers Input: {numbers.Count}");
                Console.WriteLine($"Valid Numbers Count: {validNumbers.Count}");
                Console.WriteLine($"Receivers: {receivers}");
                Console.WriteLine($"DateToSend: {dateToSend}");
                Console.WriteLine($"HTTP Status Code: {(int)response.StatusCode} {response.StatusCode}");
                Console.WriteLine($"Raw XML Response:\n{responseContent}");
                Console.WriteLine($"Parsed Result Code: {resultCode}");
                Console.WriteLine("==============================");

                return responseContent;
            }
            catch (Exception ex)
            {
                Console.WriteLine("خطا در ارسال پیام SOAP:");
                Console.WriteLine(ex.ToString());
                throw;
            }
        }
    }
}
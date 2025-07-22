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
        public async Task<int> GetUserIdFromTokenAsync(string token)
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

        // محاسبه تعداد صفحات پیام
        private int CalculateMessagePages(string message)
        {
            int charCount = message.Length;
            if (charCount <= 70)
                return 1;
            else
                return (int)Math.Ceiling((charCount - 70) / 67.0) + 1;
        }

        // بررسی موجودی کافی
        public async Task<bool> CheckSufficientCreditAsync(int userId, int requiredPages)
        {
            var currentCredit = await GetUserCreditFromDatabaseAsync(userId);
            return currentCredit >= requiredPages;
        }

        // Get SMS Plan Info
        public async Task<object> GetSMSPlanInfoAsync(string token)
        {
            try
            {
                var userId = await GetUserIdFromTokenAsync(token);

                var planInfo = new
                {
                    status = "success",
                    msg = "بسته شارژ خط خدماتی",
                    data = new
                    {
                        title = "بسته 1000 تایی پیامک",
                        price = "50000",
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
                    data = new
                    {
                        value = credit.ToString("0.00"),
                        balance = credit.ToString("0.00")
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

        // Get User Info with token
        public async Task<object> GetUserInfoAsync(string token)
        {
            try
            {
                Console.WriteLine($"=== GetUserInfoAsync Debug Start ===");
                Console.WriteLine($"Token received: {token?.Substring(0, Math.Min(10, token?.Length ?? 0))}...");

                var userId = await GetUserIdFromTokenAsync(token);
                Console.WriteLine($"UserID extracted from token: {userId}");

                var credit = await GetUserCreditFromDatabaseAsync(userId);
                Console.WriteLine($"User credit retrieved: {credit}");

                var userInfo = await GetFullUserInfoAsync(userId);
                Console.WriteLine($"User info retrieved: Name='{userInfo.Name}', CellPhone='{userInfo.CellPhone}'");

                var result = new
                {
                    status = "success",
                    data = new
                    {
                        name = userInfo.Name,
                        balance = credit.ToString("0.00"),
                        userId = userId,
                        cellPhone = userInfo.CellPhone,
                        mobile = userInfo.CellPhone,
                        phone = userInfo.CellPhone
                    }
                };

                Console.WriteLine($"Final result to return: {System.Text.Json.JsonSerializer.Serialize(result)}");
                Console.WriteLine($"=== GetUserInfoAsync Debug End ===");

                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Unauthorized error in GetUserInfoAsync: {ex.Message}");
                return new { status = "error", msg = ex.Message, data = (object)null };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error in GetUserInfoAsync: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new { status = "error", msg = ex.Message, data = (object)null };
            }
        }

        // **متد اصلی ارسال SMS با کسر از کیف پول مشتری**
        public async Task<object> SendSMSAsync(string token, string title, string message, string numbersRaw)
        {
            try
            {
                var userId = await GetUserIdFromTokenAsync(token);

                // تشخیص نوع ارسال: تست یا انبوه
                bool isTest = numbersRaw?.ToLower() == "test";

                if (isTest)
                {
                    return await SendTestSMSAsync(userId, title, message);
                }
                else
                {
                    return await SendBulkSMSInternalAsync(userId, title, message, numbersRaw);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                return new { status = "error", error = ex.Message, data = "" };
            }
            catch (Exception ex)
            {
                return new { status = "error", error = ex.Message, data = "" };
            }
        }

        // ارسال تست SMS
        private async Task<object> SendTestSMSAsync(int userId, string title, string message)
        {
            try
            {
                // محاسبه تعداد صفحات مورد نیاز
                var pages = CalculateMessagePages(message);

                // بررسی موجودی کافی
                if (!await CheckSufficientCreditAsync(userId, pages))
                {
                    return new { status = "error", error = "موجودی کیف پول کافی نمی‌باشد" };
                }

                // دریافت شماره همراه کاربر برای ارسال تست
                var userInfo = await GetFullUserInfoAsync(userId);

                if (string.IsNullOrEmpty(userInfo.CellPhone))
                {
                    return new { status = "error", error = "شماره همراه کاربر یافت نشد" };
                }

                // ارسال پیام تست به شماره خود کاربر
                var (response, cleanedNumbers) = await SendToNumbersAsync(title, message, userInfo.CellPhone);

                // کسر از کیف پول مشتری
                await UpdateUserCreditAsync(userId, -pages, "ارسال تست پیامک", 1);

                return new
                {
                    status = "success",
                    msg = "ارسال تست با موفقیت انجام شد",
                    data = new
                    {
                        sentTo = new[] { userInfo.CellPhone },
                        count = 1,
                        pages = pages,
                        testPhone = userInfo.CellPhone
                    }
                };
            }
            catch (Exception ex)
            {
                return new { status = "error", error = $"خطا در ارسال تست: {ex.Message}" };
            }
        }

        // ارسال انبوه SMS با استفاده از AddNumberBulk
        private async Task<object> SendBulkSMSInternalAsync(int userId, string title, string message, string numbersRaw)
        {
            SqlTransaction transaction = null;
            try
            {
                // اعتبارسنجی پیام
                if (!Regex.IsMatch(message, @"لغو ?11$"))
                {
                    return new { status = "error", error = "عبارت 'لغو11' یا 'لغو 11' باید در انتهای پیام باشد" };
                }

                // محاسبه تعداد صفحات
                var pages = CalculateMessagePages(message);
                if (pages > 8)
                {
                    return new { status = "error", error = "تعداد صفحات پیام بیش از حد مجاز (8 صفحه) است" };
                }

                // پردازش شماره‌ها
                var numbers = numbersRaw
                    .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Trim())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                if (numbers.Count == 0)
                {
                    return new { status = "error", error = "هیچ شماره‌ای یافت نشد" };
                }

                // فیلتر شماره‌های معتبر
                var validNumbers = numbers
                    .Where(n => n.Length == 11 && n.StartsWith("09") && Regex.IsMatch(n, @"^\d{11}$"))
                    .Distinct()
                    .ToList();

                if (validNumbers.Count < 10)
                {
                    return new { status = "error", error = $"حداقل 10 شماره صحیح مورد نیاز است. تعداد شماره‌های صحیح: {validNumbers.Count}" };
                }

                // محاسبه کل اعتبار مورد نیاز
                var totalCreditsRequired = validNumbers.Count * pages;

                // بررسی موجودی کافی
                if (!await CheckSufficientCreditAsync(userId, totalCreditsRequired))
                {
                    return new { status = "error", error = "موجودی کیف پول کافی نمی‌باشد" };
                }

                // دریافت اطلاعات کاربر (شماره ادمین)
                var userInfo = await GetFullUserInfoAsync(userId);

                // شروع تراکنش دیتابیس
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    transaction = connection.BeginTransaction();

                    try
                    {
                        // ارسال انبوه
                        var receivers = string.Join(",", validNumbers);
                        var dateToSend = DateTime.Now.AddMinutes(1).ToString("yyyy/MM/dd HH:mm:ss");

                        var bulkRequest = new AddNumberBulkRequest
                        {
                            Title = title,
                            Message = message,
                            Receivers = receivers,
                            DateToSend = dateToSend
                        };

                        Console.WriteLine($"=== BULK SMS REQUEST ===");
                        Console.WriteLine($"Title: {title}");
                        Console.WriteLine($"Message: {message.Substring(0, Math.Min(50, message.Length))}...");
                        Console.WriteLine($"Numbers Count: {validNumbers.Count}");
                        Console.WriteLine($"Date to Send: {dateToSend}");
                        Console.WriteLine($"========================");

                        // ارسال به فراپیامک
                        var bulkResponse = await AddNumberBulkAsync(bulkRequest);

                        // بررسی نتیجه ارسال
                        var responseResult = ParseBulkSMSResponse(bulkResponse);

                        if (responseResult.IsSuccess)
                        {
                            // ذخیره اطلاعات bulk برای پیگیری وضعیت
                            await SaveBulkSmsTracking(userId, responseResult.ResponseId, totalCreditsRequired,
                                validNumbers.Count, title, message, userInfo.CellPhone, connection, transaction);

                            // موفق - تایید تراکنش بدون کسر اعتبار (اعتبار بعداً بر اساس تایید نهایی کسر می‌شود)
                            transaction.Commit();

                            Console.WriteLine($"✅ BULK SMS SENT SUCCESSFULLY");
                            Console.WriteLine($"Response ID: {responseResult.ResponseId}");

                            return new
                            {
                                status = "success",
                                statusCode = responseResult.StatusCode,
                                msg = "پیامک انبوه ارسال شد و در انتظار تایید نهایی است",
                                data = new
                                {
                                    sentTo = validNumbers.ToArray(),
                                    count = validNumbers.Count,
                                    pages = pages,
                                    totalCreditsReserved = totalCreditsRequired,
                                    scheduledTime = dateToSend,
                                    responseId = responseResult.ResponseId,
                                    note = "اعتبار پس از تایید نهایی از فراپیامک کسر خواهد شد"
                                }
                            };
                        }
                        else
                        {
                            // خطا - برگشت تراکنش
                            transaction.Rollback();

                            Console.WriteLine($"❌ BULK SMS FAILED");
                            Console.WriteLine($"Error: {responseResult.ErrorMessage}");
                            Console.WriteLine($"Status Code: {responseResult.StatusCode}");

                            return new
                            {
                                status = "error",
                                statusCode = responseResult.StatusCode,
                                error = $"خطا در ارسال به فراپیامک: {responseResult.ErrorMessage}",
                                rawResponse = responseResult.RawResponse
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        // خطا - برگشت تراکنش
                        transaction?.Rollback();

                        Console.WriteLine($"❌ EXCEPTION IN BULK SMS");
                        Console.WriteLine($"Error: {ex.Message}");
                        Console.WriteLine($"Stack Trace: {ex.StackTrace}");

                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ CRITICAL ERROR IN SendBulkSMSInternalAsync: {ex.Message}");
                return new { status = "error", error = $"خطا در ارسال انبوه: {ex.Message}" };
            }
        }

        private BulkSMSResponse ParseBulkSMSResponse(string soapResponse)
        {
            try
            {
                Console.WriteLine($"=== PARSING FARAPAYAMAK RESPONSE ===");
                Console.WriteLine($"Raw Response: {soapResponse}");

                var startTag = "<AddNumberBulkResult>";
                var endTag = "</AddNumberBulkResult>";

                var startIndex = soapResponse.IndexOf(startTag);
                if (startIndex == -1)
                {
                    return new BulkSMSResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "پاسخ نامعتبر از سرور فراپیامک",
                        RawResponse = soapResponse
                    };
                }

                startIndex += startTag.Length;
                var endIndex = soapResponse.IndexOf(endTag, startIndex);

                if (endIndex == -1)
                {
                    return new BulkSMSResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "پاسخ ناقص از سرور فراپیامک",
                        RawResponse = soapResponse
                    };
                }

                var resultValue = soapResponse.Substring(startIndex, endIndex - startIndex).Trim();

                Console.WriteLine($"Extracted Result Value: '{resultValue}'");

                // بررسی نتیجه بر اساس مقدار برگشتی
                if (long.TryParse(resultValue, out long responseId))
                {
                    if (responseId > 0)
                    {
                        // موفق
                        Console.WriteLine($"✅ SUCCESS - Response ID: {responseId}");
                        return new BulkSMSResponse
                        {
                            IsSuccess = true,
                            StatusCode = 1,
                            ResponseId = responseId.ToString(),
                            RawResponse = soapResponse
                        };
                    }
                    else
                    {
                        // خطا با کد منفی
                        var errorMessage = GetFarapayamakErrorMessage(responseId);
                        Console.WriteLine($"❌ ERROR - Code: {responseId}, Message: {errorMessage}");

                        return new BulkSMSResponse
                        {
                            IsSuccess = false,
                            StatusCode = (int)responseId,
                            ErrorMessage = errorMessage,
                            RawResponse = soapResponse
                        };
                    }
                }
                else
                {
                    // مقدار غیرعددی
                    Console.WriteLine($"❌ NON-NUMERIC RESPONSE: {resultValue}");
                    return new BulkSMSResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = $"پاسخ غیرمنتظره: {resultValue}",
                        RawResponse = soapResponse
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ EXCEPTION IN ParseBulkSMSResponse: {ex.Message}");
                return new BulkSMSResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"خطا در پردازش پاسخ: {ex.Message}",
                    RawResponse = soapResponse
                };
            }
        }

        // متد برای ترجمه کدهای خطای فراپیامک
        private string GetFarapayamakErrorMessage(long errorCode)
        {
            return errorCode switch
            {
                -1 => "نام کاربری یا رمز عبور اشتباه است",
                -2 => "اعتبار کافی نیست",
                -3 => "محدودیت در ارسال روزانه",
                -4 => "محدودیت در حجم ارسال",
                -5 => "شماره فرستنده معتبر نیست",
                -6 => "سامانه در حالت تعمیر است",
                -7 => "متن پیام خالی است",
                -8 => "دریافت کنندگان معتبر نیستند",
                -9 => "خط ارسالی فعال نیست",
                -10 => "کاربر فعال نیست",
                -11 => "عدم تطبیق اطلاعات ارسال با مشخصات کاربری",
                _ => $"خطای نامشخص با کد {errorCode}"
            };
        }

        // کلاس کمکی برای پاسخ ارسال انبوه
        public class BulkSMSResponse
        {
            public bool IsSuccess { get; set; }
            public int StatusCode { get; set; }
            public string ResponseId { get; set; } = "";
            public string ErrorMessage { get; set; } = "";
            public string RawResponse { get; set; } = "";
        }

        // Update user credit in database
        public async Task UpdateUserCreditAsync(int userId, decimal creditChange, string description, int messageCount)
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
                        command.Parameters.AddWithValue("@Date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        command.Parameters.AddWithValue("@Status", 1); // Success
                        command.Parameters.AddWithValue("@IP", "127.0.0.1");

                        await command.ExecuteNonQueryAsync();
                    }
                }

                Console.WriteLine($"Credit updated for user {userId}: {creditChange} (Description: {description})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating credit: {ex.Message}");
                throw;
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
      <receivers>{System.Security.SecurityElement.Escape(request.Receivers)}</receivers>
      <DateToSend>{System.Security.SecurityElement.Escape(request.DateToSend)}</DateToSend>
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

                Console.WriteLine("===== SMS BULK SEND LOG =====");
                Console.WriteLine($"Title: {request.Title}");
                Console.WriteLine($"Message: {request.Message}");
                Console.WriteLine($"Receivers: {request.Receivers}");
                Console.WriteLine($"DateToSend: {request.DateToSend}");
                Console.WriteLine($"HTTP Status Code: {(int)response.StatusCode} {response.StatusCode}");
                Console.WriteLine($"Raw XML Response:\n{responseContent}");
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

        private async Task SaveBulkSmsTracking(int userId, string bulkId, decimal totalCredits,
    int messageCount, string title, string message, string adminPhone,
    SqlConnection connection, SqlTransaction transaction)
        {
            try
            {
                var query = @"
        INSERT INTO [toranjdata_crm_2018].[dbo].[BulkSmsTracking] 
        (BulkId, UserId, TotalCreditsUsed, MessageCount, AdminPhone, Title, MessageText, 
         BodyParts, SentCount, FailedCount, DateSent, DateProcessed, Status, ResultMessage, OriginalXml)
        VALUES 
        (@BulkId, @UserId, @TotalCreditsUsed, @MessageCount, @AdminPhone, @Title, @MessageText, 
         @BodyParts, @SentCount, @FailedCount, @DateSent, @DateProcessed, @Status, @ResultMessage, @OriginalXml)";

                using var command = new SqlCommand(query, connection, transaction);
                command.Parameters.AddWithValue("@BulkId", bulkId);
                command.Parameters.AddWithValue("@UserId", userId);
                command.Parameters.AddWithValue("@TotalCreditsUsed", totalCredits);
                command.Parameters.AddWithValue("@MessageCount", messageCount);
                command.Parameters.AddWithValue("@AdminPhone", adminPhone ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Title", title ?? "");
                command.Parameters.AddWithValue("@MessageText", message ?? "");

                // فیلدهای جدید (مقداردهی اولیه)
                command.Parameters.AddWithValue("@BodyParts", 1); // فرض پیش‌فرض
                command.Parameters.AddWithValue("@SentCount", 0);
                command.Parameters.AddWithValue("@FailedCount", 0);
                command.Parameters.AddWithValue("@DateSent", DateTime.Now);
                command.Parameters.AddWithValue("@DateProcessed", DBNull.Value); // هنوز ارسال نشده

                command.Parameters.AddWithValue("@Status", 0); // وضعیت PENDING (عدد)
                command.Parameters.AddWithValue("@ResultMessage", "در انتظار تایید از فراپیامک");
                command.Parameters.AddWithValue("@OriginalXml", DBNull.Value); // اگر داری XML، اینجا بذار

                await command.ExecuteNonQueryAsync();

                Console.WriteLine($"Bulk SMS tracking saved for bulk ID: {bulkId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"خطا در ذخیره tracking: {ex.Message}");
                throw;
            }
        }


        // متد دریافت اطلاعات کامل کاربر
        private async Task<UserInfo> GetFullUserInfoAsync(int userId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = @"
                SELECT TOP 1 
                    COALESCE(FirstName + ' ' + LastName, FirstName, LastName, Username, OrganizationName) as Name,
                    CellPhone,
                    Tel as Phone
                FROM [toranjdata_crm_2018].[dbo].[User] 
                WHERE UserID = @UserId";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (reader.Read())
                            {
                                var name = reader["Name"]?.ToString() ?? "کاربر";
                                var cellPhone = reader["CellPhone"]?.ToString() ?? "";
                                var phone = reader["Phone"]?.ToString() ?? "";

                                var finalPhone = !string.IsNullOrWhiteSpace(cellPhone) ? cellPhone : phone;

                                Console.WriteLine($"User Info Retrieved: Name={name}, CellPhone={cellPhone}, Phone={phone}, Final={finalPhone}");

                                return new UserInfo
                                {
                                    Name = name,
                                    CellPhone = finalPhone
                                };
                            }
                        }
                    }

                    Console.WriteLine("No user found in database");
                    return new UserInfo { Name = "کاربر", CellPhone = "" };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"خطا در دریافت اطلاعات کاربر: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                return new UserInfo { Name = "کاربر", CellPhone = "" };
            }
        }

        // این متد را به کلاس SmsService اضافه کنید

        public async Task<CreditResponse> GetCreditLegacyAsync()
        {
            try
            {
                // ساخت درخواست SOAP برای دریافت اعتبار
                var soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
               xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
               xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <GetCredit xmlns=""http://tempuri.org/"">
      <username>{System.Security.SecurityElement.Escape(_settings.Username)}</username>
      <password>{System.Security.SecurityElement.Escape(_settings.Password)}</password>
    </GetCredit>
  </soap:Body>
</soap:Envelope>";

                var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
                content.Headers.Add("SOAPAction", "\"http://tempuri.org/GetCredit\"");

                var response = await _httpClient.PostAsync(_settings.ServiceUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                // پارس کردن پاسخ XML
                var creditValue = ExtractCreditFromSoapResponse(responseContent);

                return new CreditResponse
                {
                    RetStatus = creditValue >= 0 ? 1 : 0,
                    Value = creditValue,
                    StrRetStatus = creditValue >= 0 ? "موفق" : "خطا در دریافت اعتبار"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"خطا در دریافت اعتبار: {ex.Message}");
                return new CreditResponse
                {
                    RetStatus = 0,
                    Value = 0,
                    StrRetStatus = $"خطا: {ex.Message}"
                };
            }
        }

        // متد کمکی برای استخراج مقدار اعتبار از پاسخ SOAP
        private decimal ExtractCreditFromSoapResponse(string soapResponse)
        {
            try
            {
                var startTag = "<GetCreditResult>";
                var endTag = "</GetCreditResult>";

                var startIndex = soapResponse.IndexOf(startTag);
                if (startIndex == -1) return -1;

                startIndex += startTag.Length;
                var endIndex = soapResponse.IndexOf(endTag, startIndex);

                if (endIndex == -1) return -1;

                var valueStr = soapResponse.Substring(startIndex, endIndex - startIndex).Trim();

                if (decimal.TryParse(valueStr, out decimal result))
                    return result;

                return -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private async Task UpdateUserCreditWithTransactionAsync(int userId, decimal creditChange, string description,
    int messageCount, SqlConnection connection, SqlTransaction transaction)
        {
            try
            {
                // Get current credit
                var currentCredit = await GetUserCreditFromDatabaseWithTransactionAsync(userId, connection, transaction);
                var newCredit = currentCredit + creditChange;

                var query = @"
            INSERT INTO [toranjdata_crm_2018].[dbo].[apiMessaging] 
            (UserID, Type, CreditChanges, RemainingCredit, Description, Date, Status, ip)
            VALUES 
            (@UserId, @Type, @CreditChanges, @RemainingCredit, @Description, @Date, @Status, @IP)";

                using (var command = new SqlCommand(query, connection, transaction))
                {
                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@Type", 1);
                    command.Parameters.AddWithValue("@CreditChanges", creditChange);
                    command.Parameters.AddWithValue("@RemainingCredit", newCredit);
                    command.Parameters.AddWithValue("@Description", $"{description} - تعداد: {messageCount}");
                    command.Parameters.AddWithValue("@Date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.Parameters.AddWithValue("@Status", 1);
                    command.Parameters.AddWithValue("@IP", "127.0.0.1");

                    await command.ExecuteNonQueryAsync();
                }

                Console.WriteLine($"Credit updated in transaction for user {userId}: {creditChange} (New Balance: {newCredit})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating credit in transaction: {ex.Message}");
                throw;
            }
        }

        // متد برای دریافت موجودی با تراکنش
        private async Task<decimal> GetUserCreditFromDatabaseWithTransactionAsync(int userId, SqlConnection connection, SqlTransaction transaction)
        {
            try
            {
                var query = @"
            SELECT TOP 1 RemainingCredit
            FROM [toranjdata_crm_2018].[dbo].[apiMessaging]
            WHERE UserID = @UserId AND ISDATE([Date]) = 1
            ORDER BY TRY_CAST([Date] AS DATETIME) DESC;
        ";

                using (var command = new SqlCommand(query, connection, transaction))
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
            catch (Exception)
            {
                throw new Exception("خطا در دریافت موجودی کیف پول");
            }
        }

        // متد برای بروزرسانی توضیحات اعتبار
        private async Task UpdateUserCreditDescriptionAsync(int userId, string newDescription, SqlConnection connection, SqlTransaction transaction)
        {
            try
            {
                var query = @"
            UPDATE [toranjdata_crm_2018].[dbo].[apiMessaging] 
            SET Description = @NewDescription
            WHERE UserID = @UserId 
            AND Date = (SELECT MAX(Date) FROM [toranjdata_crm_2018].[dbo].[apiMessaging] WHERE UserID = @UserId)";

                using (var command = new SqlCommand(query, connection, transaction))
                {
                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@NewDescription", newDescription);

                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"خطا در بروزرسانی توضیحات: {ex.Message}");
            }
        }


        // کلاس مدل برای پاسخ اعتبار
        public class CreditResponse
        {
            public int RetStatus { get; set; }
            public decimal Value { get; set; }
            public string StrRetStatus { get; set; }
        }

        // کلاس کمکی
        public class UserInfo
        {
            public string Name { get; set; } = "";
            public string CellPhone { get; set; } = "";
        }
    }
}
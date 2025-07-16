using Microsoft.Extensions.Options;
using SendBulk.Models;
using SendBulk.Models.Request;
using SendBulk.Models.Response;
using System.Runtime;
using System.ServiceModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SendBulk.Services
{
    public class SmsService
    {
        private readonly FarapayamakSettings _settings;
        private readonly HttpClient _httpClient;

        public SmsService(IOptions<FarapayamakSettings> options)
        {
            _settings = options.Value;
            _httpClient = new HttpClient();
        }

        // Get Credit
        public async Task<SmsCreditResponse> GetCreditAsync()
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
                result.Value = Math.Round(decimalValue, 2).ToString("0.00"); // نمایش با 2 رقم اعشار
            }


            return result ?? new SmsCreditResponse { RetStatus = -1, StrRetStatus = "خطا در تبدیل پاسخ" };
        }



        // Send To Numbers
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

            // شناسایی شماره‌های صحیح
            var validNumbers = numbers.Where(n => n.Length == 11 && n.StartsWith("09") && Regex.IsMatch(n, @"^\d{11}$")).ToList();

            // بررسی حداقل 10 شماره صحیح
            if (validNumbers.Count < 10)
                throw new ArgumentException($"حداقل 10 شماره صحیح مورد نیاز است. تعداد شماره‌های صحیح وارد شده: {validNumbers.Count}");

            // حذف تکراری‌ها از شماره‌های صحیح
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

            // استفاده از HTTPS (در صورت پشتیبانی)
            var url = "https://api.payamak-panel.com/post/newbulks.asmx";

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                // استخراج نتیجه از XML
                var match = Regex.Match(responseContent, @"<AddNumberBulkResult>(\d+)</AddNumberBulkResult>");
                var resultCode = match.Success ? match.Groups[1].Value : "NoMatch";

                // لاگ کامل برای بررسی وضعیت
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

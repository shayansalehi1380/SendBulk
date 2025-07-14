using Microsoft.Extensions.Options;
using SendBulk.Models;
using SendBulk.Models.Request;
using SendBulk.Models.Response;
using System.Runtime;
using System.ServiceModel;
using System.Text;
using System.Text.Json;

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


    }
}

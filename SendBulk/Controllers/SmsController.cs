using Microsoft.AspNetCore.Mvc;
using SendBulk.Models;
using SendBulk.Models.Request;
using SendBulk.Models.Response;
using SendBulk.Services;

namespace SendBulk.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SmsController : ControllerBase
    {
        private readonly SmsService _smsService;

        public SmsController(SmsService smsService)
        {
            _smsService = smsService;
        }

        /// <summary>
        /// Get Credit (مشاهده موجودی تعداد پیامک)
        /// </summary>
        /// <returns>Get Credit (مشاهده موجودی تعداد پیامک)</returns>
        [HttpPost("credit")]
        public async Task<IActionResult> GetCredit()
        {
            try
            {
                // استفاده از متد Legacy برای backward compatibility
                var credit = await _smsService.GetCreditLegacyAsync();
                if (credit.RetStatus == 1)
                    return Ok(credit);

                return BadRequest(credit);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Get Credit with Token (مشاهده موجودی با توکن)
        /// </summary>
        /// <param name="token">User token</param>
        /// <returns>Get Credit with Token</returns>
        [HttpPost("credit-with-token")]
        public async Task<IActionResult> GetCreditWithToken([FromBody] string token)
        {
            try
            {
                var result = await _smsService.GetCreditAsync(token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        [HttpGet("user/info")]
        public async Task<IActionResult> GetUserInfo()
        {
            try
            {
                var credit = await _smsService.GetCreditLegacyAsync();

                if (credit.RetStatus == 1)
                {
                    var userInfo = new
                    {
                        name = "نام کاربر تستی", // یا از دیتابیس بخوان
                        balance = credit.Value
                    };
                    return Ok(userInfo);
                }

                return BadRequest(credit);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get User Info with Token (اطلاعات کاربر با توکن)
        /// </summary>
        /// <param name="token">User token</param>
        /// <returns>User Info with Token</returns>
        [HttpPost("user/info-with-token")]
        public async Task<IActionResult> GetUserInfoWithToken([FromBody] string token)
        {
            try
            {
                var result = await _smsService.GetUserInfoAsync(token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// SendMultipleSMS (ارسال چندتایی)
        /// </summary>
        /// <param name="request"></param>
        /// <returns>SendMultipleSMS (ارسال چندتایی)</returns>
        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] DirectSmsRequest request)
        {
            try
            {
                var (response, cleanedNumbers) = await _smsService.SendToNumbersAsync(request.Title, request.Message, request.Numbers);
                return Ok(new
                {
                    SentTo = cleanedNumbers,
                    Response = response
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Send SMS with Token (ارسال با توکن)
        /// </summary>
        /// <param name="request"></param>
        /// <returns>Send SMS with Token</returns>
        [HttpPost("send-with-token")]
        public async Task<IActionResult> SendWithToken([FromBody] TokenSmsRequest request)
        {
            try
            {
                var result = await _smsService.SendSMSAsync(request.Token, request.Title, request.Message, request.Numbers);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Send Bulk (ارسال انبوه)
        /// </summary>
        /// <param name="request"></param>
        /// <returns>Send Bulk (ارسال انبوه)</returns>
        [HttpPost("add-bulk")]
        public async Task<ActionResult<AddNumberBulkResponse>> AddNumberBulk([FromBody] AddNumberBulkRequest request)
        {
            try
            {
                var rawResponse = await _smsService.AddNumberBulkAsync(request);

                var resultValue = ExtractResultFromSoap(rawResponse);

                return Ok(new AddNumberBulkResponse
                {
                    StatusCode = resultValue,
                    RawResponse = rawResponse
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new AddNumberBulkResponse
                {
                    StatusCode = -1,
                    RawResponse = $"خطا: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get SMS Plan Info with Token (اطلاعات بسته با توکن)
        /// </summary>
        /// <param name="token">User token</param>
        /// <returns>SMS Plan Info</returns>
        [HttpPost("plan-info")]
        public async Task<IActionResult> GetSMSPlanInfo([FromBody] string token)
        {
            try
            {
                var result = await _smsService.GetSMSPlanInfoAsync(token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // متدی برای استخراج عدد برگشتی از داخل XML SOAP
        private int ExtractResultFromSoap(string soapResponse)
        {
            try
            {
                var startTag = "<AddNumberBulkResult>";
                var endTag = "</AddNumberBulkResult>";

                var startIndex = soapResponse.IndexOf(startTag) + startTag.Length;
                var endIndex = soapResponse.IndexOf(endTag);

                if (startIndex >= 0 && endIndex > startIndex)
                {
                    var value = soapResponse.Substring(startIndex, endIndex - startIndex);
                    return int.TryParse(value, out int result) ? result : -999;
                }
                return -998;
            }
            catch
            {
                return -997;
            }
        }
    }

    // کلاس درخواست برای ارسال با توکن
    public class TokenSmsRequest
    {
        public string Token { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Numbers { get; set; }
    }
}
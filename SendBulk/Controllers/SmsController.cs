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
                var credit = await _smsService.GetCreditAsync();
                if (credit.RetStatus == 1)
                    return Ok(credit);

                return BadRequest(credit);
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
                var credit = await _smsService.GetCreditAsync();

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
        /// Send Bulk (ارسال انبوه)
        /// </summary>
        /// <param name="request"></param>
        /// <returns>Send Bulk (ارسال انبوه)</returns>
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
}

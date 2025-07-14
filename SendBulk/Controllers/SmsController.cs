using Microsoft.AspNetCore.Mvc;
using SendBulk.Models;
using SendBulk.Models.Request;
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

    }
}

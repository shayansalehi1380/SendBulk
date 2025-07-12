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

namespace SendBulk.Models.Response
{
    public class ErrorResponse
    {
        public string Error { get; set; } = default!;
        public string Details { get; set; } = default!;
    }
}

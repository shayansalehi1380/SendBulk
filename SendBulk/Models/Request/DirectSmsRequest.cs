namespace SendBulk.Models.Request
{
    public class DirectSmsRequest
    {
        public string Title { get; set; } = default!;
        public string Message { get; set; } = default!;
        public string Numbers { get; set; } = default!;
    }
}

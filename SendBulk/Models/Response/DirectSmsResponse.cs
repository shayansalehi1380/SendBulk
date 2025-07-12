namespace SendBulk.Models.Response
{
    public class DirectSmsResponse
    {
        public string Title { get; set; } = default!;
        public string Message { get; set; } = default!;
        public List<string> SentTo { get; set; } = new();
        public List<long> MessageIds { get; set; } = new();
    }
}

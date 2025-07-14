namespace SendBulk.Models.Response
{
    public class SmsCreditResponse
    {
        public string Value { get; set; } = default!;
        public int RetStatus { get; set; }
        public string StrRetStatus { get; set; } = default!;
    }
}

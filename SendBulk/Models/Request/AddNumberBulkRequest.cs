namespace SendBulk.Models.Request
{
    public class AddNumberBulkRequest
    {
        public string Title { get; set; } = default!;
        public string Message { get; set; } = default!;

        /// <summary>
        /// شماره‌ها به صورت کاما جدا شده (مثلاً: "09120000000,09130000000")
        /// </summary>
        public string Receivers { get; set; } = default!;

        /// <summary>
        /// فرمت: YYYY-MM-DDTHH:MM:SS — اگر خالی باشد، بلافاصله ارسال می‌شود.
        /// </summary>
        public string DateToSend { get; set; } = string.Empty;
    }
}

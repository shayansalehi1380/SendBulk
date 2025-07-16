namespace SendBulk.Models.Response
{
    public class AddNumberBulkResponse
    {
        /// <summary>
        /// مقدار عددی برگشتی از وب‌سرویس (کد وضعیت)
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// متن کامل پاسخ XML یا توضیح خطا
        /// </summary>
        public string RawResponse { get; set; } = string.Empty;
    }
}

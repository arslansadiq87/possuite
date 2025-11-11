namespace Pos.Domain.Models.Sales
{
    public sealed record InvoicePreviewDto(int CounterId, int NextInvoiceNumber)
    {
        public string Human => $"{CounterId}-{NextInvoiceNumber}";
    }
}

namespace Pos.Domain.Settings
{
    public enum ReceiptKind { Sale, SaleReturn, Voucher, ZReport }

    public static class InvoiceSettingsHelpers
    {
        public static string ResolveFooter(InvoiceSettingsScoped s, ReceiptKind kind) =>
            kind switch
            {
                ReceiptKind.Sale => s.FooterSale ?? "",
                ReceiptKind.SaleReturn => s.FooterSaleReturn ?? "",
                ReceiptKind.Voucher => s.FooterVoucher ?? "",
                ReceiptKind.ZReport => s.FooterZReport ?? "",
                _ => ""
            };
    }
}

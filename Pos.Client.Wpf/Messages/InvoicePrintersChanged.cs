namespace Pos.Client.Wpf.Messages;

// Plain record; NO Toolkit dependency here
public sealed record InvoicePrintersChanged(
    int CounterId,
    int OutletId,
    string? ReceiptPrinter,
    string? LabelPrinter
);

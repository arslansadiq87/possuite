// Pos.Client.Wpf/Printing/ReceiptPreviewModels.cs
using System;

namespace Pos.Client.Wpf.Printing
{
    public sealed class ReceiptPreviewLine
    {
        public string? Name { get; set; }
        public string? Sku { get; set; }
        public int Qty { get; set; }
        public decimal Unit { get; set; }
        public decimal LineDiscount { get; set; }
        public decimal LineTotal => Qty * Unit - LineDiscount;
    }

    public sealed class ReceiptPreviewSale
    {
        // Totals
        public decimal Subtotal { get; set; }
        public decimal InvoiceDiscount { get; set; }
        public decimal Tax { get; set; }
        public decimal OtherExpenses { get; set; }
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal Balance { get; set; }

        // Header / meta
        public DateTime Ts { get; set; } = DateTime.Now;
        public int? OutletId { get; set; }
        public string? OutletCode { get; set; }   // NEW
        public int? CounterId { get; set; }
        public string? CounterName { get; set; }   // NEW
        public int? InvoiceNumber { get; set; }
        public string? CashierName { get; set; }   // NEW
        public string? CustomerName { get; set; }   // NEW

        // Optional barcode/QR demo payloads
        public string? BarcodeText { get; set; }
        public string? QrText { get; set; }
    }
}

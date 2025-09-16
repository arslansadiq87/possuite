// Pos.Client.Wpf/Models/CartLine.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Pos.Client.Wpf.Models
{
    public class CartLine : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public int ItemId { get; set; }
        public string Sku { get; set; } = "";
        public string DisplayName { get; set; } = "";
        private int _qty;
        public int Qty
        {
            get => _qty;
            set
            {
                if (_qty == value) return;
                _qty = value;
                OnPropertyChanged();           // <- tells DataGrid to refresh Qty cell
                                               // If your RecalcLine depends on Qty, caller will recalc and set amounts
            }
        }

        private decimal _unitPrice;
        public decimal UnitPrice
        {
            get => _unitPrice;
            set { if (_unitPrice == value) return; _unitPrice = value; OnPropertyChanged(); }
        }

        // --- Discount "last-entered-wins" logic ---
        private decimal? _discountPct;
        public decimal? DiscountPct
        {
            get => _discountPct;
            set
            {
                if (_discountPct == value) return;
                _discountPct = value;
                if ((_discountPct ?? 0) > 0) DiscountAmt = null;
                OnPropertyChanged();
            }
        }

        private decimal? _discountAmt;
        public decimal? DiscountAmt
        {
            get => _discountAmt;
            set
            {
                if (_discountAmt == value) return;
                _discountAmt = value;
                if ((_discountAmt ?? 0) > 0) DiscountPct = null;
                OnPropertyChanged();
            }
        }

        public string? TaxCode { get; set; }
        public decimal TaxRatePct { get; set; }
        public bool TaxInclusive { get; set; }

        // Derived amounts (raise change notifications so grid updates)
        private decimal _unitNet;
        public decimal UnitNet { get => _unitNet; set { if (_unitNet == value) return; _unitNet = value; OnPropertyChanged(); } }

        private decimal _lineNet;
        public decimal LineNet { get => _lineNet; set { if (_lineNet == value) return; _lineNet = value; OnPropertyChanged(); } }

        private decimal _lineTax;
        public decimal LineTax { get => _lineTax; set { if (_lineTax == value) return; _lineTax = value; OnPropertyChanged(); } }

        private decimal _lineTotal;
        public decimal LineTotal { get => _lineTotal; set { if (_lineTotal == value) return; _lineTotal = value; OnPropertyChanged(); } }
    }
}

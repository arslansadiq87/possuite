using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;          // AppState, PartyLookupService
using Pos.Domain.Entities;
using Pos.Domain.Formatting;            // ProductNameComposer
using Pos.Persistence;
using Pos.Persistence.Services;         // PurchasesService

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class PurchaseReturnWindow : Window
    {
        // ----- Modes -----
        private readonly int? _refPurchaseId;   // Return With
        private readonly int? _returnId;        // Amend existing return
        private readonly bool _freeForm;        // Return Without

        // ----- Data -----
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private readonly PurchasesService _svc;
        private readonly PartyLookupService _partySvc;

        public ReturnVM VM { get; } = new();

        // ===== Constructors =====
        public PurchaseReturnWindow()             // Return Without (free-form)
        {
            InitializeComponent();
            _freeForm = true;

            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;

            _svc = new PurchasesService(new PosClientDbContext(_opts));
            _partySvc = new PartyLookupService(new PosClientDbContext(_opts));

            DataContext = VM;
            Loaded += async (_, __) => await InitFreeFormAsync();
        }

        public PurchaseReturnWindow(int refPurchaseId)      // Return With base purchase
        {
            InitializeComponent();
            _refPurchaseId = refPurchaseId;

            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;

            _svc = new PurchasesService(new PosClientDbContext(_opts));
            _partySvc = new PartyLookupService(new PosClientDbContext(_opts));

            DataContext = VM;
            Loaded += async (_, __) => await InitFromBaseAsync(refPurchaseId);
        }

        public PurchaseReturnWindow(int returnId, bool isAmend = true) // Amend return
        {
            InitializeComponent();
            _returnId = returnId;

            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;

            _svc = new PurchasesService(new PosClientDbContext(_opts));
            _partySvc = new PartyLookupService(new PosClientDbContext(_opts));

            DataContext = VM;
            Loaded += async (_, __) => await InitFromExistingReturnAsync(returnId);
        }

        // ===== Initialization flows =====
        private async Task InitFromBaseAsync(int refPurchaseId)
        {
            using var db = new PosClientDbContext(_opts);

            // Draft with remaining caps
            var draft = await _svc.BuildReturnDraftAsync(refPurchaseId);

            // Load base purchase header to show DocNo etc.
            var baseP = await db.Purchases
                .Include(p => p.Party)
                .FirstAsync(p => p.Id == refPurchaseId);

            // Fill VM header
            VM.IsSupplierReadonly = true;
            VM.SupplierId = draft.PartyId;
            VM.SupplierDisplay = baseP.Party?.Name ?? $"Supplier #{draft.PartyId}";
            VM.TargetType = draft.TargetType;
            VM.OutletId = draft.OutletId;
            VM.WarehouseId = draft.WarehouseId;
            VM.RefPurchaseId = draft.RefPurchaseId;
            VM.BasePurchaseDisplay = !string.IsNullOrWhiteSpace(baseP.DocNo) ? baseP.DocNo : $"#{baseP.Id}";

            // Map draft lines → grid VMs (attach product meta for display)
            var original = await db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == refPurchaseId);
            var itemIds = original.Lines.Select(l => l.ItemId).Distinct().ToList();

            var meta = (
                from i in db.Items.AsNoTracking()
                join pr in db.Products.AsNoTracking() on i.ProductId equals pr.Id into gp
                from pr in gp.DefaultIfEmpty()
                where itemIds.Contains(i.Id)
                select new
                {
                    i.Id,
                    ItemName = i.Name,
                    ProductName = pr != null ? pr.Name : null,
                    i.Variant1Name,
                    i.Variant1Value,
                    i.Variant2Name,
                    i.Variant2Value,
                    i.Sku
                }
            ).ToList().ToDictionary(x => x.Id);

            VM.Lines.Clear();
            foreach (var d in draft.Lines)
            {
                meta.TryGetValue(d.ItemId, out var m);
                var display = ProductNameComposer.Compose(
                    m?.ProductName, m?.ItemName,
                    m?.Variant1Name, m?.Variant1Value,
                    m?.Variant2Name, m?.Variant2Value);

                VM.Lines.Add(new LineVM
                {
                    OriginalLineId = d.OriginalLineId,
                    ItemId = d.ItemId,
                    DisplayName = display,
                    Sku = m?.Sku ?? "",
                    UnitCost = d.UnitCost,
                    Discount = 0m,
                    TaxRate = 0m,
                    MaxReturnQty = d.MaxReturnQty,
                    ReturnQty = d.ReturnQty
                });
            }

            VM.RecomputeTotals();
        }

        private async Task InitFreeFormAsync()
        {
            VM.IsSupplierReadonly = false;
            VM.SupplierPickerVisibility = Visibility.Visible;
            VM.BasePurchaseDisplay = "—";

            // Prefer outlet from AppState
            VM.TargetType = StockTargetType.Outlet;
            VM.OutletId = AppState.Current?.CurrentOutletId > 0 ? AppState.Current.CurrentOutletId : (int?)null;
            VM.WarehouseId = null;

            VM.Lines.Clear();
        }

        private async Task InitFromExistingReturnAsync(int returnId)
        {
            using var db = new PosClientDbContext(_opts);
            var ret = await db.Purchases
                .Include(p => p.Party)
                .Include(p => p.Lines)
                .FirstAsync(p => p.Id == returnId && p.IsReturn);

            VM.IsSupplierReadonly = true;
            VM.SupplierId = ret.PartyId;
            VM.SupplierDisplay = ret.Party?.Name ?? $"Supplier #{ret.PartyId}";
            VM.TargetType = ret.TargetType;
            VM.OutletId = ret.OutletId;
            VM.WarehouseId = ret.WarehouseId;
            VM.RefPurchaseId = ret.RefPurchaseId;
            VM.ReturnNoDisplay = string.IsNullOrWhiteSpace(ret.DocNo) ? $"#{ret.Id}" : ret.DocNo;
            VM.BasePurchaseDisplay = ret.RefPurchaseId is int rid ? $"#{rid}" : "—";

            var itemIds = ret.Lines.Select(l => l.ItemId).Distinct().ToList();
            var meta = (
                from i in db.Items.AsNoTracking()
                join pr in db.Products.AsNoTracking() on i.ProductId equals pr.Id into gp
                from pr in gp.DefaultIfEmpty()
                where itemIds.Contains(i.Id)
                select new
                {
                    i.Id,
                    ItemName = i.Name,
                    ProductName = pr != null ? pr.Name : null,
                    i.Variant1Name,
                    i.Variant1Value,
                    i.Variant2Name,
                    i.Variant2Value,
                    i.Sku
                }
            ).ToList().ToDictionary(x => x.Id);

            VM.Lines.Clear();
            foreach (var l in ret.Lines)
            {
                meta.TryGetValue(l.ItemId, out var m);
                var display = ProductNameComposer.Compose(
                    m?.ProductName, m?.ItemName,
                    m?.Variant1Name, m?.Variant1Value,
                    m?.Variant2Name, m?.Variant2Value);

                VM.Lines.Add(new LineVM
                {
                    OriginalLineId = l.RefPurchaseLineId,
                    ItemId = l.ItemId,
                    DisplayName = display,
                    Sku = m?.Sku ?? "",
                    UnitCost = l.UnitCost,
                    Discount = l.Discount,
                    TaxRate = l.TaxRate,
                    ReturnQty = Math.Abs(l.Qty),
                    MaxReturnQty = 999999m
                });
            }

            VM.OtherCharges = ret.OtherCharges;
            VM.RecomputeTotals();
        }

        // ===== UI handlers =====
        private void GridLines_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row?.Item is LineVM vm)
            {
                vm.ClampQty();
                vm.RecomputeLineTotal();
                VM.RecomputeTotals();
            }
        }

        private async void BtnPickSupplier_Click(object sender, RoutedEventArgs e)
        {
            var term = Microsoft.VisualBasic.Interaction.InputBox("Search supplier name:", "Find Supplier", "");
            if (string.IsNullOrWhiteSpace(term)) return;

            var outletId = AppState.Current?.CurrentOutletId ?? 0;
            var list = await _partySvc.SearchSuppliersAsync(term, outletId);
            var first = list.FirstOrDefault();
            if (first == null)
            {
                MessageBox.Show("No supplier found.");
                return;
            }

            VM.SupplierId = first.Id;
            VM.SupplierDisplay = first.Name;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (VM.SupplierId <= 0)
                {
                    MessageBox.Show("Select a supplier.");
                    return;
                }

                if (VM.Lines.Count == 0 || VM.Lines.All(l => l.ReturnQty <= 0))
                {
                    MessageBox.Show("Add at least one line with Return Qty > 0.");
                    return;
                }

                // Build model
                var model = new Purchase
                {
                    Id = _returnId ?? 0,                     // 0 for new
                    IsReturn = true,
                    RefPurchaseId = _refPurchaseId ?? VM.RefPurchaseId,
                    PartyId = VM.SupplierId,
                    TargetType = VM.TargetType,
                    OutletId = VM.TargetType == StockTargetType.Outlet ? VM.OutletId : null,
                    WarehouseId = VM.TargetType == StockTargetType.Warehouse ? VM.WarehouseId : null,
                    PurchaseDate = DateTime.UtcNow,
                    ReceivedAtUtc = DateTime.UtcNow,

                    OtherCharges = Math.Round(VM.OtherCharges, 2),
                    Subtotal = VM.Subtotal,
                    Discount = VM.Discount,
                    Tax = VM.Tax,
                    GrandTotal = VM.GrandTotal
                };

                // Lines (service will enforce negative qty and compute)
                var lines = VM.Lines
                    .Where(l => l.ReturnQty > 0.0001m)
                    .Select(l => new PurchaseLine
                    {
                        ItemId = l.ItemId,
                        Qty = -Math.Abs(l.ReturnQty),          // returns are negative
                        UnitCost = Math.Max(0, l.UnitCost),
                        Discount = Math.Max(0, l.Discount),
                        TaxRate = Math.Max(0, l.TaxRate),
                        RefPurchaseLineId = l.OriginalLineId
                    })
                    .ToList();

                // Build refunds list if operator entered one
                var refunds = new System.Collections.Generic.List<SupplierRefundSpec>();
                if (VM.RefundAmount > 0m)
                {
                    refunds.Add(new SupplierRefundSpec(VM.RefundMethod, VM.RefundAmount, "Refund on return"));
                }

                var user = AppState.Current?.CurrentUserName ?? "system";

                await _svc.SaveReturnAsync(
                    model,
                    lines,
                    user,
                    refunds,
                    tillSessionId: AppState.Current?.CurrentTillSessionId,
                    counterId: AppState.Current?.CurrentCounterId
                );

                MessageBox.Show("Purchase Return saved.");
                this.DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save return: " + ex.Message);
            }
        }

        // ===== View Models =====
        public class ReturnVM : INotifyPropertyChanged
        {
            // Header
            private int _supplierId;
            private string _supplierDisplay = "";
            private StockTargetType _targetType = StockTargetType.Outlet;
            private int? _outletId;
            private int? _warehouseId;
            private int? _refPurchaseId;

            private bool _isSupplierReadonly;
            private Visibility _supplierPickerVisibility = Visibility.Collapsed;

            private string _returnNoDisplay = "Auto";
            private string _basePurchaseDisplay = "—";
            private DateTime _date = DateTime.Now;

            // Totals
            private decimal _subtotal;
            private decimal _discount;
            private decimal _tax;
            private decimal _other = 0m;
            private decimal _grand;

            // Refund UI (NEW)
            public ObservableCollection<TenderMethod> RefundMethods { get; } =
                new ObservableCollection<TenderMethod>(Enum.GetValues(typeof(TenderMethod)).Cast<TenderMethod>());

            private TenderMethod _refundMethod = TenderMethod.Cash;
            public TenderMethod RefundMethod { get => _refundMethod; set { _refundMethod = value; OnChanged(); } }

            private decimal _refundAmount = 0m;
            public decimal RefundAmount
            {
                get => _refundAmount;
                set { _refundAmount = Math.Max(0, value); OnChanged(); }
            }

            public ObservableCollection<LineVM> Lines { get; } = new();

            public int SupplierId { get => _supplierId; set { _supplierId = value; OnChanged(); } }
            public string SupplierDisplay { get => _supplierDisplay; set { _supplierDisplay = value; OnChanged(); } }

            public bool IsSupplierReadonly { get => _isSupplierReadonly; set { _isSupplierReadonly = value; OnChanged(); } }
            public Visibility SupplierPickerVisibility { get => _supplierPickerVisibility; set { _supplierPickerVisibility = value; OnChanged(); } }

            public StockTargetType TargetType { get => _targetType; set { _targetType = value; OnChanged(); OnChanged(nameof(TargetDisplay)); } }
            public int? OutletId { get => _outletId; set { _outletId = value; OnChanged(); OnChanged(nameof(TargetDisplay)); } }
            public int? WarehouseId { get => _warehouseId; set { _warehouseId = value; OnChanged(); OnChanged(nameof(TargetDisplay)); } }
            public int? RefPurchaseId { get => _refPurchaseId; set { _refPurchaseId = value; OnChanged(); } }

            public string ReturnNoDisplay { get => _returnNoDisplay; set { _returnNoDisplay = value; OnChanged(); } }
            public string BasePurchaseDisplay { get => _basePurchaseDisplay; set { _basePurchaseDisplay = value; OnChanged(); } }

            public string TargetDisplay =>
                TargetType == StockTargetType.Outlet
                    ? (OutletId is int o ? $"Outlet #{o}" : "Outlet —")
                    : (WarehouseId is int w ? $"Warehouse #{w}" : "Warehouse —");

            public string DateDisplay => _date.ToString("dd-MMM-yyyy HH:mm");

            public decimal Subtotal { get => _subtotal; set { _subtotal = value; OnChanged(); } }
            public decimal Discount { get => _discount; set { _discount = value; OnChanged(); } }
            public decimal Tax { get => _tax; set { _tax = value; OnChanged(); } }
            public decimal OtherCharges { get => _other; set { _other = value; OnChanged(); RecomputeTotals(); } }
            public decimal GrandTotal { get => _grand; set { _grand = value; OnChanged(); } }

            public void RecomputeTotals()
            {
                var subtotal = Lines.Sum(x => Math.Abs(x.ReturnQty) * Math.Max(0, x.UnitCost));
                var discount = Lines.Sum(x => Math.Max(0, x.Discount));
                var tax = Lines.Sum(x =>
                {
                    var taxable = Math.Max(0, Math.Abs(x.ReturnQty) * Math.Max(0, x.UnitCost) - Math.Max(0, x.Discount));
                    return Math.Round(taxable * (Math.Max(0, x.TaxRate) / 100m), 2);
                });

                Subtotal = Math.Round(subtotal, 2);
                Discount = Math.Round(discount, 2);
                Tax = Math.Round(tax, 2);
                GrandTotal = Math.Round(Subtotal - Discount + Tax + OtherCharges, 2);

                foreach (var l in Lines) l.RecomputeLineTotal();
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class LineVM : INotifyPropertyChanged
        {
            public int? OriginalLineId { get; set; }
            public int ItemId { get; set; }
            public string DisplayName { get; set; } = "";
            public string Sku { get; set; } = "";

            private decimal _unitCost;
            private decimal _discount;
            private decimal _taxRate;
            private decimal _maxReturnQty;
            private decimal _returnQty;
            private decimal _lineTotal;

            public decimal UnitCost { get => _unitCost; set { _unitCost = Math.Max(0, value); OnChanged(); RecomputeLineTotal(); } }
            public decimal Discount { get => _discount; set { _discount = Math.Max(0, value); OnChanged(); RecomputeLineTotal(); } }
            public decimal TaxRate { get => _taxRate; set { _taxRate = Math.Max(0, value); OnChanged(); RecomputeLineTotal(); } }
            public decimal MaxReturnQty { get => _maxReturnQty; set { _maxReturnQty = Math.Max(0, value); OnChanged(); } }
            public decimal ReturnQty { get => _returnQty; set { _returnQty = Math.Max(0, value); ClampQty(); OnChanged(); RecomputeLineTotal(); } }
            public decimal LineTotal { get => _lineTotal; private set { _lineTotal = value; OnChanged(); } }

            public void ClampQty()
            {
                if (MaxReturnQty > 0 && ReturnQty > MaxReturnQty)
                    _returnQty = MaxReturnQty;
                if (_returnQty < 0) _returnQty = 0;
            }

            public void RecomputeLineTotal()
            {
                var qtyAbs = Math.Abs(ReturnQty);
                var baseAmt = qtyAbs * Math.Max(0, UnitCost);
                var taxable = Math.Max(0, baseAmt - Math.Max(0, Discount));
                var tax = Math.Round(taxable * (Math.Max(0, TaxRate) / 100m), 2);
                LineTotal = Math.Round(taxable + tax, 2);
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

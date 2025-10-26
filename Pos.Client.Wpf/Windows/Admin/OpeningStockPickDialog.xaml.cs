using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class OpeningStockPickDialog : Window
    {
        public enum Mode { Drafts, Locked }

        private readonly IDbContextFactory<Pos.Persistence.PosClientDbContext> _dbf;
        private readonly InventoryLocationType _locType;
        private readonly int _locId;
        private readonly Mode _mode;

        public string HeaderText { get; set; } = "";
        public ObservableCollection<Row> Docs { get; } = new();
        public int? SelectedDocId { get; private set; }

        public sealed class Row
        {
            public int Id { get; set; }
            public System.DateTime EffectiveDateUtc { get; set; }
            public int LineCount { get; set; }
            public decimal TotalQty { get; set; }
            public decimal TotalValue { get; set; }
            public string? Note { get; set; }
            public StockDocStatus Status { get; set; }            // new
            public string StatusText => Status.ToString();        // optional helper
        }

        public OpeningStockPickDialog(IDbContextFactory<Pos.Persistence.PosClientDbContext> dbf,
            InventoryLocationType locType, int locId, Mode mode)
        {
            InitializeComponent();
            DataContext = this;
            _dbf = dbf;
            _locType = locType;
            _locId = locId;
            _mode = mode;
            HeaderText = mode == Mode.Drafts ? "Select a draft to open" : "Select a locked document to clone";
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await using var db = await _dbf.CreateDbContextAsync();

            var status = _mode == Mode.Drafts ? StockDocStatus.Draft : StockDocStatus.Locked;

            // Load docs for this location
            //var docs = await db.StockDocs
            //    .Where(d => d.DocType == StockDocType.Opening
            //             && d.Status == status
            //             && d.LocationType == _locType
            //             && d.LocationId == _locId)
            //    .OrderByDescending(d => d.Id)
            //    .ToListAsync();
            var docs = await db.StockDocs.AsNoTracking()
                .Where(d => d.DocType == StockDocType.Opening
                         && d.LocationType == _locType
                         && d.LocationId == _locId)
                .OrderByDescending(d => d.Id)
                .ToListAsync();

            if (docs.Count == 0) return;

            var docIds = docs.Select(d => d.Id).ToList();

            // Aggregate lines per doc
            var lines = await db.StockEntries.AsNoTracking()
                .Where(se => se.StockDocId.HasValue && docIds.Contains(se.StockDocId.Value))
                .GroupBy(se => se.StockDocId)
                .Select(g => new
                {
                    DocId = g.Key,
                    Count = g.Count(),
                    Qty = g.Sum(x => x.QtyChange),
                    Value = g.Sum(x => x.QtyChange * x.UnitCost)
                }).ToListAsync();

            var map = lines.ToDictionary(x => x.DocId, x => x);

            Docs.Clear();
            foreach (var d in docs)
            {
                map.TryGetValue(d.Id, out var agg);
                Docs.Add(new Row
                {
                    Id = d.Id,
                    EffectiveDateUtc = d.EffectiveDateUtc,
                    LineCount = agg?.Count ?? 0,
                    TotalQty = agg?.Qty ?? 0m,
                    TotalValue = agg?.Value ?? 0m,
                    Note = d.Note,
                    Status = d.Status
                });
            }
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is Row r)
            {
                SelectedDocId = r.Id;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Select a document.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}

using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
//using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
//using Pos.Persistence;
using Pos.Persistence.Features.OpeningStock;
using Pos.Persistence.Services;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class OpeningStockPickDialog : Window
    {
        public enum Mode { Drafts, Locked }

        //private readonly IDbContextFactory<Pos.Persistence.PosClientDbContext> _dbf;
        private readonly IOpeningStockService _svc;
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

        public OpeningStockPickDialog(IOpeningStockService svc,
            InventoryLocationType locType, int locId, Mode mode)
        {
            InitializeComponent();
            DataContext = this;
            //_dbf = dbf;
            _svc = svc;
            _locType = locType;
            _locId = locId;
            _mode = mode;
            HeaderText = mode == Mode.Drafts ? "Select a draft to open" : "Select a locked document to clone";
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                StockDocStatus? filter = _mode switch
                {
                    Mode.Drafts => StockDocStatus.Draft,
                    Mode.Locked => StockDocStatus.Locked,
                    _ => null
                };

                var list = await _svc.GetOpeningDocSummariesAsync(_locType, _locId, filter);

                Docs.Clear();
                foreach (var d in list)
                {
                    Docs.Add(new Row
                    {
                        Id = d.Id,
                        EffectiveDateUtc = d.EffectiveDateUtc,
                        LineCount = d.LineCount,
                        TotalQty = d.TotalQty,
                        TotalValue = d.TotalValue,
                        Note = d.Note,
                        Status = d.Status
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Load error", MessageBoxButton.OK, MessageBoxImage.Error);
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

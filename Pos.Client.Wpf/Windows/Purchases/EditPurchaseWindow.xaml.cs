using System.Windows;
using static Pos.Client.Wpf.Windows.Purchases.PurchaseView;

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class EditPurchaseWindow : Window
    {
        public bool Confirmed { get; private set; }
        public int NewRevision { get; private set; }
        public EditPurchaseWindow(int purchaseId)
        {
            InitializeComponent();
            Loaded += (_, __) =>
            {
                Editor.PurchaseId = purchaseId;
                Editor.Mode = PurchaseEditorMode.Amend;
            };
            Closed += (_, __) => Confirmed = true;
        }
    }
}

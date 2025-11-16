using System.Windows;

namespace Pos.Client.Wpf.Windows.Inventory
{
    public partial class EditTransferWindow : Window
    {
        public bool Confirmed { get; private set; }

        public EditTransferWindow(int transferId)
        {
            InitializeComponent();

            Loaded += async (_, __) =>
            {
                // method we’ll add to TransferEditorView in step 3
                await Editor.LoadTransferAsync(transferId);
            };

            Closed += (_, __) => Confirmed = true;
        }
    }
}

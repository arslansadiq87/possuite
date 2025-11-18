using System.Windows;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class AccountEditorDialog : Window
    {
        private readonly ICoaService _coa;
        private readonly int _parentId;
        private readonly int? _editId; // null = create

        public string TitleText => _editId == null ? "New Account/Header" : "Edit Account/Header";

        public string ParentPath { get; set; } = "";
        public string AccountName { get; set; } = "";
        public bool IsHeader { get; set; }

        public AccountEditorDialog(ICoaService coa, int parentId, string parentPath, int? editId = null, string? currentName = null, bool isHeader = false)
        {
            InitializeComponent();
            _coa = coa;
            _parentId = parentId;
            _editId = editId;

            ParentPath = parentPath;
            AccountName = currentName ?? "";
            IsHeader = isHeader;

            DataContext = this;
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AccountName))
            {
                MessageBox.Show("Name is required.");
                return;
            }

            try
            {
                if (_editId is null)
                {
                    if (IsHeader)
                        await _coa.CreateHeaderAsync(_parentId, AccountName.Trim());
                    else
                        await _coa.CreateAccountAsync(_parentId, AccountName.Trim());
                }
                else
                {
                    // AllowPosting = !IsHeader
                    await _coa.EditAsync(new Pos.Domain.Models.Accounting.AccountEdit(
                        _editId.Value,      // accountId
                        "",                 // code (unchanged)
                        AccountName.Trim(), // name
                        IsHeader,           // isHeader
                        !IsHeader           // allowPosting
                    ));

                }

                DialogResult = true;
                Close();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, "Save", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

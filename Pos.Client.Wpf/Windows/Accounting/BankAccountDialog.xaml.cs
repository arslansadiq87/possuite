using System.Windows;
using Pos.Domain.Models;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class BankAccountDialog : Window
    {
        private readonly IBankAccountService _svc;
        private readonly int _bankHeaderId;
        private readonly int? _bankAccountId;
        private readonly int? _accountId;

        public string TitleText => _bankAccountId == null ? "New Bank Account" : "Edit Bank Account";
        public string ParentPath { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string BankName { get; set; } = "";
        public string? Branch { get; set; }
        public string? AccountNumber { get; set; }
        public string? IBAN { get; set; }
        public string? SwiftBic { get; set; }
        public string? Notes { get; set; }
        public bool IsActiveFlag { get; set; } = true;


        // Create mode
        public BankAccountDialog(IBankAccountService svc, int bankHeaderId, string parentPath)
        {
            InitializeComponent();
            _svc = svc;
            _bankHeaderId = bankHeaderId;
            ParentPath = parentPath;
            DataContext = this;
        }

        // Edit mode
        public BankAccountDialog(IBankAccountService svc, int bankAccountId, int accountId, string parentPath,
                                 string code, string name)
        {
            InitializeComponent();
            _svc = svc;
            _bankAccountId = bankAccountId;
            _accountId = accountId;
            ParentPath = parentPath;
            AccountName = name;   // was Name = name;
            DataContext = this;
            _ = LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            if (_bankAccountId is null) return;
            var row = await _svc.GetByIdAsync(_bankAccountId.Value);
            if (row == null) return;

            BankName = row.BankName;
            Branch = row.Branch;
            AccountNumber = row.AccountNumber;
            IBAN = row.IBAN;
            SwiftBic = row.SwiftBic;
            Notes = row.Notes;
            IsActiveFlag = row.IsActive;

            DataContext = null; DataContext = this;
        }


        private async void OnSave(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AccountName) || string.IsNullOrWhiteSpace(BankName))
            {
                MessageBox.Show("Account Name and Bank are required.");
                return;
            }

            try
            {
                if (_bankAccountId is null)
                {
                    var dto = new BankAccountUpsertDto(
                        Id: null,
                        AccountId: null,
                        Name: AccountName.Trim(),       // was Name
                        BankName: BankName.Trim(),
                        Branch: Branch?.Trim(),
                        AccountNumber: AccountNumber?.Trim(),
                        IBAN: IBAN?.Trim(),
                        SwiftBic: SwiftBic?.Trim(),
                        Notes: Notes?.Trim(),
                        IsActive: IsActiveFlag          // was IsActive
                    );
                    await _svc.CreateAsync(_bankHeaderId, dto);
                }
                else
                {
                    var dto = new BankAccountUpsertDto(
                        Id: _bankAccountId.Value,
                        AccountId: _accountId!.Value,
                        Name: AccountName.Trim(),
                        BankName: BankName.Trim(),
                        Branch: Branch?.Trim(),
                        AccountNumber: AccountNumber?.Trim(),
                        IBAN: IBAN?.Trim(),
                        SwiftBic: SwiftBic?.Trim(),
                        Notes: Notes?.Trim(),
                        IsActive: IsActiveFlag
                    );
                    await _svc.UpdateAsync(dto);
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

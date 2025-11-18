using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Windows.Accounting;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class VoucherCenterView : UserControl
    {
        public VoucherCenterView()
        {
            InitializeComponent();
            DataContext = App.Services.GetRequiredService<VoucherCenterVm>();

            Loaded += async (_, __) =>
            {
                // initialize date pickers with VM values and do first load
                if (DataContext is VoucherCenterVm vm)
                {
                    FromDate.SelectedDate = vm.StartDate;
                    ToDate.SelectedDate = vm.EndDate;
                    await vm.RefreshCommand.ExecuteAsync(null);
                }
            };
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is VoucherCenterVm vm) await vm.RefreshCommand.ExecuteAsync(null);
        }
        private async void Amend_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is VoucherCenterVm vm) await vm.AmendCommand.ExecuteAsync(null);
        }
        private async void Void_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is VoucherCenterVm vm) await vm.VoidCommand.ExecuteAsync(null);
        }

        // CommandBindings
        private async void Refresh_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) => await Refresh_ClickAsync();
        private async void Search_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) => await ApplyFilter_ClickAsync();
        private void Amend_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        => Amend_Click(sender, (RoutedEventArgs)e);

        private void Void_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
            => Void_Click(sender, (RoutedEventArgs)e);

        private async Task Refresh_ClickAsync()
        {
            if (DataContext is VoucherCenterVm vm) await vm.RefreshCommand.ExecuteAsync(null);
        }

        private async void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            ChkJournal.IsChecked = ChkDebit.IsChecked = ChkCredit.IsChecked = true;
            ChkPosted.IsChecked = ChkDraft.IsChecked = ChkAmended.IsChecked = true;
            ChkVoided.IsChecked = false;

            if (DataContext is VoucherCenterVm vm)
            {
                FromDate.SelectedDate = vm.StartDate = DateTime.Today.AddDays(-30);
                ToDate.SelectedDate = vm.EndDate = DateTime.Today.AddDays(1).AddSeconds(-1);
                vm.TypeFilter = null;
                vm.StatusFilter = null;
                await vm.RefreshCommand.ExecuteAsync(null);
            }
        }

        private async void ApplyFilter_Click(object sender, RoutedEventArgs e) => await ApplyFilter_ClickAsync();

        private async Task ApplyFilter_ClickAsync()
        {
            if (DataContext is not VoucherCenterVm vm) return;

            vm.StartDate = FromDate.SelectedDate ?? DateTime.Today.AddDays(-30);
            vm.EndDate = (ToDate.SelectedDate ?? DateTime.Today).Date.AddDays(1).AddSeconds(-1);

            var types = new System.Collections.Generic.List<Pos.Domain.Accounting.VoucherType>();
            if (ChkJournal.IsChecked == true) types.Add(Pos.Domain.Accounting.VoucherType.Journal);
            if (ChkDebit.IsChecked == true) types.Add(Pos.Domain.Accounting.VoucherType.Payment);
            if (ChkCredit.IsChecked == true) types.Add(Pos.Domain.Accounting.VoucherType.Receipt);
            vm.TypeMulti = types;   // see VM addition below

            var statuses = new System.Collections.Generic.List<Pos.Domain.Accounting.VoucherStatus>();
            if (ChkPosted.IsChecked == true) statuses.Add(Pos.Domain.Accounting.VoucherStatus.Posted);
            if (ChkDraft.IsChecked == true) statuses.Add(Pos.Domain.Accounting.VoucherStatus.Draft);
            if (ChkAmended.IsChecked == true) statuses.Add(Pos.Domain.Accounting.VoucherStatus.Amended);
            if (ChkVoided.IsChecked == true) statuses.Add(Pos.Domain.Accounting.VoucherStatus.Voided);
            vm.StatusMulti = statuses;

            vm.SearchText = (SearchBox.Text ?? "").Trim();

            await vm.RefreshCommand.ExecuteAsync(null);
        }

        private async void Search_Click(object sender, RoutedEventArgs e) => await ApplyFilter_ClickAsync();

        private async void VouchersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not VoucherCenterVm vm) return;

            if (VouchersGrid.SelectedItem is VoucherRow row && !ReferenceEquals(vm.Selected, row))
                vm.Selected = row;

            var sel = vm.Selected;
            if (sel != null)
            {
                HeaderText.Text = $"Voucher #{sel.Id} • {sel.Type} • {sel.Status}";
                await vm.LoadLinesAsync(sel.Id);   // LinesGrid is bound to vm.Lines
            }
            else
            {
                HeaderText.Text = "";
            }
        }
    }
}

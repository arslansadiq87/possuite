using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Purchases
{
    // Public, top-level commands so XAML can resolve {x:Static local:PurchaseCenterWindowCommands.*}
    public static class PurchaseCenterWindowCommands
    {
        public static readonly RoutedUICommand Amend =
            new RoutedUICommand("Amend", nameof(Amend), typeof(PurchaseCenterWindowCommands),
                new InputGestureCollection { new KeyGesture(Key.E, ModifierKeys.Control) });

        public static readonly RoutedUICommand ReturnWith =
            new RoutedUICommand("Return With", nameof(ReturnWith), typeof(PurchaseCenterWindowCommands),
                new InputGestureCollection { new KeyGesture(Key.R, ModifierKeys.Control) });

        public static readonly RoutedUICommand AmendReturn =
            new RoutedUICommand("Amend Return", nameof(AmendReturn), typeof(PurchaseCenterWindowCommands));

        public static readonly RoutedUICommand VoidReturn =
            new RoutedUICommand("Void Return", nameof(VoidReturn), typeof(PurchaseCenterWindowCommands));

        public static readonly RoutedUICommand Refresh =
            new RoutedUICommand("Refresh", nameof(Refresh), typeof(PurchaseCenterWindowCommands),
                new InputGestureCollection { new KeyGesture(Key.F5) });

        public static readonly RoutedUICommand Export =
            new RoutedUICommand("Export", nameof(Export), typeof(PurchaseCenterWindowCommands));
    }

    public partial class PurchaseCenterWindow : Window
    {
        private readonly DbContextOptions<PosClientDbContext> _dbOptions;
        private readonly ObservableCollection<PurchaseRowVM> _rows = new();


        // Row VM - mirror your Sales VM shape
        public class PurchaseRowVM
        {
            public string Number { get; set; } = "";
            public DateTime Date { get; set; }
            public string SupplierName { get; set; } = "";
            public string Destination { get; set; } = ""; // Warehouse or "Outlet: Gulberg"
            public int ItemCount { get; set; }
            public decimal TotalQty { get; set; }
            public decimal GrandTotal { get; set; }
            public string Status { get; set; } = "";
            public string CreatedBy { get; set; } = "";
        }

        private readonly List<PurchaseRowVM> _all = new(); // replace with real DB results

        public PurchaseCenterWindow()
        {
            InitializeComponent();

            // Command bindings
            CommandBindings.Add(new CommandBinding(PurchaseCenterWindowCommands.Amend, Amend_Executed, RequireSelection_CanExecute));
            CommandBindings.Add(new CommandBinding(PurchaseCenterWindowCommands.ReturnWith, ReturnWith_Executed, RequireSelection_CanExecute));
            CommandBindings.Add(new CommandBinding(PurchaseCenterWindowCommands.AmendReturn, AmendReturn_Executed, RequireSelection_CanExecute));
            CommandBindings.Add(new CommandBinding(PurchaseCenterWindowCommands.VoidReturn, VoidReturn_Executed, RequireSelection_CanExecute));
            CommandBindings.Add(new CommandBinding(PurchaseCenterWindowCommands.Refresh, Refresh_Executed, Always_CanExecute));
            CommandBindings.Add(new CommandBinding(PurchaseCenterWindowCommands.Export, Export_Executed, Always_CanExecute));

            // Demo data so the UI renders; replace with actual load
            SeedDemo();
            BindResults(_all);
        }

        // ===== Command handlers =====

        private void Always_CanExecute(object? sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

        private void RequireSelection_CanExecute(object? sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ResultsGrid?.SelectedItem is PurchaseRowVM;
        }

        private void Amend_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PurchaseRowVM row) return;
            // TODO: open PurchaseWindow in amend mode (like Sales → InvoiceCenterWindow flow)
            MessageBox.Show($"Amend purchase {row.Number}", "Amend");
        }

        private void ReturnWith_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PurchaseRowVM row) return;
            // TODO: open Return With workflow
            MessageBox.Show($"Return with purchase {row.Number}", "Return With");
        }

        private void AmendReturn_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PurchaseRowVM row) return;
            // TODO: open Amend Return window
            MessageBox.Show($"Amend return for {row.Number}", "Amend Return");
        }

        private void VoidReturn_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PurchaseRowVM row) return;
            // TODO: permissions/confirm + void logic
            MessageBox.Show($"Void return for {row.Number}", "Void Return");
        }

        private void Refresh_Executed(object sender, ExecutedRoutedEventArgs e) => RunSearch();

        private void Export_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            // TODO: export ResultsGrid.ItemsSource to CSV/Excel
            MessageBox.Show("Export not implemented yet.", "Export");
        }

        // ===== UI events =====

        private void SupplierPicker_Click(object sender, RoutedEventArgs e)
        {
            // TODO: open supplier picker dialog and set SupplierSearchBox.Text
            MessageBox.Show("Supplier picker not implemented yet.", "Supplier");
        }

        private void SearchBtn_Click(object sender, RoutedEventArgs e) => RunSearch();

   


        // ===== Filtering & Binding =====

        private void RunSearch()
        {
            var q = SupplierSearchBox.Text?.Trim() ?? "";
            var no = PurchaseNoBox.Text?.Trim() ?? "";
            var from = FromDateBox.SelectedDate;
            var to = ToDateBox.SelectedDate?.Date.AddDays(1).AddTicks(-1);
            var status = (StatusBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Any";

            var filtered = _all.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(q))
                filtered = filtered.Where(r => r.SupplierName.Contains(q, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(no))
                filtered = filtered.Where(r => r.Number.Contains(no, StringComparison.OrdinalIgnoreCase));

            if (from.HasValue)
                filtered = filtered.Where(r => r.Date >= from.Value);

            if (to.HasValue)
                filtered = filtered.Where(r => r.Date <= to.Value);

            if (!string.Equals(status, "Any", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));

            BindResults(filtered.ToList());
        }

        private void BindResults(IList<PurchaseRowVM> rows)
        {
            ResultsGrid.ItemsSource = rows;
            CountLabel.Text = $"{rows.Count} records";

            var totalQty = rows.Sum(r => r.TotalQty);
            var grand = rows.Sum(r => r.GrandTotal);

            TotalQtyLabel.Text = totalQty.ToString("N2");
            GrandTotalLabel.Text = grand.ToString("N2");
        }

        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsGrid.SelectedItem is PurchaseRowVM row) HandleAmend(row);
        }

        private void HandleAmend(PurchaseRowVM row)
        {
            // TODO: open PurchaseEditorWindow for row.Number
            MessageBox.Show($"Amend purchase {row.Number}", "Amend");
        }

        private PurchaseRowVM? SelectedRow => ResultsGrid?.SelectedItem as PurchaseRowVM;

        private static bool IsStatus(string? s, params string[] values)
    => values.Any(v => string.Equals(s, v, StringComparison.OrdinalIgnoreCase));


        // === CmdVoid ===
        private void CmdVoid_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = SelectedRow != null && IsStatus(SelectedRow.Status, "Draft");
        }
        private void CmdVoid_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (SelectedRow == null) return;

            if (MessageBox.Show($"Void draft {SelectedRow.Number}?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // TODO: mark void (audit/log)
                RunSearch();
            }
        }


        // === CmdReceive ===
        private void CmdReceive_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = SelectedRow != null && IsStatus(SelectedRow.Status, "Draft");
        }
        private void CmdReceive_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (SelectedRow == null) return;

            // TODO: finalize/receive + stock IN
            MessageBox.Show($"Receive purchase {SelectedRow.Number}", "Receive", MessageBoxButton.OK, MessageBoxImage.Information);

            RunSearch();
        }


        // === CmdReturn ===
        private void CmdReturn_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = SelectedRow != null && IsStatus(SelectedRow.Status, "Received");
        }
        private void CmdReturn_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (SelectedRow == null) return;

            // TODO: open Purchase Return flow
            MessageBox.Show($"Return against {SelectedRow.Number}", "Return", MessageBoxButton.OK, MessageBoxImage.Information);

            RunSearch();
        }



        private void SeedDemo()
        {
            _all.Clear();
            _all.AddRange(new[]
            {
                new PurchaseRowVM { Number="PO-000145", Date=DateTime.Today.AddDays(-1), SupplierName="Al-Madina Traders",
                    Destination="Warehouse", ItemCount=12, TotalQty=36, GrandTotal=154320.00m, Status="Received", CreatedBy="ahmad" },
                new PurchaseRowVM { Number="PO-000146", Date=DateTime.Today, SupplierName="Nizam Sons",
                    Destination="Outlet: Gulberg", ItemCount=4, TotalQty=9, GrandTotal=32450.00m, Status="Draft", CreatedBy="sana" },
                new PurchaseRowVM { Number="PR-000147", Date=DateTime.Today.AddDays(-3), SupplierName="Imtiaz Supplies",
                    Destination="Warehouse", ItemCount=2, TotalQty=2, GrandTotal=-5200.00m, Status="Return", CreatedBy="admin" },
            });
        }
    }
}

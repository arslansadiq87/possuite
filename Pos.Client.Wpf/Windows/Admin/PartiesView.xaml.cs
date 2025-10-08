using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class PartiesView : UserControl
    {
        private IDbContextFactory<PosClientDbContext>? _dbf;
        private Func<EditPartyWindow>? _editFactory;
        private readonly bool _design;

        public class PartyRowVM
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string? Phone { get; set; }
            public string? Email { get; set; }
            public string? TaxNumber { get; set; }
            public bool IsActive { get; set; }
            public bool IsSharedAcrossOutlets { get; set; }
            public string RolesText { get; set; } = "";
        }

        private ObservableCollection<PartyRowVM> _rows = new();

        public PartiesView()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;

            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            _editFactory = () => App.Services.GetRequiredService<EditPartyWindow>();

            Loaded += (_, __) => RefreshRows();
            Grid.ItemsSource = _rows;
        }

        private async void RefreshRows()
        {
            if (_design || _dbf is null) return;

            using var db = await _dbf.CreateDbContextAsync();
            var term = (SearchText.Text ?? "").Trim();
            var onlyActive = OnlyActiveCheck.IsChecked == true;
            var wantCust = RoleCustomerCheck.IsChecked == true;
            var wantSupp = RoleSupplierCheck.IsChecked == true;

            var baseQ = db.Parties
                .Include(p => p.Roles)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(term))
            {
                baseQ = baseQ.Where(p =>
                    p.Name.Contains(term) ||
                    (p.Phone != null && p.Phone.Contains(term)) ||
                    (p.Email != null && p.Email.Contains(term)) ||
                    (p.TaxNumber != null && p.TaxNumber.Contains(term)));
            }
            if (onlyActive) baseQ = baseQ.Where(p => p.IsActive);

            // Role filter
            if (wantCust ^ wantSupp) // exactly one is checked
            {
                var role = wantCust ? RoleType.Customer : RoleType.Supplier;
                baseQ = baseQ.Where(p => p.Roles.Any(r => r.Role == role));
            }

            var list = await baseQ
                .OrderBy(p => p.Name)
                .Select(p => new PartyRowVM
                {
                    Id = p.Id,
                    Name = p.Name,
                    Phone = p.Phone,
                    Email = p.Email,
                    TaxNumber = p.TaxNumber,
                    IsActive = p.IsActive,
                    IsSharedAcrossOutlets = p.IsSharedAcrossOutlets,
                    RolesText = string.Join(", ", p.Roles.Select(r => r.Role.ToString()))
                })
                .ToListAsync();

            _rows.Clear();
            foreach (var r in list) _rows.Add(r);
        }

        private void SearchText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshRows();
        private void FilterChanged(object sender, RoutedEventArgs e) => RefreshRows();

        private void New_Click(object sender, RoutedEventArgs e)
        {
            var w = _editFactory!();
            //w.Owner = this;
            if (w.ShowDialog() == true) RefreshRows();
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not PartyRowVM row) return;
            var w = _editFactory!();
            //w.Owner = this;
            w.LoadParty(row.Id);
            if (w.ShowDialog() == true) RefreshRows();
        }

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Edit_Click(sender, e);

        //private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            //if (e.Key == Key.Escape) Close();
            if (e.Key == Key.N && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) New_Click(sender, e);
            if (e.Key == Key.Enter) Edit_Click(sender, e);
        }
    }
}

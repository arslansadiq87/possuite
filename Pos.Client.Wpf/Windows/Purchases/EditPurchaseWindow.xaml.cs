using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Services;
using Microsoft.VisualBasic;
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

            // Minimal: mark confirmed on close; Center can simply refresh
            Closed += (_, __) => Confirmed = true;

            // If later you raise an event from PurchaseView after save, set NewRevision there.
        }
    }




}

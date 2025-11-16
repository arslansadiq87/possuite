using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
//using Pos.Domain.S;                // InventoryLocationType enum
using Pos.Domain.Services;
using Pos.Domain.Utils;
//using Pos.Domain.Services.Inventory;       // IInventoryReadService


namespace Pos.Client.Wpf.Controls
{
    public enum StockBadgeMode
    {
        AvailableForIssue = 0,  // respects staged qty & effective time
        OnHand = 1              // raw on-hand if you need it elsewhere
    }

    public partial class StockAvailableBadge : UserControl
    {
        private IInventoryReadService? _invRead;
        private CancellationTokenSource? _cts;

        public StockAvailableBadge()
        {
            InitializeComponent();
            TryResolveServices();
            Loaded += (_, __) => TriggerRefresh();
            Unloaded += (_, __) => CancelInFlight();
        }

        private void TryResolveServices()
        {
            try
            {
                // If App.Services is a static property, access it via the type — not the instance.
                var sp = App.Services; // static accessor

                // If you prefer being ultra explicit:
                // var sp = global::Pos.Client.Wpf.App.Services;

                _invRead = sp?.GetService<IInventoryReadService>();
            }
            catch
            {
                // Optional fallback for projects that ALSO expose an instance Services:
                // (kept only for portability if you reuse the control elsewhere)
                try
                {
                    _invRead = Services.Di.Get<IInventoryReadService>();
                }
                catch { /* ignore */ }
            }
        }


        #region Dependency Properties

        public int? ItemId
        {
            get => (int?)GetValue(ItemIdProperty);
            set => SetValue(ItemIdProperty, value);
        }
        public static readonly DependencyProperty ItemIdProperty =
            DependencyProperty.Register(nameof(ItemId), typeof(int?), typeof(StockAvailableBadge),
                new PropertyMetadata(null, OnAnyPropChanged));

        public InventoryLocationType? LocationType
        {
            get => (InventoryLocationType?)GetValue(LocationTypeProperty);
            set => SetValue(LocationTypeProperty, value);
        }
        public static readonly DependencyProperty LocationTypeProperty =
            DependencyProperty.Register(nameof(LocationType), typeof(InventoryLocationType?), typeof(StockAvailableBadge),
                new PropertyMetadata(null, OnAnyPropChanged));

        public int? LocationId
        {
            get => (int?)GetValue(LocationIdProperty);
            set => SetValue(LocationIdProperty, value);
        }
        public static readonly DependencyProperty LocationIdProperty =
            DependencyProperty.Register(nameof(LocationId), typeof(int?), typeof(StockAvailableBadge),
                new PropertyMetadata(null, OnAnyPropChanged));

        /// <summary>
        /// Local effective date used to compose the UTC cutoff (same rule you use in posting).
        /// If null, today is assumed.
        /// </summary>
        public DateTime? EffectiveDate
        {
            get => (DateTime?)GetValue(EffectiveDateProperty);
            set => SetValue(EffectiveDateProperty, value);
        }
        public static readonly DependencyProperty EffectiveDateProperty =
            DependencyProperty.Register(nameof(EffectiveDate), typeof(DateTime?), typeof(StockAvailableBadge),
                new PropertyMetadata(null, OnAnyPropChanged));

        /// <summary>
        /// Quantity staged on the current screen for THIS item (to be subtracted when showing "available").
        /// Optional; defaults to 0.
        /// </summary>
        public decimal StagedQty
        {
            get => (decimal)GetValue(StagedQtyProperty);
            set => SetValue(StagedQtyProperty, value);
        }
        public static readonly DependencyProperty StagedQtyProperty =
            DependencyProperty.Register(nameof(StagedQty), typeof(decimal), typeof(StockAvailableBadge),
                new PropertyMetadata(0m, OnAnyPropChanged));

        public StockBadgeMode Mode
        {
            get => (StockBadgeMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }
        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(nameof(Mode), typeof(StockBadgeMode), typeof(StockAvailableBadge),
                new PropertyMetadata(StockBadgeMode.AvailableForIssue, OnAnyPropChanged));

        /// <summary>
        /// Read-only: the computed quantity (null => em dash).
        /// </summary>
        public decimal? Quantity
        {
            get => (decimal?)GetValue(QuantityProperty);
            private set => SetValue(QuantityPropertyKey, value);
        }
        private static readonly DependencyPropertyKey QuantityPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(Quantity), typeof(decimal?), typeof(StockAvailableBadge),
                new PropertyMetadata(null));
        public static readonly DependencyProperty QuantityProperty = QuantityPropertyKey.DependencyProperty;

        private static void OnAnyPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((StockAvailableBadge)d).TriggerRefresh();

        #endregion

        private void CancelInFlight()
        {
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts = null;
        }

        private void SetBusy(bool busy)
        {
            BusyDot.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetLabel(decimal? value)
        {
            Quantity = value;
            Text.Text = value.HasValue ? $"Available: {value.Value:0.####}" : "Available: —";
        }

        private async void TriggerRefresh()
        {
            // debounce: cancel any pending and schedule a new run
            CancelInFlight();
            _cts = new CancellationTokenSource();

            await Task.Delay(120, _cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);
            if (_cts.IsCancellationRequested) return;

            await RefreshAsync(_cts.Token);
        }

        private async Task RefreshAsync(CancellationToken ct)
        {
            try
            {
                if (_invRead == null)
                {
                    TryResolveServices();
                    if (_invRead == null) { SetLabel(null); return; }
                }

                if (ItemId is null || LocationType is null || LocationId is null)
                {
                    SetLabel(null);
                    return;
                }

                SetBusy(true);

                var localDate = EffectiveDate ?? DateTime.Today;
                // Reuse your existing helper if available, else assume local midnight-now rule:
                // If you already have EffectiveTime.ComposeUtcFromDateAndNowTime(date), call it here.
                //var cutoffUtc = EffectiveTime.ComposeUtcFromDateAndNowTime(localDate);
                var cutoffUtc = EffectiveTime.ComposeUtcFromDateAndNowTime(localDate);

                decimal result;
                if (Mode == StockBadgeMode.AvailableForIssue)
                {
                    var available = await _invRead.GetAvailableForIssueAsync(
                        ItemId.Value, LocationType.Value, LocationId.Value, cutoffUtc, StagedQty, ct);
                    result = available;
                }
                else // OnHand
                {
                    var onHand = await _invRead.GetOnHandAsync(
                        ItemId.Value, LocationType.Value, LocationId.Value, cutoffUtc, ct);
                    result = onHand;
                }

                if (ct.IsCancellationRequested) return;

                Dispatcher.Invoke(() => SetLabel(result));
            }
            catch
            {
                if (!ct.IsCancellationRequested)
                    Dispatcher.Invoke(() => SetLabel(null));
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                    Dispatcher.Invoke(() => SetBusy(false));
            }
        }
    }
}

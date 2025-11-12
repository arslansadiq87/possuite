using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class TillSessionSummaryWindow : Window
    {
        public TillSessionSummaryWindow(int tillSessionId, int outletId, int counterId)
        {
            InitializeComponent();

            var sp = App.Services; // ✅ use static accessor
            var dialogs = sp.GetRequiredService<IDialogService>();
            var vm = sp.GetRequiredService<TillSessionSummaryVmFactory>()
                       .Create(tillSessionId, outletId, counterId);

            DataContext = vm;

            Loaded += async (_, __) => await vm.LoadCmd.ExecuteAsync(null);
            PreviewKeyDown += async (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.F5)
                {
                    await vm.LoadCmd.ExecuteAsync(null);
                    e.Handled = true;
                }
            };

            vm.RequestClose += _ => Close();
        }
    }

    // Small factory so constructor stays DI-friendly
    public sealed class TillSessionSummaryVmFactory
    {
        private readonly System.IServiceProvider _sp;
        public TillSessionSummaryVmFactory(System.IServiceProvider sp) => _sp = sp;

        public TillSessionSummaryVm Create(int tillId, int outletId, int counterId)
        {
            var tillRead = _sp.GetRequiredService<Pos.Domain.Services.ITillReadService>();
            var dialogs = _sp.GetRequiredService<IDialogService>();
            return new TillSessionSummaryVm(tillRead, dialogs, tillId, outletId, counterId);
        }
    }
}

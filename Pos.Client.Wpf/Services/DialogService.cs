// Pos.Client.Wpf/Services/DialogService.cs
using System.Threading.Tasks;
using Pos.Client.Wpf.Windows.Common;

namespace Pos.Client.Wpf.Services
{
    public sealed class DialogService : IDialogService
    {
        private readonly IViewNavigator _views;
        public DialogService(IViewNavigator views) => _views = views;

        public Task<bool> ConfirmAsync(string message, string? title = null)
            => ShowAsync(message, title, DialogButtons.YesNo)
               .ContinueWith(t => t.Result == DialogResult.Yes);

        public async Task AlertAsync(string message, string? title = null)
            => _ = await ShowAsync(message, title, DialogButtons.OK);

        public Task<DialogResult> ShowAsync(string message, string? title = null, DialogButtons buttons = DialogButtons.OK)
        {
            var tcs = new TaskCompletionSource<DialogResult>();
            var dialog = new ConfirmDialog(message, title, buttons);

            dialog.OnResult += res =>
            {
                _views.HideOverlay();
                tcs.TrySetResult(res);
            };

            _views.ShowOverlay(dialog);
            return tcs.Task;
        }
    }
}

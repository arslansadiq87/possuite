// Pos.Client.Wpf/Services/IDialogService.cs
using System.Threading.Tasks;

namespace Pos.Client.Wpf.Services
{
    public enum DialogButtons { OK, OKCancel, YesNo, YesNoCancel }
    public enum DialogResult { None, OK, Cancel, Yes, No }

    public interface IDialogService
    {
        // Generic overlay "MessageBox"
        Task<DialogResult> ShowAsync(
            string message,
            string? title = null,
            DialogButtons buttons = DialogButtons.OK);

        // Helpers
        Task AlertAsync(string message, string? title = null);           // OK
        Task<bool> ConfirmAsync(string message, string? title = null);   // Yes/No
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace Pos.Client.Wpf.Diagnostics
{
    public static class CrashReporter
    {
        private static int _showing;                 // 0/1 – prevents dialog storms
        private static int _installed;               // install only once
        private static string? _lastHash;            // de-dupe identical errors within a session
        private static readonly object _lock = new();

        public static void Install(Application app)
        {
            if (Interlocked.Exchange(ref _installed, 1) == 1) return;

            // UI thread exceptions
            app.DispatcherUnhandledException += (s, e) =>
            {
                try { Handle("DispatcherUnhandledException", e.Exception); }
                catch { /* swallow */ }
                e.Handled = true; // keep app alive so user can copy the error
            };

            // Background thread exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { Handle("AppDomain.UnhandledException", e.ExceptionObject as Exception); }
                catch { /* swallow */ }
                // cannot mark handled here
            };

            // Task exceptions not observed
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try { Handle("TaskScheduler.UnobservedTaskException", e.Exception); }
                catch { /* swallow */ }
                e.SetObserved();
            };
        }

        private static void Handle(string source, Exception? ex)
        {
            var now = DateTime.Now;
            var msg = BuildReport(source, ex, now);

            // de-dupe identical consecutive messages
            var hash = msg.GetHashCode().ToString("X");
            lock (_lock)
            {
                if (_lastHash == hash) return;
                _lastHash = hash;
            }

            // write log first
            TryWriteLog(msg, now);

            // copy to clipboard
            TryCopyToClipboard(msg);

            // show one dialog only (avoid storms)
            if (Interlocked.Exchange(ref _showing, 1) == 1) return;
            try
            {
                MessageBox.Show(
                    "An unexpected error occurred.\n\n" +
                    "➡ The full details have been COPIED to your clipboard.\n" +
                    "➡ A log file was also written under %LOCALAPPDATA%\\PosSuite\\logs.\n\n" +
                    "You can paste here for me to diagnose.",
                    "Application Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Interlocked.Exchange(ref _showing, 0);
            }
        }

        private static string BuildReport(string source, Exception? ex, DateTime ts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("==== PosSuite Crash Report ====");
            sb.AppendLine($"Timestamp : {ts:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Source    : {source}");
            sb.AppendLine($"Process   : {Process.GetCurrentProcess().ProcessName} ({Environment.ProcessId})");
            sb.AppendLine($"Thread    : {Environment.CurrentManagedThreadId}");
            sb.AppendLine($".NET      : {Environment.Version}");
            sb.AppendLine($"OS        : {Environment.OSVersion}");
            sb.AppendLine();

            if (ex == null)
            {
                sb.AppendLine("No Exception object (null).");
                return sb.ToString();
            }

            void dump(Exception e, int depth)
            {
                var pad = new string('>', depth);
                sb.AppendLine($"{pad} {e.GetType().FullName}: {e.Message}");
                sb.AppendLine(e.StackTrace);
                if (e is AggregateException agg)
                {
                    foreach (var inner in agg.InnerExceptions)
                        dump(inner, depth + 1);
                }
                else if (e.InnerException != null)
                {
                    dump(e.InnerException, depth + 1);
                }
            }

            dump(ex, 0);
            return sb.ToString();
        }

        private static void TryWriteLog(string text, DateTime ts)
        {
            try
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PosSuite", "logs");
                Directory.CreateDirectory(root);
                var file = Path.Combine(root, $"crash-{ts:yyyyMMdd-HHmmss-fff}.log");
                File.WriteAllText(file, text, Encoding.UTF8);
            }
            catch { /* ignore */ }
        }

        private static void TryCopyToClipboard(string text)
        {
            try
            {
                // If we're already on an STA thread with a message pump (WPF UI), this is fine:
                if (Application.Current?.Dispatcher?.CheckAccess() == true)
                {
                    Clipboard.SetText(text);
                    return;
                }

                // Otherwise marshal to a temporary STA thread:
                var t = new Thread(() =>
                {
                    try { Clipboard.SetText(text); }
                    catch { /* ignore */ }
                });
                t.SetApartmentState(ApartmentState.STA);
                t.IsBackground = true;
                t.Start();
                t.Join(300); // don’t block forever
            }
            catch { /* ignore */ }
        }

        public static event Action<string>? Line;

        public static void Log(string text)
        {
            try
            {
                // time stamp + thread id for quick triage
                var msg = $"{DateTime.Now:HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId}] {text}";
                Line?.Invoke(msg);                // live UI
                Debug.WriteLine(msg);             // VS Output
                TryWriteLog(msg + Environment.NewLine, DateTime.Now); // optional: append
            }
            catch { /* ignore */ }
        }


    }
}

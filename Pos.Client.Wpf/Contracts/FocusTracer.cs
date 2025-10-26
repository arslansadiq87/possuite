using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pos.Client.Wpf.Debugging
{
    public static class FocusTracer
    {
        public static void Attach(FrameworkElement root, string tag = "FOCUS")
        {
            if (root == null) return;

            root.AddHandler(Keyboard.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler((s, e) => Log(tag, "PREVIEW GOT", e)), true);
            root.AddHandler(Keyboard.GotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler((s, e) => Log(tag, "GOT", e)), true);
            root.AddHandler(Keyboard.PreviewLostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler((s, e) => Log(tag, "PREVIEW LOST", e)), true);
            root.AddHandler(Keyboard.LostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler((s, e) => Log(tag, "LOST", e)), true);
        }

        private static void Log(string tag, string phase, KeyboardFocusChangedEventArgs e)
        {
            Debug.WriteLine($"[{tag}] {phase} handled={e.Handled}  OLD={Describe(e.OldFocus)}  NEW={Describe(e.NewFocus)}  PATH={Path(e.NewFocus as DependencyObject)}");
        }

        private static string Describe(object? el)
        {
            if (el is FrameworkElement fe)
                return $"{fe.GetType().Name}#{fe.Name ?? "(no-name)"} Visible={fe.IsVisible} Enabled={fe.IsEnabled} Focusable={fe.Focusable}";
            if (el is FrameworkContentElement fce)
                return $"{fce.GetType().Name} Enabled={fce.IsEnabled} Focusable={fce.Focusable}";
            return el?.GetType().Name ?? "<null>";
        }

        private static string Path(DependencyObject? d)
        {
            if (d == null) return "";
            var cur = d; int hops = 0;
            string chain = "";
            while (cur != null && hops++ < 50)
            {
                string node = cur is FrameworkElement fe ? $"{fe.GetType().Name}#{fe.Name}" : cur.GetType().Name;
                if (string.IsNullOrEmpty(chain)) chain = node; else chain = node + " ← " + chain;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return chain;
        }
    }
}

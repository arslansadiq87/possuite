using System;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Windows.Shell;

namespace Pos.Client.Wpf.Services
{
    // Partial to keep things tidy (optional). Or merge into your existing ViewNavigator.cs.
    public sealed partial class ViewNavigator : IViewNavigator
    {
        public ViewTab OpenTab<TView>(string? title = null, string? contextKey = null, bool activate = true)
            where TView : FrameworkElement
        {
            var vm = EnsureShell();
            var view = _sp.GetRequiredService<TView>();

            var tab = new ViewTab
            {
                Title = title ?? typeof(TView).Name.Replace("View", string.Empty),
                Content = view,
                ContextKey = contextKey
            };

            vm.Tabs.Add(tab);
            if (activate) vm.ActiveTab = tab;

            UpdateContextualGroups(vm);
            return tab;
        }

        public void CloseTab(ViewTab tab)
        {
            var vm = EnsureShell();
            var idx = vm.Tabs.IndexOf(tab);
            if (idx < 0) return;

            bool isActive = ReferenceEquals(vm.ActiveTab, tab);
            vm.Tabs.RemoveAt(idx);

            if (isActive)
            {
                // Activate neighbor
                if (vm.Tabs.Count > 0)
                {
                    vm.ActiveTab = vm.Tabs[Math.Clamp(idx - 1, 0, vm.Tabs.Count - 1)];
                }
                else
                {
                    vm.ActiveTab = null;
                }
            }
            UpdateContextualGroups(vm);
        }

        public void CloseActiveTab()
        {
            var vm = EnsureShell();
            if (vm.ActiveTab != null) CloseTab(vm.ActiveTab);
        }

        public void ActivateTab(ViewTab tab)
        {
            var vm = EnsureShell();
            if (vm.Tabs.Contains(tab))
            {
                vm.ActiveTab = tab;
                UpdateContextualGroups(vm);
            }
        }

        private static void UpdateContextualGroups(DashboardVm vm)
        {
            // Simple rule: show Transfer contextual group only when active content is a TransferView
            var typeName = vm.ActiveTab?.Content?.GetType().FullName ?? string.Empty;
            vm.TransferTabVisible = typeName.EndsWith(".TransferView", StringComparison.Ordinal);
        }
    }
}

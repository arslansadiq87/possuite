namespace Pos.Client.Wpf.Infrastructure
{
    /// Implement on tab views that want a refresh whenever the tab becomes active.
    public interface IRefreshOnActivate
    {
        void OnActivated();
    }
}

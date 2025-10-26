public interface ITillService
{
    Task<bool> OpenTillAsync();
    Task<bool> CloseTillAsync();            // returns true if closed
    string GetStatusText();                 // e.g., "OPEN (Id=2, Opened 16:37)" or "Closed"
                                            // NEW: used by DashboardVm to toggle Open/Close buttons
    bool IsTillOpen();
}

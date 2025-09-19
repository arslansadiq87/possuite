//Pos.Client.Wpf/Services/AppState.cs
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Services
{
    public class AppState
    {
        // Global singleton instance
        public static AppState Current { get; } = new AppState();

        // Private constructor so nobody else creates it
        private AppState() { }

        // Session properties
        public User? CurrentUser { get; set; }
        public int CurrentUserId { get; set; }
        public string CurrentUserName { get; set; } = "";
        public string CurrentUserRole { get; set; } = "";
        public int CurrentOutletId { get; set; }
        public int CurrentCounterId { get; set; }
        public int? CurrentTillSessionId { get; set; }
        public bool IsLoggedIn => CurrentUserId > 0;
    }
}

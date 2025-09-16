//Pos.Client.Wpf/Services/AppState.cs
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Services
{
    /// <summary>
    /// Simple place to keep the signed-in user and other app-wide state.
    /// </summary>
    public class AppState
    {
        public User? CurrentUser { get; set; }
        // The outlet/counter context
        public int CurrentUserId { get; set; }
        public string CurrentUserName { get; set; } = "";
        public string CurrentUserRole { get; set; } = "";

        public int CurrentOutletId { get; set; }
        public int CurrentCounterId { get; set; }
        // Current till session (nullable until opened)
        public int? CurrentTillSessionId { get; set; }

        public bool IsLoggedIn => CurrentUserId > 0;
    }
}

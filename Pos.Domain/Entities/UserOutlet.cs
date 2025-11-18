// Pos.Domain/Entities/UserOutlet.cs

namespace Pos.Domain.Entities
{
    // Composite PK (UserId, OutletId)
    public class UserOutlet
    {
        public int UserId { get; set; }
        public int OutletId { get; set; }
        public UserRole Role { get; set; }
        public User User { get; set; } = null!;
        public Outlet Outlet { get; set; } = null!;
    }
}
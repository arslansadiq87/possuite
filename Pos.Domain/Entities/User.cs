// Pos.Domain/Entities/User.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public enum UserRole { Admin, Manager, Cashier, Salesman }

    public class User : BaseEntity
    {
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public UserRole Role { get; set; } = UserRole.Cashier;
        public bool IsActive { get; set; } = true;

        // BCrypt hash stored as string
        //public string PasswordHash { get; set; } = string.Empty; // ← was byte[]
        public string PasswordHash { get; set; } = null!; // <- string, NOT byte[]


    }
}

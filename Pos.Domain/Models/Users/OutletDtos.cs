// Pos.Domain/Models/Users/OutletDtos.cs
using Pos.Domain;

namespace Pos.Domain.Models.Users
{
    /// <summary>Lightweight outlet row for user editor sidebars.</summary>
    public sealed record OutletLite(int Id, string Name);

    /// <summary>Desired assignment shape used to reconcile user-outlet roles.</summary>
    public sealed record UserOutletAssignDto(int OutletId, bool IsAssigned, UserRole Role);
}

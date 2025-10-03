// Pos.Client.Wpf/Services/CurrentUserService.cs
using Pos.Domain.Entities;

public sealed class CurrentUserService
{
    // Set this when the user signs in
    public User? CurrentUser { get; set; }
}

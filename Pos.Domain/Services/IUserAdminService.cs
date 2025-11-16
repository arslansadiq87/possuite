// Pos.Domain/Services/IUserAdminService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models.Users;

namespace Pos.Domain.Services
{
    public interface IUserAdminService
    {
        // Users (Queries)
        Task<List<User>> GetAllAsync(CancellationToken ct = default);
        Task<User?> GetAsync(int id, CancellationToken ct = default);
        Task<bool> IsUsernameTakenAsync(string username, int? excludingId = null, CancellationToken ct = default);

        // Users (Commands)
        /// <summary>
        /// Creates or updates a user; if newPassword provided, updates hash.
        /// Enqueues upsert to outbox. Returns saved User.Id.
        /// </summary>
        Task<int> CreateOrUpdateAsync(User input, string? newPassword, CancellationToken ct = default);
        Task DeleteAsync(int userId, CancellationToken ct = default);

        // Outlets + Assignments (for editor)
        Task<List<OutletLite>> GetOutletsAsync(CancellationToken ct = default);
        Task<Dictionary<int, UserRole>> GetUserAssignmentsAsync(int userId, CancellationToken ct = default);

        /// <summary>
        /// Reconcile assignments for a user: create/update/remove, with outbox sync.
        /// </summary>
        Task SaveAssignmentsAsync(int userId, IEnumerable<UserOutletAssignDto> desired, CancellationToken ct = default);

        // Self-service security for logged-in user
        Task ChangeOwnPasswordAsync(
            int userId,
            string currentPassword,
            string newPassword,
            CancellationToken ct = default);

        Task ChangeOwnPinAsync(
            int userId,
            string currentPassword,
            string? newPin,
            CancellationToken ct = default);

        /// <summary>
        /// Verifies that the given PIN is valid for the given user.
        /// Throws InvalidOperationException if PIN not set, incorrect, or user missing.
        /// </summary>
        Task VerifyPinAsync(
            int userId,
            string pin,
            CancellationToken ct = default);

    }
}

// Pos.Persistence/Services/UserAdminService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models.Users;
using Pos.Domain.Services;
using Pos.Domain.Utils;
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class UserAdminService : IUserAdminService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public UserAdminService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        // ─────────── Users (Queries) ───────────
        public async Task<List<User>> GetAllAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);
        }

        public async Task<User?> GetAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        }

        public async Task<bool> IsUsernameTakenAsync(string username, int? excludingId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("Username is required.");

            var canonical = username.Trim().ToLower(CultureInfo.InvariantCulture);
            await using var db = await _dbf.CreateDbContextAsync(ct);

            return await db.Users.AnyAsync(
                u => u.Username.ToLower() == canonical && (!excludingId.HasValue || u.Id != excludingId.Value),
                ct);
        }

        // ─────────── Users (Commands) ───────────
        /// <summary>
        /// Creates or updates a User. If newPassword is provided, update hash.
        /// Enqueues upsert to outbox. Returns saved User.Id.
        /// </summary>
        public async Task<int> CreateOrUpdateAsync(User input, string? newPassword, CancellationToken ct = default)
        {
            if (input is null) throw new InvalidOperationException("User payload is required.");
            if (string.IsNullOrWhiteSpace(input.Username))
                throw new InvalidOperationException("Username is required.");
            if (string.IsNullOrWhiteSpace(input.DisplayName))
                throw new InvalidOperationException("Display name is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var isCreate = input.Id == 0;
            User entity;

            if (isCreate)
            {
                entity = new User();
                await db.Users.AddAsync(entity, ct);
            }
            else
            {
                entity = await db.Users.FirstOrDefaultAsync(u => u.Id == input.Id, ct)
                         ?? throw new InvalidOperationException("User not found.");
            }

            // Map
            entity.Username = input.Username.Trim();
            entity.DisplayName = input.DisplayName.Trim();
            entity.Role = input.Role;
            entity.IsActive = input.IsActive;
            entity.IsGlobalAdmin = input.IsGlobalAdmin;

            if (isCreate)
            {
                var pwd = string.IsNullOrWhiteSpace(newPassword) ? "1234" : newPassword;
                entity.PasswordHash = BCrypt.Net.BCrypt.HashPassword(pwd);
            }
            else if (!string.IsNullOrWhiteSpace(newPassword))
            {
                entity.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            }

            // Persist + outbox (enqueue before final save/commit)
            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, entity, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return entity.Id;
        }

        public async Task DeleteAsync(int userId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var entity = await db.Users.Include(u => u.UserOutlets).FirstOrDefaultAsync(u => u.Id == userId, ct)
                         ?? throw new InvalidOperationException("User not found.");

            if (entity.UserOutlets.Any())
                throw new InvalidOperationException("User has outlet assignments. Remove them first.");

            db.Users.Remove(entity);

            // Persist + outbox (enqueue before final save/commit)
            await db.SaveChangesAsync(ct);
            // If your sync uses upsert-on-delete semantics:
            await _outbox.EnqueueUpsertAsync(db, entity, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // ─────────── Outlets + Assignments (for editor sidebar) ───────────
        public async Task<List<OutletLite>> GetOutletsAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Outlets.AsNoTracking()
                .OrderBy(o => o.Name)
                .Select(o => new OutletLite(o.Id, o.Name))
                .ToListAsync(ct);
        }

        public async Task<Dictionary<int, UserRole>> GetUserAssignmentsAsync(int userId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.UserOutlets.AsNoTracking()
                .Where(x => x.UserId == userId)
                .ToDictionaryAsync(x => x.OutletId, x => x.Role, ct);
        }

        /// <summary>
        /// Reconcile assignments for a user: create/update/remove, with outbox sync.
        /// </summary>
        public async Task SaveAssignmentsAsync(
            int userId,
            IEnumerable<UserOutletAssignDto> desired,
            CancellationToken ct = default)
        {
            if (desired is null) throw new InvalidOperationException("Desired assignments are required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var current = await db.UserOutlets.Where(uo => uo.UserId == userId).ToListAsync(ct);
            var map = current.ToDictionary(uo => uo.OutletId);

            foreach (var d in desired)
            {
                if (map.TryGetValue(d.OutletId, out var existing))
                {
                    if (!d.IsAssigned)
                    {
                        db.UserOutlets.Remove(existing);
                        await db.SaveChangesAsync(ct);
                        await _outbox.EnqueueDeleteAsync(
                            db,
                            nameof(UserOutlet),
                            GuidUtility.FromString($"useroutlet:{existing.UserId}:{existing.OutletId}"),
                            ct
                        );
                        await db.SaveChangesAsync(ct);
                    }
                    else if (existing.Role != d.Role)
                    {
                        existing.Role = d.Role;
                        await db.SaveChangesAsync(ct);
                        await _outbox.EnqueueUpsertAsync(db, existing, ct);
                        await db.SaveChangesAsync(ct);
                    }
                }
                else if (d.IsAssigned)
                {
                    var entity = new UserOutlet { UserId = userId, OutletId = d.OutletId, Role = d.Role };
                    await db.UserOutlets.AddAsync(entity, ct);
                    await db.SaveChangesAsync(ct);
                    await _outbox.EnqueueUpsertAsync(db, entity, ct);
                    await db.SaveChangesAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
        }

        public async Task ChangeOwnPasswordAsync(
    int userId,
    string currentPassword,
    string newPassword,
    CancellationToken ct = default)
        {
            if (userId <= 0)
                throw new InvalidOperationException("User is not logged in.");

            if (string.IsNullOrWhiteSpace(currentPassword))
                throw new InvalidOperationException("Current password is required.");

            if (string.IsNullOrWhiteSpace(newPassword))
                throw new InvalidOperationException("New password is required.");

            if (newPassword.Length < 4)
                throw new InvalidOperationException("New password must be at least 4 characters.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                       ?? throw new InvalidOperationException("User not found.");

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
                throw new InvalidOperationException("Current password is incorrect.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, user, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task ChangeOwnPinAsync(
    int userId,
    string currentPassword,
    string? newPin,
    CancellationToken ct = default)
        {
            if (userId <= 0)
                throw new InvalidOperationException("User is not logged in.");

            if (string.IsNullOrWhiteSpace(currentPassword))
                throw new InvalidOperationException("Current password is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                       ?? throw new InvalidOperationException("User not found.");

            // Verify current password against PasswordHash
            if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
                throw new InvalidOperationException("Current password is incorrect.");

            if (string.IsNullOrWhiteSpace(newPin))
            {
                // Clear PIN
                user.PinHash = null;
            }
            else
            {
                var pin = newPin.Trim();
                if (pin.Length < 4 || pin.Length > 6 || !pin.All(char.IsDigit))
                    throw new InvalidOperationException("PIN must be 4–6 digits.");

                // IMPORTANT: store in PinHash (not PasswordHash)
                user.PinHash = BCrypt.Net.BCrypt.HashPassword(pin);
            }

            user.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, user, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }


        public async Task VerifyPinAsync(
    int userId,
    string pin,
    CancellationToken ct = default)
        {
            if (userId <= 0)
                throw new InvalidOperationException("User is not logged in.");

            if (string.IsNullOrWhiteSpace(pin))
                throw new InvalidOperationException("PIN is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId, ct)
                ?? throw new InvalidOperationException("User not found.");

            if (string.IsNullOrWhiteSpace(user.PinHash))
                throw new InvalidOperationException(
                    "No PIN configured for the current user. Set a PIN in settings first.");

            // IMPORTANT: verify against PinHash, not PasswordHash
            if (!BCrypt.Net.BCrypt.Verify(pin, user.PinHash))
                throw new InvalidOperationException("Incorrect PIN.");
        }



    }
}

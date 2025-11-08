using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class UserAdminService
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
            username = (username ?? "").Trim().ToLowerInvariant();
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Users.AnyAsync(u =>
                u.Username.ToLower() == username && (excludingId == null || u.Id != excludingId.Value), ct);
        }

        // ─────────── Users (Commands) ───────────
        /// <summary>
        /// Creates or updates a User. If newPassword is provided, update hash.
        /// Enqueues upsert to outbox. Returns saved User.Id.
        /// </summary>
        public async Task<int> CreateOrUpdateAsync(User input, string? newPassword, CancellationToken ct = default)
        {
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
            entity.Username = (input.Username ?? "").Trim();
            entity.DisplayName = (input.DisplayName ?? "").Trim();
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
            await db.SaveChangesAsync(ct);

            // Keep your previous behavior: enqueue upsert after delete (if your sync expects it).
            await _outbox.EnqueueUpsertAsync(db, entity, ct);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }

        // ─────────── Outlets + Assignments (for editor sidebar) ───────────
        public sealed record OutletLite(int Id, string Name);

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
            IEnumerable<(int OutletId, bool IsAssigned, UserRole Role)> desired,
            CancellationToken ct = default)
        {
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
                            Pos.Domain.Utils.GuidUtility.FromString($"useroutlet:{existing.UserId}:{existing.OutletId}"),
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
    }
}

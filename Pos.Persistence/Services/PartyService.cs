// Pos.Persistence/Services/PartyService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Models.Parties;
using Pos.Domain.Services;
using Pos.Persistence.Sync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pos.Persistence.Services
{
    public sealed class PartyService : IPartyService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public PartyService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public async Task<List<PartyRowDto>> SearchAsync(
            string? term,
            bool onlyActive,
            bool includeCustomer,
            bool includeSupplier,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            term = (term ?? "").Trim();
            IQueryable<Party> q = db.Parties
                .Include(p => p.Roles)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(term))
            {
                q = q.Where(p =>
                    p.Name.Contains(term) ||
                    (p.Phone != null && p.Phone.Contains(term)) ||
                    (p.Email != null && p.Email.Contains(term)) ||
                    (p.TaxNumber != null && p.TaxNumber.Contains(term)));
            }

            if (onlyActive)
                q = q.Where(p => p.IsActive);

            if (includeCustomer ^ includeSupplier)
            {
                var role = includeCustomer ? RoleType.Customer : RoleType.Supplier;
                q = q.Where(p => p.Roles.Any(r => r.Role == role));
            }

            return await q
                .OrderBy(p => p.Name)
                .Select(p => new PartyRowDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Phone = p.Phone,
                    Email = p.Email,
                    TaxNumber = p.TaxNumber,
                    IsActive = p.IsActive,
                    IsSharedAcrossOutlets = p.IsSharedAcrossOutlets,
                    RolesText = string.Join(", ", p.Roles.Select(r => r.Role.ToString()))
                })
                .ToListAsync(ct);
        }

        public async Task<Party?> GetPartyAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Parties
                .Include(p => p.Roles)
                .Include(p => p.Outlets)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, ct);
        }

        public async Task SavePartyAsync(
            int? id,
            string name,
            string? phone,
            string? email,
            string? taxNumber,
            bool isActive,
            bool isShared,
            bool roleCustomer,
            bool roleSupplier,
            IEnumerable<(int OutletId, bool IsActive, bool AllowCredit, decimal? CreditLimit)> outlets,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            Party party;
            var removedLinks = new List<PartyOutlet>();

            if (id is null)
            {
                party = new Party();
                db.Parties.Add(party);
            }
            else
            {
                party = await db.Parties
                    .Include(p => p.Roles)
                    .Include(p => p.Outlets)
                    .FirstAsync(p => p.Id == id.Value, ct);
            }

            // --- basics ---
            party.Name = name.Trim();
            party.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
            party.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
            party.TaxNumber = string.IsNullOrWhiteSpace(taxNumber) ? null : taxNumber.Trim();
            party.IsActive = isActive;
            party.IsSharedAcrossOutlets = isShared;

            // --- roles ---
            var want = new HashSet<RoleType>();
            if (roleCustomer) want.Add(RoleType.Customer);
            if (roleSupplier) want.Add(RoleType.Supplier);

            var have = party.Roles.Select(r => r.Role).ToHashSet();
            foreach (var r in want.Except(have))
                party.Roles.Add(new PartyRole { Role = r });
            foreach (var r in have.Except(want))
                db.Remove(party.Roles.First(x => x.Role == r));

            // --- outlets ---
            var byId = party.Outlets.ToDictionary(x => x.OutletId, x => x);
            foreach (var vm in outlets)
            {
                if (!byId.TryGetValue(vm.OutletId, out var link))
                {
                    if (vm.IsActive || vm.AllowCredit || vm.CreditLimit.HasValue)
                    {
                        link = new PartyOutlet
                        {
                            OutletId = vm.OutletId,
                            IsActive = vm.IsActive,
                            AllowCredit = vm.AllowCredit,
                            CreditLimit = vm.CreditLimit
                        };
                        party.Outlets.Add(link);
                    }
                }
                else
                {
                    if (vm.IsActive || vm.AllowCredit || vm.CreditLimit.HasValue)
                    {
                        link.IsActive = vm.IsActive;
                        link.AllowCredit = vm.AllowCredit;
                        link.CreditLimit = vm.CreditLimit;
                    }
                    else
                    {
                        removedLinks.Add(link);
                        db.Remove(link);
                    }
                }
            }

            // --- ensure CoA ---
            var isCust = party.Roles.Any(r => r.Role == RoleType.Customer);
            var isSupp = party.Roles.Any(r => r.Role == RoleType.Supplier);
            string headerCode = isCust ? "62" : "61";

            var parent = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == headerCode, ct);
            if (parent == null)
                throw new InvalidOperationException($"CoA header {headerCode} not found.");

            Account? linked = null;
            if (party.AccountId.HasValue)
            {
                linked = await db.Accounts
                    .FirstOrDefaultAsync(a => a.Id == party.AccountId.Value, ct);
                if (linked == null) party.AccountId = null;
            }

            if (linked == null)
            {
                var code = await GenerateNextChildCodeAsync(db, parent, forHeader: false, ct);
                linked = new Account
                {
                    Code = code,
                    Name = party.Name,
                    Type = AccountType.Parties,
                    NormalSide = isSupp && !isCust ? NormalSide.Credit : NormalSide.Debit,
                    IsHeader = false,
                    AllowPosting = true,
                    ParentId = parent.Id
                };
                db.Accounts.Add(linked);
                await db.SaveChangesAsync(ct);
                party.AccountId = linked.Id;
            }
            else
            {
                linked.Name = party.Name;
                linked.ParentId = parent.Id;
                linked.NormalSide = isSupp && !isCust ? NormalSide.Credit : NormalSide.Debit;
            }

            await db.SaveChangesAsync(ct);

            // --- Outbox (enqueue before final save/commit) ---
            await _outbox.EnqueueUpsertAsync(db, party, ct);
            foreach (var link in party.Outlets)
                await _outbox.EnqueueUpsertAsync(db, link, ct);

            if (linked != null)
                await _outbox.EnqueueUpsertAsync(db, linked, ct);

            foreach (var del in removedLinks)
            {
                var topic = nameof(PartyOutlet);
                var publicId = Pos.Domain.Utils.GuidUtility.FromString($"{topic}:{del.PartyId}:{del.OutletId}");
                await _outbox.EnqueueDeleteAsync(db, topic, publicId, ct);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task<string?> GetPartyNameAsync(int partyId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Parties
                .AsNoTracking()
                .Where(p => p.Id == partyId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(ct);
        }

        private static async Task<string> GenerateNextChildCodeAsync(
            PosClientDbContext db,
            Account parent,
            bool forHeader,
            CancellationToken ct)
        {
            var sibs = await db.Accounts.AsNoTracking()
                .Where(a => a.ParentId == parent.Id)
                .Select(a => a.Code)
                .ToListAsync(ct);

            int max = 0;
            foreach (var code in sibs)
            {
                var last = code?.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            var next = max + 1;
            var suffix = forHeader ? next.ToString("D2") : next.ToString("D3");
            return $"{parent.Code}-{suffix}";
        }
    }
}

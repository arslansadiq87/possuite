// Pos.Persistence/Services/VoucherCenterService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Models.Accounting;     // DTOs
using Pos.Domain.Services;              // Interface
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class VoucherCenterService : IVoucherCenterService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IGlPostingService _gl;
        private readonly IOutboxWriter _outbox;

        public VoucherCenterService(
            IDbContextFactory<PosClientDbContext> dbf,
            IGlPostingService gl,
            IOutboxWriter outbox)
        {
            _dbf = dbf;
            _gl = gl;
            _outbox = outbox;
        }

        public async Task<IReadOnlyList<VoucherRowDto>> SearchAsync(
            DateTime startUtc,
            DateTime endUtc,
            string? searchText,
            int? outletId,
            IReadOnlyCollection<VoucherType>? types,
            IReadOnlyCollection<VoucherStatus>? statuses,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var q = db.Vouchers.AsNoTracking()
                .Where(v => v.TsUtc >= startUtc && v.TsUtc <= endUtc);

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var s = searchText.Trim();
                if (int.TryParse(s, out var idVal))
                    q = q.Where(v => v.Id == idVal || (v.Memo != null && EF.Functions.Like(v.Memo, $"%{s}%")));
                else
                    q = q.Where(v => v.Memo != null && EF.Functions.Like(v.Memo, $"%{s}%"));
            }

            if (outletId.HasValue)
                q = q.Where(v => v.OutletId == outletId.Value);

            if (types is { Count: > 0 } && types.Count < Enum.GetNames(typeof(VoucherType)).Length)
                q = q.Where(v => types.Contains(v.Type));

            if (statuses is { Count: > 0 } && statuses.Count < Enum.GetNames(typeof(VoucherStatus)).Length)
                q = q.Where(v => statuses.Contains(v.Status));

            var list = await q
                .OrderByDescending(v => v.TsUtc)
                .Select(v => new VoucherRowDto(
                    v.Id,
                    v.TsUtc,
                    v.Type,
                    v.Memo,
                    v.OutletId,
                    v.Status,
                    v.RevisionNo,
                    v.Lines.Sum(l => l.Debit),
                    v.Lines.Sum(l => l.Credit),
                    v.RevisionNo > 1
                ))
                .ToListAsync(ct);

            return list;
        }

        public async Task<IReadOnlyList<VoucherLineDto>> GetLinesAsync(int voucherId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var rows = await db.VoucherLines
                .Where(l => l.VoucherId == voucherId)
                .Select(l => new VoucherLineDto(
                    l.AccountId,
                    db.Accounts.Where(a => a.Id == l.AccountId).Select(a => a.Name).FirstOrDefault() ?? "",
                    l.Description,
                    l.Debit,
                    l.Credit
                ))
                .ToListAsync(ct);

            return rows;
        }

        public async Task<int> CreateRevisionDraftAsync(int sourceVoucherId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var old = await db.Vouchers
                .Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.Id == sourceVoucherId, ct)
                ?? throw new InvalidOperationException($"Voucher #{sourceVoucherId} not found.");

            if (old.Status == VoucherStatus.Voided)
                throw new InvalidOperationException("Cannot amend a voided voucher.");

            var newV = new Voucher
            {
                TsUtc = DateTime.UtcNow,
                OutletId = old.OutletId,
                Type = old.Type,
                Memo = $"Revision of #{old.Id}: {old.Memo}",
                Status = VoucherStatus.Draft,
                RevisionNo = old.RevisionNo + 1,
                AmendedFromId = old.Id,
                AmendedAtUtc = DateTime.UtcNow
            };

            db.Vouchers.Add(newV);
            await db.SaveChangesAsync(ct);

            foreach (var ln in old.Lines)
            {
                db.VoucherLines.Add(new VoucherLine
                {
                    VoucherId = newV.Id,
                    AccountId = ln.AccountId,
                    Debit = ln.Debit,
                    Credit = ln.Credit,
                    Description = ln.Description
                });
            }

            await db.SaveChangesAsync(ct);
            return newV.Id;
        }

        public async Task DeleteDraftAsync(int draftVoucherId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var draft = await db.Vouchers
                .Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.Id == draftVoucherId, ct);

            if (draft is null || draft.Status != VoucherStatus.Draft) return;

            if (draft.Lines.Count > 0) db.VoucherLines.RemoveRange(draft.Lines);
            db.Vouchers.Remove(draft);
            await db.SaveChangesAsync(ct);
        }

        public async Task FinalizeRevisionAsync(int newVoucherId, int oldVoucherId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var newV = await db.Vouchers
                .Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.Id == newVoucherId, ct)
                ?? throw new InvalidOperationException($"New voucher #{newVoucherId} not found.");

            var old = await db.Vouchers
                .Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.Id == oldVoucherId, ct)
                ?? throw new InvalidOperationException($"Old voucher #{oldVoucherId} not found.");

            // Post GL delta between newV and old lines
            await _gl.PostVoucherRevisionAsync(newV, old.Lines.ToList());

            old.Status = VoucherStatus.Amended;
            old.AmendedAtUtc = DateTime.UtcNow;

            if (newV.Status == VoucherStatus.Draft)
                newV.Status = VoucherStatus.Posted;

            // enqueue outbox before final save+commit
            await _outbox.EnqueueUpsertAsync(db, newV);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task VoidAsync(int voucherId, string reason, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new InvalidOperationException("A void reason is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var v = await db.Vouchers.FirstOrDefaultAsync(x => x.Id == voucherId, ct)
                ?? throw new InvalidOperationException($"Voucher #{voucherId} not found.");

            if (v.Status != VoucherStatus.Posted)
                throw new InvalidOperationException("Only posted vouchers can be voided.");

            await _gl.PostVoucherVoidAsync(v); // reversal GL
            v.Status = VoucherStatus.Voided;
            v.VoidReason = reason.Trim();
            v.VoidedAtUtc = DateTime.UtcNow;

            // outbox before final save/commit
            await _outbox.EnqueueUpsertAsync(db, v);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task<VoucherEditLoadDto> LoadAsync(int voucherId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var v = await db.Vouchers.AsNoTracking()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == voucherId, ct)
                ?? throw new InvalidOperationException($"Voucher #{voucherId} not found.");

            var lines = v.Lines
                .OrderBy(l => l.Id)
                .Select(l => new VoucherEditLineDto(l.AccountId, l.Description, l.Debit, l.Credit))
                .ToList();

            return new VoucherEditLoadDto(
                v.Id,
                v.TsUtc,
                v.OutletId,
                v.RefNo,
                v.Memo,
                v.Type,
                lines
            );
        }

        public async Task<int> SaveAsync(VoucherEditLoadDto dto, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            try
            {
                Voucher v;
                var baseDocTypes = new[] { GlDocType.JournalVoucher, GlDocType.CashPayment, GlDocType.CashReceipt };

                if (dto.Id > 0)
                {
                    // ----- UPDATE EXISTING -----
                    v = await db.Vouchers.Include(x => x.Lines)
                        .FirstOrDefaultAsync(x => x.Id == dto.Id, ct)
                        ?? throw new InvalidOperationException($"Voucher #{dto.Id} not found.");

                    v.TsUtc = dto.TsUtc;
                    v.OutletId = dto.OutletId;
                    v.RefNo = dto.RefNo?.Trim();
                    v.Memo = dto.Memo?.Trim();
                    v.Type = dto.Type;

                    // remove prior base GL
                    var oldGl = await db.GlEntries
                        .Where(g => g.DocId == v.Id && baseDocTypes.Contains(g.DocType))
                        .ToListAsync(ct);
                    if (oldGl.Count > 0) db.GlEntries.RemoveRange(oldGl);

                    // replace lines
                    if (v.Lines.Count > 0) db.VoucherLines.RemoveRange(v.Lines);
                    await db.SaveChangesAsync(ct);

                    foreach (var ln in dto.Lines)
                    {
                        db.VoucherLines.Add(new VoucherLine
                        {
                            VoucherId = v.Id,
                            AccountId = ln.AccountId,
                            Description = ln.Description,
                            Debit = ln.Debit,
                            Credit = ln.Credit
                        });
                    }
                    await db.SaveChangesAsync(ct);

                    await _gl.PostVoucherAsync(v); // base posting

                    // outbox first, then save+commit
                    await _outbox.EnqueueUpsertAsync(db, v);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    return v.Id;
                }
                else
                {
                    // ----- CREATE NEW -----
                    v = new Voucher
                    {
                        TsUtc = dto.TsUtc,
                        OutletId = dto.OutletId,
                        RefNo = dto.RefNo?.Trim(),
                        Memo = dto.Memo?.Trim(),
                        Type = dto.Type
                    };
                    db.Vouchers.Add(v);
                    await db.SaveChangesAsync(ct);

                    foreach (var ln in dto.Lines)
                    {
                        db.VoucherLines.Add(new VoucherLine
                        {
                            VoucherId = v.Id,
                            AccountId = ln.AccountId,
                            Description = ln.Description,
                            Debit = ln.Debit,
                            Credit = ln.Credit
                        });
                    }
                    await db.SaveChangesAsync(ct);

                    await _gl.PostVoucherAsync(v); // base posting

                    // outbox first, then save+commit
                    await _outbox.EnqueueUpsertAsync(db, v);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    return v.Id;
                }
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
    }
}

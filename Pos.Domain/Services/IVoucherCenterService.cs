// Pos.Domain/Services/IVoucherCenterService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Accounting;
using Pos.Domain.Models.Accounting; // DTOs

namespace Pos.Domain.Services
{
    public interface IVoucherCenterService
    {
        Task<IReadOnlyList<VoucherRowDto>> SearchAsync(
            DateTime startUtc,
            DateTime endUtc,
            string? searchText,
            int? outletId,
            IReadOnlyCollection<VoucherType>? types,
            IReadOnlyCollection<VoucherStatus>? statuses,
            CancellationToken ct = default);

        Task<IReadOnlyList<VoucherLineDto>> GetLinesAsync(int voucherId, CancellationToken ct = default);

        // Revision workflow
        Task<int> CreateRevisionDraftAsync(int sourceVoucherId, CancellationToken ct = default);
        Task DeleteDraftAsync(int draftVoucherId, CancellationToken ct = default);
        Task FinalizeRevisionAsync(int newVoucherId, int oldVoucherId, CancellationToken ct = default);

        // Void workflow
        Task VoidAsync(int voucherId, string reason, CancellationToken ct = default);

        Task<VoucherEditLoadDto> LoadAsync(int voucherId, CancellationToken ct = default);

        /// <summary>
        /// Creates a new voucher (Id > 0 => update, else create),
        /// re-posts base GL, and enqueues outbox (before final save).
        /// Returns persisted voucher Id.
        /// </summary>
        Task<int> SaveAsync(VoucherEditLoadDto dto, CancellationToken ct = default);
    }
}

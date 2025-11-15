#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Persistence.Services
{
    /// <summary>
    /// Internal (Persistence-layer) GL posting interface that accepts an existing PosClientDbContext.
    /// Use inside other persistence services to participate in the same transaction/UoW.
    /// </summary>
    public interface IGlPostingServiceDb
    {
        // ----- Purchases -----
        Task PostPurchaseAsync(PosClientDbContext db, Purchase p, CancellationToken ct = default);
        Task PostPurchaseRevisionAsync(PosClientDbContext db, Purchase amended, decimal deltaGrand, CancellationToken ct = default);
        Task PostPurchaseReturnAsync(PosClientDbContext db, Purchase p, CancellationToken ct = default);

        Task PostPurchaseVoidAsync(PosClientDbContext db, Purchase p, CancellationToken ct = default);
        Task PostPurchaseReturnVoidAsync(PosClientDbContext db, Purchase p, CancellationToken ct = default);

        // ----- Generic chain void helpers (optional, but handy for admin/ops) -----
        Task VoidChainAsync(Guid chainId, CancellationToken ct = default);
        Task VoidChainWithReversalsAsync(Guid chainId, DateTime tsUtc, bool invalidateOriginalsAfter = false, CancellationToken ct = default);

        Task PostTillCloseAsync(PosClientDbContext db, TillSession session, decimal declaredToMove, decimal systemCash, CancellationToken ct = default);
        Task PostPurchasePaymentAddedAsync(PosClientDbContext db, Purchase p, PurchasePayment pay, CancellationToken ct);
        Task PostPurchasePaymentReversalAsync(PosClientDbContext db, Purchase p, PurchasePayment oldPay, CancellationToken ct);


    }
}

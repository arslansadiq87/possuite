using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models.Purchases;

namespace Pos.Domain.Services
{
    public interface IPurchasesService
    {
        // Draft & receive
        Task<Purchase> SaveDraftAsync(Purchase draft, IEnumerable<PurchaseLine> lines, string? user = null, CancellationToken ct = default);
        Task<Purchase> ReceiveAsync(Purchase model, IEnumerable<PurchaseLine> lines, string? user = null, CancellationToken ct = default);
        Task<Purchase> FinalizeReceiveAsync(
            Purchase purchase,
            IEnumerable<PurchaseLine> lines,
            IEnumerable<(TenderMethod method, decimal amount, string? note)> onReceivePayments,
            int outletId, int supplierId, int? tillSessionId, int? counterId, string user,
            CancellationToken ct = default);

        // Returns
        Task<PurchaseReturnDraft> BuildReturnDraftAsync(int originalPurchaseId, CancellationToken ct = default);
        Task<Purchase> SaveReturnAsync(Purchase model, IEnumerable<PurchaseLine> lines, string? user = null, CancellationToken ct = default);
        Task<Purchase> SaveReturnAsync(Purchase model, IEnumerable<PurchaseLine> lines, string? user,
                                       IEnumerable<SupplierRefundSpec>? refunds, int? tillSessionId, int? counterId, CancellationToken ct = default);

        // Queries
        Task<List<Purchase>> ListHeldAsync(CancellationToken ct = default);
        Task<List<Purchase>> ListPostedAsync(CancellationToken ct = default);
        Task<Purchase> LoadWithLinesAsync(int id, CancellationToken ct = default);
        Task<Purchase?> LoadDraftWithLinesAsync(int id, CancellationToken ct = default);
        Task<Purchase?> LoadReturnWithLinesAsync(int returnId, CancellationToken ct = default);

        // Pickers/helpers
        Task<(decimal unitCost, decimal discount, decimal taxRate)?> GetLastPurchaseDefaultsAsync(int itemId, CancellationToken ct = default);
        Task<decimal> GetOnHandAsync(int itemId, StockTargetType target, int? outletId, int? warehouseId, CancellationToken ct = default);
        Task<string?> GetPartyNameAsync(int partyId, CancellationToken ct = default);
        Task<Dictionary<int, (string sku, string name)>> GetItemsMetaAsync(IEnumerable<int> itemIds, CancellationToken ct = default);
        Task<List<PurchaseLineEffective>> GetEffectiveLinesAsync(int purchaseId, CancellationToken ct = default);
        Task<decimal> GetRemainingReturnableQtyAsync(int purchaseLineId, CancellationToken ct = default);

        // Payments
        Task<(Purchase purchase, List<PurchasePayment> payments)> GetWithPaymentsAsync(int purchaseId, CancellationToken ct = default);

        Task<PurchasePayment> AddPaymentAsync(int purchaseId, PurchasePaymentKind kind, TenderMethod method,
            decimal amount, string? note, int outletId, int supplierId, int? tillSessionId, int? counterId,
            string user, int? bankAccountId = null, CancellationToken ct = default);
        Task UpdatePaymentAsync(int paymentId, decimal newAmount, TenderMethod newMethod, string? newNote, string user, CancellationToken ct = default);
        Task RemovePaymentAsync(int paymentId, string user, CancellationToken ct = default);

        Task<bool> IsPurchaseBankConfiguredAsync(int outletId, CancellationToken ct = default);
        Task<List<Account>> ListBankAccountsForOutletAsync(int outletId, CancellationToken ct = default);
        Task<int?> GetConfiguredPurchaseBankAccountIdAsync(int outletId, CancellationToken ct = default);

        // Void
        Task VoidPurchaseAsync(int purchaseId, string reason, string? user = null, CancellationToken ct = default);
        Task VoidReturnAsync(int returnId, string reason, string? user = null, CancellationToken ct = default);
    }
}

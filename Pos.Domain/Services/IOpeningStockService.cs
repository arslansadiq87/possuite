// Pos.Domain/Services/IOpeningStockService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain;                  // InventoryLocationType, StockDocStatus, etc.
using Pos.Domain.Entities;         // StockDoc
using Pos.Domain.Models.OpeningStock; // DTOs moved here per DTO policy

namespace Pos.Domain.Services
{
    /// <summary>
    /// Opening Stock flow (Outlet & Warehouse):
    /// Draft -> Post (writes StockEntries IN) -> Lock -> (optional) Unlock -> Void.
    /// </summary>
    public interface IOpeningStockService
    {
        // Draft lifecycle
        Task<StockDoc> CreateDraftAsync(OpeningStockCreateRequest req, CancellationToken ct = default);
        Task DeleteDraftAsync(int stockDocId, CancellationToken ct = default);

        // Draft content & validation
        Task<OpeningStockValidationResult> ValidateLinesAsync(
            int stockDocId,
            IEnumerable<OpeningStockLineDto> lines,
            CancellationToken ct = default);

        Task UpsertLinesAsync(OpeningStockUpsertRequest req, CancellationToken ct = default);

        // Transitions
        Task PostAsync(int stockDocId, int postedByUserId, CancellationToken ct = default);
        Task LockAsync(int stockDocId, int adminUserId, CancellationToken ct = default);
        Task UnlockAsync(int stockDocId, int adminUserId, CancellationToken ct = default);
        Task VoidAsync(int stockDocId, int userId, string? reason, CancellationToken ct = default);

        // Reads
        Task<StockDoc?> GetAsync(int stockDocId, CancellationToken ct = default);

        // Latest draft for a location (with UI-ready lines)
        Task<(StockDoc? Doc, List<(int ItemId, string Sku, string Display, decimal Qty, decimal UnitCost, string? Note)> Lines)>
            GetLatestDraftForLocationAsync(InventoryLocationType locationType, int locationId, CancellationToken ct = default);

        // Read any doc (Draft/Posted/Locked/Void) as UI lines + doc meta
        Task<(StockDoc Doc, List<(int ItemId, string Sku, string Display, decimal Qty, decimal UnitCost, string? Note)> Lines)>
            ReadDocumentForUiAsync(int stockDocId, CancellationToken ct = default);

        // Item display helpers (UI conveniences)
        Task<(string Sku, string Display)> GetItemDisplayByIdAsync(int itemId, CancellationToken ct = default);
        Task<Dictionary<string, string>> GetDisplayBySkuAsync(IEnumerable<string> skus, CancellationToken ct = default);

        // Lists for CSV/templates
        Task<List<(string Sku, string Display)>> GetAllActiveItemDisplaysAsync(CancellationToken ct = default);
        Task<List<(string Sku, string Display)>> GetMissingOpeningItemDisplaysAsync(
            InventoryLocationType locationType, int locationId, CancellationToken ct = default);

        // Header maintenance
        Task UpdateEffectiveDateAsync(int stockDocId, DateTime effectiveDateLocal, CancellationToken ct = default);

        /// <summary>
        /// Returns summaries for Opening Stock documents at a location.
        /// If statusFilter is provided, only that status is returned (e.g., Draft or Locked).
        /// Draft aggregates from draft lines; non-draft from posted ledger (Opening IN).
        /// </summary>
        Task<List<OpeningDocSummaryDto>> GetOpeningDocSummariesAsync(
            InventoryLocationType locationType,
            int locationId,
            StockDocStatus? statusFilter = null,
            CancellationToken ct = default);
    }
}

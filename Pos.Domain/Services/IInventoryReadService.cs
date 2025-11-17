using System;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface IInventoryReadService
    {
        /// <summary>
        /// On-hand strictly before <paramref name="cutoffUtc"/> at a location.
        /// </summary>
        Task<decimal> GetOnHandAsync(
            int itemId,
            InventoryLocationType locType,
            int locId,
            DateTime cutoffUtc,
            CancellationToken ct = default);

        /// <summary>
        /// Convenience overload that uses now if <paramref name="atUtc"/> is null.
        /// </summary>
        Task<decimal> GetOnHandAtLocationAsync(
            int itemId,
            InventoryLocationType locType,
            int locId,
            DateTime? atUtc = null,
            CancellationToken ct = default);
        /// Bulk on-hand strictly before cutoffUtc for many items at one location.
        /// Returns a dictionary for itemIds provided; items not present get 0.
        Task<Dictionary<int, decimal>> GetOnHandBulkAsync(
            IEnumerable<int> itemIds,
            InventoryLocationType locType,
            int locId,
            DateTime cutoffUtc,
            CancellationToken ct = default);

        /// Convenience bulk overload; uses DateTime.UtcNow if atUtc is null.
        Task<Dictionary<int, decimal>> GetOnHandBulkAsync(
            IEnumerable<int> itemIds,
            InventoryLocationType locType,
            int locId,
            DateTime? atUtc = null,
            CancellationToken ct = default);

        // NEW: centralized “available for issue” (on-hand minus staged), clamped to >= 0
        Task<decimal> GetAvailableForIssueAsync(
            int itemId,
            InventoryLocationType locType,
            int locId,
            DateTime cutoffUtc,
            decimal stagedUi = 0m,
            CancellationToken ct = default);

        Task<Dictionary<int, decimal>> GetAvailableForIssueBulkAsync(
            IEnumerable<(int itemId, decimal stagedUi)> items,
            InventoryLocationType locType,
            int locId,
            DateTime cutoffUtc,
            CancellationToken ct = default);

        Task<decimal> GetMovingAverageCostAsync(
            int itemId,
            InventoryLocationType locType,
            int locId,
            DateTime cutoffUtc,
            CancellationToken ct = default);
    }
}

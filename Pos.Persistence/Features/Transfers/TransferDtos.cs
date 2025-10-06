using System;
using System.Collections.Generic;

namespace Pos.Persistence.Features.Transfers
{
    public sealed class TransferLineDto
    {
        public int ItemId { get; set; }
        public decimal QtyExpected { get; set; }    // > 0
        public string? Remarks { get; set; }
    }

    public sealed class ReceiveLineDto
    {
        public int LineId { get; set; }             // existing StockDocLine.Id
        public decimal QtyReceived { get; set; }    // >= 0
        public string? VarianceNote { get; set; }
    }

    public interface ITransferService
    {
        // Create draft with From & To
        System.Threading.Tasks.Task<Pos.Domain.Entities.StockDoc> CreateDraftAsync(
            Pos.Domain.Entities.InventoryLocationType fromType, int fromId,
            Pos.Domain.Entities.InventoryLocationType toType, int toId,
            DateTime effectiveDateUtc,
            int createdByUserId);

        // Merge or replace lines
        System.Threading.Tasks.Task<Pos.Domain.Entities.StockDoc> UpsertLinesAsync(
            int stockDocId,
            IReadOnlyList<TransferLineDto> lines,
            bool replaceAll);

        // Lock From (OUT entries + numbering + cost snapshot)
        System.Threading.Tasks.Task<Pos.Domain.Entities.StockDoc> DispatchAsync(
            int stockDocId,
            DateTime effectiveDateUtc,
            int actedByUserId);

        // Lock To (IN entries, partials/overage, finalize)
        System.Threading.Tasks.Task<Pos.Domain.Entities.StockDoc> ReceiveAsync(
            int stockDocId,
            DateTime receivedAtUtc,
            IReadOnlyList<ReceiveLineDto> lines,
            int actedByUserId);

        System.Threading.Tasks.Task<Pos.Domain.Entities.StockDoc?> GetAsync(int stockDocId);
    }
}

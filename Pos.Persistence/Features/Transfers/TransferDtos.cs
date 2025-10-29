using System;
using System.Collections.Generic;
using Pos.Domain.Entities;

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
        Task<StockDoc> DispatchAsync(int stockDocId, DateTime effectiveDateUtc, int actedByUserId, bool autoReceive);

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
        
        // Undo a mistaken dispatch (reverses OUT ledger, returns to Draft)
        System.Threading.Tasks.Task<Pos.Domain.Entities.StockDoc> UndoDispatchAsync(
            int stockDocId,
            DateTime effectiveDateUtc,
            int actedByUserId,
            string? reason = null);

        // Undo a completed receive: remove IN ledger, return transfer to Dispatched
        System.Threading.Tasks.Task<Pos.Domain.Entities.StockDoc> UndoReceiveAsync(
            int stockDocId,
            DateTime effectiveDateUtc,
            int actedByUserId,
            string? reason = null);

        // Lock To (IN entries, partials/overage, finalize)
        System.Threading.Tasks.Task<Pos.Domain.Entities.StockDoc> ReceiveAsync(
            int stockDocId,
            DateTime receivedAtUtc,
            IReadOnlyList<ReceiveLineDto> lines,
            int actedByUserId);

        System.Threading.Tasks.Task<Pos.Domain.Entities.StockDoc?> GetAsync(int stockDocId);


    }
}

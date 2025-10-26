// Pos.Persistence.Features.OpeningStock/IOpeningStockService.cs
using Pos.Domain.Entities;
using Pos.Persistence.Features.OpeningStock;
using System.ComponentModel.DataAnnotations;

public interface IOpeningStockService
{
    Task<StockDoc> CreateDraftAsync(OpeningStockCreateRequest req, CancellationToken ct = default);
    Task<OpeningStockValidationResult> ValidateLinesAsync(int stockDocId, IEnumerable<OpeningStockLineDto> lines, CancellationToken ct = default);
    Task UpsertLinesAsync(OpeningStockUpsertRequest req, CancellationToken ct = default);
    Task LockAsync(int stockDocId, int adminUserId, CancellationToken ct = default);
    Task UnlockAsync(int stockDocId, int adminUserId, CancellationToken ct = default);
    Task<StockDoc?> GetAsync(int stockDocId, CancellationToken ct = default);
}

using Pos.Domain.DTO;

namespace Pos.Domain.Services
{
    public interface IItemsReadService
    {
        Task<List<ItemIndexDto>> BuildIndexAsync();
        Task<ItemIndexDto?> FindOneAsync(string text);
        Task<Dictionary<int, (string display, string sku)>> GetDisplayMetaAsync(
            IEnumerable<int> itemIds, CancellationToken ct = default);
        Task<(string display, string sku, decimal? lastCost)?> GetItemMetaForReturnAsync(
    int itemId, CancellationToken ct = default);

    }
}

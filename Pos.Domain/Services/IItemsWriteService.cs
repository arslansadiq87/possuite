// Pos.Domain/Services/IItemsWriteService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models; // ItemVariantRow

namespace Pos.Domain.Services
{
    /// <summary>
    /// Authoritative write surface for Items/Variants and their barcodes.
    /// </summary>
    public interface IItemsWriteService
    {
        // Create/update items
        Task<Item> CreateItemAsync(Item i, CancellationToken ct = default);
        Task<Item> UpdateItemAsync(Item updated, CancellationToken ct = default);

        // Bulk create variants for a product
        Task<List<Item>> BulkCreateVariantsAsync(
            int productId,
            string itemBaseName,
            string axis1Name, IEnumerable<string> axis1Values,
            string axis2Name, IEnumerable<string> axis2Values,
            decimal price, string? taxCode, decimal taxPct, bool taxInclusive,
            decimal? defDiscPct, decimal? defDiscAmt,
            CancellationToken ct = default);

        // Edit + barcodes
        Task<ItemVariantRow> EditSingleItemAsync(Item editedWithBarcodes, CancellationToken ct = default);
        Task<(Item item, string? primaryCode)> ReplaceBarcodesAsync(int itemId, IEnumerable<ItemBarcode> newBarcodes, CancellationToken ct = default);

        // Batch row save (used by grids)
        Task<List<ItemVariantRow>> SaveVariantRowsAsync(IEnumerable<ItemVariantRow> rows, CancellationToken ct = default);
    }
}

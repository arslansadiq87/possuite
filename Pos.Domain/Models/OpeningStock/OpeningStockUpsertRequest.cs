using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pos.Domain.Models.OpeningStock
{
    public sealed class OpeningStockUpsertRequest
    {
        [Required] public int StockDocId { get; set; }

        [Required] public List<OpeningStockLineDto> Lines { get; set; } = new();

        /// <summary>
        /// True = replace all existing lines, false = merge/add by SKU
        /// </summary>
        public bool ReplaceAll { get; set; } = true;
    }
}

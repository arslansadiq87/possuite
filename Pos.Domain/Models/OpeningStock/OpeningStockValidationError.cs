using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pos.Domain.Models.OpeningStock
{
    public sealed class OpeningStockValidationError
    {
        public int? RowIndex { get; set; }
        public string Field { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Sku { get; set; }
    }
}

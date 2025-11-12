using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pos.Domain.Models.OpeningStock
{
    public sealed class OpeningStockLineDto
    {
        [Required] public string Sku { get; set; } = "";

        [Range(typeof(decimal), "0.0001", "9999999999", ErrorMessage = "Qty must be greater than zero.")]
        public decimal Qty { get; set; } // > 0

        [Range(typeof(decimal), "0.0000", "9999999999", ErrorMessage = "Invalid Unit Cost.")]
        public decimal UnitCost { get; set; } // required, 4dp

        public string? Note { get; set; }
    }
}

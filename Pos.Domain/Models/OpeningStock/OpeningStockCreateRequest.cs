using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Models.OpeningStock
{
    public sealed class OpeningStockCreateRequest
    {
        [Required] public InventoryLocationType LocationType { get; set; }
        [Required] public int LocationId { get; set; }

        [Required] public DateTime EffectiveDateUtc { get; set; }

        public string? Note { get; set; }

        [Required] public int CreatedByUserId { get; set; }
    }
}

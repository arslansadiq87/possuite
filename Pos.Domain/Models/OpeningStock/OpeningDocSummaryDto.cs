using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Models.OpeningStock
{
    public sealed class OpeningDocSummaryDto
    {
        public int Id { get; init; }
        public DateTime EffectiveDateUtc { get; init; }
        public int LineCount { get; init; }
        public decimal TotalQty { get; init; }
        public decimal TotalValue { get; init; }
        public string? Note { get; init; }
        public StockDocStatus Status { get; init; }
    }
}

// Pos.Domain/Models/OpeningStock/OpeningStockValidationResult.cs
using System.Collections.Generic;
using System.Linq;

namespace Pos.Domain.Models.OpeningStock
{
    public sealed class OpeningStockValidationResult
    {
        public List<OpeningStockValidationError> Errors { get; } = new();
        public bool Ok => Errors.Count == 0;
    }
}

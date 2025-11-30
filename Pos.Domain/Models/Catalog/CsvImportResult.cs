// Pos.Domain/Models/Catalog/CsvImportResult.cs
using System.Collections.Generic;

namespace Pos.Domain.Models.Catalog
{
    public sealed class CsvImportResult
    {
        public List<CsvImportRow> Rows { get; set; } = new();
        public int ValidCount { get; set; }
        public int ErrorCount { get; set; }
    }
}

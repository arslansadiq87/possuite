using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class ReceiptProfile : BaseEntity
    {
        // keep Id from BaseEntity
        public int OutletId { get; set; }

        // Defaults (used if a language override is missing)
        public string OutletName { get; set; } = "";
        public string Address { get; set; } = "";
        public string Phone { get; set; } = "";
        public string TaxOrRegNo { get; set; } = "";
        public string DefaultLanguage { get; set; } = "en";

        public ICollection<ReceiptProfileText> Texts { get; set; } = new List<ReceiptProfileText>();
    }

    public class ReceiptProfileText : BaseEntity // optional but recommended
    {
        public int ReceiptProfileId { get; set; }
        public string Language { get; set; } = "en";

        // Optional per-language overrides (fallback to parent defaults)
        public string? OutletName { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }

        public string Footer { get; set; } = "";

        public ReceiptProfile? Profile { get; set; }
    }
}

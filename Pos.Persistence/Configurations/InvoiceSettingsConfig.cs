// Pos.Persistence/Configurations/InvoiceLocalizationConfig.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Entities;

namespace Pos.Persistence.Configurations;

public class InvoiceLocalizationConfig : IEntityTypeConfiguration<InvoiceLocalization>
{
    public void Configure(EntityTypeBuilder<InvoiceLocalization> b)
    {
        b.ToTable("InvoiceLocalizations");
        b.HasKey(x => x.Id);

        b.Property(x => x.Lang).HasMaxLength(16).IsRequired();

        // Prevent duplicates per settings+lang
        b.HasIndex(x => new { x.InvoiceSettingsId, x.Lang }).IsUnique();
    }
}

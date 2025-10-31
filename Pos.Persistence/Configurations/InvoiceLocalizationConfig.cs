// Pos.Persistence/Configurations/InvoiceSettingsConfig.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Entities;

namespace Pos.Persistence.Configurations;

public class InvoiceSettingsConfig : IEntityTypeConfiguration<InvoiceSettings>
{
    public void Configure(EntityTypeBuilder<InvoiceSettings> b)
    {
        b.ToTable("InvoiceSettings");
        b.HasKey(x => x.Id);

        b.HasIndex(x => x.OutletId).IsUnique(false);

        b.Property(x => x.OutletDisplayName).HasMaxLength(200);
        b.Property(x => x.AddressLine1).HasMaxLength(200);
        b.Property(x => x.AddressLine2).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.PrinterName).HasMaxLength(200);

        b.HasMany(x => x.Localizations)
         .WithOne(x => x.InvoiceSettings)
         .HasForeignKey(x => x.InvoiceSettingsId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
using Mercadex.Domain.Cobranza;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercadex.Infrastructure.Persistence.Configurations;

public sealed class ConceptoCobroConfiguration :
    IEntityTypeConfiguration<ConceptoCobro>
{
    public void Configure(
        EntityTypeBuilder<ConceptoCobro> builder)
    {
        builder.ToTable("conceptos_cobro");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Codigo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Nombre)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Descripcion)
            .HasMaxLength(500);

        builder.Property(x => x.Activo)
            .IsRequired();

        // Número para los reportes: único entre los que lo tienen.
        builder.HasIndex(x => x.Numero)
            .HasDatabaseName("UX_conceptos_cobro_numero")
            .IsUnique()
            .HasFilter("\"Numero\" IS NOT NULL");

        builder.HasIndex(x => x.Codigo)
            .IsUnique();

        builder.HasIndex(x => x.Nombre);
    }
}

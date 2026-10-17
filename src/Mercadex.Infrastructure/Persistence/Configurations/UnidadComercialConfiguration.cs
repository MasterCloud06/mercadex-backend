using Mercadex.Domain.Organizacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercadex.Infrastructure.Persistence.Configurations;

public sealed class UnidadComercialConfiguration :
    IEntityTypeConfiguration<UnidadComercial>
{
    public void Configure(
        EntityTypeBuilder<UnidadComercial> builder)
    {
        builder.ToTable("unidades_comerciales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Codigo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Tipo)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.Bloque)
            .HasMaxLength(50);

        builder.Property(x => x.Activa)
            .IsRequired();

        builder.Property(x => x.FechaRegistro)
            .IsRequired();

        builder.HasIndex(x => x.Codigo)
            .IsUnique();

        // PlanoX / PlanoY: posición en el plano del mercado (int, opcionales).

        builder.HasOne<Pasillo>()
            .WithMany()
            .HasForeignKey(x => x.PasilloId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

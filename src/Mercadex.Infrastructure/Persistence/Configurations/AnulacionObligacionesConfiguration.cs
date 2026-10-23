using Mercadex.Domain.Cobranza;
using Mercadex.Domain.Organizacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercadex.Infrastructure.Persistence.Configurations;

public sealed class AnulacionObligacionesConfiguration :
    IEntityTypeConfiguration<AnulacionObligaciones>
{
    public void Configure(
        EntityTypeBuilder<AnulacionObligaciones> builder)
    {
        builder.ToTable("anulaciones_obligaciones");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.ClaveOperacion)
            .IsRequired();

        builder.Property(x => x.Tipo)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.Motivo)
            .HasMaxLength(AnulacionObligaciones.MotivoMaximo)
            .IsRequired();

        builder.Property(x => x.Responsable)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.ResponsableId)
            .HasMaxLength(120);

        builder.Property(x => x.Filtros)
            .HasMaxLength(AnulacionObligaciones.FiltrosMaximo);

        builder.Property(x => x.FechaUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.Cantidad)
            .IsRequired();

        // La misma confirmación no se aplica dos veces.
        builder.HasIndex(x => x.ClaveOperacion)
            .IsUnique();

        builder.HasIndex(x => x.FechaUtc);

        builder.HasMany(x => x.Detalles)
            .WithOne()
            .HasForeignKey(x => x.AnulacionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Detalles)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class DetalleAnulacionObligacionConfiguration :
    IEntityTypeConfiguration<DetalleAnulacionObligacion>
{
    public void Configure(
        EntityTypeBuilder<DetalleAnulacionObligacion> builder)
    {
        builder.ToTable("detalles_anulacion_obligacion");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Monto)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.PeriodoAnio);

        builder.Property(x => x.PeriodoMes);

        builder.Property(x => x.EstadoAnterior)
            .HasConversion<int>()
            .IsRequired();

        builder.HasOne<Obligacion>()
            .WithMany()
            .HasForeignKey(x => x.ObligacionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnidadComercial>()
            .WithMany()
            .HasForeignKey(x => x.UnidadComercialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ConceptoCobro>()
            .WithMany()
            .HasForeignKey(x => x.ConceptoCobroId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Moneda>()
            .WithMany()
            .HasForeignKey(x => x.MonedaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Una deuda solo puede anularse una vez: la base rechaza
        // un segundo registro aunque dos anulaciones lleguen a la vez.
        builder.HasIndex(x => x.ObligacionId)
            .IsUnique();

        builder.HasIndex(x => x.UnidadComercialId);
    }
}

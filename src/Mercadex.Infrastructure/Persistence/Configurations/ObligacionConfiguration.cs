using Mercadex.Domain.Cobranza;
using Mercadex.Domain.Organizacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercadex.Infrastructure.Persistence.Configurations;

public sealed class ObligacionConfiguration :
    IEntityTypeConfiguration<Obligacion>
{
    public void Configure(
        EntityTypeBuilder<Obligacion> builder)
    {
        builder.ToTable("obligaciones");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Monto)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.FechaEmision)
            .IsRequired();

        builder.Property(x => x.FechaVencimiento);

        builder.Property(x => x.PeriodoAnio);

        builder.Property(x => x.PeriodoMes);

        builder.Property(x => x.Estado)
            .HasConversion<int>()
            .IsRequired();

        builder.Ignore(x => x.EstaPendiente);
        builder.Ignore(x => x.EstaPagada);
        builder.Ignore(x => x.EstaAnulada);

        // Una obligacion vigente (pendiente o pagada) por caseta, concepto y
        // periodo. No afecta obligaciones sin periodo mensual. Las anuladas
        // (estado 3) quedan fuera: se puede volver a registrar a mano una
        // deuda anulada; el cargo mensual masivo igual las respeta.
        builder.HasIndex(x => new
            {
                x.UnidadComercialId,
                x.ConceptoCobroId,
                x.PeriodoAnio,
                x.PeriodoMes
            })
            .HasDatabaseName(
                "UX_obligaciones_unidad_concepto_periodo")
            .IsUnique()
            .HasFilter(
                "\"PeriodoAnio\" IS NOT NULL AND \"PeriodoMes\" IS NOT NULL AND \"Estado\" <> 3");


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

        builder.HasIndex(x =>
            new
            {
                x.UnidadComercialId,
                x.Estado
            });

        builder.HasIndex(x =>
            new
            {
                x.ConceptoCobroId,
                x.PeriodoAnio,
                x.PeriodoMes
            });
    }
}

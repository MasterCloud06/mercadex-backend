using Mercadex.Domain.Caja;
using Mercadex.Domain.Organizacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercadex.Infrastructure.Persistence.Configurations;

public sealed class ReciboConfiguration :
    IEntityTypeConfiguration<Recibo>
{
    public void Configure(
        EntityTypeBuilder<Recibo> builder)
    {
        builder.ToTable("recibos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Numero)
            .IsRequired();

        builder.Property(x => x.FechaEmision)
            .IsRequired();

        builder.Property(x => x.Estado)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.MotivoAnulacion)
            .HasMaxLength(500);

        builder.Property(x => x.FechaAnulacion);
builder.Property(x => x.RegistradoPor)
            .HasMaxLength(120);

        builder.Property(x => x.CargoEmisor)
            .HasMaxLength(80);

        builder.Property(x => x.RegistradoPorId)
            .HasMaxLength(64);

        builder.Property(x => x.Observaciones)
            .HasMaxLength(Recibo.ObservacionesMaximo);

        builder.Property(x => x.AnuladoPor)
    .HasMaxLength(150);

        builder.Ignore(x => x.Total);
        builder.Ignore(x => x.EstaAnulado);

        builder.HasIndex(x => x.Numero)
            .IsUnique();

        builder.HasIndex(x => x.FechaEmision);

        builder.HasOne<UnidadComercial>()
            .WithMany()
            .HasForeignKey(x => x.UnidadComercialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Pago>()
            .WithOne()
            .HasForeignKey<Recibo>(x => x.PagoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Detalles)
            .WithOne()
            .HasForeignKey("ReciboId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Detalles)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

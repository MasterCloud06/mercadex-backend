using Mercadex.Domain.Socios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercadex.Infrastructure.Persistence.Configurations;

public sealed class SocioConfiguration :
    IEntityTypeConfiguration<Socio>
{
    public void Configure(
        EntityTypeBuilder<Socio> builder)
    {
        builder.ToTable("socios");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Nombres)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Apellidos)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.DocumentoIdentidad)
            .HasMaxLength(50);

        builder.Property(x => x.Telefono)
            .HasMaxLength(50);

        builder.Property(x => x.Activo)
            .IsRequired();

        builder.Property(x => x.FechaRegistro)
            .IsRequired();

        builder.Ignore(x => x.NombreCompleto);

        builder.Ignore(x => x.EsSocio);
        builder.Property(x => x.LugarExpedicion).HasMaxLength(30);

        builder.HasIndex(x => x.DocumentoIdentidad);
    }
}

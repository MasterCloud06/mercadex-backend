using Mercadex.Domain.Comunicacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercadex.Infrastructure.Persistence.Configurations;

public sealed class ComunicadoConfiguration : IEntityTypeConfiguration<Comunicado>
{
    public void Configure(EntityTypeBuilder<Comunicado> builder)
    {
        builder.ToTable("comunicados");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Titulo).HasMaxLength(Comunicado.MaximoTitulo).IsRequired();
        builder.Property(x => x.Texto).HasMaxLength(Comunicado.MaximoTexto).IsRequired();
        builder.Property(x => x.Autor).HasMaxLength(120);
        builder.Property(x => x.FechaUtc).IsRequired();
        builder.HasIndex(x => x.FechaUtc);
    }
}

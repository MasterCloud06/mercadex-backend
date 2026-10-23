using Mercadex.Infrastructure.Persistence.Entities;
using Mercadex.Domain.Caja;
using Mercadex.Domain.Cobranza;
using Mercadex.Domain.Organizacion;
using Mercadex.Domain.Socios;
using Mercadex.Domain.Comercial;
using Microsoft.EntityFrameworkCore;
using Mercadex.Domain.Finanzas;
using Mercadex.Domain.AppSocios;
using Mercadex.Domain.Usuarios;
using Mercadex.Domain.Auditoria;

namespace Mercadex.Infrastructure.Persistence;

public sealed class MercadexDbContext : DbContext
{
    public MercadexDbContext(
        DbContextOptions<MercadexDbContext> options)
        : base(options)
    {
    }


    public DbSet<Socio> Socios =>
        Set<Socio>();

    public DbSet<DocumentoKardex> DocumentosKardex =>
        Set<DocumentoKardex>();


    public DbSet<Pasillo> Pasillos =>
        Set<Pasillo>();


    public DbSet<UnidadComercial> UnidadesComerciales =>
        Set<UnidadComercial>();


    public DbSet<AsignacionUnidadComercial> AsignacionesUnidades =>
        Set<AsignacionUnidadComercial>();


    public DbSet<AsignacionSocioCaseta> AsignacionesSocioCaseta =>
        Set<AsignacionSocioCaseta>();


    public DbSet<ConceptoCobro> ConceptosCobro =>
        Set<ConceptoCobro>();


    public DbSet<Moneda> Monedas =>
        Set<Moneda>();


    public DbSet<Obligacion> Obligaciones =>
        Set<Obligacion>();

    public DbSet<CambioMontoObligacion> CambiosMontoObligacion =>
        Set<CambioMontoObligacion>();

    public DbSet<TarifaUnidad> TarifasUnidad =>
        Set<TarifaUnidad>();


    public DbSet<Pago> Pagos =>
        Set<Pago>();


    public DbSet<Recibo> Recibos =>
        Set<Recibo>();

    public DbSet<ComprobanteAnulacionRecibo> ComprobantesAnulacion =>
        Set<ComprobanteAnulacionRecibo>();

    public DbSet<CierreCaja> CierresCaja =>
        Set<CierreCaja>();

    public DbSet<RegistroCambio> RegistroCambios =>
        Set<RegistroCambio>();

    public DbSet<Mercadex.Domain.Comunicacion.Comunicado> Comunicados =>
        Set<Mercadex.Domain.Comunicacion.Comunicado>();


    public DbSet<DetalleRecibo> DetallesRecibo =>
        Set<DetalleRecibo>();


    public DbSet<KardexCaseta> KardexCasetas =>
        Set<KardexCaseta>();

public DbSet<Egreso> Egresos =>
    Set<Egreso>();

    public DbSet<CuentaSocioApp> CuentasSocioApp =>
        Set<CuentaSocioApp>();

    public DbSet<PagoReportado> PagosReportados =>
        Set<PagoReportado>();

    public DbSet<ConfiguracionPagoApp> ConfiguracionPagoApp =>
        Set<ConfiguracionPagoApp>();

    public DbSet<Usuario> Usuarios =>
        Set<Usuario>();

    public DbSet<ArchivoKardex> ArchivosKardex =>
        Set<ArchivoKardex>();

    public DbSet<AnulacionObligaciones> AnulacionesObligaciones =>
        Set<AnulacionObligaciones>();

    public DbSet<DetalleAnulacionObligacion> DetallesAnulacionObligacion =>
        Set<DetalleAnulacionObligacion>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);


        // Secuencia para el número de comprobante de egreso.
        modelBuilder.HasSequence<long>("numeracion_egresos")
            .StartsAt(1)
            .IncrementsBy(1);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(MercadexDbContext).Assembly);
    }
}

using Mercadex.Domain.Caja;
using Mercadex.Domain.Cobranza;

namespace Mercadex.Domain.Tests.Cobranza;

public sealed class AnulacionObligacionesTests
{
    private const string Motivo = "Caseta cerrada por remodelación del bloque";

    private static Obligacion CrearObligacion(
        decimal monto = 150m,
        Guid? unidadId = null,
        Guid? monedaId = null)
    {
        return new Obligacion(
            unidadId ?? Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            monedaId ?? Guid.CreateVersion7(),
            monto,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31),
            2026,
            10);
    }

    private static AnulacionObligaciones Anular(
        IEnumerable<Obligacion> obligaciones,
        TipoAnulacionObligaciones tipo = TipoAnulacionObligaciones.Masiva,
        string motivo = Motivo,
        Guid? clave = null)
    {
        return ServicioAnulacionObligaciones.Anular(
            obligaciones,
            clave ?? Guid.CreateVersion7(),
            tipo,
            motivo,
            "Ana Quispe",
            "usuario-1",
            "Concepto: Alquiler de caseta · Período: 10/2026",
            DateTime.UtcNow);
    }

    [Fact]
    public void Anular_Pendiente_DebeQuedarAnuladaSinCambiarMonto()
    {
        var obligacion = CrearObligacion(175.50m);

        obligacion.Anular();

        Assert.True(obligacion.EstaAnulada);
        Assert.False(obligacion.EstaPendiente);
        Assert.Equal(EstadoObligacion.Anulada, obligacion.Estado);
        Assert.Equal(175.50m, obligacion.Monto);
        Assert.Equal(2026, obligacion.PeriodoAnio);
        Assert.Equal(10, obligacion.PeriodoMes);
    }

    [Fact]
    public void Anular_Pagada_DebeRechazar()
    {
        var obligacion = CrearObligacion();
        obligacion.MarcarComoPagada();

        Assert.Throws<InvalidOperationException>(() => obligacion.Anular());

        Assert.True(obligacion.EstaPagada);
    }

    [Fact]
    public void Anular_DosVeces_DebeRechazar()
    {
        var obligacion = CrearObligacion();
        obligacion.Anular();

        Assert.Throws<InvalidOperationException>(() => obligacion.Anular());
    }

    [Fact]
    public void Anulada_NoSePuedeCobrarCorregirNiRestaurar()
    {
        var obligacion = CrearObligacion();
        obligacion.Anular();

        Assert.Throws<InvalidOperationException>(() => obligacion.MarcarComoPagada());
        Assert.Throws<InvalidOperationException>(() => obligacion.CorregirMonto(200m));
        Assert.Throws<InvalidOperationException>(() => obligacion.RestaurarComoPendiente());

        Assert.True(obligacion.EstaAnulada);
        Assert.Equal(150m, obligacion.Monto);
    }

    [Fact]
    public void Anulada_NoSePuedeCobrarEnCaja()
    {
        var obligacion = CrearObligacion();
        obligacion.Anular();

        Assert.Throws<InvalidOperationException>(() => ServicioCobro.Registrar(
            1,
            new[] { new ItemCobro(obligacion, "Alquiler de caseta") },
            FormaPago.Efectivo,
            "Juan Mamani",
            DateTimeOffset.UtcNow));

        Assert.True(obligacion.EstaAnulada);
    }

    [Fact]
    public void Servicio_Masiva_DebeAnularTodasYGuardarCopia()
    {
        var monedaId = Guid.CreateVersion7();
        var deudas = new[]
        {
            CrearObligacion(100m, monedaId: monedaId),
            CrearObligacion(120m, monedaId: monedaId),
            CrearObligacion(80.25m, monedaId: monedaId)
        };

        var anulacion = Anular(deudas);

        Assert.All(deudas, d => Assert.True(d.EstaAnulada));
        Assert.Equal(3, anulacion.Cantidad);
        Assert.Equal(3, anulacion.Detalles.Count);
        Assert.Equal(TipoAnulacionObligaciones.Masiva, anulacion.Tipo);
        Assert.Equal(Motivo, anulacion.Motivo);
        Assert.Equal("Ana Quispe", anulacion.Responsable);
        Assert.Equal("usuario-1", anulacion.ResponsableId);
        Assert.Equal(300.25m, anulacion.Detalles.Sum(d => d.Monto));

        foreach (var deuda in deudas)
        {
            var detalle = Assert.Single(anulacion.Detalles, d => d.ObligacionId == deuda.Id);
            Assert.Equal(anulacion.Id, detalle.AnulacionId);
            Assert.Equal(deuda.Monto, detalle.Monto);
            Assert.Equal(deuda.UnidadComercialId, detalle.UnidadComercialId);
            Assert.Equal(deuda.ConceptoCobroId, detalle.ConceptoCobroId);
            Assert.Equal(deuda.MonedaId, detalle.MonedaId);
            Assert.Equal(deuda.PeriodoAnio, detalle.PeriodoAnio);
            Assert.Equal(deuda.PeriodoMes, detalle.PeriodoMes);
            Assert.Equal(EstadoObligacion.Pendiente, detalle.EstadoAnterior);
        }
    }

    [Fact]
    public void Servicio_ConUnaPagada_NoDebeAnularNinguna()
    {
        var pendiente = CrearObligacion();
        var pagada = CrearObligacion();
        pagada.MarcarComoPagada();

        Assert.Throws<InvalidOperationException>(() => Anular(new[] { pendiente, pagada }));

        Assert.True(pendiente.EstaPendiente);
        Assert.True(pagada.EstaPagada);
    }

    [Fact]
    public void Servicio_ConUnaYaAnulada_NoDebeAnularNinguna()
    {
        var pendiente = CrearObligacion();
        var anulada = CrearObligacion();
        anulada.Anular();

        Assert.Throws<InvalidOperationException>(() => Anular(new[] { pendiente, anulada }));

        Assert.True(pendiente.EstaPendiente);
    }

    [Fact]
    public void Servicio_DeudaRepetida_DebeRechazar()
    {
        var deuda = CrearObligacion();

        Assert.Throws<InvalidOperationException>(() => Anular(new[] { deuda, deuda }));

        Assert.True(deuda.EstaPendiente);
    }

    [Fact]
    public void Servicio_SinDeudas_DebeRechazar()
    {
        Assert.Throws<ArgumentException>(() => Anular(Array.Empty<Obligacion>()));
    }

    [Fact]
    public void Servicio_IndividualConVarias_DebeRechazar()
    {
        var deudas = new[] { CrearObligacion(), CrearObligacion() };

        Assert.Throws<InvalidOperationException>(
            () => Anular(deudas, TipoAnulacionObligaciones.Individual));

        Assert.All(deudas, d => Assert.True(d.EstaPendiente));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Servicio_SinMotivo_DebeAnularYDejarConstancia(string? motivo)
    {
        var deuda = CrearObligacion();

        var anulacion = ServicioAnulacionObligaciones.Anular(
            new[] { deuda }, Guid.CreateVersion7(), TipoAnulacionObligaciones.Individual,
            motivo, "Ana Quispe", "usuario-1", null, DateTime.UtcNow);

        Assert.True(deuda.EstaAnulada);
        Assert.Equal(AnulacionObligaciones.SinMotivo, anulacion.Motivo);
        Assert.Equal("Ana Quispe", anulacion.Responsable);
    }

    [Fact]
    public void Servicio_MotivoCorto_DebeAceptarse()
    {
        var deuda = CrearObligacion();

        var anulacion = Anular(new[] { deuda }, TipoAnulacionObligaciones.Individual, "Error");

        Assert.True(deuda.EstaAnulada);
        Assert.Equal("Error", anulacion.Motivo);
    }

    [Fact]
    public void Servicio_MotivoMuyLargo_DebeRechazar()
    {
        var deuda = CrearObligacion();

        Assert.Throws<ArgumentException>(() => Anular(
            new[] { deuda },
            TipoAnulacionObligaciones.Individual,
            new string('x', AnulacionObligaciones.MotivoMaximo + 1)));

        Assert.True(deuda.EstaPendiente);
    }

    [Fact]
    public void Servicio_ClaveVacia_DebeRechazar()
    {
        var deuda = CrearObligacion();

        Assert.Throws<ArgumentException>(() => Anular(
            new[] { deuda }, TipoAnulacionObligaciones.Individual, clave: Guid.Empty));

        Assert.True(deuda.EstaPendiente);
    }

    [Fact]
    public void Servicio_FechaNoUtc_DebeRechazar()
    {
        var deuda = CrearObligacion();

        Assert.Throws<ArgumentException>(() => ServicioAnulacionObligaciones.Anular(
            new[] { deuda },
            Guid.CreateVersion7(),
            TipoAnulacionObligaciones.Individual,
            Motivo,
            "Ana Quispe",
            null,
            null,
            DateTime.Now));

        Assert.True(deuda.EstaPendiente);
    }

    [Fact]
    public void Servicio_DebeLimpiarMotivoYResponsable()
    {
        var anulacion = ServicioAnulacionObligaciones.Anular(
            new[] { CrearObligacion() },
            Guid.CreateVersion7(),
            TipoAnulacionObligaciones.Individual,
            "   " + Motivo + "   ",
            "  Ana Quispe  ",
            "   ",
            "   ",
            DateTime.UtcNow);

        Assert.Equal(Motivo, anulacion.Motivo);
        Assert.Equal("Ana Quispe", anulacion.Responsable);
        Assert.Null(anulacion.ResponsableId);
        Assert.Null(anulacion.Filtros);
    }
}

using Mercadex.Domain.Caja;
using Mercadex.Domain.Cobranza;

namespace Mercadex.Domain.Tests.Caja;

public sealed class ObservacionesReciboTests
{
    private static Recibo CrearRecibo()
    {
        var obligacion = new Obligacion(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            150m, new DateOnly(2026, 10, 1), null, 2026, 10);

        return ServicioCobro.Registrar(
            1,
            new[] { new ItemCobro(obligacion, "Alquiler de caseta") },
            FormaPago.Efectivo,
            "Rocío Loayza",
            DateTimeOffset.UtcNow).Recibo;
    }

    [Fact]
    public void SinObservaciones_QuedaVacio()
    {
        var recibo = CrearRecibo();

        Assert.Null(recibo.Observaciones);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ObservacionesVacias_QuedanVacias(string? texto)
    {
        var recibo = CrearRecibo();

        recibo.AsignarObservaciones(texto);

        Assert.Null(recibo.Observaciones);
    }

    [Fact]
    public void Observaciones_SeGuardanSinEspaciosSobrantes()
    {
        var recibo = CrearRecibo();

        recibo.AsignarObservaciones("  Pagó con billetes de 200; queda saldo a favor de Bs 10  ");

        Assert.Equal("Pagó con billetes de 200; queda saldo a favor de Bs 10", recibo.Observaciones);
        Assert.Equal(150m, recibo.Total);
    }

    [Fact]
    public void ObservacionesMuyLargas_SeRechazan()
    {
        var recibo = CrearRecibo();

        Assert.Throws<ArgumentException>(() =>
            recibo.AsignarObservaciones(new string('x', Recibo.ObservacionesMaximo + 1)));

        Assert.Null(recibo.Observaciones);
    }
}

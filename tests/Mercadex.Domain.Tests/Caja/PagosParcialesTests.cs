using Mercadex.Domain.Caja;
using Mercadex.Domain.Cobranza;

namespace Mercadex.Domain.Tests.Caja;

/// <summary>Pagos parciales: abonos, saldo, recibos y su anulación.</summary>
public sealed class PagosParcialesTests
{
    private static readonly Guid Unidad = Guid.CreateVersion7();
    private static readonly Guid Moneda = Guid.CreateVersion7();

    private static Obligacion Deuda(decimal monto) =>
        new(Unidad, Guid.CreateVersion7(), Moneda, monto, new DateOnly(2026, 8, 1), null, 2026, 8);

    private static ResultadoCobro Cobrar(long numero, params ItemCobro[] items) =>
        ServicioCobro.Registrar(numero, items, FormaPago.Efectivo, "Socio", DateTimeOffset.UtcNow);

    [Fact]
    public void Un_abono_deja_la_deuda_pendiente_con_su_saldo()
    {
        var d = Deuda(750);

        var r = Cobrar(1, new ItemCobro(d, "TECHO-ESCOMBRO", 300));

        Assert.True(d.EstaPendiente);
        Assert.Equal(300m, d.MontoPagado);
        Assert.Equal(450m, d.Saldo);
        Assert.Equal(750m, d.Monto);
        Assert.Equal(300m, r.Pago.Monto);
        Assert.Equal(300m, r.Recibo.Total);
        Assert.Equal(300m, r.Recibo.Detalles.Single().Monto);
    }

    [Fact]
    public void Abonos_que_completan_el_monto_dejan_la_deuda_pagada()
    {
        var d = Deuda(750);

        Cobrar(1, new ItemCobro(d, "TECHO", 300));
        Cobrar(2, new ItemCobro(d, "TECHO"));

        Assert.True(d.EstaPagada);
        Assert.Equal(750m, d.MontoPagado);
        Assert.Equal(0m, d.Saldo);
    }

    [Fact]
    public void Sin_monto_se_paga_todo_el_saldo()
    {
        var d = Deuda(100);
        var r = Cobrar(1, new ItemCobro(d, "Mensualidad"));

        Assert.True(d.EstaPagada);
        Assert.Equal(100m, r.Recibo.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.555)]
    public void El_abono_debe_ser_positivo_y_con_dos_decimales(decimal monto)
    {
        var d = Deuda(100);
        Assert.Throws<ArgumentException>(() => Cobrar(1, new ItemCobro(d, "X", monto)));
        Assert.Equal(0m, d.MontoPagado);
    }

    [Fact]
    public void No_se_puede_pagar_mas_que_el_saldo()
    {
        var d = Deuda(100);
        Cobrar(1, new ItemCobro(d, "X", 60));

        Assert.Throws<InvalidOperationException>(() => Cobrar(2, new ItemCobro(d, "X", 41)));
        Assert.Equal(60m, d.MontoPagado);
    }

    [Fact]
    public void Anular_un_abono_devuelve_solo_lo_que_ese_recibo_pago()
    {
        var d = Deuda(750);
        var primero = Cobrar(1, new ItemCobro(d, "TECHO", 300));
        var segundo = Cobrar(2, new ItemCobro(d, "TECHO"));
        Assert.True(d.EstaPagada);

        ServicioCobro.Anular(primero.Recibo, primero.Pago, new[] { d }, "Error de cobro", DateTimeOffset.UtcNow);

        Assert.True(d.EstaPendiente);
        Assert.Equal(450m, d.MontoPagado);
        Assert.Equal(300m, d.Saldo);

        ServicioCobro.Anular(segundo.Recibo, segundo.Pago, new[] { d }, "Error de cobro", DateTimeOffset.UtcNow);
        Assert.Equal(0m, d.MontoPagado);
        Assert.Equal(750m, d.Saldo);
    }

    [Fact]
    public void Una_deuda_pagada_antes_de_los_pagos_parciales_se_restituye_completa()
    {
        // Las deudas pagadas antes de esta versión tienen MontoPagado 0.
        var d = Deuda(200);
        d.MarcarComoPagada();
        typeof(Obligacion).GetProperty(nameof(Obligacion.MontoPagado))!.SetValue(d, 0m);

        Assert.Equal(200m, d.Pagado);
        d.RevertirAbono(200);

        Assert.True(d.EstaPendiente);
        Assert.Equal(200m, d.Saldo);
    }

    [Fact]
    public void Con_abonos_no_se_anula_la_deuda_ni_se_cambia_el_monto()
    {
        var d = Deuda(500);
        Cobrar(1, new ItemCobro(d, "X", 100));

        Assert.Throws<InvalidOperationException>(() => d.Anular());
        Assert.Throws<InvalidOperationException>(() => d.CorregirMonto(400));
        Assert.Equal(500m, d.Monto);
    }

    [Fact]
    public void Un_recibo_puede_combinar_pagos_totales_y_parciales()
    {
        var a = Deuda(71);
        var b = Deuda(750);

        var r = Cobrar(1, new ItemCobro(a, "MENSUALIDAD"), new ItemCobro(b, "TECHO", 250));

        Assert.Equal(321m, r.Pago.Monto);
        Assert.True(a.EstaPagada);
        Assert.Equal(500m, b.Saldo);
    }
}

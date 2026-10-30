using Mercadex.Domain.Cobranza;

namespace Mercadex.Domain.Caja;

public static class ServicioCobro
{
    public static ResultadoCobro Registrar(
        long numeroRecibo,
        IEnumerable<ItemCobro> items,
        FormaPago formaPago,
        string nombrePagador,
        DateTimeOffset fecha,
        decimal tipoCambio = 1m,
        string? referencia = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var lista = items.ToList();

        if (lista.Count == 0)
        {
            throw new ArgumentException(
                "Debe existir al menos una obligación para cobrar.",
                nameof(items));
        }

        if (lista.Any(x => x.Obligacion is null))
        {
            throw new ArgumentException(
                "Todos los ítems deben contener una obligación válida.",
                nameof(items));
        }

        if (lista.Any(x => string.IsNullOrWhiteSpace(x.NombreConcepto)))
        {
            throw new ArgumentException(
                "Todos los ítems deben tener una descripción de concepto.",
                nameof(items));
        }

        if (lista
            .GroupBy(x => x.Obligacion.Id)
            .Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException(
                "No se puede cobrar la misma obligación más de una vez.");
        }

        if (lista.Any(x => !x.Obligacion.EstaPendiente))
        {
            throw new InvalidOperationException(
                "Todas las obligaciones deben estar pendientes.");
        }

        // Pago total o parcial: nunca más que el saldo de cada deuda.
        if (lista.Any(x => x.APagar <= 0 || decimal.Round(x.APagar, 2) != x.APagar))
        {
            throw new ArgumentException(
                "El monto a pagar de cada deuda debe ser mayor a cero y tener maximo dos decimales.",
                nameof(items));
        }

        if (lista.Any(x => x.APagar > x.Obligacion.Saldo))
        {
            throw new InvalidOperationException(
                "El monto a pagar no puede superar el saldo de la deuda.");
        }

        var unidadComercialId =
            lista[0].Obligacion.UnidadComercialId;

        if (lista.Any(x =>
            x.Obligacion.UnidadComercialId != unidadComercialId))
        {
            throw new InvalidOperationException(
                "Todas las obligaciones deben pertenecer a la misma unidad comercial.");
        }

        var monedaId =
            lista[0].Obligacion.MonedaId;

        if (lista.Any(x =>
            x.Obligacion.MonedaId != monedaId))
        {
            throw new InvalidOperationException(
                "Todas las obligaciones del recibo deben tener la misma moneda.");
        }

        var total =
            lista.Sum(x => x.APagar);

        var pago = new Pago(
            monedaId,
            total,
            formaPago,
            nombrePagador,
            fecha,
            tipoCambio,
            referencia);

        var detalles = lista
            .Select(x =>
                new DetalleRecibo(
                    x.Obligacion.Id,
                    x.Obligacion.ConceptoCobroId,
                    x.NombreConcepto,
                    x.APagar,
                    x.Obligacion.PeriodoAnio,
                    x.Obligacion.PeriodoMes))
            .ToList();

        var recibo = new Recibo(
            numeroRecibo,
            unidadComercialId,
            pago.Id,
            fecha,
            detalles);

        foreach (var item in lista)
        {
            item.Obligacion.Abonar(item.APagar);
        }

        return new ResultadoCobro(
            pago,
            recibo);
    }

    public static void Anular(
        Recibo recibo,
        Pago pago,
        IEnumerable<Obligacion> obligaciones,
        string motivo,
        DateTimeOffset fechaAnulacion)
    {
        ArgumentNullException.ThrowIfNull(recibo);
        ArgumentNullException.ThrowIfNull(pago);
        ArgumentNullException.ThrowIfNull(obligaciones);

        if (recibo.EstaAnulado)
        {
            throw new InvalidOperationException(
                "El recibo ya se encuentra anulado.");
        }

        if (pago.EstaAnulado)
        {
            throw new InvalidOperationException(
                "El pago ya se encuentra anulado.");
        }

        if (recibo.PagoId != pago.Id)
        {
            throw new InvalidOperationException(
                "El pago no pertenece al recibo indicado.");
        }

        var listaObligaciones =
            obligaciones.ToList();

        if (listaObligaciones
            .GroupBy(x => x.Id)
            .Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException(
                "La lista contiene obligaciones repetidas.");
        }

        var idsEsperados =
            recibo.Detalles
                .Select(x => x.ObligacionId)
                .ToHashSet();

        var idsRecibidos =
            listaObligaciones
                .Select(x => x.Id)
                .ToHashSet();

        if (!idsEsperados.SetEquals(idsRecibidos))
        {
            throw new InvalidOperationException(
                "Las obligaciones proporcionadas no coinciden con las del recibo.");
        }

        // Cada deuda debe tener pagado al menos lo que este recibo pagó de ella.
        var pagadoPorRecibo = recibo.Detalles.ToDictionary(x => x.ObligacionId, x => x.Monto);
        if (listaObligaciones.Any(x => x.Pagado < pagadoPorRecibo[x.Id]))
        {
            throw new InvalidOperationException(
                "Todas las obligaciones del recibo deben estar pagadas antes de anularlo.");
        }

        recibo.Anular(
            motivo,
            fechaAnulacion);

        pago.Anular();

        // Cada deuda recupera lo que este recibo pagó de ella (todo o una parte).
        foreach (var obligacion in listaObligaciones)
        {
            obligacion.RevertirAbono(pagadoPorRecibo[obligacion.Id]);
        }
    }
}

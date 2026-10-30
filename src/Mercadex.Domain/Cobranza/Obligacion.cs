using Mercadex.Domain.Common;

namespace Mercadex.Domain.Cobranza;

public sealed class Obligacion : Entity
{
    private Obligacion()
    {
    }


    public Obligacion(
        Guid unidadComercialId,
        Guid conceptoCobroId,
        Guid monedaId,
        decimal monto,
        DateOnly fechaEmision,
        DateOnly? fechaVencimiento = null,
        int? periodoAnio = null,
        int? periodoMes = null)
    {
        if (unidadComercialId == Guid.Empty)
        {
            throw new ArgumentException(
                "La unidad comercial es obligatoria.",
                nameof(unidadComercialId));
        }


        if (conceptoCobroId == Guid.Empty)
        {
            throw new ArgumentException(
                "El concepto de cobro es obligatorio.",
                nameof(conceptoCobroId));
        }


        if (monedaId == Guid.Empty)
        {
            throw new ArgumentException(
                "La moneda es obligatoria.",
                nameof(monedaId));
        }


        if (monto <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monto),
                "El monto debe ser mayor a cero.");
        }


        if (fechaVencimiento.HasValue &&
            fechaVencimiento.Value < fechaEmision)
        {
            throw new ArgumentException(
                "La fecha de vencimiento no puede ser anterior a la emisión.",
                nameof(fechaVencimiento));
        }


        if (periodoMes.HasValue &&
            (periodoMes.Value < 1 ||
             periodoMes.Value > 12))
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodoMes),
                "El mes debe estar entre 1 y 12.");
        }


        if (periodoMes.HasValue &&
            !periodoAnio.HasValue)
        {
            throw new ArgumentException(
                "Si existe mes debe existir año.",
                nameof(periodoAnio));
        }


        UnidadComercialId = unidadComercialId;
        ConceptoCobroId = conceptoCobroId;
        MonedaId = monedaId;

        Monto = monto;

        FechaEmision = fechaEmision;
        FechaVencimiento = fechaVencimiento;

        PeriodoAnio = periodoAnio;
        PeriodoMes = periodoMes;

        Estado = EstadoObligacion.Pendiente;
    }


    public Guid UnidadComercialId { get; private set; }


    public Guid ConceptoCobroId { get; private set; }


    public Guid MonedaId { get; private set; }


    public decimal Monto { get; private set; }


    public DateOnly FechaEmision { get; private set; }


    public DateOnly? FechaVencimiento { get; private set; }


    public int? PeriodoAnio { get; private set; }


    public int? PeriodoMes { get; private set; }


    public EstadoObligacion Estado { get; private set; }


    // Pagos parciales: lo abonado con recibos vigentes mientras la deuda
    // sigue pendiente. Las deudas pagadas antes de los pagos parciales
    // tienen 0 aquí (y Estado Pagada): su saldo también es 0.

    public decimal MontoPagado { get; private set; }


    /// <summary>Lo que falta pagar: solo una deuda pendiente tiene saldo.</summary>
    public decimal Saldo =>
        Estado == EstadoObligacion.Pendiente ? Monto - MontoPagado : 0m;


    public bool TieneAbonos =>
        Estado == EstadoObligacion.Pendiente && MontoPagado > 0;


    /// <summary>
    /// Lo pagado con recibos vigentes. Una deuda pagada antes de los pagos
    /// parciales tiene MontoPagado 0: estaba pagada completa.
    /// </summary>
    public decimal Pagado =>
        Estado == EstadoObligacion.Pagada && MontoPagado == 0 ? Monto
        : Estado is EstadoObligacion.Pagada or EstadoObligacion.Pendiente ? MontoPagado
        : 0m;


    public bool EstaPendiente =>
        Estado == EstadoObligacion.Pendiente;


    public bool EstaPagada =>
        Estado == EstadoObligacion.Pagada;


    public bool EstaAnulada =>
        Estado == EstadoObligacion.Anulada;


    // Solo se permite corregir una obligacion pendiente.
    // El controlador debe comprobar ademas que no existan
    // pagos o recibos asociados antes de ejecutar el cambio.

    public void CorregirMonto(decimal nuevoMonto)
    {
        if (Estado != EstadoObligacion.Pendiente)
        {
            throw new InvalidOperationException(
                "No se puede modificar una obligacion pagada o anulada.");
        }

        if (MontoPagado > 0)
        {
            throw new InvalidOperationException(
                "No se puede modificar una obligacion con pagos parciales.");
        }

        if (nuevoMonto <= 0 ||
            decimal.Round(nuevoMonto, 2) != nuevoMonto)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nuevoMonto),
                "El monto debe ser positivo y tener maximo dos decimales.");
        }

        if (Monto == nuevoMonto)
        {
            throw new InvalidOperationException(
                "El nuevo monto debe ser diferente al anterior.");
        }

        Monto = nuevoMonto;
    }

    /// <summary>Paga todo lo que falta.</summary>
    public void MarcarComoPagada()
    {
        if (Estado != EstadoObligacion.Pendiente)
        {
            throw new InvalidOperationException(
                "Solamente una obligación pendiente puede pagarse.");
        }

        Abonar(Saldo);
    }


    /// <summary>
    /// Pago total o parcial. Si con este abono se completa el monto, la
    /// deuda queda pagada; si no, sigue pendiente con el saldo que falta.
    /// </summary>
    public void Abonar(decimal monto)
    {
        if (Estado != EstadoObligacion.Pendiente)
        {
            throw new InvalidOperationException(
                "Solamente una obligación pendiente puede pagarse.");
        }

        if (monto <= 0 || decimal.Round(monto, 2) != monto)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monto),
                "El monto a pagar debe ser mayor a cero y tener maximo dos decimales.");
        }

        if (monto > Saldo)
        {
            throw new InvalidOperationException(
                "El monto a pagar no puede superar el saldo de la deuda.");
        }

        MontoPagado += monto;

        if (MontoPagado == Monto)
        {
            Estado = EstadoObligacion.Pagada;
        }
    }


    /// <summary>
    /// Al anular un recibo: se devuelve lo que ese recibo abonó y la deuda
    /// queda pendiente con ese saldo.
    /// </summary>
    public void RevertirAbono(decimal monto)
    {
        if (Estado is not (EstadoObligacion.Pendiente or EstadoObligacion.Pagada))
        {
            throw new InvalidOperationException(
                "Solamente una obligación pagada o con pagos parciales puede restaurarse.");
        }

        if (monto <= 0 || monto > Pagado)
        {
            throw new InvalidOperationException(
                "El monto a restituir no coincide con lo pagado de la deuda.");
        }

        MontoPagado = Pagado - monto;
        Estado = EstadoObligacion.Pendiente;
    }


    // Anulacion logica: la deuda deja de contar en los saldos pero
    // conserva su monto, concepto y periodo. Solo una deuda pendiente
    // puede anularse; el registro de quien, cuando y por que queda en
    // AnulacionObligaciones (ver ServicioAnulacionObligaciones).

    public void Anular()
    {
        if (Estado != EstadoObligacion.Pendiente)
        {
            throw new InvalidOperationException(
                "Solamente una obligación pendiente puede anularse.");
        }

        if (MontoPagado > 0)
        {
            throw new InvalidOperationException(
                "No se puede anular una obligación con pagos parciales.");
        }

        Estado = EstadoObligacion.Anulada;
    }


    /// <summary>Vuelve a dejar pendiente todo el monto (sin abonos).</summary>
    public void RestaurarComoPendiente()
    {
        if (Estado != EstadoObligacion.Pagada)
        {
            throw new InvalidOperationException(
                "Solamente una obligación pagada puede restaurarse.");
        }

        MontoPagado = 0;
        Estado = EstadoObligacion.Pendiente;
    }
}
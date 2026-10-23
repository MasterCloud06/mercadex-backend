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

    public void MarcarComoPagada()
    {
        if (Estado != EstadoObligacion.Pendiente)
        {
            throw new InvalidOperationException(
                "Solamente una obligación pendiente puede pagarse.");
        }

        Estado = EstadoObligacion.Pagada;
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

        Estado = EstadoObligacion.Anulada;
    }


    public void RestaurarComoPendiente()
    {
        if (Estado != EstadoObligacion.Pagada)
        {
            throw new InvalidOperationException(
                "Solamente una obligación pagada puede restaurarse.");
        }

        Estado = EstadoObligacion.Pendiente;
    }
}
namespace Mercadex.Application.DTOs;

public sealed record ObligacionDto(
    Guid Id,
    Guid UnidadComercialId,
    Guid ConceptoCobroId,
    Guid MonedaId,
    decimal Monto,
    DateOnly FechaEmision,
    DateOnly? FechaVencimiento,
    int? PeriodoAnio,
    int? PeriodoMes,
    string Estado)
{
    /// <summary>Lo abonado con pagos parciales (0 si no hay abonos).</summary>
    public decimal MontoPagado { get; init; }

    /// <summary>Lo que falta pagar (pendiente: Monto - MontoPagado).</summary>
    public decimal Saldo { get; init; }

    /// <summary>Cantidad de veces que se modificó el monto.</summary>
    public int Modificaciones { get; init; }

    /// <summary>Resumen de la última modificación: fecha, quién y motivo.</summary>
    public string? UltimaModificacion { get; init; }
}
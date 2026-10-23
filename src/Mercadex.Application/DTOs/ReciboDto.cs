namespace Mercadex.Application.DTOs;

public sealed record ReciboDto(
    Guid Id,
    long Numero,
    Guid UnidadComercialId,
    Guid PagoId,
    DateTimeOffset FechaEmision,
    string Estado,
    decimal Total,
    IEnumerable<DetalleReciboDto> Detalles,
    string? MotivoAnulacion = null,
    DateTimeOffset? FechaAnulacion = null,
    string? AnuladoPor = null)
{
    /// <summary>Usuario que registró el cobro.</summary>
    public string? RegistradoPor { get; init; }

    /// <summary>Cargo de quien emitió el recibo.</summary>
    public string? CargoEmisor { get; init; }

    /// <summary>Observaciones del cobro (opcional).</summary>
    public string? Observaciones { get; init; }
}
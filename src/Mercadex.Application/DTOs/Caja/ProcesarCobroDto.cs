namespace Mercadex.Application.DTOs.Caja;

public sealed record ProcesarCobroDto(
    long NumeroRecibo,
    Guid UnidadComercialId,
    Guid MonedaId,
    decimal Monto,
    int FormaPago,
    string NombrePagador,
    decimal TipoCambio,
    string? Referencia,
    IEnumerable<ItemCobroDto> Items,
    // Opcional: se imprime en el recibo. Los instaladores anteriores no lo envían.
    string? Observaciones = null,
    // Opcional: fecha del cobro (hora de Bolivia) para registrar un pago de un día
    // anterior (por ejemplo, un QR del domingo). Sin valor, la fecha y hora actuales.
    DateOnly? FechaCobro = null
);
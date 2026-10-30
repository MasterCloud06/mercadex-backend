namespace Mercadex.Application.DTOs.Caja;

public sealed record ItemCobroDto(
    Guid ObligacionId,
    string NombreConcepto,
    // Pago parcial: lo que se paga de esta deuda. Sin valor, se paga todo el saldo
    // (los instaladores anteriores no lo envían).
    decimal? Monto = null
);
using Mercadex.Domain.Cobranza;

namespace Mercadex.Domain.Caja;

/// <param name="Monto">Lo que se paga de la deuda: todo el saldo si es null, o una parte (pago parcial).</param>
public sealed record ItemCobro(
    Obligacion Obligacion,
    string NombreConcepto,
    decimal? Monto = null)
{
    public decimal APagar => Monto ?? Obligacion.Saldo;
}

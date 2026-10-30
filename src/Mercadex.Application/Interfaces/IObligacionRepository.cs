using Mercadex.Application.DTOs;
using Mercadex.Domain.Cobranza;

namespace Mercadex.Application.Interfaces;

public interface IObligacionRepository
{
    Task<IEnumerable<ObligacionDto>> GetAllAsync();


    Task<ObligacionDto?> GetByIdAsync(
        Guid id);


    Task<ObligacionDto> CrearAsync(
        CrearObligacionDto dto);


    Task<IReadOnlyList<Obligacion>> ObtenerPorIdsAsync(
        IEnumerable<Guid> ids);


    Task<bool> ExisteAsync(
        Guid unidadComercialId,
        Guid conceptoCobroId,
        int periodoAnio,
        int periodoMes);


    Task<bool> MarcarComoPagadaAsync(
        Guid id);


    Task<bool> RestaurarComoPendienteAsync(
        Guid id);


    /// <summary>Al anular un recibo: devuelve a la deuda lo que ese recibo pagó de ella.</summary>
    Task<bool> RevertirAbonoAsync(
        Guid id,
        decimal monto);

Task<IEnumerable<ObligacionDto>> ObtenerPendientesPorUnidadAsync(
    Guid unidadComercialId);
}
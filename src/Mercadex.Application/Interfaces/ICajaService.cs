using Mercadex.Application.DTOs;
using Mercadex.Application.DTOs.Caja;

namespace Mercadex.Application.Interfaces;

public interface ICajaService
{
    Task<PagoDto> RegistrarPagoAsync(
        CrearPagoDto dto);

    Task<ReciboDto> GenerarReciboAsync(
        CrearReciboDto dto);

    Task<bool> AnularReciboAsync(
        Guid reciboId,
        AnularReciboDto dto,
        string anuladoPor);

    Task<ResultadoCobroDto> ProcesarCobroAsync(
        ProcesarCobroDto dto,
        string? registradoPor = null,
        string? cargoEmisor = null,
        string? registradoPorId = null,
        DateTimeOffset? fechaEmision = null);
}
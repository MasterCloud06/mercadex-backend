using Mercadex.Api.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Mercadex.Application.DTOs;
using Mercadex.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Mercadex.Application.DTOs.Caja;

namespace Mercadex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CajaController : ControllerBase
{
    private readonly ICajaService _service;
    private readonly IPagoRepository _pagoRepository;
    private readonly IReciboRepository _reciboRepository;

    public CajaController(
        ICajaService service,
        IPagoRepository pagoRepository,
        IReciboRepository reciboRepository)
    {
        _service = service;
        _pagoRepository = pagoRepository;
        _reciboRepository = reciboRepository;
    }


    [HttpPost("pagos")]
    public async Task<ActionResult<PagoDto>> CrearPago(
        CrearPagoDto dto)
    {
        var pago = await _service.RegistrarPagoAsync(dto);

        return Ok(pago);
    }


    [HttpGet("pagos/{id:guid}")]
    public async Task<ActionResult<PagoDto>> ObtenerPago(
        Guid id)
    {
        var pago = await _pagoRepository.GetByIdAsync(id);

        if (pago is null)
        {
            return NotFound();
        }

        return Ok(pago);
    }


    [HttpPost("recibos")]
    public async Task<ActionResult<ReciboDto>> CrearRecibo(
        CrearReciboDto dto)
    {
        var recibo = await _service.GenerarReciboAsync(dto);

        return Ok(recibo);
    }


    [HttpGet("recibos/{id:guid}")]
    public async Task<ActionResult<ReciboDto>> ObtenerRecibo(
        Guid id)
    {
        var recibo = await _reciboRepository.GetByIdAsync(id);

        if (recibo is null)
        {
            return NotFound();
        }

        return Ok(recibo);
    }

[HttpPut("recibos/{id:guid}/anular")]
public async Task<IActionResult> AnularRecibo(
    Guid id,
    AnularReciboDto dto,
    [FromServices] Mercadex.Infrastructure.Persistence.MercadexDbContext db,
    CancellationToken ct)
{
    // Sin inicio de sesion: si no hay usuario autenticado,
    // el comprobante registra "Sistema" como responsable.
    var usuario = string.IsNullOrWhiteSpace(User.Identity?.Name)
        ? "Sistema"
        : User.Identity!.Name!;

    // Datos del recibo para el registro de cambios.
    var antes = await (
            from r in db.Recibos.AsNoTracking()
            join p in db.Pagos.AsNoTracking() on r.PagoId equals p.Id
            join m in db.Monedas.AsNoTracking() on p.MonedaId equals m.Id
            join u in db.UnidadesComerciales.AsNoTracking() on r.UnidadComercialId equals u.Id
            where r.Id == id
            select new { r.Numero, Caseta = u.Codigo, p.Monto, Moneda = m.Codigo, r.RegistradoPor })
        .FirstOrDefaultAsync(ct);

    var resultado =
        await _service.AnularReciboAsync(
            id,
            dto,
            usuario);

    if (!resultado)
    {
        return NotFound(new
        {
            mensaje = "No se encontró el recibo."
        });
    }

    if (antes is not null)
    {
        HttpContext.Detalle(
            $"Recibo N.º {antes.Numero} · Caseta {antes.Caseta} · {Cambios.Monto(antes.Monto, antes.Moneda)}" +
            (string.IsNullOrWhiteSpace(antes.RegistradoPor) ? "" : $" · cobrado por {antes.RegistradoPor}") +
            $" · Motivo: {dto?.Motivo}");
    }

    return NoContent();
}

[HttpPost("cobrar")]
public async Task<ActionResult<ResultadoCobroDto>> ProcesarCobro(
    ProcesarCobroDto dto,
    [FromServices] Mercadex.Infrastructure.Persistence.MercadexDbContext db,
    CancellationToken ct)
{
    // Nombre completo y cargo de quien emite el recibo.
    var (registradoPor, cargo) = await Mercadex.Api.Seguridad.EmisorRecibo.ObtenerAsync(User, db, ct);

    var resultado = await _service
        .ProcesarCobroAsync(dto, registradoPor, cargo, SeguridadUsuarios.IdResponsable(User));

    var caseta = await db.UnidadesComerciales.AsNoTracking()
        .Where(u => u.Id == dto.UnidadComercialId).Select(u => u.Codigo).FirstOrDefaultAsync(ct);
    var moneda = await db.Monedas.AsNoTracking()
        .Where(m => m.Id == dto.MonedaId).Select(m => m.Codigo).FirstOrDefaultAsync(ct);

    HttpContext.Referencia(resultado.Recibo.Id);
    HttpContext.Detalle(
        $"Recibo N.º {resultado.Recibo.Numero} · Caseta {caseta} · {resultado.Pago.FormaPago} · " +
        $"{Cambios.Monto(resultado.Pago.Monto, moneda ?? "")} · " +
        string.Join(", ", resultado.Recibo.Detalles.Select(d => d.Concepto)) +
        (string.IsNullOrWhiteSpace(resultado.Recibo.Observaciones) ? "" : $" · Obs.: {resultado.Recibo.Observaciones}"));

    return Ok(resultado);
}

[HttpGet("recibos")]
public async Task<ActionResult<IEnumerable<ReciboDto>>> ObtenerRecibos()
{
    var recibos = await _reciboRepository.GetAllAsync();

    return Ok(recibos);
}

}
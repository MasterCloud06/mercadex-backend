using Mercadex.Domain.Caja;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

/// <summary>
/// Reimpresión de recibos (manual, 2): buscar por número de recibo o por
/// fecha (también por caseta y estado). Solo consulta: reimprimir no crea
/// ningún cobro ni cambia deudas. Un recibo anulado se informa como ANULADO.
/// </summary>
[ApiController]
[Route("api/recibos")]
public sealed class RecibosController : ControllerBase
{
    private const int Limite = 500;
    private static readonly TimeSpan ZonaBolivia = TimeSpan.FromHours(-4);

    private readonly MercadexDbContext _db;

    public RecibosController(MercadexDbContext db)
    {
        _db = db;
    }

    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar(
        [FromQuery] long? numero,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] string? caseta,
        [FromQuery] string? estado,
        CancellationToken ct)
    {
        var consulta =
            from r in _db.Recibos.AsNoTracking()
            join p in _db.Pagos.AsNoTracking() on r.PagoId equals p.Id
            join u in _db.UnidadesComerciales.AsNoTracking() on r.UnidadComercialId equals u.Id
            join m in _db.Monedas.AsNoTracking() on p.MonedaId equals m.Id
            select new { r, p, u, m };

        // Con número se busca ese recibo exacto (el 1 no trae el 10 ni el 100).
        if (numero is { } n)
        {
            consulta = consulta.Where(x => x.r.Numero == n);
        }
        else
        {
            if (desde is { } d)
            {
                var inicio = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), ZonaBolivia).ToUniversalTime();
                consulta = consulta.Where(x => x.r.FechaEmision >= inicio);
            }

            if (hasta is { } h)
            {
                var fin = new DateTimeOffset(h.AddDays(1).ToDateTime(TimeOnly.MinValue), ZonaBolivia).ToUniversalTime();
                consulta = consulta.Where(x => x.r.FechaEmision < fin);
            }

            if (!string.IsNullOrWhiteSpace(caseta))
            {
                var codigo = caseta.Trim().ToUpper();
                consulta = consulta.Where(x => x.u.Codigo.ToUpper() == codigo);
            }

            if (Enum.TryParse<EstadoRecibo>(estado, ignoreCase: true, out var e))
                consulta = consulta.Where(x => x.r.Estado == e);
        }

        var total = await consulta.CountAsync(ct);

        var filas = await consulta
            .OrderByDescending(x => x.r.FechaEmision)
            .ThenByDescending(x => x.r.Numero)
            .Take(Limite)
            .Select(x => new
            {
                id = x.r.Id,
                numero = x.r.Numero,
                fechaEmision = x.r.FechaEmision,
                unidadId = x.u.Id,
                caseta = x.u.Codigo,
                bloque = x.u.Bloque,
                pagador = x.p.NombrePagador,
                formaPago = x.p.FormaPago.ToString(),
                referencia = x.p.Referencia,
                moneda = x.m.Codigo,
                total = x.p.Monto,
                estado = x.r.Estado.ToString(),
                registradoPor = x.r.RegistradoPor,
                cargoEmisor = x.r.CargoEmisor,
                observaciones = x.r.Observaciones,
                anuladoPor = x.r.AnuladoPor,
                fechaAnulacion = x.r.FechaAnulacion,
                motivoAnulacion = x.r.MotivoAnulacion,
                conceptos = x.r.Detalles.Count
            })
            .ToListAsync(ct);

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            total,
            mostrados = filas.Count,
            limite = Limite,
            recibos = filas
        });
    }
}

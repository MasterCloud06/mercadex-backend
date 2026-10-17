using Mercadex.Domain.Cobranza;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

/// <summary>
/// Listas de deudores (manual: 5. AVISO PARA CORTE y la tabla "Lista de
/// Deudores p/ realizar corte" y "p/ Contabilidad").
///
/// Cada caseta aparece UNA sola vez aunque deba uno o varios servicios, con
/// sus servicios pendientes y el total por moneda. Cada fila trae también el
/// detalle (fecha, servicio, período, monto) para la lista de contabilidad.
/// Filtros: rango de fechas de emisión, bloque, moneda y conceptos.
/// </summary>
[ApiController]
[Route("api/reportes/deudores")]
public sealed class ReportesDeudasController : ControllerBase
{
    private readonly MercadexDbContext _db;

    public ReportesDeudasController(MercadexDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] string? bloque,
        [FromQuery] string? moneda,
        [FromQuery] string? conceptos,
        [FromQuery] string? caseta,
        CancellationToken ct)
    {
        if (desde is { } d0 && hasta is { } h0 && d0 > h0)
            return BadRequest(new { mensaje = "La fecha inicial no puede ser posterior a la final." });

        var consulta =
            from o in _db.Obligaciones.AsNoTracking()
            join u in _db.UnidadesComerciales.AsNoTracking() on o.UnidadComercialId equals u.Id
            join c in _db.ConceptosCobro.AsNoTracking() on o.ConceptoCobroId equals c.Id
            join m in _db.Monedas.AsNoTracking() on o.MonedaId equals m.Id
            where o.Estado == EstadoObligacion.Pendiente
            select new { o, u, c, m };

        if (desde is { } d)
            consulta = consulta.Where(x => x.o.FechaEmision >= d);

        if (hasta is { } h)
            consulta = consulta.Where(x => x.o.FechaEmision <= h);

        if (!string.IsNullOrWhiteSpace(bloque))
        {
            var b = bloque.Trim().ToUpper();
            consulta = consulta.Where(x => x.u.Bloque != null && x.u.Bloque.Trim().ToUpper() == b);
        }

        if (!string.IsNullOrWhiteSpace(moneda))
        {
            var mo = moneda.Trim().ToUpper();
            consulta = consulta.Where(x => x.m.Codigo.ToUpper() == mo);
        }

        if (!string.IsNullOrWhiteSpace(caseta))
        {
            var cs = caseta.Trim().ToUpper();
            consulta = consulta.Where(x => x.u.Codigo.ToUpper() == cs);
        }

        var idsConceptos = (conceptos ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => Guid.TryParse(t, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();

        if (idsConceptos.Count > 0)
            consulta = consulta.Where(x => idsConceptos.Contains(x.o.ConceptoCobroId));

        var deudas = await consulta
            .Select(x => new
            {
                x.u.Id,
                x.u.Codigo,
                x.u.Bloque,
                x.u.Tipo,
                x.u.PasilloId,
                Fecha = x.o.FechaEmision,
                Concepto = x.c.Nombre,
                x.o.PeriodoAnio,
                x.o.PeriodoMes,
                Moneda = x.m.Codigo,
                x.o.Monto
            })
            .ToListAsync(ct);

        var ids = deudas.Select(x => x.Id).Distinct().ToList();

        var titulares = (await (
                from a in _db.AsignacionesSocioCaseta.AsNoTracking()
                join s in _db.Socios.AsNoTracking() on a.SocioId equals s.Id
                where a.Activa && ids.Contains(a.UnidadComercialId)
                orderby a.FechaAsignacion descending
                select new { a.UnidadComercialId, Nombre = s.Nombres + " " + s.Apellidos, s.Telefono })
            .ToListAsync(ct))
            .GroupBy(t => t.UnidadComercialId)
            .ToDictionary(g => g.Key, g => g.First());

        var pasillos = await _db.Pasillos.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);

        var filas = deudas
            .GroupBy(x => x.Id)
            .Select(g =>
            {
                var u = g.First();
                titulares.TryGetValue(u.Id, out var t);
                var detalle = g
                    .OrderBy(x => x.Fecha)
                    .ThenBy(x => x.PeriodoAnio).ThenBy(x => x.PeriodoMes)
                    .ThenBy(x => x.Concepto)
                    .Select(x => new
                    {
                        fecha = x.Fecha,
                        concepto = x.Concepto,
                        periodo = x.PeriodoAnio is { } a && x.PeriodoMes is { } mm ? $"{mm:00}/{a}" : "",
                        moneda = x.Moneda,
                        monto = x.Monto
                    })
                    .ToList();

                return new
                {
                    unidadId = u.Id,
                    codigo = u.Codigo,
                    tipo = u.Tipo.ToString(),
                    bloque = u.Bloque,
                    pasillo = u.PasilloId is { } p && pasillos.TryGetValue(p, out var np) ? np : null,
                    titular = t?.Nombre,
                    telefono = t?.Telefono,
                    servicios = g.Select(x => x.Concepto).Distinct().OrderBy(x => x).ToList(),
                    cantidad = g.Count(),
                    desde = g.Min(x => x.Fecha),
                    totales = g.GroupBy(x => x.Moneda)
                        .Select(m => new { moneda = m.Key, monto = m.Sum(x => x.Monto) })
                        .OrderBy(m => m.moneda)
                        .ToList(),
                    deudas = detalle
                };
            })
            .OrderBy(f => f.bloque ?? "~")
            .ThenBy(f => f.codigo.Length)
            .ThenBy(f => f.codigo, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            generadoUtc = DateTimeOffset.UtcNow,
            casetas = filas.Count,
            deudas = deudas.Count,
            totales = deudas.GroupBy(x => x.Moneda)
                .Select(m => new { moneda = m.Key, monto = m.Sum(x => x.Monto) })
                .OrderBy(m => m.moneda)
                .ToList(),
            porConcepto = deudas.GroupBy(x => new { x.Concepto, x.Moneda })
                .Select(g => new { concepto = g.Key.Concepto, moneda = g.Key.Moneda, casetas = g.Select(x => x.Id).Distinct().Count(), cantidad = g.Count(), monto = g.Sum(x => x.Monto) })
                .OrderBy(x => x.concepto)
                .ToList(),
            filas
        });
    }
}

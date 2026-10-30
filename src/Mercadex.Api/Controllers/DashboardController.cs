using Mercadex.Domain.Caja;
using Mercadex.Domain.Cobranza;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

/// <summary>
/// Datos agregados para el Panel principal del Desktop.
/// Una sola consulta de solo lectura. Los importes nunca
/// mezclan monedas: cada total va acompañado de su moneda.
/// </summary>
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private static readonly TimeSpan ZonaBolivia = TimeSpan.FromHours(-4);

    private readonly MercadexDbContext _db;

    public DashboardController(MercadexDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Obtener(CancellationToken ct)
    {
        var ahoraBolivia = DateTimeOffset.UtcNow.ToOffset(ZonaBolivia);
        var hoy = DateOnly.FromDateTime(ahoraBolivia.DateTime);
        var inicioMes = new DateOnly(hoy.Year, hoy.Month, 1);
        var inicioMesAnterior = inicioMes.AddMonths(-1);
        var inicio12Meses = inicioMes.AddMonths(-11);
        var inicio30Dias = hoy.AddDays(-29);

        // ---------- Catálogos ----------

        var monedas = await _db.Monedas
            .AsNoTracking()
            .Select(m => new { m.Id, m.Codigo, m.Simbolo })
            .ToListAsync(ct);

        var codigoMoneda = monedas.ToDictionary(m => m.Id, m => m.Codigo);
        var bob = monedas.FirstOrDefault(m =>
            string.Equals(m.Codigo, "BOB", StringComparison.OrdinalIgnoreCase));

        var unidades = await _db.UnidadesComerciales
            .AsNoTracking()
            .Select(u => new { u.Id, u.Codigo, u.Bloque, Tipo = (int)u.Tipo, u.Activa })
            .ToListAsync(ct);

        var unidadPorId = unidades.ToDictionary(u => u.Id);
        var activas = unidades.Where(u => u.Activa).ToList();

        // ---------- Cobros válidos (últimos 12 meses) ----------

        var desdeUtc = AUtc(inicio12Meses);

        var cobros = await (
                from r in _db.Recibos.AsNoTracking()
                join p in _db.Pagos.AsNoTracking() on r.PagoId equals p.Id
                where r.Estado == EstadoRecibo.Emitido
                      && p.Estado == EstadoPago.Registrado
                      && r.FechaEmision >= desdeUtc
                select new
                {
                    r.Id,
                    r.Numero,
                    r.UnidadComercialId,
                    r.FechaEmision,
                    p.MonedaId,
                    p.Monto,
                    p.FormaPago,
                    p.NombrePagador
                })
            .ToListAsync(ct);

        var cobrosLocal = cobros
            .Select(c => new
            {
                c.Id,
                c.Numero,
                c.UnidadComercialId,
                c.MonedaId,
                c.Monto,
                c.FormaPago,
                c.NombrePagador,
                Fecha = DateOnly.FromDateTime(c.FechaEmision.ToOffset(ZonaBolivia).DateTime),
                FechaHora = c.FechaEmision.ToOffset(ZonaBolivia)
            })
            .ToList();

        // KPI de recaudación por moneda.
        var recaudacion = cobrosLocal
            .GroupBy(c => c.MonedaId)
            .Select(g => new
            {
                moneda = codigoMoneda.GetValueOrDefault(g.Key, "?"),
                hoy = g.Where(c => c.Fecha == hoy).Sum(c => c.Monto),
                recibosHoy = g.Count(c => c.Fecha == hoy),
                mes = g.Where(c => c.Fecha >= inicioMes).Sum(c => c.Monto),
                recibosMes = g.Count(c => c.Fecha >= inicioMes),
                mesAnterior = g.Where(c => c.Fecha >= inicioMesAnterior && c.Fecha < inicioMes).Sum(c => c.Monto),
                // Mismo tramo del mes anterior (día 1 al día de hoy) para comparar justo.
                mesAnteriorMismoTramo = g.Where(c =>
                        c.Fecha >= inicioMesAnterior &&
                        c.Fecha < inicioMes &&
                        c.Fecha.Day <= hoy.Day)
                    .Sum(c => c.Monto)
            })
            .OrderBy(x => x.moneda == "BOB" ? 0 : 1)
            .ThenBy(x => x.moneda)
            .ToList();

        // Series para gráficos: solo bolivianos (moneda principal).
        var cobrosBob = bob == null
            ? cobrosLocal.Take(0).ToList()
            : cobrosLocal.Where(c => c.MonedaId == bob.Id).ToList();

        var diario = Enumerable.Range(0, 30)
            .Select(i => inicio30Dias.AddDays(i))
            .Select(d => new
            {
                fecha = d,
                total = cobrosBob.Where(c => c.Fecha == d).Sum(c => c.Monto),
                recibos = cobrosBob.Count(c => c.Fecha == d)
            })
            .ToList();

        var mensual = Enumerable.Range(0, 12)
            .Select(i => inicio12Meses.AddMonths(i))
            .Select(m => new
            {
                anio = m.Year,
                mes = m.Month,
                total = cobrosBob.Where(c => c.Fecha.Year == m.Year && c.Fecha.Month == m.Month).Sum(c => c.Monto),
                recibos = cobrosBob.Count(c => c.Fecha.Year == m.Year && c.Fecha.Month == m.Month)
            })
            .ToList();

        var formasPago = cobrosBob
            .Where(c => c.Fecha >= inicioMes)
            .GroupBy(c => c.FormaPago)
            .Select(g => new
            {
                forma = g.Key.ToString(),
                total = g.Sum(c => c.Monto),
                cantidad = g.Count()
            })
            .OrderByDescending(x => x.total)
            .ToList();

        // Por concepto (mes actual, BOB): a partir de los detalles.
        var idsMesBob = cobrosBob
            .Where(c => c.Fecha >= inicioMes)
            .Select(c => c.Id)
            .ToList();

        var detallesMes = idsMesBob.Count == 0
            ? new List<(string Concepto, decimal Monto)>()
            : (await _db.DetallesRecibo
                    .AsNoTracking()
                    .Where(d => idsMesBob.Contains(EF.Property<Guid>(d, "ReciboId")))
                    .Select(d => new { d.Concepto, d.Monto })
                    .ToListAsync(ct))
                .Select(d => (d.Concepto, d.Monto))
                .ToList();

        var conceptosOrdenados = detallesMes
            .GroupBy(d => d.Concepto)
            .Select(g => new { concepto = g.Key, total = g.Sum(x => x.Monto), pagos = g.Count() })
            .OrderByDescending(x => x.total)
            .ToList();

        var porConcepto = conceptosOrdenados.Take(6).ToList();

        if (conceptosOrdenados.Count > 6)
        {
            var resto = conceptosOrdenados.Skip(6).ToList();
            porConcepto.Add(new
            {
                concepto = "Otros",
                total = resto.Sum(x => x.total),
                pagos = resto.Sum(x => x.pagos)
            });
        }

        // ---------- Deuda pendiente ----------

        var pendientes = await _db.Obligaciones
            .AsNoTracking()
            .Where(o => o.Estado == EstadoObligacion.Pendiente)
            .Select(o => new { o.UnidadComercialId, o.MonedaId, Monto = o.Monto - o.MontoPagado, o.FechaEmision })
            .ToListAsync(ct);

        var deuda = pendientes
            .GroupBy(o => o.MonedaId)
            .Select(g => new
            {
                moneda = codigoMoneda.GetValueOrDefault(g.Key, "?"),
                total = g.Sum(o => o.Monto),
                obligaciones = g.Count(),
                casetas = g.Select(o => o.UnidadComercialId).Distinct().Count()
            })
            .OrderBy(x => x.moneda == "BOB" ? 0 : 1)
            .ToList();

        var pendientesBob = bob == null
            ? pendientes.Take(0).ToList()
            : pendientes.Where(o => o.MonedaId == bob.Id).ToList();

        string Tramo(DateOnly emision)
        {
            var dias = hoy.DayNumber - emision.DayNumber;
            return dias switch
            {
                <= 30 => "0 a 30 días",
                <= 60 => "31 a 60 días",
                <= 90 => "61 a 90 días",
                _ => "Más de 90 días"
            };
        }

        var tramos = new[] { "0 a 30 días", "31 a 60 días", "61 a 90 días", "Más de 90 días" };

        var antiguedad = tramos
            .Select((t, i) => new
            {
                tramo = t,
                orden = i,
                total = pendientesBob.Where(o => Tramo(o.FechaEmision) == t).Sum(o => o.Monto),
                obligaciones = pendientesBob.Count(o => Tramo(o.FechaEmision) == t)
            })
            .ToList();

        var topDeudores = pendientesBob
            .GroupBy(o => o.UnidadComercialId)
            .Select(g => new
            {
                caseta = unidadPorId.TryGetValue(g.Key, out var u) ? u.Codigo : "?",
                bloque = unidadPorId.TryGetValue(g.Key, out var u2) ? u2.Bloque : null,
                total = g.Sum(o => o.Monto),
                obligaciones = g.Count(),
                desde = g.Min(o => o.FechaEmision)
            })
            .OrderByDescending(x => x.total)
            .Take(5)
            .ToList();

        var unidadesConDeuda = pendientes
            .Select(o => o.UnidadComercialId)
            .Distinct()
            .Count(id => unidadPorId.TryGetValue(id, out var u) && u.Activa);

        // ---------- Otros indicadores del mes ----------

        var inicioMesUtc = AUtc(inicioMes);

        var anulacionesMes = await _db.Recibos
            .AsNoTracking()
            .CountAsync(r =>
                r.Estado == EstadoRecibo.Anulado &&
                r.FechaAnulacion.HasValue &&
                r.FechaAnulacion.Value >= inicioMesUtc, ct);

        var egresosMes = (await _db.Egresos
                .AsNoTracking()
                .Where(e => !e.Anulado && e.Fecha >= inicioMes)
                .Select(e => new { e.MonedaId, e.Monto })
                .ToListAsync(ct))
            .GroupBy(e => e.MonedaId)
            .Select(g => new
            {
                moneda = codigoMoneda.GetValueOrDefault(g.Key, "?"),
                total = g.Sum(e => e.Monto)
            })
            .ToList();

        var ultimosCobros = cobrosLocal
            .OrderByDescending(c => c.FechaHora)
            .Take(6)
            .Select(c => new
            {
                numero = c.Numero,
                fecha = c.FechaHora,
                caseta = unidadPorId.TryGetValue(c.UnidadComercialId, out var u) ? u.Codigo : "?",
                pagador = c.NombrePagador,
                total = c.Monto,
                moneda = codigoMoneda.GetValueOrDefault(c.MonedaId, "?")
            })
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            generado = ahoraBolivia,
            hoy,
            unidades = new
            {
                casetas = activas.Count(u => u.Tipo == 1),
                comedores = activas.Count(u => u.Tipo == 2),
                ambulantes = activas.Count(u => u.Tipo == 3),
                activas = activas.Count,
                conDeuda = unidadesConDeuda,
                alDia = Math.Max(0, activas.Count - unidadesConDeuda)
            },
            recaudacion,
            deuda,
            diario,
            mensual,
            porConcepto,
            formasPago,
            antiguedad,
            topDeudores,
            ultimosCobros,
            anulacionesMes,
            egresosMes
        });
    }

    private static DateTimeOffset AUtc(DateOnly fechaBolivia) =>
        new DateTimeOffset(fechaBolivia.ToDateTime(TimeOnly.MinValue), ZonaBolivia)
            .ToUniversalTime();
}

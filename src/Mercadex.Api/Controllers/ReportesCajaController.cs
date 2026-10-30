using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mercadex.Domain.Caja;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

[ApiController]
[Route("api/reportes-caja")]
public sealed class ReportesCajaController : ControllerBase
{
    // MERCADEX_REPORTES_CAJA_LOCAL_V1

    private readonly MercadexDbContext _db;

    private static readonly TimeSpan ZonaBolivia =
        TimeSpan.FromHours(-4);

    public ReportesCajaController(MercadexDbContext db)
    {
        _db = db;
    }

    // Modulo de Mercadex 1.5 (sin autorizacion de administrador).
    // Ya no depende de variables de entorno ni del nombre de la base.
    private static bool EntornoPermitido() => true;

    [HttpGet]
    public async Task<IActionResult> Consultar(
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] Guid? conceptoId,
        [FromQuery] string? bloque,
        [FromQuery] int? periodoAnio,
        [FromQuery] int? periodoMes,
        CancellationToken ct)
    {
        if (!EntornoPermitido())
            return StatusCode(403);

        // Período de la deuda (por ejemplo, la mensualidad de agosto): se cuentan
        // solo los conceptos de ese mes, sin importar la fecha en que se pagaron.
        if (periodoAnio.HasValue != periodoMes.HasValue || periodoMes is < 1 or > 12 || periodoAnio is < 2000 or > 2100)
            return BadRequest(new { mensaje = "Indica el mes y el año de la deuda." });

        var porPeriodo = periodoAnio.HasValue;

        if (porPeriodo && !desde.HasValue && !hasta.HasValue)
        {
            desde = new DateOnly(2000, 1, 1);
            hasta = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ZonaBolivia).DateTime);
        }

        if (!desde.HasValue || !hasta.HasValue)
        {
            return BadRequest(new
            {
                mensaje =
                    "Debe indicar desde y hasta con formato AAAA-MM-DD."
            });
        }

        if (desde.Value > hasta.Value)
        {
            return BadRequest(new
            {
                mensaje =
                    "La fecha inicial no puede superar la final."
            });
        }

        if (
            (!porPeriodo && hasta.Value.DayNumber - desde.Value.DayNumber > 366) ||
            hasta.Value == DateOnly.MaxValue
        )
        {
            return BadRequest(new
            {
                mensaje =
                    "El rango debe ser de 367 dias o menos."
            });
        }

        // Las fechas introducidas corresponden a Bolivia.
        // Los limites son inclusivos para el usuario;
        // internamente se usa [inicio, fin exclusivo).

        var inicio = new DateTimeOffset(
            desde.Value.ToDateTime(TimeOnly.MinValue),
            ZonaBolivia).ToUniversalTime();

        var fin = new DateTimeOffset(
            hasta.Value.AddDays(1)
                .ToDateTime(TimeOnly.MinValue),
            ZonaBolivia).ToUniversalTime();

        // Incluir recibos emitidos dentro del periodo
        // y anulaciones realizadas dentro del periodo.
        // Consultar no modifica ningun dato.

        var consultaRecibos = _db.Recibos
            .AsNoTracking()
            .Include(x => x.Detalles)
            .Where(x =>
                (
                    x.FechaEmision >= inicio &&
                    x.FechaEmision < fin
                ) ||
                (
                    x.Estado == EstadoRecibo.Anulado &&
                    x.FechaAnulacion.HasValue &&
                    x.FechaAnulacion.Value >= inicio &&
                    x.FechaAnulacion.Value < fin
                ));

        if (porPeriodo)
        {
            consultaRecibos = consultaRecibos.Where(x =>
                x.Detalles.Any(d => d.PeriodoAnio == periodoAnio && d.PeriodoMes == periodoMes));
        }

        var recibos = await consultaRecibos
            .OrderBy(x => x.FechaEmision)
            .Take(5001)
            .ToListAsync(ct);

        if (recibos.Count > 5000)
        {
            return BadRequest(new
            {
                mensaje =
                    "Demasiados recibos para una sola consulta. " +
                    "Seleccione un rango menor."
            });
        }

        var pagoIds = recibos
            .Select(x => x.PagoId)
            .Distinct()
            .ToList();

        var pagos = await _db.Pagos
            .AsNoTracking()
            .Where(x => pagoIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var advertencias = new List<string>();

        var movimientos = new List<MovimientoReporte>();

        // Solo ingresos que actualmente permanecen validos.
        // Un recibo anulado nunca se suma como ingreso.

        foreach (var recibo in recibos)
        {
            if (
                recibo.FechaEmision < inicio ||
                recibo.FechaEmision >= fin ||
                recibo.Estado != EstadoRecibo.Emitido
            )
            {
                continue;
            }

            if (!pagos.TryGetValue(recibo.PagoId, out var pago))
            {
                advertencias.Add(
                    $"Recibo {recibo.Numero}: no tiene pago asociado.");

                continue;
            }

            if (pago.Estado != EstadoPago.Registrado)
            {
                advertencias.Add(
                    $"Recibo {recibo.Numero}: pago no registrado.");

                continue;
            }

            if (
                recibo.Detalles.Count == 0 ||
                recibo.Total != pago.Monto
            )
            {
                advertencias.Add(
                    $"Recibo {recibo.Numero}: " +
                    "el importe no coincide con el pago.");

                continue;
            }

            movimientos.Add(new MovimientoReporte(
                recibo.Id,
                recibo.Numero,
                recibo.UnidadComercialId,
                pago.MonedaId,
                recibo.Total,
                pago.FormaPago.ToString(),
                pago.NombrePagador,
                pago.Referencia,
                recibo.RegistradoPor,
                recibo.FechaEmision,
                recibo.Detalles
                    .Select(d => new ConceptoDetalle(
                        d.ConceptoCobroId,
                        d.Concepto,
                        d.Monto,
                        d.PeriodoAnio,
                        d.PeriodoMes))
                    .ToList()
            ));
        }

        // Filtros opcionales ("pago por grupo de bloques"):
        // un concepto y/o un bloque. Con concepto, cada recibo cuenta
        // solo el importe de ese concepto.

        HashSet<Guid>? unidadesDelBloque = null;
        var bloqueFiltro = string.IsNullOrWhiteSpace(bloque) ? null : bloque.Trim();

        if (bloqueFiltro is not null)
        {
            var bloqueMayus = bloqueFiltro.ToUpper();

            unidadesDelBloque = (await _db.UnidadesComerciales
                    .AsNoTracking()
                    .Where(u => u.Bloque != null && u.Bloque.Trim().ToUpper() == bloqueMayus)
                    .Select(u => u.Id)
                    .ToListAsync(ct))
                .ToHashSet();

            movimientos = movimientos
                .Where(m => unidadesDelBloque.Contains(m.UnidadComercialId))
                .ToList();
        }

        if (conceptoId.HasValue && conceptoId.Value != Guid.Empty)
        {
            movimientos = movimientos
                .Select(m =>
                {
                    var delConcepto = m.Conceptos
                        .Where(c => c.ConceptoCobroId == conceptoId.Value)
                        .ToList();

                    return m with
                    {
                        Conceptos = delConcepto,
                        Monto = delConcepto.Sum(c => c.Monto)
                    };
                })
                .Where(m => m.Conceptos.Count > 0)
                .ToList();
        }

        if (porPeriodo)
        {
            // Un recibo puede pagar varios meses: cuenta solo el del período.
            movimientos = movimientos
                .Select(m =>
                {
                    var delPeriodo = m.Conceptos
                        .Where(c => c.PeriodoAnio == periodoAnio && c.PeriodoMes == periodoMes)
                        .ToList();

                    return m with
                    {
                        Conceptos = delPeriodo,
                        Monto = delPeriodo.Sum(c => c.Monto)
                    };
                })
                .Where(m => m.Conceptos.Count > 0)
                .ToList();
        }

        // Los totales se calculan POR MONEDA.
        // No se suman BOB, USD u otras monedas entre si.

        var resumen = movimientos
            .GroupBy(x => x.MonedaId)
            .Select(g => new
            {
                monedaId = g.Key,
                cantidadRecibos = g.Count(),
                total = g.Sum(x => x.Monto)
            })
            .OrderBy(x => x.monedaId)
            .ToList();

        var formasPago = movimientos
            .GroupBy(x => new
            {
                x.MonedaId,
                x.FormaPago
            })
            .Select(g => new
            {
                monedaId = g.Key.MonedaId,
                formaPago = g.Key.FormaPago,
                cantidad = g.Count(),
                total = g.Sum(x => x.Monto)
            })
            .OrderBy(x => x.formaPago)
            .ToList();

        // Código de cada cuenta, para el diario por servicio.
        var idsConcepto = movimientos.SelectMany(m => m.Conceptos.Select(c => c.ConceptoCobroId)).Distinct().ToList();
        var codigosConcepto = await _db.ConceptosCobro.AsNoTracking()
            .Where(c => idsConcepto.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Codigo, ct);

        var conceptos = movimientos
            .SelectMany(m =>
                m.Conceptos.Select(d => new
                {
                    m.MonedaId,
                    d.ConceptoCobroId,
                    d.Concepto,
                    d.Monto
                }))
            .GroupBy(x => new
            {
                x.MonedaId,
                x.ConceptoCobroId,
                x.Concepto
            })
            .Select(g => new
            {
                monedaId = g.Key.MonedaId,
                conceptoCobroId = g.Key.ConceptoCobroId,
                codigo = codigosConcepto.GetValueOrDefault(g.Key.ConceptoCobroId, ""),
                concepto = g.Key.Concepto,
                cantidad = g.Count(),
                total = g.Sum(x => x.Monto)
            })
            .OrderBy(x => x.concepto)
            .ToList();

        var diasBase = movimientos
            .GroupBy(x => new
            {
                x.MonedaId,
                Dia = x.FechaEmisionUtc
                    .ToOffset(ZonaBolivia)
                    .ToString("yyyy-MM-dd")
            })
            .Select(g => new DiaReporte(
                g.Key.Dia,
                g.Key.MonedaId,
                g.Count(),
                g.Sum(x => x.Monto),
                0m
            ))
            .ToList();

        var dias = new List<DiaReporte>();

        foreach (
            var moneda in diasBase.GroupBy(x => x.MonedaId)
        )
        {
            decimal acumulado = 0m;

            foreach (
                var dia in moneda.OrderBy(x => x.Fecha)
            )
            {
                acumulado += dia.Total;

                dias.Add(dia with
                {
                    Acumulado = acumulado
                });
            }
        }

        dias = dias
            .OrderBy(x => x.Fecha)
            .ThenBy(x => x.MonedaId)
            .ToList();

        // Anulaciones: informacion de auditoria.
        // No se restan otra vez de los ingresos validos.

        var anulaciones = recibos
            .Where(x =>
                x.Estado == EstadoRecibo.Anulado &&
                x.FechaAnulacion.HasValue &&
                x.FechaAnulacion.Value >= inicio &&
                x.FechaAnulacion.Value < fin &&
                (unidadesDelBloque == null || unidadesDelBloque.Contains(x.UnidadComercialId)) &&
                (!conceptoId.HasValue || conceptoId.Value == Guid.Empty ||
                    x.Detalles.Any(d => d.ConceptoCobroId == conceptoId.Value)) &&
                (!porPeriodo ||
                    x.Detalles.Any(d => d.PeriodoAnio == periodoAnio && d.PeriodoMes == periodoMes)))
            .Select(x => new
            {
                reciboId = x.Id,
                numeroRecibo = x.Numero,
                fechaAnulacionUtc = x.FechaAnulacion,
                montoExcluido = x.Total,
                monedaId = pagos.TryGetValue(
                    x.PagoId, out var pago)
                    ? (Guid?)pago.MonedaId
                    : null,
                motivo = x.MotivoAnulacion,
                responsable = x.AnuladoPor
            })
            .OrderByDescending(x => x.fechaAnulacionUtc)
            .ToList();

        var cobros = movimientos
            .OrderBy(x => x.FechaEmisionUtc)
            .ThenBy(x => x.NumeroRecibo)
            .Select(x => new
            {
                reciboId = x.ReciboId,
                numeroRecibo = x.NumeroRecibo,
                unidadComercialId = x.UnidadComercialId,
                monedaId = x.MonedaId,
                monto = x.Monto,
                formaPago = x.FormaPago,
                pagador = x.Pagador,
                referencia = x.Referencia,
                cobradoPor = x.RegistradoPor,
                fechaEmisionUtc = x.FechaEmisionUtc,
                fechaBolivia = x.FechaEmisionUtc
                    .ToOffset(ZonaBolivia)
                    .ToString("dd/MM/yyyy HH:mm"),
                conceptos = x.Conceptos.Select(c => new
                {
                    conceptoCobroId = c.ConceptoCobroId,
                    codigo = codigosConcepto.GetValueOrDefault(c.ConceptoCobroId, ""),
                    concepto = c.Concepto,
                    monto = c.Monto,
                    periodoAnio = c.PeriodoAnio,
                    periodoMes = c.PeriodoMes
                })
            })
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            desde = desde.Value.ToString("yyyy-MM-dd"),
            hasta = hasta.Value.ToString("yyyy-MM-dd"),
            zonaHoraria = "Bolivia UTC-04:00",
            generadoUtc = DateTimeOffset.UtcNow,
            tipo = "Consulta de caja - no es un cierre definitivo",
            filtroConceptoId = conceptoId,
            filtroPeriodo = porPeriodo ? $"{periodoMes:00}/{periodoAnio}" : null,
            filtroBloque = bloqueFiltro,

            cantidadRecibosValidos = movimientos.Count,
            cantidadAnulaciones = anulaciones.Count,

            resumen,
            dias,
            formasPago,
            conceptos,
            cobros,
            anulaciones,
            advertencias
        });
    }

    // =============================================================
    // HISTORIAL DE PAGOS DE UNA CASETA
    // =============================================================

    /// <summary>
    /// Pagos de una caseta, cuenta por cuenta (fecha, recibo, cuenta, período
    /// y monto), desde el primer pago o en un rango de fechas. Solo recibos
    /// válidos: los anulados no se cuentan. Solo lectura.
    /// </summary>
    [HttpGet("caseta/{unidadId:guid}")]
    public async Task<IActionResult> HistorialCaseta(
        Guid unidadId,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        CancellationToken ct)
    {
        if (desde.HasValue && hasta.HasValue && desde.Value > hasta.Value)
            return BadRequest(new { mensaje = "La fecha inicial no puede ser posterior a la final." });

        var unidad = await _db.UnidadesComerciales.AsNoTracking()
            .Where(u => u.Id == unidadId)
            .Select(u => new { u.Codigo, u.Bloque })
            .FirstOrDefaultAsync(ct);

        if (unidad is null)
            return NotFound(new { mensaje = "La caseta no existe." });

        var titular = await (
                from a in _db.AsignacionesSocioCaseta.AsNoTracking()
                join so in _db.Socios.AsNoTracking() on a.SocioId equals so.Id
                where a.UnidadComercialId == unidadId && a.Activa
                select so.Nombres + " " + so.Apellidos)
            .FirstOrDefaultAsync(ct);

        var consulta = _db.Recibos.AsNoTracking()
            .Include(x => x.Detalles)
            .Where(x => x.UnidadComercialId == unidadId && x.Estado == EstadoRecibo.Emitido);

        if (desde.HasValue)
        {
            var inicio = new DateTimeOffset(desde.Value.ToDateTime(TimeOnly.MinValue), ZonaBolivia).ToUniversalTime();
            consulta = consulta.Where(x => x.FechaEmision >= inicio);
        }

        if (hasta.HasValue)
        {
            var fin = new DateTimeOffset(hasta.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), ZonaBolivia).ToUniversalTime();
            consulta = consulta.Where(x => x.FechaEmision < fin);
        }

        var recibos = await consulta
            .OrderBy(x => x.FechaEmision)
            .ThenBy(x => x.Numero)
            .Take(5001)
            .ToListAsync(ct);

        if (recibos.Count > 5000)
            return BadRequest(new { mensaje = "Demasiados recibos para una sola consulta. Seleccione un rango menor." });

        var pagoIds = recibos.Select(x => x.PagoId).Distinct().ToList();
        var pagos = await _db.Pagos.AsNoTracking()
            .Where(x => pagoIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var monedas = await _db.Monedas.AsNoTracking()
            .ToDictionaryAsync(m => m.Id, m => m.Codigo, ct);

        // Igual que el diario: solo cuenta el recibo con su pago registrado.
        var lineas = recibos
            .Where(r => pagos.TryGetValue(r.PagoId, out var p) && p.Estado == EstadoPago.Registrado)
            .SelectMany(r => r.Detalles.Select(d => new
            {
                fecha = r.FechaEmision.ToOffset(ZonaBolivia).ToString("dd/MM/yyyy"),
                fechaUtc = r.FechaEmision,
                numeroRecibo = r.Numero,
                concepto = d.Concepto,
                periodo = d.PeriodoAnio is { } anio && d.PeriodoMes is { } mes ? $"{mes:00}/{anio}" : null,
                moneda = monedas.GetValueOrDefault(pagos[r.PagoId].MonedaId, "?"),
                monto = d.Monto
            }))
            .ToList();

        var totales = lineas
            .GroupBy(l => new { l.concepto, l.moneda })
            .Select(g => new { g.Key.concepto, g.Key.moneda, cantidad = g.Count(), total = g.Sum(l => l.monto) })
            .OrderBy(t => t.moneda == "BOB" ? 0 : 1)
            .ThenByDescending(t => t.total)
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            caseta = unidad.Codigo,
            bloque = unidad.Bloque,
            titular,
            desde = desde?.ToString("yyyy-MM-dd"),
            hasta = hasta?.ToString("yyyy-MM-dd"),
            recibos = lineas.Select(l => l.numeroRecibo).Distinct().Count(),
            lineas,
            totales,
            totalPorMoneda = lineas
                .GroupBy(l => l.moneda)
                .Select(g => new { moneda = g.Key, total = g.Sum(l => l.monto) })
                .OrderBy(t => t.moneda == "BOB" ? 0 : 1)
                .ToList()
        });
    }

    private sealed record ConceptoDetalle(
        Guid ConceptoCobroId,
        string Concepto,
        decimal Monto,
        int? PeriodoAnio = null,
        int? PeriodoMes = null);

    private sealed record MovimientoReporte(
        Guid ReciboId,
        long NumeroRecibo,
        Guid UnidadComercialId,
        Guid MonedaId,
        decimal Monto,
        string FormaPago,
        string Pagador,
        string? Referencia,
        string? RegistradoPor,
        DateTimeOffset FechaEmisionUtc,
        IReadOnlyList<ConceptoDetalle> Conceptos);

    private sealed record DiaReporte(
        string Fecha,
        Guid MonedaId,
        int CantidadRecibos,
        decimal Total,
        decimal Acumulado);
}
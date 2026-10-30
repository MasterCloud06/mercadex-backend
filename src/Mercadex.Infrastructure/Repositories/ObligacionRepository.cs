using Mercadex.Application.DTOs;
using Mercadex.Application.Interfaces;
using Mercadex.Domain.Cobranza;
using Mercadex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Infrastructure.Repositories;

public sealed class ObligacionRepository : IObligacionRepository
{
    private readonly MercadexDbContext _context;

    public ObligacionRepository(
        MercadexDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<ObligacionDto>> GetAllAsync()
    {
        return await _context.Obligaciones
            .AsNoTracking()
            .Select(o => new ObligacionDto(
                o.Id,
                o.UnidadComercialId,
                o.ConceptoCobroId,
                o.MonedaId,
                o.Monto,
                o.FechaEmision,
                o.FechaVencimiento,
                o.PeriodoAnio,
                o.PeriodoMes,
                o.Estado.ToString()
            ))
            .ToListAsync();
    }

    public async Task<ObligacionDto?> GetByIdAsync(Guid id)
    {
        return await _context.Obligaciones
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new ObligacionDto(
                o.Id,
                o.UnidadComercialId,
                o.ConceptoCobroId,
                o.MonedaId,
                o.Monto,
                o.FechaEmision,
                o.FechaVencimiento,
                o.PeriodoAnio,
                o.PeriodoMes,
                o.Estado.ToString()
            )
            {
                MontoPagado = o.MontoPagado,
                Saldo = o.Estado == EstadoObligacion.Pendiente ? o.Monto - o.MontoPagado : 0m
            })
            .FirstOrDefaultAsync();
    }

    public async Task<ObligacionDto> CrearAsync(
        CrearObligacionDto dto)
    {
        var obligacion = new Obligacion(
            dto.UnidadComercialId,
            dto.ConceptoCobroId,
            dto.MonedaId,
            dto.Monto,
            dto.FechaEmision,
            dto.FechaVencimiento,
            dto.PeriodoAnio,
            dto.PeriodoMes
        );

        _context.Obligaciones.Add(obligacion);

        await _context.SaveChangesAsync();

        return new ObligacionDto(
            obligacion.Id,
            obligacion.UnidadComercialId,
            obligacion.ConceptoCobroId,
            obligacion.MonedaId,
            obligacion.Monto,
            obligacion.FechaEmision,
            obligacion.FechaVencimiento,
            obligacion.PeriodoAnio,
            obligacion.PeriodoMes,
            obligacion.Estado.ToString()
        );
    }


public async Task<IReadOnlyList<Obligacion>> ObtenerPorIdsAsync(
    IEnumerable<Guid> ids)
{
    var listaIds = ids.Distinct().ToArray();

    if (listaIds.Length == 0)
    {
        return Array.Empty<Obligacion>();
    }

    // FOR UPDATE bloquea las obligaciones seleccionadas hasta
    // que la transacciÃ³n confirme o revierta el cobro.
    // Ordenamos los identificadores para reducir el riesgo
    // de interbloqueos entre cobros simultÃ¡neos.
    return await _context.Obligaciones
        .FromSqlInterpolated($"""
            SELECT *
            FROM obligaciones
            WHERE "Id" = ANY ({listaIds})
            ORDER BY "Id"
            FOR UPDATE
            """)
        .ToListAsync();
}


    public async Task<bool> MarcarComoPagadaAsync(Guid id)
    {
        var obligacion = await _context.Obligaciones
            .FirstOrDefaultAsync(o => o.Id == id);

        if (obligacion is null)
        {
            return false;
        }

        obligacion.MarcarComoPagada();

        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> RevertirAbonoAsync(Guid id, decimal monto)
    {
        // FOR UPDATE: con pagos parciales, un cobro y la anulación de otro
        // recibo pueden tocar la misma deuda a la vez; se espera al otro y se
        // parte de lo que dejó (si no, se perdería su abono).
        var obligacion = await _context.Obligaciones
            .FromSqlInterpolated($"""
                SELECT *
                FROM obligaciones
                WHERE "Id" = {id}
                FOR UPDATE
                """)
            .FirstOrDefaultAsync();

        if (obligacion is null)
        {
            return false;
        }

        obligacion.RevertirAbono(monto);

        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> RestaurarComoPendienteAsync(Guid id)
    {
        var obligacion = await _context.Obligaciones
            .FirstOrDefaultAsync(o => o.Id == id);

        if (obligacion is null)
        {
            return false;
        }

        obligacion.RestaurarComoPendiente();

        await _context.SaveChangesAsync();

        return true;
    }
public async Task<bool> ExisteAsync(
    Guid unidadComercialId,
    Guid conceptoCobroId,
    int periodoAnio,
    int periodoMes)
{
    return await _context.Obligaciones
        .AsNoTracking()
        .AnyAsync(o =>
            o.UnidadComercialId == unidadComercialId &&
            o.ConceptoCobroId == conceptoCobroId &&
            o.PeriodoAnio == periodoAnio &&
            o.PeriodoMes == periodoMes);
}
public async Task<IEnumerable<ObligacionDto>> ObtenerPendientesPorUnidadAsync(
    Guid unidadComercialId)
{
    var pendientes = await _context.Obligaciones
        .AsNoTracking()
        .Where(o =>
            o.UnidadComercialId == unidadComercialId &&
            o.Estado == EstadoObligacion.Pendiente)
        .Select(o => new ObligacionDto(
            o.Id,
            o.UnidadComercialId,
            o.ConceptoCobroId,
            o.MonedaId,
            o.Monto,
            o.FechaEmision,
            o.FechaVencimiento,
            o.PeriodoAnio,
            o.PeriodoMes,
            o.Estado.ToString()
        )
        {
            MontoPagado = o.MontoPagado,
            Saldo = o.Monto - o.MontoPagado
        })
        .ToListAsync();

    if (pendientes.Count == 0)
        return pendientes;

    // Marca de "deuda modificada": cuántas veces, cuándo, quién y por qué.
    var ids = pendientes.Select(o => o.Id).ToList();

    var cambios = await _context.CambiosMontoObligacion
        .AsNoTracking()
        .Where(c => ids.Contains(c.ObligacionId))
        .Select(c => new { c.ObligacionId, c.FechaUtc, c.Responsable, c.Motivo, c.MontoAnterior, c.MontoNuevo })
        .ToListAsync();

    var porObligacion = cambios
        .GroupBy(c => c.ObligacionId)
        .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.FechaUtc).ToList());

    var zonaBolivia = TimeSpan.FromHours(-4);

    return pendientes
        .Select(o =>
        {
            if (!porObligacion.TryGetValue(o.Id, out var lista))
                return o;

            var ultimo = lista[0];
            var fecha = new DateTimeOffset(DateTime.SpecifyKind(ultimo.FechaUtc, DateTimeKind.Utc))
                .ToOffset(zonaBolivia);

            return o with
            {
                Modificaciones = lista.Count,
                UltimaModificacion =
                    $"Modificada el {fecha:dd/MM/yyyy HH:mm} por {ultimo.Responsable}: " +
                    $"{ultimo.MontoAnterior:N2} → {ultimo.MontoNuevo:N2}. Motivo: {ultimo.Motivo}"
            };
        })
        .ToList();
}
}

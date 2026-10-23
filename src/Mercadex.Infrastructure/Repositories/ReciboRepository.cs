using Mercadex.Application.DTOs;
using Mercadex.Application.Interfaces;
using Mercadex.Domain.Caja;
using Mercadex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Infrastructure.Repositories;

public sealed class ReciboRepository : IReciboRepository
{
    private readonly MercadexDbContext _context;

    public ReciboRepository(
        MercadexDbContext context)
    {
        _context = context;
    }

    public async Task<ReciboDto?> GetByIdAsync(
        Guid id)
    {
        return await _context.Recibos
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new ReciboDto(
                r.Id,
                r.Numero,
                r.UnidadComercialId,
                r.PagoId,
                r.FechaEmision,
                r.Estado.ToString(),
                r.Detalles.Sum(d => d.Monto),
                r.Detalles.Select(d =>
                    new DetalleReciboDto(
                        d.ObligacionId,
                        d.ConceptoCobroId,
                        d.Concepto,
                        d.Monto,
                        d.PeriodoAnio,
                        d.PeriodoMes)),
                r.MotivoAnulacion,
                r.FechaAnulacion,
                r.AnuladoPor)
            {
                RegistradoPor = r.RegistradoPor,
                CargoEmisor = r.CargoEmisor,
                Observaciones = r.Observaciones
            })
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<ReciboDto>>
        GetAllAsync()
    {
        return await _context.Recibos
            .AsNoTracking()
            .OrderByDescending(r => r.FechaEmision)
            .Select(r => new ReciboDto(
                r.Id,
                r.Numero,
                r.UnidadComercialId,
                r.PagoId,
                r.FechaEmision,
                r.Estado.ToString(),
                r.Detalles.Sum(d => d.Monto),
                r.Detalles.Select(d =>
                    new DetalleReciboDto(
                        d.ObligacionId,
                        d.ConceptoCobroId,
                        d.Concepto,
                        d.Monto,
                        d.PeriodoAnio,
                        d.PeriodoMes)),
                r.MotivoAnulacion,
                r.FechaAnulacion,
                r.AnuladoPor)
            {
                RegistradoPor = r.RegistradoPor,
                CargoEmisor = r.CargoEmisor,
                Observaciones = r.Observaciones
            })
            .ToListAsync();
    }

    public async Task<ReciboDto> CrearAsync(
        CrearReciboDto dto)
    {
        var detallesDominio = dto.Detalles
            .Select(d => new DetalleRecibo(
                d.ObligacionId,
                d.ConceptoCobroId,
                d.Concepto,
                d.Monto,
                d.PeriodoAnio,
                d.PeriodoMes))
            .ToList();

        var recibo = new Recibo(
            dto.Numero,
            dto.UnidadComercialId,
            dto.PagoId,
            DateTimeOffset.UtcNow,
            detallesDominio);

        _context.Recibos.Add(recibo);

        await _context.SaveChangesAsync();

        return new ReciboDto(
            recibo.Id,
            recibo.Numero,
            recibo.UnidadComercialId,
            recibo.PagoId,
            recibo.FechaEmision,
            recibo.Estado.ToString(),
            recibo.Total,
            recibo.Detalles.Select(d =>
                new DetalleReciboDto(
                    d.ObligacionId,
                    d.ConceptoCobroId,
                    d.Concepto,
                    d.Monto,
                    d.PeriodoAnio,
                    d.PeriodoMes)),
            recibo.MotivoAnulacion,
            recibo.FechaAnulacion,
            recibo.AnuladoPor);
    }

    public async Task GuardarAsync(
        Recibo recibo)
    {
        _context.Recibos.Add(recibo);

        await _context.SaveChangesAsync();
    }

    public async Task<bool> AnularAsync(
        Guid id,
        string motivo,
        string anuladoPor)
    {
        var recibo = await _context.Recibos
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recibo is null)
        {
            return false;
        }

        recibo.Anular(
            motivo,
            DateTimeOffset.UtcNow,
            anuladoPor);

        await _context.SaveChangesAsync();

        return true;
    }
}
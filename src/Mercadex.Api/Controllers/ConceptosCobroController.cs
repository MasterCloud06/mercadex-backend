using Mercadex.Domain.Cobranza;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

/// <summary>
/// Conceptos de cobro (las "cuentas de ingreso": Mensualidad, Alquiler de
/// caseta, Excedente de luz…). Observación de la directiva: poder retirar
/// los que ya no se usan.
///
/// Un concepto no se elimina: se DESACTIVA. Así los recibos, deudas y
/// reportes anteriores siguen mostrando su nombre, pero ya no aparece al
/// cargar deudas nuevas ni al asignar tarifas. Se puede volver a activar.
/// Cambiar el nombre no altera los recibos ya emitidos (guardan su texto).
/// </summary>
[ApiController]
[Route("api/conceptos-cobro")]
public sealed class ConceptosCobroController : ControllerBase
{
    private readonly MercadexDbContext _db;

    public ConceptosCobroController(MercadexDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var conceptos = await _db.ConceptosCobro.AsNoTracking()
            .Select(c => new { c.Id, c.Codigo, c.Nombre, c.Descripcion, c.Activo })
            .ToListAsync(ct);

        var deudas = await _db.Obligaciones.AsNoTracking()
            .GroupBy(o => o.ConceptoCobroId)
            .Select(g => new
            {
                Id = g.Key,
                Total = g.Count(),
                Pendientes = g.Count(o => o.Estado == EstadoObligacion.Pendiente)
            })
            .ToDictionaryAsync(x => x.Id, ct);

        var tarifas = await _db.TarifasUnidad.AsNoTracking()
            .GroupBy(t => t.ConceptoCobroId)
            .Select(g => new { Id = g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Cantidad, ct);

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(conceptos
            .OrderByDescending(c => c.Activo)
            .ThenBy(c => c.Nombre)
            .Select(c => new
            {
                id = c.Id,
                codigo = c.Codigo,
                nombre = c.Nombre,
                descripcion = c.Descripcion,
                activo = c.Activo,
                deudas = deudas.TryGetValue(c.Id, out var d) ? d.Total : 0,
                pendientes = deudas.TryGetValue(c.Id, out var p) ? p.Pendientes : 0,
                tarifas = tarifas.GetValueOrDefault(c.Id)
            }));
    }

    public sealed record ConceptoRequest(string? Nombre, string? Descripcion);

    /// <summary>Corrige el nombre o la descripción (el código no cambia).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] ConceptoRequest r, CancellationToken ct)
    {
        var c = await _db.ConceptosCobro.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
            return NotFound(new { mensaje = "El concepto no existe." });

        var nombre = r.Nombre?.Trim() ?? "";
        if (nombre.Length < 3)
            return BadRequest(new { mensaje = "Escribe el nombre del concepto (al menos 3 letras)." });

        var repetido = await _db.ConceptosCobro.AsNoTracking()
            .AnyAsync(x => x.Id != id && x.Nombre.ToUpper() == nombre.ToUpper(), ct);
        if (repetido)
            return Conflict(new { mensaje = $"Ya existe otro concepto llamado «{nombre}»." });

        c.CambiarNombre(nombre);
        c.CambiarDescripcion(r.Descripcion);
        await _db.SaveChangesAsync(ct);

        return Ok(new { mensaje = $"Se guardó el concepto «{c.Nombre}». Los recibos ya emitidos conservan el nombre con el que se cobraron." });
    }

    [HttpPost("{id:guid}/desactivar")]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct)
    {
        var c = await _db.ConceptosCobro.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
            return NotFound(new { mensaje = "El concepto no existe." });

        if (!c.Activo)
            return Ok(new { mensaje = $"«{c.Nombre}» ya estaba desactivado." });

        var pendientes = await _db.Obligaciones.AsNoTracking()
            .CountAsync(o => o.ConceptoCobroId == id && o.Estado == EstadoObligacion.Pendiente, ct);

        c.Desactivar();
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            mensaje = $"«{c.Nombre}» quedó desactivado: ya no aparece para cargar deudas nuevas ni tarifas. " +
                      "Los recibos y reportes anteriores lo siguen mostrando." +
                      (pendientes > 0 ? $" Sus {pendientes} deudas pendientes se pueden seguir cobrando." : "")
        });
    }

    /// <summary>
    /// Elimina un concepto que nunca se usó (sin deudas ni tarifas). Uno que
    /// ya se usó no se puede borrar: se desactiva, para que los recibos y
    /// reportes anteriores sigan mostrando su nombre.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct)
    {
        var c = await _db.ConceptosCobro.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
            return NotFound(new { mensaje = "El concepto no existe." });

        var deudas = await _db.Obligaciones.AsNoTracking().CountAsync(o => o.ConceptoCobroId == id, ct);
        var tarifas = await _db.TarifasUnidad.AsNoTracking().CountAsync(t => t.ConceptoCobroId == id, ct);

        if (deudas > 0 || tarifas > 0)
        {
            return Conflict(new
            {
                mensaje = $"«{c.Nombre}» ya se usó en {deudas} {(deudas == 1 ? "deuda" : "deudas")}" +
                          (tarifas > 0 ? $" y {tarifas} {(tarifas == 1 ? "tarifa" : "tarifas")}" : "") +
                          ": no se puede eliminar sin dañar los recibos y reportes. Desactívalo para que ya no aparezca."
            });
        }

        var nombre = c.Nombre;
        _db.ConceptosCobro.Remove(c);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { mensaje = $"«{nombre}» está relacionado con otros datos y no se puede eliminar. Desactívalo." });
        }

        Mercadex.Api.Seguridad.Cambios.Detalle(HttpContext, $"Concepto {nombre} ({c.Codigo})");
        return Ok(new { mensaje = $"Se eliminó el concepto «{nombre}»." });
    }

    [HttpPost("{id:guid}/activar")]
    public async Task<IActionResult> Activar(Guid id, CancellationToken ct)
    {
        var c = await _db.ConceptosCobro.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
            return NotFound(new { mensaje = "El concepto no existe." });

        c.Activar();
        await _db.SaveChangesAsync(ct);
        return Ok(new { mensaje = $"«{c.Nombre}» está activo otra vez." });
    }
}

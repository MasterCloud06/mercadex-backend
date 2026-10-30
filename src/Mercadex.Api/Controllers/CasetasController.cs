using Mercadex.Domain.Cobranza;
using Mercadex.Domain.Comercial;
using Mercadex.Domain.Organizacion;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

/// <summary>
/// Pantalla Casetas: lista con filtros y ficha de cada unidad
/// (ubicación, titular con historial, deuda pendiente y estado).
/// Consultar: todos los roles. Modificar: administradores (PermisosPorRolFilter).
/// </summary>
[ApiController]
[Route("api/casetas")]
public sealed class CasetasController : ControllerBase
{
    private readonly MercadexDbContext _db;

    public CasetasController(MercadexDbContext db)
    {
        _db = db;
    }

    // =====================================================================
    // LISTA
    // =====================================================================

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? buscar,
        [FromQuery] int? tipo,
        [FromQuery] string? bloque,
        [FromQuery] Guid? pasilloId,
        [FromQuery] bool? sinPasillo,
        [FromQuery] string? estado,
        [FromQuery] bool? sinTitular,
        CancellationToken ct)
    {
        var unidades = await _db.UnidadesComerciales.AsNoTracking()
            .Select(u => new { u.Id, u.Codigo, u.Tipo, u.Bloque, u.PasilloId, u.Activa, u.FechaRegistro })
            .ToListAsync(ct);

        var pasillos = await _db.Pasillos.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Codigo + " · " + p.Nombre, ct);

        var titulares = (await (
                    from a in _db.AsignacionesSocioCaseta.AsNoTracking()
                    join s in _db.Socios.AsNoTracking() on a.SocioId equals s.Id
                    where a.Activa
                    select new { a.UnidadComercialId, a.SocioId, Nombre = s.Nombres + " " + s.Apellidos, s.DocumentoIdentidad, a.FechaAsignacion })
                .ToListAsync(ct))
            .GroupBy(t => t.UnidadComercialId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.FechaAsignacion).First());

        var monedas = await _db.Monedas.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Codigo, ct);

        var deudas = (await _db.Obligaciones.AsNoTracking()
                .Where(o => o.Estado == EstadoObligacion.Pendiente)
                .GroupBy(o => new { o.UnidadComercialId, o.MonedaId })
                .Select(g => new { g.Key.UnidadComercialId, g.Key.MonedaId, Cantidad = g.Count(), Total = g.Sum(o => o.Monto - o.MontoPagado) })
                .ToListAsync(ct))
            .GroupBy(d => d.UnidadComercialId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var filas = unidades.Select(u =>
        {
            titulares.TryGetValue(u.Id, out var t);
            deudas.TryGetValue(u.Id, out var d);

            return new
            {
                id = u.Id,
                codigo = u.Codigo,
                tipo = (int)u.Tipo,
                tipoTexto = TipoTexto(u.Tipo),
                bloque = u.Bloque,
                pasilloId = u.PasilloId,
                pasillo = u.PasilloId is { } p && pasillos.TryGetValue(p, out var nombre) ? nombre : null,
                activa = u.Activa,
                fechaRegistro = u.FechaRegistro,
                titularId = t?.SocioId,
                titular = t?.Nombre,
                titularDocumento = t?.DocumentoIdentidad,
                deudasPendientes = d?.Sum(x => x.Cantidad) ?? 0,
                deuda = d is null
                    ? new List<MontoMoneda>()
                    : d.Select(x => new MontoMoneda(monedas.GetValueOrDefault(x.MonedaId, "?"), x.Total)).ToList()
            };
        }).ToList();

        // ---------- Filtros ----------

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var texto = buscar.Trim();
            filas = filas.Where(f =>
                    f.codigo.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                    (f.titular?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (f.titularDocumento?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        if (tipo is { } t1)
            filas = filas.Where(f => f.tipo == t1).ToList();

        if (!string.IsNullOrWhiteSpace(bloque))
            filas = filas.Where(f => string.Equals(f.bloque?.Trim(), bloque.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        if (pasilloId is { } p1)
            filas = filas.Where(f => f.pasilloId == p1).ToList();

        if (sinPasillo == true)
            filas = filas.Where(f => f.pasilloId is null).ToList();

        filas = (estado ?? "activas").ToLowerInvariant() switch
        {
            "inactivas" => filas.Where(f => !f.activa).ToList(),
            "todas" => filas,
            _ => filas.Where(f => f.activa).ToList()
        };

        if (sinTitular == true)
            filas = filas.Where(f => f.titularId is null).ToList();

        // Orden natural: 2, 10, 100; después códigos con letras.
        filas = filas
            .OrderBy(f => f.tipo)
            .ThenBy(f => int.TryParse(f.codigo, out _) ? 0 : 1)
            .ThenBy(f => f.codigo.Length)
            .ThenBy(f => f.codigo, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            total = filas.Count,
            sinTitular = filas.Count(f => f.titularId is null),
            sinPasillo = filas.Count(f => f.pasilloId is null),
            conDeuda = filas.Count(f => f.deudasPendientes > 0),
            unidades = filas
        });
    }

    // =====================================================================
    // FICHA
    // =====================================================================

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Ficha(Guid id, CancellationToken ct)
    {
        var u = await _db.UnidadesComerciales.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null)
            return NotFound(new { mensaje = "La unidad no existe." });

        var historial = await (
                from a in _db.AsignacionesSocioCaseta.AsNoTracking()
                join s in _db.Socios.AsNoTracking() on a.SocioId equals s.Id
                where a.UnidadComercialId == id
                orderby a.FechaAsignacion descending
                select new
                {
                    socioId = s.Id,
                    nombre = s.Nombres + " " + s.Apellidos,
                    documento = s.DocumentoIdentidad,
                    telefono = s.Telefono,
                    desde = a.FechaAsignacion,
                    actual = a.Activa
                })
            .ToListAsync(ct);

        var monedas = await _db.Monedas.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Codigo, ct);

        var deudas = await _db.Obligaciones.AsNoTracking()
            .Where(o => o.UnidadComercialId == id && o.Estado == EstadoObligacion.Pendiente)
            .GroupBy(o => o.MonedaId)
            .Select(g => new { MonedaId = g.Key, Cantidad = g.Count(), Total = g.Sum(o => o.Monto - o.MontoPagado) })
            .ToListAsync(ct);

        var pasillo = u.PasilloId is null
            ? null
            : await _db.Pasillos.AsNoTracking().Where(p => p.Id == u.PasilloId)
                .Select(p => p.Codigo + " · " + p.Nombre).FirstOrDefaultAsync(ct);

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            id = u.Id,
            codigo = u.Codigo,
            tipo = (int)u.Tipo,
            tipoTexto = TipoTexto(u.Tipo),
            bloque = u.Bloque,
            pasilloId = u.PasilloId,
            pasillo,
            activa = u.Activa,
            fechaRegistro = u.FechaRegistro,
            titular = historial.FirstOrDefault(h => h.actual),
            historial,
            deudasPendientes = deudas.Sum(d => d.Cantidad),
            deuda = deudas.Select(d => new MontoMoneda(monedas.GetValueOrDefault(d.MonedaId, "?"), d.Total)).ToList()
        });
    }

    // =====================================================================
    // MODIFICAR
    // =====================================================================

    public sealed record UbicacionRequest(string? Bloque, Guid? PasilloId);

    /// <summary>Bloque y pasillo. El pasillo es opcional.</summary>
    [HttpPut("{id:guid}/ubicacion")]
    public async Task<IActionResult> Ubicacion(Guid id, [FromBody] UbicacionRequest r, CancellationToken ct)
    {
        var u = await _db.UnidadesComerciales.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null)
            return NotFound(new { mensaje = "La unidad no existe." });

        var bloque = string.IsNullOrWhiteSpace(r.Bloque) ? null : r.Bloque.Trim();
        if (bloque?.Length > 50)
            return BadRequest(new { mensaje = "El bloque no puede tener más de 50 caracteres." });

        if (r.PasilloId is { } p && !await _db.Pasillos.AnyAsync(x => x.Id == p && x.Activo, ct))
            return BadRequest(new { mensaje = "El pasillo no existe o está inactivo." });

        u.CambiarUbicacion(r.PasilloId, bloque);
        await _db.SaveChangesAsync(ct);

        return Ok(new { mensaje = $"Ubicación de la unidad {u.Codigo} guardada." });
    }

    public sealed record TitularRequest(Guid SocioId);

    /// <summary>Asigna o cambia el titular. El anterior queda en el historial.</summary>
    [HttpPost("{id:guid}/titular")]
    public async Task<IActionResult> Titular(Guid id, [FromBody] TitularRequest r, CancellationToken ct)
    {
        var u = await _db.UnidadesComerciales.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null)
            return NotFound(new { mensaje = "La unidad no existe." });

        var socio = await _db.Socios.AsNoTracking()
            .Where(s => s.Id == r.SocioId)
            .Select(s => new { s.Activo, Nombre = s.Nombres + " " + s.Apellidos })
            .FirstOrDefaultAsync(ct);

        if (socio is null)
            return BadRequest(new { mensaje = "Elige un socio de la lista." });

        if (!socio.Activo)
            return BadRequest(new { mensaje = $"{socio.Nombre} está inactivo. Actívalo en Socios antes de asignarlo." });

        var activas = await _db.AsignacionesSocioCaseta
            .Where(a => a.UnidadComercialId == id && a.Activa)
            .ToListAsync(ct);

        if (activas.Count == 1 && activas[0].SocioId == r.SocioId)
            return Ok(new { mensaje = $"{socio.Nombre} ya es el titular de la unidad {u.Codigo}." });

        foreach (var anterior in activas)
            anterior.Desactivar();

        _db.AsignacionesSocioCaseta.Add(new AsignacionSocioCaseta(r.SocioId, id, DateTimeOffset.UtcNow));
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            mensaje = activas.Count == 0
                ? $"{socio.Nombre} es ahora el titular de la unidad {u.Codigo}."
                : $"{socio.Nombre} es ahora el titular de la unidad {u.Codigo}. El anterior quedó en el historial."
        });
    }

    /// <summary>Deja la unidad sin titular. Queda en el historial.</summary>
    [HttpPost("{id:guid}/quitar-titular")]
    public async Task<IActionResult> QuitarTitular(Guid id, CancellationToken ct)
    {
        var activas = await _db.AsignacionesSocioCaseta
            .Where(a => a.UnidadComercialId == id && a.Activa)
            .ToListAsync(ct);

        if (activas.Count == 0)
            return Ok(new { mensaje = "La unidad ya estaba sin titular." });

        foreach (var a in activas)
            a.Desactivar();

        await _db.SaveChangesAsync(ct);
        return Ok(new { mensaje = "La unidad quedó sin titular. El anterior quedó en el historial." });
    }

    [HttpPost("{id:guid}/desactivar")]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct)
    {
        var u = await _db.UnidadesComerciales.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null)
            return NotFound(new { mensaje = "La unidad no existe." });

        if (!u.Activa)
            return Ok(new { mensaje = $"La unidad {u.Codigo} ya estaba inactiva." });

        u.Desactivar();
        await _db.SaveChangesAsync(ct);
        return Ok(new { mensaje = $"La unidad {u.Codigo} quedó inactiva: no recibe cargos mensuales. Sus deudas pendientes se conservan." });
    }

    [HttpPost("{id:guid}/activar")]
    public async Task<IActionResult> Activar(Guid id, CancellationToken ct)
    {
        var u = await _db.UnidadesComerciales.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null)
            return NotFound(new { mensaje = "La unidad no existe." });

        u.Activar();
        await _db.SaveChangesAsync(ct);
        return Ok(new { mensaje = $"La unidad {u.Codigo} está activa." });
    }

    public sealed record MontoMoneda(string Moneda, decimal Total);

    private static string TipoTexto(TipoUnidadComercial tipo) => tipo switch
    {
        TipoUnidadComercial.Comedor => "Comedor",
        TipoUnidadComercial.Ambulante => "Ambulante",
        _ => "Caseta"
    };
}

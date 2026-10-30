using Mercadex.Api.Seguridad;
using Mercadex.Application.DTOs.Socios;
using Mercadex.Application.Services;
using Mercadex.Domain.Caja;
using Mercadex.Domain.Cobranza;
using Mercadex.Domain.Socios;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

/// <summary>
/// Gestión de socios (manual, 1. GESTIÓN DE SOCIOS): el padrón con sus
/// casetas, deudas y estado; la ficha de cada socio; corregir datos y
/// dar de baja o reactivar. El Kardex de cada caseta guarda los datos del
/// manual que dependen de la caseta (fecha de ingreso, vivo o fallecido,
/// documento de propiedad, aporte) y los documentos escaneados.
/// </summary>
[ApiController]
[Route("api/socios")]
public sealed class SociosController : ControllerBase
{
    private const string SinDocumento = "SIN_DOCUMENTO";

    private readonly SocioService _service;
    private readonly MercadexDbContext _db;

    public SociosController(SocioService service, MercadexDbContext db)
    {
        _service = service;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Obtener()
    {
        return Ok(await _service.ObtenerAsync());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObtenerPorId(Guid id)
    {
        var socio = await _service.ObtenerPorIdAsync(id);
        return socio is null ? NotFound() : Ok(socio);
    }

    /// <summary>
    /// Registro de un socio o de un comerciante que no es socio (comedores
    /// y ambulantes): esSocio = false. El lugar de expedición del CI es
    /// opcional (el Kardex lo pide). Al no socio se le puede registrar sin CI.
    /// </summary>
    public sealed record CrearPersonaRequest(
        string? Nombres,
        string? Apellidos,
        string? DocumentoIdentidad,
        string? Telefono,
        string? LugarExpedicion,
        bool? EsSocio);

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearPersonaRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Nombres) || string.IsNullOrWhiteSpace(r.Apellidos))
            return BadRequest(new { mensaje = "Nombres y apellidos son obligatorios." });

        var esSocio = r.EsSocio ?? true;

        if (esSocio && string.IsNullOrWhiteSpace(r.DocumentoIdentidad))
            return BadRequest(new { mensaje = "El CI es obligatorio." });

        var documento = string.IsNullOrWhiteSpace(r.DocumentoIdentidad) ? SinDocumento : r.DocumentoIdentidad.Trim();

        if (documento != SinDocumento && await DuplicadoAsync(documento, null, ct) is { } otro)
            return Conflict(new { mensaje = $"Ya existe una persona con el CI {documento}: {otro}." });

        SocioDto creado;
        try
        {
            creado = await _service.CrearAsync(new CrearSocioDto(r.Nombres.Trim(), r.Apellidos.Trim(), documento, r.Telefono));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message.Split(" (Parameter")[0] });
        }

        var socio = await _db.Socios.FirstAsync(x => x.Id == creado.Id, ct);
        try
        {
            socio.DefinirTipo(esSocio);
            socio.CambiarLugarExpedicion(r.LugarExpedicion);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message.Split(" (Parameter")[0] });
        }

        await _db.SaveChangesAsync(ct);
        HttpContext.Referencia(socio.Id);
        HttpContext.Detalle($"{socio.Nombres} {socio.Apellidos}" + (esSocio ? "" : " (no socio)") +
                            (documento == SinDocumento ? "" : $" · CI {documento}"));

        return Ok(creado);
    }

    // =====================================================================
    // PADRÓN
    // =====================================================================

    /// <summary>
    /// Padrón de socios. estado: activos (por defecto), inactivos, todos,
    /// con-deuda, sin-caseta, fallecidos. texto busca en nombre, CI,
    /// celular o código de caseta.
    /// </summary>
    [HttpGet("padron")]
    public async Task<IActionResult> Padron(
        [FromQuery] string? texto,
        [FromQuery] string? estado,
        CancellationToken ct)
    {
        var socios = await _db.Socios.AsNoTracking()
            .Select(s => new { s.Id, s.Nombres, s.Apellidos, s.DocumentoIdentidad, s.Telefono, s.FechaRegistro, s.Activo, s.SinAfiliacion, s.LugarExpedicion })
            .ToListAsync(ct);

        var unidades = await (
                from a in _db.AsignacionesSocioCaseta.AsNoTracking()
                join u in _db.UnidadesComerciales.AsNoTracking() on a.UnidadComercialId equals u.Id
                where a.Activa
                select new { a.SocioId, u.Id, u.Codigo, u.Bloque, u.Tipo })
            .ToListAsync(ct);

        var deudas = await (
                from o in _db.Obligaciones.AsNoTracking()
                join m in _db.Monedas.AsNoTracking() on o.MonedaId equals m.Id
                where o.Estado == EstadoObligacion.Pendiente
                group o by new { o.UnidadComercialId, m.Codigo } into g
                select new { g.Key.UnidadComercialId, Moneda = g.Key.Codigo, Monto = g.Sum(x => x.Monto - x.MontoPagado), Cantidad = g.Count() })
            .ToListAsync(ct);

        var fallecidos = await FallecidosAsync(ct);

        var conCuentaApp = (await _db.CuentasSocioApp.AsNoTracking()
                .Select(c => new { c.SocioId, c.Activa })
                .ToListAsync(ct))
            .GroupBy(c => c.SocioId)
            .ToDictionary(g => g.Key, g => g.Any(c => c.Activa));

        var porSocio = unidades.ToLookup(u => u.SocioId);
        var deudaPorUnidad = deudas.ToLookup(d => d.UnidadComercialId);

        var filas = socios.Select(s =>
        {
            var suyas = porSocio[s.Id].OrderBy(u => u.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
            var deuda = suyas.SelectMany(u => deudaPorUnidad[u.Id])
                .GroupBy(d => d.Moneda)
                .Select(g => new { moneda = g.Key, monto = g.Sum(d => d.Monto), cantidad = g.Sum(d => d.Cantidad) })
                .OrderBy(d => d.moneda)
                .ToList();

            return new
            {
                id = s.Id,
                nombres = s.Nombres,
                apellidos = s.Apellidos,
                // Primero nombres y luego apellidos (pedido de la directiva).
                nombreCompleto = s.Nombres + " " + s.Apellidos,
                documento = s.DocumentoIdentidad == SinDocumento ? "" : s.DocumentoIdentidad,
                lugarExpedicion = s.LugarExpedicion,
                esSocio = !s.SinAfiliacion,
                tiposUnidad = suyas.Select(u => (int)u.Tipo).Distinct().ToList(),
                telefono = s.Telefono,
                fechaRegistro = s.FechaRegistro,
                activo = s.Activo,
                fallecido = fallecidos.Contains(s.Id),
                casetas = suyas.Select(u => u.Codigo).ToList(),
                deuda,
                deudasPendientes = deuda.Sum(d => d.cantidad),
                app = conCuentaApp.TryGetValue(s.Id, out var activa) ? (activa ? "Activada" : "Pendiente") : "Sin cuenta"
            };
        });

        filas = (estado ?? "activos").Trim().ToLowerInvariant() switch
        {
            "todos" => filas,
            "inactivos" => filas.Where(f => !f.activo),
            "con-deuda" => filas.Where(f => f.activo && f.deudasPendientes > 0),
            "sin-caseta" => filas.Where(f => f.activo && f.casetas.Count == 0),
            "fallecidos" => filas.Where(f => f.fallecido),
            "socios" => filas.Where(f => f.activo && f.esSocio),
            "no-socios" => filas.Where(f => f.activo && !f.esSocio),
            _ => filas.Where(f => f.activo)
        };

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var palabras = texto.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            filas = filas.Where(f => palabras.All(p =>
                f.nombreCompleto.Contains(p, StringComparison.OrdinalIgnoreCase) ||
                f.documento.Contains(p, StringComparison.OrdinalIgnoreCase) ||
                (f.telefono ?? "").Contains(p, StringComparison.OrdinalIgnoreCase) ||
                f.casetas.Any(c => string.Equals(c, p, StringComparison.OrdinalIgnoreCase))));
        }

        var lista = filas
            .OrderBy(f => f.nombreCompleto, StringComparer.Create(new System.Globalization.CultureInfo("es-BO"), true))
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            total = lista.Count,
            activos = socios.Count(s => s.Activo),
            noSocios = socios.Count(s => s.Activo && s.SinAfiliacion),
            inactivos = socios.Count(s => !s.Activo),
            socios = lista
        });
    }

    // =====================================================================
    // FICHA
    // =====================================================================

    [HttpGet("{id:guid}/ficha")]
    public async Task<IActionResult> Ficha(Guid id, CancellationToken ct)
    {
        var s = await _db.Socios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null)
            return NotFound(new { mensaje = "El socio no existe." });

        // Todas las casetas que tuvo, actuales y anteriores.
        var asignaciones = await (
                from a in _db.AsignacionesSocioCaseta.AsNoTracking()
                join u in _db.UnidadesComerciales.AsNoTracking() on a.UnidadComercialId equals u.Id
                where a.SocioId == id
                orderby a.Activa descending, a.FechaAsignacion descending
                select new { a.Activa, a.FechaAsignacion, UnidadId = u.Id, u.Codigo, u.Bloque, u.Tipo, u.PasilloId })
            .ToListAsync(ct);

        var idsActuales = asignaciones.Where(a => a.Activa).Select(a => a.UnidadId).Distinct().ToList();

        var pasillos = await _db.Pasillos.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);

        var deudas = await (
                from o in _db.Obligaciones.AsNoTracking()
                join m in _db.Monedas.AsNoTracking() on o.MonedaId equals m.Id
                where o.Estado == EstadoObligacion.Pendiente && idsActuales.Contains(o.UnidadComercialId)
                group o by new { o.UnidadComercialId, m.Codigo } into g
                select new { g.Key.UnidadComercialId, Moneda = g.Key.Codigo, Monto = g.Sum(x => x.Monto - x.MontoPagado), Cantidad = g.Count() })
            .ToListAsync(ct);

        var kardex = await _db.KardexCasetas.AsNoTracking()
            .Where(k => idsActuales.Contains(k.UnidadComercialId) || k.SocioId == id)
            .Select(k => new { k.UnidadComercialId, k.SocioId, k.FechaIngreso, k.EstadoVida, k.AporteSocioPagado, k.TipoDocumentoPropiedad })
            .ToListAsync(ct);

        var documentos = await _db.DocumentosKardex.AsNoTracking()
            .Where(d => d.SocioId == id)
            .Select(d => new { d.Tipo })
            .ToListAsync(ct);

        var ultimoPago = await (
                from r in _db.Recibos.AsNoTracking()
                where idsActuales.Contains(r.UnidadComercialId) && r.Estado == EstadoRecibo.Emitido
                orderby r.FechaEmision descending
                select new { r.Numero, r.FechaEmision })
            .FirstOrDefaultAsync(ct);

        var cuentas = await _db.CuentasSocioApp.AsNoTracking()
            .Where(c => c.SocioId == id)
            .Select(c => new { c.Activa, c.UltimoAccesoUtc })
            .ToListAsync(ct);

        var casetas = asignaciones.Select(a =>
        {
            var k = kardex.FirstOrDefault(x => x.UnidadComercialId == a.UnidadId);
            return new
            {
                unidadId = a.UnidadId,
                codigo = a.Codigo,
                bloque = a.Bloque,
                tipo = a.Tipo.ToString(),
                pasillo = a.PasilloId is { } p && pasillos.TryGetValue(p, out var nombre) ? nombre : null,
                actual = a.Activa,
                desde = a.FechaAsignacion,
                fechaIngreso = a.Activa ? k?.FechaIngreso : null,
                tieneKardex = a.Activa && k is not null,
                deuda = a.Activa
                    ? deudas.Where(d => d.UnidadComercialId == a.UnidadId)
                        .Select(d => new { moneda = d.Moneda, monto = d.Monto, cantidad = d.Cantidad })
                        .ToList()
                    : new()
            };
        }).ToList();

        var fallecido = kardex.Any(k =>
            string.Equals(k.EstadoVida, "FALLECIDO", StringComparison.OrdinalIgnoreCase));

        var tipos = documentos.Select(d => d.Tipo).ToHashSet();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            id = s.Id,
            nombres = s.Nombres,
            apellidos = s.Apellidos,
            documento = s.DocumentoIdentidad == SinDocumento ? "" : s.DocumentoIdentidad,
            lugarExpedicion = s.LugarExpedicion,
            esSocio = s.EsSocio,
            telefono = s.Telefono,
            fechaRegistro = s.FechaRegistro,
            activo = s.Activo,
            fallecido,
            fechaIngreso = kardex.Where(k => k.FechaIngreso != null).Min(k => k.FechaIngreso),
            aporteSocio = kardex.Any(k => k.AporteSocioPagado == true) ? "Pagado"
                : kardex.Any(k => k.AporteSocioPagado == false) ? "No pagado" : "Sin registrar",
            casetas,
            deudaTotal = deudas.GroupBy(d => d.Moneda)
                .Select(g => new { moneda = g.Key, monto = g.Sum(d => d.Monto), cantidad = g.Sum(d => d.Cantidad) })
                .OrderBy(d => d.moneda)
                .ToList(),
            ultimoPago,
            documentosEscaneados = documentos.Count,
            faltan = new[]
                {
                    (Falta: !tipos.Contains(TipoDocumentoKardex.Identidad), Texto: "CI escaneado"),
                    (Falta: !tipos.Contains(TipoDocumentoKardex.Fotografia), Texto: "Foto 3x2"),
                    (Falta: fallecido && !tipos.Contains(TipoDocumentoKardex.Defuncion), Texto: "Certificado de óbito"),
                    (Falta: string.IsNullOrWhiteSpace(s.Telefono), Texto: "Celular"),
                    (Falta: s.DocumentoIdentidad == SinDocumento, Texto: "Número de CI")
                }
                .Where(x => x.Falta)
                .Select(x => x.Texto)
                .ToList(),
            app = cuentas.Count == 0 ? "Sin cuenta" : cuentas.Any(c => c.Activa) ? "Activada" : "Pendiente de activar",
            appUltimoAcceso = cuentas.Max(c => c.UltimoAccesoUtc)
        });
    }

    // =====================================================================
    // CAMBIOS (solo administrador, por PermisosPorRolFilter)
    // =====================================================================

    public sealed record DatosSocioRequest(
        string Nombres,
        string Apellidos,
        string? DocumentoIdentidad,
        string? Telefono,
        string? LugarExpedicion = null,
        bool? EsSocio = null);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] DatosSocioRequest r, CancellationToken ct)
    {
        var s = await _db.Socios.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null)
            return NotFound(new { mensaje = "El socio no existe." });

        var documento = string.IsNullOrWhiteSpace(r.DocumentoIdentidad) ? SinDocumento : r.DocumentoIdentidad.Trim();

        if (documento != SinDocumento && await DuplicadoAsync(documento, id, ct) is { } otro)
            return Conflict(new { mensaje = $"El CI {documento} ya es de otro socio: {otro}." });

        try
        {
            s.ActualizarDatos(r.Nombres ?? "", r.Apellidos ?? "", documento, r.Telefono);
            // Las versiones anteriores del escritorio no envían estos datos: se conservan.
            if (r.LugarExpedicion is not null)
                s.CambiarLugarExpedicion(r.LugarExpedicion);
            if (r.EsSocio is { } esSocio)
                s.DefinirTipo(esSocio);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message.Split(" (Parameter")[0] });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { mensaje = $"Se guardaron los datos de {s.Nombres} {s.Apellidos}." });
    }

    /// <summary>
    /// Baja del socio. No se puede mientras sea titular de alguna caseta:
    /// primero se cambia o se quita el titular en Casetas. Sus recibos,
    /// deudas y documentos se conservan.
    /// </summary>
    [HttpPost("{id:guid}/desactivar")]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct)
    {
        var s = await _db.Socios.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null)
            return NotFound(new { mensaje = "El socio no existe." });

        if (!s.Activo)
            return Ok(new { mensaje = $"{s.Nombres} {s.Apellidos} ya estaba dado de baja." });

        var casetas = await (
                from a in _db.AsignacionesSocioCaseta
                join u in _db.UnidadesComerciales on a.UnidadComercialId equals u.Id
                where a.SocioId == id && a.Activa
                select u.Codigo)
            .ToListAsync(ct);

        if (casetas.Count > 0)
            return BadRequest(new
            {
                mensaje = $"{s.Nombres} {s.Apellidos} todavía es titular de {string.Join(", ", casetas)}. " +
                          "Primero asigna otro titular o quita el titular en Casetas."
            });

        s.Desactivar();
        await _db.SaveChangesAsync(ct);
        return Ok(new { mensaje = $"{s.Nombres} {s.Apellidos} quedó dado de baja. Sus recibos y documentos se conservan." });
    }

    [HttpPost("{id:guid}/activar")]
    public async Task<IActionResult> Activar(Guid id, CancellationToken ct)
    {
        var s = await _db.Socios.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null)
            return NotFound(new { mensaje = "El socio no existe." });

        s.Activar();
        await _db.SaveChangesAsync(ct);
        return Ok(new { mensaje = $"{s.Nombres} {s.Apellidos} está activo otra vez." });
    }

    // =====================================================================

    private async Task<string?> DuplicadoAsync(string documento, Guid? excepto, CancellationToken ct)
    {
        var ci = documento.Trim().ToUpper();
        if (ci == SinDocumento)
            return null;

        return await _db.Socios.AsNoTracking()
            .Where(s => s.DocumentoIdentidad.ToUpper() == ci && (excepto == null || s.Id != excepto))
            .Select(s => s.Nombres + " " + s.Apellidos)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Socios marcados FALLECIDO en el Kardex de alguna caseta (por socio del Kardex o titular).</summary>
    private async Task<HashSet<Guid>> FallecidosAsync(CancellationToken ct)
    {
        var porKardex = await _db.KardexCasetas.AsNoTracking()
            .Where(k => k.EstadoVida == "FALLECIDO" && k.SocioId != null)
            .Select(k => k.SocioId!.Value)
            .ToListAsync(ct);

        var porTitular = await (
                from k in _db.KardexCasetas.AsNoTracking()
                join a in _db.AsignacionesSocioCaseta.AsNoTracking() on k.UnidadComercialId equals a.UnidadComercialId
                where k.EstadoVida == "FALLECIDO" && k.SocioId == null && a.Activa
                select a.SocioId)
            .ToListAsync(ct);

        return porKardex.Concat(porTitular).ToHashSet();
    }
}

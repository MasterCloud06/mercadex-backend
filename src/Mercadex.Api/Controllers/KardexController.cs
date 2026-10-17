
using Mercadex.Domain.Organizacion;
using Mercadex.Domain.Socios;
using Mercadex.Infrastructure.Persistence;
using Mercadex.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

[ApiController]
[Route("api/kardex")]
public sealed class KardexController : ControllerBase
{
    private readonly MercadexDbContext _db;

    public KardexController(MercadexDbContext db)
    {
        _db = db;
    }

    // Consultar el Kardex de una caseta.
    // Si todavía no tiene ficha, devuelve sus datos básicos.

    [HttpGet("caseta/{unidadId:guid}")]
    public async Task<IActionResult> Obtener(
        Guid unidadId,
        CancellationToken cancellationToken)
    {
        var unidad = await _db.UnidadesComerciales
            .AsNoTracking()
            .Where(u =>
                u.Id == unidadId &&
                u.Tipo == TipoUnidadComercial.Caseta)
            .Select(u => new
            {
                u.Id,
                u.Codigo,
                u.Bloque,
                u.PasilloId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (unidad is null)
        {
            return NotFound(new
            {
                mensaje = "No se encontró la caseta."
            });
        }

        var kardex = await _db.KardexCasetas
            .AsNoTracking()
            .FirstOrDefaultAsync(
                k => k.UnidadComercialId == unidadId,
                cancellationToken);

        // ---------- Ficha completa (manual: Kardex de cada caseta) ----------

        var pasillo = unidad.PasilloId is null
            ? null
            : await _db.Pasillos.AsNoTracking()
                .Where(p => p.Id == unidad.PasilloId)
                .Select(p => new { p.Codigo, p.Nombre })
                .FirstOrDefaultAsync(cancellationToken);

        var titular = await (
                from a in _db.AsignacionesSocioCaseta.AsNoTracking()
                join so in _db.Socios.AsNoTracking() on a.SocioId equals so.Id
                where a.UnidadComercialId == unidadId && a.Activa
                orderby a.FechaAsignacion descending
                select new
                {
                    so.Id,
                    nombre = so.Nombres + " " + so.Apellidos,
                    documento = so.DocumentoIdentidad,
                    lugarExpedicion = so.LugarExpedicion,
                    telefono = so.Telefono,
                    fechaRegistro = so.FechaRegistro
                })
            .FirstOrDefaultAsync(cancellationToken);

        // Los documentos se buscan del socio del Kardex o, si no tiene, del titular.
        var socioDocumentos = kardex?.SocioId ?? titular?.Id;

        var documentos = socioDocumentos is null
            ? new List<DocumentoResumen>()
            : await _db.DocumentosKardex.AsNoTracking()
                .Where(d => d.SocioId == socioDocumentos &&
                            (d.UnidadComercialId == null || d.UnidadComercialId == unidadId))
                .OrderByDescending(d => d.FechaRegistroUtc)
                .Select(d => new DocumentoResumen(
                    d.Id, (int)d.Tipo, d.Tipo.ToString(), d.NombreArchivo, d.TipoContenido,
                    d.RegistradoPor, d.FechaRegistroUtc))
                .ToListAsync(cancellationToken);

        var tipos = documentos.Select(d => d.TipoNumero).ToHashSet();
        var faltantes = CalcularFaltantes(
                socioDocumentos is not null, tipos, kardex?.FechaIngreso,
                kardex?.EstadoVida, kardex?.TipoDocumentoPropiedad,
                kardex?.PropietarioAnterior, kardex?.AporteSocioPagado,
                TieneExpedicion(titular?.lugarExpedicion, titular?.documento))
            .Select(f => (object)new { tipo = f.Tipo, texto = f.Texto })
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            unidad,
            kardex,
            pasillo,
            titular,
            socioDocumentosId = socioDocumentos,
            documentos = documentos.Select(d => new
            {
                id = d.Id,
                tipo = d.TipoNumero,
                tipoNombre = d.TipoNombre,
                nombreArchivo = d.NombreArchivo,
                tipoContenido = d.TipoContenido,
                registradoPor = d.RegistradoPor,
                fechaRegistroUtc = d.FechaRegistroUtc
            }),
            faltantes
        });
    }

    private sealed record DocumentoResumen(
        Guid Id,
        int TipoNumero,
        string TipoNombre,
        string NombreArchivo,
        string TipoContenido,
        string RegistradoPor,
        DateTimeOffset FechaRegistroUtc);

    // Crear o actualizar la ficha de una caseta.

    [HttpPut("caseta/{unidadId:guid}")]
    public async Task<IActionResult> Guardar(
        Guid unidadId,
        [FromBody] GuardarKardexRequest datos,
        CancellationToken cancellationToken)
    {
        var existeCaseta = await _db.UnidadesComerciales
            .AnyAsync(
                u => u.Id == unidadId &&
                     u.Tipo == TipoUnidadComercial.Caseta,
                cancellationToken);

        if (!existeCaseta)
        {
            return NotFound(new
            {
                mensaje = "La caseta no existe."
            });
        }

        if (datos.SocioId.HasValue)
        {
            var existeSocio = await _db.Socios
                .AnyAsync(
                    s => s.Id == datos.SocioId.Value,
                    cancellationToken);

            if (!existeSocio)
            {
                return BadRequest(new
                {
                    mensaje = "El socio seleccionado no existe."
                });
            }
        }

        var estadosPermitidos = new[]
        {
            "VIVO",
            "FALLECIDO"
        };

        var documentosPermitidos = new[]
        {
            "DERECHOS_REALES",
            "MINUTA",
            "OTRO"
        };

        var estado = Limpiar(datos.EstadoVida)?
            .ToUpperInvariant();

        var tipoDocumento =
            Limpiar(datos.TipoDocumentoPropiedad)?
                .ToUpperInvariant();

        if (estado is not null &&
            !estadosPermitidos.Contains(estado))
        {
            return BadRequest(new
            {
                mensaje = "Estado de vida no válido."
            });
        }

        if (tipoDocumento is not null &&
            !documentosPermitidos.Contains(tipoDocumento))
        {
            return BadRequest(new
            {
                mensaje = "Tipo de documento no válido."
            });
        }

        if (datos.FechaIngreso.HasValue &&
            datos.FechaIngreso.Value >
                DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return BadRequest(new
            {
                mensaje = "La fecha de ingreso no puede ser futura."
            });
        }

        var kardex = await _db.KardexCasetas
            .FirstOrDefaultAsync(
                k => k.UnidadComercialId == unidadId,
                cancellationToken);

        var nuevo = kardex is null;

        if (kardex is null)
        {
            kardex = new KardexCaseta
            {
                Id = Guid.NewGuid(),
                UnidadComercialId = unidadId,
                FechaRegistro = DateTimeOffset.UtcNow
            };

            _db.KardexCasetas.Add(kardex);
        }

        kardex.SocioId = datos.SocioId;
        kardex.FechaIngreso = datos.FechaIngreso;
        kardex.EstadoVida = estado;
        kardex.TipoDocumentoPropiedad = tipoDocumento;

        kardex.PropietarioAnterior =
            Limpiar(datos.PropietarioAnterior);

        kardex.AporteSocioPagado =
            datos.AporteSocioPagado;

        kardex.Observaciones =
            Limpiar(datos.Observaciones);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new
            {
                mensaje =
                    "No se pudo guardar. Comprueba si la ficha " +
                    "fue modificada simultáneamente."
            });
        }

        return Ok(new
        {
            mensaje = nuevo
                ? "Kardex creado correctamente."
                : "Kardex actualizado correctamente.",

            kardex.Id,
            kardex.UnidadComercialId,
            kardex.FechaRegistro
        });
    }

    /// <summary>
    /// Lo que le falta al expediente según el manual (tabla del Kardex):
    /// CI y foto escaneados, documento de propiedad si se indicó, óbito si
    /// falleció, comprobante del aporte si lo pagó, fecha de ingreso y el
    /// propietario anterior cuando el documento es Minuta.
    /// </summary>
    internal static List<(int? Tipo, string Texto)> CalcularFaltantes(
        bool tieneSocio,
        IReadOnlySet<int> tipos,
        DateOnly? fechaIngreso,
        string? estadoVida,
        string? tipoDocumentoPropiedad,
        string? propietarioAnterior,
        bool? aportePagado,
        bool tieneExpedicion = true)
    {
        var faltantes = new List<(int? Tipo, string Texto)>();

        void Falta(TipoDocumentoKardex tipo, string texto)
        {
            if (!tipos.Contains((int)tipo))
                faltantes.Add(((int)tipo, texto));
        }

        if (!tieneSocio)
        {
            faltantes.Add((null, "La caseta no tiene titular ni socio en el Kardex."));
        }
        else
        {
            Falta(TipoDocumentoKardex.Identidad, "CI escaneado");
            Falta(TipoDocumentoKardex.Fotografia, "Foto 3x2");
            if (!string.IsNullOrWhiteSpace(tipoDocumentoPropiedad))
                Falta(TipoDocumentoKardex.Titularidad, "Documento de propiedad escaneado (Derechos Reales o Minuta)");
            if (string.Equals(estadoVida, "FALLECIDO", StringComparison.OrdinalIgnoreCase))
                Falta(TipoDocumentoKardex.Defuncion, "Certificado de óbito escaneado");
            if (aportePagado == true)
                Falta(TipoDocumentoKardex.ComprobanteAporte, "Comprobante del aporte de socio escaneado");

            // En el Kardex el lugar de expedición del CI es obligatorio (en Socios es opcional).
            if (!tieneExpedicion)
                faltantes.Add((null, "Lugar de expedición del CI"));
        }

        if (fechaIngreso is null)
            faltantes.Add((null, "Fecha de ingreso"));

        if (string.Equals(tipoDocumentoPropiedad, "MINUTA", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(propietarioAnterior))
            faltantes.Add((null, "Nombre del propietario anterior (Minuta)"));

        return faltantes;
    }

    /// <summary>
    /// Estado del expediente de todas las casetas, para la lista del Kardex:
    /// titular, si ya tiene ficha y cuántas cosas le faltan.
    /// </summary>
    [HttpGet("resumen")]
    public async Task<IActionResult> Resumen(CancellationToken ct)
    {
        var casetas = await _db.UnidadesComerciales.AsNoTracking()
            .Where(u => u.Tipo == TipoUnidadComercial.Caseta)
            .Select(u => new { u.Id, u.Codigo, u.Bloque, u.PasilloId, u.Activa })
            .ToListAsync(ct);

        var titulares = (await (
                from a in _db.AsignacionesSocioCaseta.AsNoTracking()
                join s in _db.Socios.AsNoTracking() on a.SocioId equals s.Id
                where a.Activa
                orderby a.FechaAsignacion descending
                select new { a.UnidadComercialId, s.Id, Nombre = s.Nombres + " " + s.Apellidos, s.LugarExpedicion, s.DocumentoIdentidad })
            .ToListAsync(ct))
            .GroupBy(t => t.UnidadComercialId)
            .ToDictionary(g => g.Key, g => g.First());

        var kardex = (await _db.KardexCasetas.AsNoTracking().ToListAsync(ct))
            .GroupBy(k => k.UnidadComercialId)
            .ToDictionary(g => g.Key, g => g.First());

        var documentos = (await _db.DocumentosKardex.AsNoTracking()
                .Select(d => new { d.SocioId, d.UnidadComercialId, Tipo = (int)d.Tipo })
                .ToListAsync(ct))
            .ToLookup(d => d.SocioId);

        var pasillos = await _db.Pasillos.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);

        var filas = casetas.Select(u =>
            {
                kardex.TryGetValue(u.Id, out var k);
                titulares.TryGetValue(u.Id, out var t);
                var socio = k?.SocioId ?? t?.Id;
                var tipos = socio is null
                    ? new HashSet<int>()
                    : documentos[socio.Value]
                        .Where(d => d.UnidadComercialId == null || d.UnidadComercialId == u.Id)
                        .Select(d => d.Tipo)
                        .ToHashSet();

                var faltan = CalcularFaltantes(socio is not null, tipos, k?.FechaIngreso, k?.EstadoVida,
                    k?.TipoDocumentoPropiedad, k?.PropietarioAnterior, k?.AporteSocioPagado,
                    t is null || TieneExpedicion(t.LugarExpedicion, t.DocumentoIdentidad));

                return new
                {
                    unidadId = u.Id,
                    codigo = u.Codigo,
                    bloque = u.Bloque,
                    pasillo = u.PasilloId is { } p && pasillos.TryGetValue(p, out var np) ? np : null,
                    activa = u.Activa,
                    titular = t?.Nombre,
                    tieneKardex = k is not null,
                    fallecido = string.Equals(k?.EstadoVida, "FALLECIDO", StringComparison.OrdinalIgnoreCase),
                    documentos = tipos.Count,
                    faltan = faltan.Count,
                    faltantes = faltan.Select(f => f.Texto).ToList()
                };
            })
            .OrderBy(f => int.TryParse(f.codigo, out var n) ? n : int.MaxValue)
            .ThenBy(f => f.codigo, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            total = filas.Count,
            completos = filas.Count(f => f.faltan == 0),
            sinKardex = filas.Count(f => !f.tieneKardex),
            sinTitular = filas.Count(f => f.titular is null),
            casetas = filas
        });
    }

    private static readonly string[] Extensiones =
        { "LP", "SC", "CB", "OR", "PT", "CH", "TJ", "BE", "PD", "PA" };

    /// <summary>
    /// Tiene lugar de expedición: el dato propio o, en registros anteriores,
    /// la extensión escrita junto al CI ("1234567 LP").
    /// </summary>
    internal static bool TieneExpedicion(string? lugar, string? documento)
    {
        if (!string.IsNullOrWhiteSpace(lugar))
            return true;

        var partes = (documento ?? "").Trim().ToUpperInvariant()
            .Split(new[] { ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
        return partes.Length > 1 && Extensiones.Contains(partes[^1]);
    }

    private static string? Limpiar(string? valor)
    {
        return string.IsNullOrWhiteSpace(valor)
            ? null
            : valor.Trim();
    }
}

public sealed class GuardarKardexRequest
{
    public Guid? SocioId { get; set; }

    public DateOnly? FechaIngreso { get; set; }

    public string? EstadoVida { get; set; }

    public string? TipoDocumentoPropiedad { get; set; }

    public string? PropietarioAnterior { get; set; }

    public bool? AporteSocioPagado { get; set; }

    public string? Observaciones { get; set; }
}

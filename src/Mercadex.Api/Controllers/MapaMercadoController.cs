using Mercadex.Api.Seguridad;
using Mercadex.Domain.Cobranza;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

/// <summary>
/// Mapa del mercado: todas las unidades agrupadas por bloque con su
/// titular y su situación de deuda. Solo lectura. Las deudas se
/// informan por moneda, sin conversión.
/// </summary>
[ApiController]
[Route("api/mapa-mercado")]
public sealed class MapaMercadoController : ControllerBase
{
    private static readonly TimeSpan ZonaBolivia = TimeSpan.FromHours(-4);

    private readonly MercadexDbContext _db;

    public MapaMercadoController(MercadexDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Obtener(CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ZonaBolivia).DateTime);

        var monedas = await _db.Monedas.AsNoTracking()
            .ToDictionaryAsync(m => m.Id, m => m.Codigo, ct);

        var unidades = await _db.UnidadesComerciales.AsNoTracking()
            .Select(u => new { u.Id, u.Codigo, u.Bloque, Tipo = (int)u.Tipo, u.Activa, u.PasilloId, u.PlanoX, u.PlanoY })
            .ToListAsync(ct);

        var pasillos = await _db.Pasillos.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => new { p.Codigo, p.Nombre }, ct);

        var titulares = await (
                from a in _db.AsignacionesSocioCaseta.AsNoTracking()
                join s in _db.Socios.AsNoTracking() on a.SocioId equals s.Id
                where a.Activa
                select new { a.UnidadComercialId, Nombre = s.Nombres + " " + s.Apellidos })
            .ToListAsync(ct);

        var titularPorUnidad = titulares
            .GroupBy(t => t.UnidadComercialId)
            .ToDictionary(g => g.Key, g => g.First().Nombre);

        var pendientes = await _db.Obligaciones.AsNoTracking()
            .Where(o => o.Estado == EstadoObligacion.Pendiente)
            .Select(o => new { o.UnidadComercialId, o.MonedaId, o.Monto, o.FechaEmision, o.ConceptoCobroId })
            .ToListAsync(ct);

        var deudaPorUnidad = pendientes
            .GroupBy(o => o.UnidadComercialId)
            .ToDictionary(g => g.Key, g => g.ToList());

        static string Situacion(bool activa, int obligaciones, int dias) =>
            !activa ? "inactiva"
            : obligaciones == 0 ? "al-dia"
            : dias <= 30 ? "reciente"
            : dias <= 90 ? "atrasada"
            : "critica";

        var resultado = unidades
            .Select(u =>
            {
                deudaPorUnidad.TryGetValue(u.Id, out var lista);
                lista ??= [];

                var dias = lista.Count == 0
                    ? 0
                    : hoy.DayNumber - lista.Min(o => o.FechaEmision).DayNumber;

                return new
                {
                    id = u.Id,
                    codigo = u.Codigo,
                    bloque = string.IsNullOrWhiteSpace(u.Bloque) ? "Sin bloque" : u.Bloque.Trim(),
                    tipo = u.Tipo,
                    activa = u.Activa,
                    pasillo = u.PasilloId is { } pid && pasillos.TryGetValue(pid, out var pa) ? pa.Nombre : null,
                    pasilloCodigo = u.PasilloId is { } pid2 && pasillos.TryGetValue(pid2, out var pb) ? pb.Codigo : null,
                    planoX = u.PlanoX,
                    planoY = u.PlanoY,
                    titular = titularPorUnidad.GetValueOrDefault(u.Id),
                    obligaciones = lista.Count,
                    diasDeuda = dias,
                    situacion = Situacion(u.Activa, lista.Count, dias),
                    deudas = lista
                        .GroupBy(o => o.MonedaId)
                        .Select(g => new { moneda = monedas.GetValueOrDefault(g.Key, "?"), total = g.Sum(o => o.Monto) })
                        .OrderBy(d => d.moneda == "BOB" ? 0 : 1)
                        .ToList(),
                    // Qué cuentas (conceptos de cobro) debe: para pintar el plano por cuenta.
                    conceptos = lista
                        .GroupBy(o => o.ConceptoCobroId)
                        .Select(g => new
                        {
                            id = g.Key,
                            obligaciones = g.Count(),
                            deudas = g.GroupBy(o => o.MonedaId)
                                .Select(m => new { moneda = monedas.GetValueOrDefault(m.Key, "?"), total = m.Sum(o => o.Monto) })
                                .OrderBy(d => d.moneda == "BOB" ? 0 : 1)
                                .ToList()
                        })
                        .ToList()
                };
            })
            .ToList();

        var bloques = resultado
            .GroupBy(u => u.bloque)
            .OrderBy(g => g.Key == "Sin bloque" ? 1 : 0)
            .ThenBy(g => g.Key)
            .Select(g => new
            {
                bloque = g.Key,
                unidades = g
                    .OrderBy(u => u.tipo)
                    .ThenBy(u => u.codigo.Length)
                    .ThenBy(u => u.codigo)
                    .ToList()
            })
            .ToList();

        // Cuentas con deuda pendiente y cuántas unidades las deben.
        var idsConDeuda = pendientes.Select(o => o.ConceptoCobroId).Distinct().ToList();
        var nombresConcepto = await _db.ConceptosCobro.AsNoTracking()
            .Where(c => idsConDeuda.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.Codigo, c.Nombre }, ct);
        var conceptos = pendientes
            .GroupBy(o => o.ConceptoCobroId)
            .Select(g => new
            {
                id = g.Key,
                codigo = nombresConcepto.GetValueOrDefault(g.Key)?.Codigo ?? "",
                nombre = nombresConcepto.GetValueOrDefault(g.Key)?.Nombre ?? "Sin nombre",
                unidades = g.Select(o => o.UnidadComercialId).Distinct().Count(),
                obligaciones = g.Count()
            })
            .OrderByDescending(c => c.unidades)
            .ThenBy(c => c.nombre)
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        var plano = PlanoMercado.Leer();

        return Ok(new
        {
            generado = DateTimeOffset.UtcNow.ToOffset(ZonaBolivia),
            bloques,
            conceptos,
            // Plano oficial: medidas, pasillos y ubicaciones que el plano repite.
            plano = new
            {
                plano.Fuente,
                plano.Ancho,
                plano.Alto,
                plano.Nota,
                pasillos = plano.Pasillos,
                porConfirmar = plano.PorConfirmar,
                casetasEnPlano = plano.Casetas.Count,
                cargadas = resultado.Count(u => u.planoX != null)
            }
        });
    }

    // =============================================================
    // CARGAR LAS CASETAS DEL PLANO
    // =============================================================

    /// <summary>
    /// Carga las casetas del plano oficial (PLANOS - ASOCIACIÓN 13 DE JULIO):
    /// crea los pasillos y galerías, crea las casetas que faltan y a cada
    /// caseta le pone su pasillo y su lugar en el mapa. No toca titulares,
    /// deudas, recibos ni el bloque. Se puede repetir sin duplicar nada.
    /// </summary>
    [HttpPost("cargar-plano")]
    public async Task<IActionResult> CargarPlano(CancellationToken ct)
    {
        var plano = PlanoMercado.Leer();

        // Pasillos y galerías: se reutiliza el que ya exista con el mismo código o nombre.
        var existentes = await _db.Pasillos.ToListAsync(ct);
        var pasilloPorCodigo = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var pasillosCreados = 0;

        foreach (var p in plano.Pasillos)
        {
            var ya = existentes.FirstOrDefault(x =>
                string.Equals(x.Codigo, p.Codigo, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Nombre, p.Nombre, StringComparison.OrdinalIgnoreCase));

            if (ya is null)
            {
                ya = new Mercadex.Domain.Organizacion.Pasillo(p.Codigo, p.Nombre);
                _db.Pasillos.Add(ya);
                existentes.Add(ya);
                pasillosCreados++;
            }

            pasilloPorCodigo[p.Codigo] = ya.Id;
        }

        var unidades = await _db.UnidadesComerciales.ToListAsync(ct);
        var porCodigo = unidades
            .GroupBy(u => NormalizarCodigo(u.Codigo))
            .ToDictionary(g => g.Key, g => g.ToList());

        int creadas = 0, actualizadas = 0, sinCambios = 0;
        var conflictos = new List<string>();

        foreach (var c in plano.Casetas)
        {
            var pasilloId = pasilloPorCodigo[c.Pasillo];
            porCodigo.TryGetValue(NormalizarCodigo(c.Numero), out var mismas);
            var caseta = mismas?.FirstOrDefault(u => u.Tipo == Mercadex.Domain.Organizacion.TipoUnidadComercial.Caseta);

            if (caseta is null && mismas is { Count: > 0 })
            {
                // Ese número ya lo usa un comedor o ambulante: no se toca.
                conflictos.Add($"{c.Numero} (ya es {mismas[0].Tipo.ToString().ToLowerInvariant()})");
                continue;
            }

            if (caseta is null)
            {
                caseta = new Mercadex.Domain.Organizacion.UnidadComercial(
                    c.Numero, Mercadex.Domain.Organizacion.TipoUnidadComercial.Caseta, pasilloId);
                caseta.UbicarEnPlano(c.X, c.Y);
                _db.UnidadesComerciales.Add(caseta);
                creadas++;
                continue;
            }

            if (caseta.PasilloId == pasilloId && caseta.PlanoX == c.X && caseta.PlanoY == c.Y)
            {
                sinCambios++;
                continue;
            }

            caseta.CambiarUbicacion(pasilloId, caseta.Bloque);
            caseta.UbicarEnPlano(c.X, c.Y);
            actualizadas++;
        }

        await _db.SaveChangesAsync(ct);

        HttpContext.Detalle(
            $"Plano oficial: {creadas} casetas nuevas, {actualizadas} actualizadas, {sinCambios} sin cambios, " +
            $"{pasillosCreados} pasillos o galerías nuevos" +
            (conflictos.Count > 0 ? $", {conflictos.Count} números ya usados por comedores o ambulantes" : ""));

        return Ok(new
        {
            mensaje = $"Listo: {creadas} casetas nuevas, {actualizadas} con pasillo y ubicación actualizados" +
                      (sinCambios > 0 ? $", {sinCambios} ya estaban al día" : "") + ".",
            creadas,
            actualizadas,
            sinCambios,
            pasillosCreados,
            conflictos,
            porConfirmar = plano.PorConfirmar
        });
    }

    /// <summary>"009" y "9" son la misma caseta; "S/N" se compara tal cual.</summary>
    private static string NormalizarCodigo(string codigo)
    {
        var c = codigo.Trim().ToUpperInvariant();
        return c.All(char.IsDigit) ? (c.TrimStart('0') is { Length: > 0 } sin ? sin : "0") : c;
    }
}

/// <summary>Datos del plano oficial del mercado (archivo incluido en la API).</summary>
public sealed class PlanoMercado
{
    public string Fuente { get; set; } = "";
    public int Ancho { get; set; }
    public int Alto { get; set; }
    public string? Nota { get; set; }
    public List<PasilloPlano> Pasillos { get; set; } = new();
    public List<CasetaPlano> Casetas { get; set; } = new();
    public List<CasetaPlano> PorConfirmar { get; set; } = new();

    public sealed class PasilloPlano
    {
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Sector { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed class CasetaPlano
    {
        public string Numero { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public string Pasillo { get; set; } = "";
    }

    private static PlanoMercado? _cache;

    public static PlanoMercado Leer()
    {
        if (_cache is not null)
            return _cache;

        using var flujo = typeof(PlanoMercado).Assembly
            .GetManifestResourceStream("Mercadex.Api.Datos.plano-mercado.json")
            ?? throw new InvalidOperationException("Falta el archivo del plano del mercado.");

        _cache = System.Text.Json.JsonSerializer.Deserialize<PlanoMercado>(flujo,
                     new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                 ?? throw new InvalidOperationException("El archivo del plano del mercado está vacío.");
        return _cache;
    }
}

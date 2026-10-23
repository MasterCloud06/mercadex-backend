using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Mercadex.Api.Seguridad;
using Mercadex.Domain.AppSocios;
using Mercadex.Domain.Caja;
using Mercadex.Domain.Cobranza;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mercadex.Api.Controllers;

/// <summary>
/// Anulación de deudas (una o varias), sin borrar nada: la deuda queda en
/// la base con estado Anulada y deja de contar en los saldos. Cada
/// anulación guarda quién, cuándo, por qué y una copia de cada deuda.
/// <list type="bullet">
/// <item>Solo deudas pendientes, sin recibos (vigentes o anulados) y sin
/// comprobantes QR. Una deuda pagada se anula anulando su recibo en Caja.</item>
/// <item>Siempre con vista previa: la confirmación lleva la huella de lo que
/// se vio; si algo cambió entre la vista previa y la confirmación, no se anula nada.</item>
/// <item>Consultar: cualquier usuario con sesión. Anular: solo el administrador.</item>
/// </list>
/// El escritorio usa api/obligaciones-anulaciones (con la clave de la app);
/// el panel web usa api/panel/obligaciones-anulaciones (solo la sesión).
/// </summary>
[ApiController]
[Authorize]
[Route("api/obligaciones-anulaciones")]
[Route("api/panel/obligaciones-anulaciones")]
public sealed class AnulacionesObligacionesController : ControllerBase
{
    /// <summary>Máximo de deudas por operación y por consulta.</summary>
    public const int Limite = 1000;

    private static readonly TimeSpan ZonaBolivia = TimeSpan.FromHours(-4);

    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly MercadexDbContext _db;

    public AnulacionesObligacionesController(MercadexDbContext db)
    {
        _db = db;
    }

    public class FiltroDeudas
    {
        /// <summary>Deudas elegidas una por una (individual o selección).</summary>
        public List<Guid>? ObligacionIds { get; set; }

        public Guid? ConceptoCobroId { get; set; }

        public int? Anio { get; set; }

        public int? Mes { get; set; }

        /// <summary>Casetas de las que el socio es titular hoy.</summary>
        public Guid? SocioId { get; set; }

        public string? Caseta { get; set; }

        public string? Bloque { get; set; }

        /// <summary>Pendiente, Pagada, Anulada o vacío (todas). Solo para consultar.</summary>
        public string? Estado { get; set; }
    }

    public sealed class ConfirmarRequest : FiltroDeudas
    {
        public string? Motivo { get; set; }

        /// <summary>El administrador marcó que entiende que la anulación es definitiva.</summary>
        public bool Confirmo { get; set; }

        /// <summary>Generada por el cliente una vez por confirmación (evita el doble envío).</summary>
        public Guid ClaveOperacion { get; set; }

        /// <summary>La huella que devolvió la vista previa.</summary>
        public string? Huella { get; set; }

        public int CantidadEsperada { get; set; }
    }

    // Con propiedades init (no constructor) para que EF Core pueda
    // traducir los filtros y el orden que se aplican después de proyectar.
    private sealed record Fila
    {
        public Guid Id { get; init; }
        public Guid UnidadId { get; init; }
        public string Caseta { get; init; } = "";
        public string? Bloque { get; init; }
        public Guid ConceptoId { get; init; }
        public string Concepto { get; init; } = "";
        public int? Anio { get; init; }
        public int? Mes { get; init; }
        public decimal Monto { get; init; }
        public Guid MonedaId { get; init; }
        public string Moneda { get; init; } = "";
        public DateOnly Emision { get; init; }
        public DateOnly? Vencimiento { get; init; }
        public EstadoObligacion Estado { get; init; }
    }

    // =====================================================================
    // CONSULTAR
    // =====================================================================

    /// <summary>
    /// Deudas con su estado, si se pueden anular (y por qué no) y, si están
    /// anuladas, quién las anuló y por qué.
    /// </summary>
    [HttpGet("deudas")]
    public async Task<IActionResult> Deudas([FromQuery] FiltroDeudas f, CancellationToken ct)
    {
        var (consulta, error, _) = await ConsultaAsync(f, exigirFiltro: false, ct);
        if (error is not null)
            return error;

        var total = await consulta!.CountAsync(ct);

        var resumen = await consulta!
            .GroupBy(x => new { x.Estado, x.Moneda })
            .Select(g => new { g.Key.Estado, g.Key.Moneda, Cantidad = g.Count(), Monto = g.Sum(x => x.Monto) })
            .ToListAsync(ct);

        var filas = await consulta!
            .OrderBy(x => x.Estado)
            .ThenByDescending(x => x.Anio)
            .ThenByDescending(x => x.Mes)
            .ThenBy(x => x.Caseta.Length)
            .ThenBy(x => x.Caseta)
            .Take(Limite)
            .ToListAsync(ct);

        var bloqueos = await BloqueosAsync(filas, ct);
        var anulaciones = await AnulacionesDeAsync(filas.Where(x => x.Estado == EstadoObligacion.Anulada).Select(x => x.Id).ToList(), ct);

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            total,
            mostradas = filas.Count,
            limite = Limite,
            resumen = resumen
                .OrderBy(r => r.Estado)
                .Select(r => new { estado = r.Estado.ToString(), moneda = r.Moneda, cantidad = r.Cantidad, monto = r.Monto }),
            deudas = filas.Select(x => new
            {
                id = x.Id,
                unidadId = x.UnidadId,
                caseta = x.Caseta,
                bloque = x.Bloque,
                conceptoId = x.ConceptoId,
                concepto = x.Concepto,
                anio = x.Anio,
                mes = x.Mes,
                periodo = Periodo(x.Anio, x.Mes),
                monto = x.Monto,
                moneda = x.Moneda,
                emision = x.Emision,
                vencimiento = x.Vencimiento,
                estado = x.Estado.ToString(),
                anulable = !bloqueos.ContainsKey(x.Id),
                motivoNoAnulable = bloqueos.GetValueOrDefault(x.Id),
                anulacion = anulaciones.TryGetValue(x.Id, out var a) ? a : null
            })
        });
    }

    /// <summary>
    /// Conceptos y bloques para los filtros. El panel web no puede usar
    /// api/conceptos-cobro (pide la clave de la app del escritorio).
    /// </summary>
    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos(CancellationToken ct)
    {
        var conceptos = await _db.ConceptosCobro.AsNoTracking()
            .OrderBy(c => c.Nombre)
            .Select(c => new { id = c.Id, codigo = c.Codigo, nombre = c.Nombre, activo = c.Activo })
            .ToListAsync(ct);

        var bloques = await _db.UnidadesComerciales.AsNoTracking()
            .Where(u => u.Bloque != null && u.Bloque.Trim() != "")
            .Select(u => u.Bloque!.Trim().ToUpper())
            .Distinct()
            .OrderBy(b => b)
            .ToListAsync(ct);

        return Ok(new { conceptos, bloques });
    }

    /// <summary>Anulaciones registradas, de la más reciente a la más antigua.</summary>
    [HttpGet("historial")]
    public async Task<IActionResult> Historial([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
    {
        var consulta = _db.AnulacionesObligaciones.AsNoTracking();

        if (desde is { } fechaDesde)
        {
            var inicio = new DateTimeOffset(fechaDesde.ToDateTime(TimeOnly.MinValue), ZonaBolivia).UtcDateTime;
            consulta = consulta.Where(x => x.FechaUtc >= inicio);
        }

        if (hasta is { } h)
        {
            var fin = new DateTimeOffset(h.AddDays(1).ToDateTime(TimeOnly.MinValue), ZonaBolivia).UtcDateTime;
            consulta = consulta.Where(x => x.FechaUtc < fin);
        }

        var lotes = await consulta
            .OrderByDescending(x => x.FechaUtc)
            .Take(500)
            .Select(x => new { x.Id, x.Tipo, x.Motivo, x.Responsable, x.Filtros, x.FechaUtc, x.Cantidad })
            .ToListAsync(ct);

        var ids = lotes.Select(x => x.Id).ToList();

        var totales = await (
                from d in _db.DetallesAnulacionObligacion.AsNoTracking()
                join m in _db.Monedas.AsNoTracking() on d.MonedaId equals m.Id
                where ids.Contains(d.AnulacionId)
                group d by new { d.AnulacionId, m.Codigo } into g
                select new { g.Key.AnulacionId, Moneda = g.Key.Codigo, Cantidad = g.Count(), Total = g.Sum(x => x.Monto) })
            .ToListAsync(ct);

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(lotes.Select(x => new
        {
            id = x.Id,
            tipo = x.Tipo.ToString(),
            motivo = x.Motivo,
            responsable = x.Responsable,
            filtros = x.Filtros,
            fechaUtc = x.FechaUtc,
            fecha = new DateTimeOffset(x.FechaUtc, TimeSpan.Zero).ToOffset(ZonaBolivia),
            cantidad = x.Cantidad,
            totales = totales
                .Where(t => t.AnulacionId == x.Id)
                .Select(t => new { moneda = t.Moneda, cantidad = t.Cantidad, total = t.Total })
        }));
    }

    /// <summary>Una anulación con todas sus deudas.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detalle(Guid id, CancellationToken ct)
    {
        var lote = await _db.AnulacionesObligaciones.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Tipo, x.Motivo, x.Responsable, x.ResponsableId, x.Filtros, x.FechaUtc, x.Cantidad })
            .FirstOrDefaultAsync(ct);

        if (lote is null)
            return NotFound(new { mensaje = "La anulación no existe." });

        var deudas = await (
                from d in _db.DetallesAnulacionObligacion.AsNoTracking()
                join o in _db.Obligaciones.AsNoTracking() on d.ObligacionId equals o.Id
                join u in _db.UnidadesComerciales.AsNoTracking() on d.UnidadComercialId equals u.Id
                join c in _db.ConceptosCobro.AsNoTracking() on d.ConceptoCobroId equals c.Id
                join m in _db.Monedas.AsNoTracking() on d.MonedaId equals m.Id
                where d.AnulacionId == id
                orderby u.Codigo
                select new
                {
                    obligacionId = d.ObligacionId,
                    caseta = u.Codigo,
                    bloque = u.Bloque,
                    concepto = c.Nombre,
                    d.PeriodoAnio,
                    d.PeriodoMes,
                    monto = d.Monto,
                    moneda = m.Codigo,
                    estadoAnterior = d.EstadoAnterior,
                    estadoActual = o.Estado
                })
            .ToListAsync(ct);

        return Ok(new
        {
            id = lote.Id,
            tipo = lote.Tipo.ToString(),
            motivo = lote.Motivo,
            responsable = lote.Responsable,
            responsableId = lote.ResponsableId,
            filtros = lote.Filtros,
            fechaUtc = lote.FechaUtc,
            fecha = new DateTimeOffset(lote.FechaUtc, TimeSpan.Zero).ToOffset(ZonaBolivia),
            cantidad = lote.Cantidad,
            totales = deudas
                .GroupBy(x => x.moneda)
                .Select(g => new { moneda = g.Key, cantidad = g.Count(), total = g.Sum(x => x.monto) }),
            deudas = deudas.Select(x => new
            {
                x.obligacionId,
                x.caseta,
                x.bloque,
                x.concepto,
                periodo = Periodo(x.PeriodoAnio, x.PeriodoMes),
                x.monto,
                x.moneda,
                estadoAnterior = x.estadoAnterior.ToString(),
                estadoActual = x.estadoActual.ToString()
            })
        });
    }

    /// <summary>Quién anuló esta deuda, cuándo y por qué.</summary>
    [HttpGet("obligacion/{obligacionId:guid}")]
    public async Task<IActionResult> DeObligacion(Guid obligacionId, CancellationToken ct)
    {
        var anulaciones = await AnulacionesDeAsync(new List<Guid> { obligacionId }, ct);

        return anulaciones.TryGetValue(obligacionId, out var a)
            ? Ok(a)
            : NotFound(new { mensaje = "Esta deuda no fue anulada." });
    }

    // =====================================================================
    // ANULAR
    // =====================================================================

    /// <summary>
    /// Qué se anularía, sin guardar nada: cantidad, total por moneda, las
    /// deudas que no se pueden anular y por qué, y la huella para confirmar.
    /// </summary>
    [Authorize(Roles = SeguridadUsuarios.RolAdministrador)]
    [HttpPost("vista-previa")]
    public async Task<IActionResult> VistaPrevia([FromBody] FiltroDeudas f, CancellationToken ct)
    {
        // Es POST por el tamaño de la selección, pero no modifica datos.
        HttpContext.NoRegistrar();

        var (candidatas, error, descripcion) = await CandidatasAsync(f, ct);
        if (error is not null)
            return error;

        var bloqueos = await BloqueosAsync(candidatas!, ct);
        var anulables = candidatas!.Where(x => !bloqueos.ContainsKey(x.Id)).ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            tipo = EsIndividual(f) ? "Individual" : "Masiva",
            filtros = descripcion,
            cantidad = anulables.Count,
            totales = Totales(anulables),
            huella = Huella(anulables),
            noAnulables = candidatas!.Count - anulables.Count,
            deudas = anulables.Select(Salida),
            bloqueadas = candidatas!
                .Where(x => bloqueos.ContainsKey(x.Id))
                .Select(x => new
                {
                    id = x.Id,
                    caseta = x.Caseta,
                    concepto = x.Concepto,
                    periodo = Periodo(x.Anio, x.Mes),
                    monto = x.Monto,
                    moneda = x.Moneda,
                    estado = x.Estado.ToString(),
                    motivo = bloqueos[x.Id]
                })
        });
    }

    /// <summary>
    /// Anula las deudas de la vista previa. Todo o nada, en una transacción,
    /// con las filas bloqueadas (igual que un cobro en Caja, que también las
    /// bloquea): si alguna cambió, no se anula ninguna.
    /// </summary>
    [Authorize(Roles = SeguridadUsuarios.RolAdministrador)]
    [HttpPost]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarRequest r, CancellationToken ct)
    {
        var motivo = r.Motivo?.Trim() ?? "";

        if (motivo.Length < AnulacionObligaciones.MotivoMinimo || motivo.Length > AnulacionObligaciones.MotivoMaximo)
        {
            return BadRequest(new
            {
                mensaje = $"Escribe el motivo de la anulación (de {AnulacionObligaciones.MotivoMinimo} a {AnulacionObligaciones.MotivoMaximo} caracteres)."
            });
        }

        if (!r.Confirmo)
            return BadRequest(new { mensaje = "Confirma que revisaste la vista previa y que la anulación es definitiva." });

        if (r.ClaveOperacion == Guid.Empty || string.IsNullOrWhiteSpace(r.Huella) || r.CantidadEsperada <= 0)
            return BadRequest(new { mensaje = "Primero revisa la vista previa de la anulación." });

        // Quien anula sale de la sesión, nunca del cuerpo de la petición.
        var responsable = SeguridadUsuarios.NombreResponsable(User);
        var responsableId = SeguridadUsuarios.IdResponsable(User);

        if (responsable == "Sistema" || responsable.Length > 120)
            return StatusCode(403, new { mensaje = "No se pudo identificar al usuario que anula." });

        if (await RepetidaAsync(r.ClaveOperacion, ct) is { } yaHecha)
            return yaHecha;

        var (candidatas, error, descripcion) = await CandidatasAsync(r, ct);
        if (error is not null)
            return error;

        if (candidatas!.Count == 0)
            return Conflict(new { mensaje = "No hay deudas para anular con estos datos. Actualiza la vista previa." });

        var ids = candidatas.Select(x => x.Id).ToArray();

        await using var transaccion = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // Bloqueo de filas en el mismo orden que los cobros de Caja
        // (ObligacionRepository.ObtenerPorIdsAsync) para no interbloquearse.
        var bloqueadas = await _db.Obligaciones
            .FromSqlInterpolated($"""
                SELECT *
                FROM obligaciones
                WHERE "Id" = ANY ({ids})
                ORDER BY "Id"
                FOR UPDATE
                """)
            .ToListAsync(ct);

        // Otra confirmación con la misma clave pudo terminar mientras esperábamos.
        if (await RepetidaAsync(r.ClaveOperacion, ct) is { } repetida)
            return repetida;

        // Con las filas bloqueadas se vuelve a revisar todo.
        var porId = bloqueadas.ToDictionary(x => x.Id);
        var actuales = candidatas
            .Where(x => porId.ContainsKey(x.Id))
            .Select(x => x with { Estado = porId[x.Id].Estado, Monto = porId[x.Id].Monto })
            .ToList();

        var bloqueos = await BloqueosAsync(actuales, ct);
        var anulables = actuales.Where(x => !bloqueos.ContainsKey(x.Id)).ToList();

        if (anulables.Count != r.CantidadEsperada ||
            !string.Equals(Huella(anulables), r.Huella, StringComparison.Ordinal))
        {
            return Conflict(new
            {
                mensaje = "Las deudas cambiaron desde la vista previa (alguien las cobró, modificó o anuló). " +
                          "No se anuló nada. Vuelve a revisar la vista previa.",
                cantidadActual = anulables.Count
            });
        }

        var aAnular = anulables.Select(x => porId[x.Id]).ToList();
        var tipo = EsIndividual(r) ? TipoAnulacionObligaciones.Individual : TipoAnulacionObligaciones.Masiva;

        var anulacion = ServicioAnulacionObligaciones.Anular(
            aAnular,
            r.ClaveOperacion,
            tipo,
            motivo,
            responsable,
            responsableId,
            descripcion,
            DateTime.UtcNow);

        _db.AnulacionesObligaciones.Add(anulacion);

        try
        {
            await _db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Respaldo de la base: la misma clave o una deuda ya anulada.
            return Conflict(new { mensaje = "Esta anulación ya se registró o alguna deuda ya estaba anulada. Actualiza la lista." });
        }

        var totales = Totales(anulables);
        var textoTotales = string.Join(" + ", totales.Select(t => Cambios.Monto(t.Total, t.Moneda)));

        HttpContext.Detalle(
            (tipo == TipoAnulacionObligaciones.Individual
                ? $"Caseta {anulables[0].Caseta} · {anulables[0].Concepto}{PeriodoTexto(anulables[0].Anio, anulables[0].Mes)}"
                : $"{anulables.Count} deudas · {descripcion}") +
            $" · {textoTotales} · Motivo: {anulacion.Motivo}");
        HttpContext.Referencia(anulacion.Id);

        return Ok(new
        {
            mensaje = anulables.Count == 1
                ? "Deuda anulada. Quedó en el historial a tu nombre."
                : $"Se anularon {anulables.Count} deudas. Quedaron en el historial a tu nombre.",
            repetida = false,
            anulacionId = anulacion.Id,
            cantidad = anulacion.Cantidad,
            totales,
            responsable = anulacion.Responsable,
            fechaUtc = anulacion.FechaUtc
        });
    }

    // =====================================================================
    // AUXILIARES
    // =====================================================================

    private async Task<IActionResult?> RepetidaAsync(Guid clave, CancellationToken ct)
    {
        var previa = await _db.AnulacionesObligaciones.AsNoTracking()
            .Where(x => x.ClaveOperacion == clave)
            .Select(x => new { x.Id, x.Cantidad, x.Responsable, x.FechaUtc })
            .FirstOrDefaultAsync(ct);

        if (previa is null)
            return null;

        HttpContext.NoRegistrar();

        return Ok(new
        {
            mensaje = "Esta anulación ya se había registrado; no se anuló nada más.",
            repetida = true,
            anulacionId = previa.Id,
            cantidad = previa.Cantidad,
            responsable = previa.Responsable,
            fechaUtc = previa.FechaUtc
        });
    }

    private static bool EsIndividual(FiltroDeudas f) =>
        f.ObligacionIds is { Count: 1 };

    /// <summary>Deudas a evaluar: las elegidas o las pendientes que cumplen los filtros.</summary>
    private async Task<(List<Fila>? Filas, IActionResult? Error, string Descripcion)> CandidatasAsync(FiltroDeudas f, CancellationToken ct)
    {
        if (f.ObligacionIds is { Count: > 0 } elegidas)
        {
            var ids = elegidas.Where(x => x != Guid.Empty).Distinct().ToList();

            if (ids.Count == 0)
                return (null, BadRequest(new { mensaje = "Elige las deudas que quieres anular." }), "");

            if (ids.Count > Limite)
                return (null, BadRequest(new { mensaje = $"Se pueden anular hasta {Limite} deudas a la vez." }), "");

            var filas = await Base().Where(x => ids.Contains(x.Id)).ToListAsync(ct);

            if (filas.Count != ids.Count)
                return (null, NotFound(new { mensaje = "Alguna de las deudas elegidas no existe. Actualiza la lista." }), "");

            var texto = filas.Count == 1
                ? $"Caseta {filas[0].Caseta} · {filas[0].Concepto}{PeriodoTexto(filas[0].Anio, filas[0].Mes)}"
                : $"Selección de {filas.Count} deudas";

            return (filas, null, texto);
        }

        // Por filtros solo se anulan deudas pendientes.
        var porFiltros = new FiltroDeudas
        {
            ConceptoCobroId = f.ConceptoCobroId,
            Anio = f.Anio,
            Mes = f.Mes,
            SocioId = f.SocioId,
            Caseta = f.Caseta,
            Bloque = f.Bloque,
            Estado = nameof(EstadoObligacion.Pendiente)
        };

        var (consulta, error, descripcion) = await ConsultaAsync(porFiltros, exigirFiltro: true, ct);
        if (error is not null)
            return (null, error, "");

        var lista = await consulta!.OrderBy(x => x.Id).Take(Limite + 1).ToListAsync(ct);

        if (lista.Count > Limite)
        {
            return (null, BadRequest(new
            {
                mensaje = $"Hay más de {Limite} deudas con esos filtros. Acota la búsqueda (por ejemplo, por mes o por bloque)."
            }), "");
        }

        return (lista, null, descripcion);
    }

    private IQueryable<Fila> Base() =>
        from o in _db.Obligaciones.AsNoTracking()
        join u in _db.UnidadesComerciales.AsNoTracking() on o.UnidadComercialId equals u.Id
        join c in _db.ConceptosCobro.AsNoTracking() on o.ConceptoCobroId equals c.Id
        join m in _db.Monedas.AsNoTracking() on o.MonedaId equals m.Id
        select new Fila
        {
            Id = o.Id,
            UnidadId = u.Id,
            Caseta = u.Codigo,
            Bloque = u.Bloque,
            ConceptoId = c.Id,
            Concepto = c.Nombre,
            Anio = o.PeriodoAnio,
            Mes = o.PeriodoMes,
            Monto = o.Monto,
            MonedaId = m.Id,
            Moneda = m.Codigo,
            Emision = o.FechaEmision,
            Vencimiento = o.FechaVencimiento,
            Estado = o.Estado
        };

    private async Task<(IQueryable<Fila>? Consulta, IActionResult? Error, string Descripcion)> ConsultaAsync(
        FiltroDeudas f, bool exigirFiltro, CancellationToken ct)
    {
        var consulta = Base();
        var partes = new List<string>();

        if (f.Mes is < 1 or > 12 || f.Anio is < 2000 or > 2100)
            return (null, BadRequest(new { mensaje = "El mes o el año no son válidos." }), "");

        if (f.ConceptoCobroId is { } conceptoId && conceptoId != Guid.Empty)
        {
            var nombre = await _db.ConceptosCobro.AsNoTracking()
                .Where(c => c.Id == conceptoId).Select(c => c.Nombre).FirstOrDefaultAsync(ct);

            if (nombre is null)
                return (null, BadRequest(new { mensaje = "El concepto no existe." }), "");

            consulta = consulta.Where(x => x.ConceptoId == conceptoId);
            partes.Add("Concepto: " + nombre);
        }

        if (f.Anio is { } anio)
        {
            consulta = consulta.Where(x => x.Anio == anio);
            partes.Add(f.Mes is { } m ? $"Período: {m:00}/{anio}" : $"Año: {anio}");
        }

        if (f.Mes is { } mes)
        {
            consulta = consulta.Where(x => x.Mes == mes);
            if (f.Anio is null)
                partes.Add($"Mes: {mes:00} (todos los años)");
        }

        if (f.SocioId is { } socioId && socioId != Guid.Empty)
        {
            var socio = await _db.Socios.AsNoTracking()
                .Where(s => s.Id == socioId).Select(s => s.Nombres + " " + s.Apellidos).FirstOrDefaultAsync(ct);

            if (socio is null)
                return (null, BadRequest(new { mensaje = "El socio no existe." }), "");

            var unidades = _db.AsignacionesSocioCaseta.AsNoTracking()
                .Where(a => a.SocioId == socioId && a.Activa)
                .Select(a => a.UnidadComercialId);

            consulta = consulta.Where(x => unidades.Contains(x.UnidadId));
            partes.Add("Socio: " + socio.Trim());
        }

        if (!string.IsNullOrWhiteSpace(f.Caseta))
        {
            var codigo = f.Caseta.Trim().ToUpper();
            consulta = consulta.Where(x => x.Caseta.ToUpper() == codigo);
            partes.Add("Caseta: " + codigo);
        }

        if (!string.IsNullOrWhiteSpace(f.Bloque))
        {
            var bloque = f.Bloque.Trim().ToUpper();
            consulta = consulta.Where(x => x.Bloque != null && x.Bloque.Trim().ToUpper() == bloque);
            partes.Add("Bloque: " + bloque);
        }

        // Un filtro es obligatorio para anular en bloque: nunca «todas las deudas».
        if (exigirFiltro && partes.Count == 0)
        {
            return (null, BadRequest(new
            {
                mensaje = "Para anular varias deudas elige al menos un filtro: concepto, período, socio, caseta o bloque."
            }), "");
        }

        if (!string.IsNullOrWhiteSpace(f.Estado))
        {
            if (!Enum.TryParse<EstadoObligacion>(f.Estado, ignoreCase: true, out var estado) || !Enum.IsDefined(estado))
                return (null, BadRequest(new { mensaje = "El estado no es válido." }), "");

            consulta = consulta.Where(x => x.Estado == estado);
            partes.Add("Estado: " + estado);
        }

        return (consulta, null, string.Join(" · ", partes));
    }

    /// <summary>
    /// Por qué no se puede anular cada deuda (las que no aparecen sí se pueden).
    /// </summary>
    private async Task<Dictionary<Guid, string>> BloqueosAsync(List<Fila> filas, CancellationToken ct)
    {
        var bloqueos = new Dictionary<Guid, string>();

        foreach (var f in filas)
        {
            if (f.Estado == EstadoObligacion.Pagada)
                bloqueos[f.Id] = "Está pagada. Para anularla primero hay que anular su recibo en Caja.";
            else if (f.Estado == EstadoObligacion.Anulada)
                bloqueos[f.Id] = "Ya está anulada.";
        }

        var ids = filas.Where(f => !bloqueos.ContainsKey(f.Id)).Select(f => f.Id).ToList();

        if (ids.Count == 0)
            return bloqueos;

        // Cualquier recibo, vigente o anulado: la deuda tiene historial de cobro.
        var recibos = await (
                from d in _db.DetallesRecibo.AsNoTracking()
                join r in _db.Recibos.AsNoTracking() on EF.Property<Guid>(d, "ReciboId") equals r.Id
                where ids.Contains(d.ObligacionId)
                select new { d.ObligacionId, r.Numero, r.Estado })
            .ToListAsync(ct);

        foreach (var g in recibos.GroupBy(x => x.ObligacionId))
        {
            var vigente = g.FirstOrDefault(x => x.Estado != EstadoRecibo.Anulado);
            bloqueos[g.Key] = vigente is not null
                ? $"Tiene el recibo N.º {vigente.Numero}. Se anula desde Caja."
                : $"Tuvo un cobro (recibo N.º {g.First().Numero}, anulado). Necesita revisión antes de anularla.";
        }

        var arreglo = ids.ToArray();

        var pagosQr = await _db.PagosReportados.AsNoTracking()
            .Where(p => p.ObligacionIds.Any(x => arreglo.Contains(x)))
            .Select(p => new { p.ObligacionIds, p.Estado })
            .ToListAsync(ct);

        foreach (var p in pagosQr)
        {
            foreach (var id in p.ObligacionIds.Where(arreglo.Contains))
            {
                if (bloqueos.ContainsKey(id))
                    continue;

                bloqueos[id] = p.Estado == EstadoPagoReportado.Pendiente
                    ? "Tiene un pago QR en revisión. Primero apruébalo o recházalo."
                    : "Tiene un comprobante QR enviado por el socio. Necesita revisión antes de anularla.";
            }
        }

        return bloqueos;
    }

    private async Task<Dictionary<Guid, object>> AnulacionesDeAsync(List<Guid> obligacionIds, CancellationToken ct)
    {
        if (obligacionIds.Count == 0)
            return new Dictionary<Guid, object>();

        var datos = await (
                from d in _db.DetallesAnulacionObligacion.AsNoTracking()
                join a in _db.AnulacionesObligaciones.AsNoTracking() on d.AnulacionId equals a.Id
                where obligacionIds.Contains(d.ObligacionId)
                select new { d.ObligacionId, a.Id, a.Tipo, a.Motivo, a.Responsable, a.FechaUtc, a.Cantidad })
            .ToListAsync(ct);

        return datos.ToDictionary(
            x => x.ObligacionId,
            x => (object)new
            {
                anulacionId = x.Id,
                tipo = x.Tipo.ToString(),
                motivo = x.Motivo,
                responsable = x.Responsable,
                fechaUtc = x.FechaUtc,
                fecha = new DateTimeOffset(x.FechaUtc, TimeSpan.Zero).ToOffset(ZonaBolivia),
                deudasEnLaAnulacion = x.Cantidad
            });
    }

    public sealed record TotalMoneda(string Moneda, int Cantidad, decimal Total);

    private static List<TotalMoneda> Totales(IEnumerable<Fila> filas) =>
        filas
            .GroupBy(x => x.Moneda)
            .OrderBy(g => g.Key)
            .Select(g => new TotalMoneda(g.Key, g.Count(), g.Sum(x => x.Monto)))
            .ToList();

    /// <summary>
    /// Resume qué deudas y con qué monto se vieron en la vista previa.
    /// Si cambia cualquiera de ellas, cambia la huella.
    /// </summary>
    private static string Huella(IEnumerable<Fila> filas)
    {
        var texto = string.Join("|", filas
            .OrderBy(x => x.Id)
            .Select(x => $"{x.Id:N}:{x.Monto.ToString("F2", CultureInfo.InvariantCulture)}:{(int)x.Estado}"));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();
    }

    private static object Salida(Fila x) => new
    {
        id = x.Id,
        unidadId = x.UnidadId,
        caseta = x.Caseta,
        bloque = x.Bloque,
        concepto = x.Concepto,
        periodo = Periodo(x.Anio, x.Mes),
        monto = x.Monto,
        moneda = x.Moneda,
        emision = x.Emision,
        vencimiento = x.Vencimiento,
        estado = x.Estado.ToString()
    };

    private static string Periodo(int? anio, int? mes) =>
        mes is { } m && anio is { } a ? $"{m:00}/{a}" : anio is { } soloAnio ? soloAnio.ToString(Bo) : "Eventual";

    private static string PeriodoTexto(int? anio, int? mes) =>
        mes is { } m && anio is { } a ? $" {m:00}/{a}" : "";
}

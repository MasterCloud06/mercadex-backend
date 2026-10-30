using System.Data;
using Mercadex.Api.Seguridad;
using Mercadex.Domain.Cobranza;
using Mercadex.Domain.Organizacion;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

/// <summary>
/// Generador de cobranza (manual, nivel 2): crea las deudas que luego se
/// cobran en Caja.
/// <list type="bullet">
/// <item>Deudas registradas: buscar por caseta, concepto, estado, período o bloque.</item>
/// <item>Cargo a varias unidades: el mismo monto o la tarifa de cada caseta,
/// para todas, un bloque o un pasillo, siempre con vista previa. Las unidades
/// que ya tienen la deuda o no tienen tarifa se omiten y se informan.</item>
/// <item>Cargo a una unidad, sin duplicar el mismo concepto y período.</item>
/// <item>Asignar la misma tarifa a varias casetas.</item>
/// </list>
/// Consultar: todos los roles. Registrar: administradores (PermisosPorRolFilter).
/// </summary>
[ApiController]
[Route("api/cobranza")]
public sealed class CobranzaController : ControllerBase
{
    private const int LimiteDeudas = 1000;

    private readonly MercadexDbContext _db;

    public CobranzaController(MercadexDbContext db)
    {
        _db = db;
    }

    // =====================================================================
    // DEUDAS REGISTRADAS
    // =====================================================================

    /// <summary>
    /// Cargos del mes en curso (hora de Bolivia), para recordar al
    /// administrador que genere la mensualidad si todavía no lo hizo.
    /// </summary>
    [HttpGet("estado-mes")]
    public async Task<IActionResult> EstadoMes(CancellationToken ct)
    {
        var hoy = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-4));
        int anio = hoy.Year, mes = hoy.Month;

        var porConcepto = await (
                from o in _db.Obligaciones.AsNoTracking()
                join c in _db.ConceptosCobro.AsNoTracking() on o.ConceptoCobroId equals c.Id
                where o.PeriodoAnio == anio && o.PeriodoMes == mes && o.Estado != EstadoObligacion.Anulada
                group o by c.Nombre into g
                select new { concepto = g.Key, casetas = g.Select(x => x.UnidadComercialId).Distinct().Count() })
            .ToListAsync(ct);

        var activas = await _db.UnidadesComerciales.AsNoTracking().CountAsync(u => u.Activa, ct);

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            anio,
            mes,
            dia = hoy.Day,
            unidadesActivas = activas,
            cargoGenerado = porConcepto.Count > 0,
            porConcepto = porConcepto.OrderByDescending(x => x.casetas).ToList()
        });
    }

    [HttpGet("deudas")]
    public async Task<IActionResult> Deudas(
        [FromQuery] string? caseta,
        [FromQuery] Guid? conceptoId,
        [FromQuery] string? estado,
        [FromQuery] int? anio,
        [FromQuery] int? mes,
        [FromQuery] string? bloque,
        CancellationToken ct)
    {
        var consulta =
            from o in _db.Obligaciones.AsNoTracking()
            join u in _db.UnidadesComerciales.AsNoTracking() on o.UnidadComercialId equals u.Id
            join c in _db.ConceptosCobro.AsNoTracking() on o.ConceptoCobroId equals c.Id
            join m in _db.Monedas.AsNoTracking() on o.MonedaId equals m.Id
            select new { o, u, c, m };

        if (!string.IsNullOrWhiteSpace(caseta))
        {
            var codigo = caseta.Trim().ToUpper();
            consulta = consulta.Where(x => x.u.Codigo.ToUpper() == codigo);
        }

        if (conceptoId is { } concepto && concepto != Guid.Empty)
            consulta = consulta.Where(x => x.o.ConceptoCobroId == concepto);

        if (Enum.TryParse<EstadoObligacion>(estado, ignoreCase: true, out var estadoBuscado))
            consulta = consulta.Where(x => x.o.Estado == estadoBuscado);

        if (anio is { } a)
            consulta = consulta.Where(x => x.o.PeriodoAnio == a);

        if (mes is { } me)
            consulta = consulta.Where(x => x.o.PeriodoMes == me);

        if (!string.IsNullOrWhiteSpace(bloque))
        {
            var b = bloque.Trim().ToUpper();
            consulta = consulta.Where(x => x.u.Bloque != null && x.u.Bloque.Trim().ToUpper() == b);
        }

        var total = await consulta.CountAsync(ct);

        // Resumen sobre todo el resultado, no solo lo que se muestra.
        var resumen = await consulta
            .GroupBy(x => new { x.o.Estado, x.m.Codigo })
            .Select(g => new
            {
                estado = g.Key.Estado,
                moneda = g.Key.Codigo,
                cantidad = g.Count(),
                // Pendientes: lo que falta pagar.
                monto = g.Sum(x => x.o.Estado == EstadoObligacion.Pendiente ? x.o.Monto - x.o.MontoPagado : x.o.Monto)
            })
            .ToListAsync(ct);

        var filas = await consulta
            .OrderBy(x => x.o.Estado)
            .ThenByDescending(x => x.o.PeriodoAnio)
            .ThenByDescending(x => x.o.PeriodoMes)
            .ThenByDescending(x => x.o.FechaEmision)
            .ThenBy(x => x.u.Codigo)
            .Take(LimiteDeudas)
            .Select(x => new
            {
                id = x.o.Id,
                unidadId = x.u.Id,
                caseta = x.u.Codigo,
                bloque = x.u.Bloque,
                conceptoId = x.c.Id,
                concepto = x.c.Nombre,
                anio = x.o.PeriodoAnio,
                mes = x.o.PeriodoMes,
                monto = x.o.Monto,
                // Pagos parciales: lo abonado y lo que falta.
                pagado = x.o.Estado == EstadoObligacion.Pagada && x.o.MontoPagado == 0 ? x.o.Monto : x.o.MontoPagado,
                saldo = x.o.Estado == EstadoObligacion.Pendiente ? x.o.Monto - x.o.MontoPagado : 0m,
                moneda = x.m.Codigo,
                emision = x.o.FechaEmision,
                vencimiento = x.o.FechaVencimiento,
                estado = x.o.Estado.ToString(),
                modificaciones = _db.CambiosMontoObligacion.Count(cm => cm.ObligacionId == x.o.Id)
            })
            .ToListAsync(ct);

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            total,
            mostradas = filas.Count,
            limite = LimiteDeudas,
            resumen = resumen.Select(r => new { estado = r.estado.ToString(), r.moneda, r.cantidad, r.monto }),
            deudas = filas
        });
    }

    // =====================================================================
    // CARGO A VARIAS UNIDADES
    // =====================================================================

    public sealed class CargoMasivoRequest
    {
        public Guid ConceptoCobroId { get; set; }

        public int Anio { get; set; }

        public int Mes { get; set; }

        /// <summary>"todas", "bloque" o "pasillo".</summary>
        public string Alcance { get; set; } = "todas";

        public string? Bloque { get; set; }

        public Guid? PasilloId { get; set; }

        public bool IncluirComedores { get; set; }

        public bool IncluirAmbulantes { get; set; }

        /// <summary>"fijo" (el mismo monto) o "tarifa" (la tarifa de cada caseta).</summary>
        public string ModoMonto { get; set; } = "fijo";

        public decimal? Monto { get; set; }

        public Guid? MonedaId { get; set; }

        public DateOnly FechaEmision { get; set; }

        public DateOnly? FechaVencimiento { get; set; }

        /// <summary>Cantidad que mostró la vista previa (para detectar cambios), sin las desmarcadas.</summary>
        public int? ListasEsperadas { get; set; }

        /// <summary>Casetas que el administrador desmarcó en la vista previa: no reciben la deuda.</summary>
        public List<Guid>? ExcluirUnidadIds { get; set; }
    }

    private sealed record FilaCargo(
        Guid UnidadId,
        string Codigo,
        string? Bloque,
        string Tipo,
        string Estado,
        decimal? Monto,
        Guid? MonedaId);

    /// <summary>Quién recibiría el cargo, sin guardar nada.</summary>
    [HttpGet("cargo-masivo/vista-previa")]
    public async Task<IActionResult> VistaPrevia([FromQuery] CargoMasivoRequest r, CancellationToken ct)
    {
        var (filas, error) = await CalcularAsync(r, ct);
        if (error is not null)
            return error;

        return Ok(await RespuestaAsync(filas!, ct));
    }

    [HttpPost("cargo-masivo")]
    public async Task<IActionResult> Generar([FromBody] CargoMasivoRequest r, CancellationToken ct)
    {
        await using var transaccion = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var (filas, error) = await CalcularAsync(r, ct);
        if (error is not null)
            return error;

        var excluir = r.ExcluirUnidadIds?.ToHashSet() ?? new HashSet<Guid>();
        var listas = filas!.Where(f => f.Estado == "Lista" && !excluir.Contains(f.UnidadId)).ToList();
        var desmarcadas = filas!.Count(f => f.Estado == "Lista" && excluir.Contains(f.UnidadId));

        if (r.ListasEsperadas is { } esperadas && esperadas != listas.Count)
        {
            return Conflict(new
            {
                mensaje = "Los datos cambiaron desde la vista previa. Vuelve a revisarla antes de generar.",
                listasActuales = listas.Count
            });
        }

        if (listas.Count == 0)
        {
            return Conflict(new
            {
                mensaje = desmarcadas > 0
                    ? "Desmarcaste todas las casetas: no hay deudas para generar."
                    : "No hay deudas nuevas para generar con estos datos."
            });
        }

        var nuevas = listas
            .Select(f => new Obligacion(
                f.UnidadId,
                r.ConceptoCobroId,
                f.MonedaId!.Value,
                f.Monto!.Value,
                r.FechaEmision,
                r.FechaVencimiento,
                r.Anio,
                r.Mes))
            .ToList();

        _db.Obligaciones.AddRange(nuevas);
        await _db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        var respuesta = await RespuestaAsync(filas!, ct);
        var generadas = await RespuestaAsync(listas, ct);

        var concepto = await _db.ConceptosCobro.AsNoTracking()
            .Where(c => c.Id == r.ConceptoCobroId).Select(c => c.Nombre).FirstOrDefaultAsync(ct);
        var sinCobrar = filas!
            .Where(f => f.Estado == "Lista" && excluir.Contains(f.UnidadId))
            .Select(f => f.Codigo)
            .ToList();

        HttpContext.Detalle(
            $"{concepto} {r.Mes:00}/{r.Anio} · {nuevas.Count} deudas · " +
            string.Join(" + ", generadas.Totales.Select(t => Cambios.Monto(t.Total, t.Moneda))) +
            (sinCobrar.Count == 0
                ? ""
                : $" · Desmarcadas ({sinCobrar.Count}): " + string.Join(", ", sinCobrar.Take(40)) + (sinCobrar.Count > 40 ? "…" : "")));

        return Ok(new
        {
            mensaje = $"Se generaron {nuevas.Count} deudas.",
            generadas = nuevas.Count,
            desmarcadas,
            yaTenian = respuesta.YaTenian,
            sinTarifa = respuesta.SinTarifa,
            totales = generadas.Totales
        });
    }

    private async Task<(List<FilaCargo>? Filas, IActionResult? Error)> CalcularAsync(CargoMasivoRequest r, CancellationToken ct)
    {
        if (r.ConceptoCobroId == Guid.Empty || r.Anio < 2000 || r.Anio > 2100 || r.Mes < 1 || r.Mes > 12)
            return (null, BadRequest(new { mensaje = "Elige el concepto y el mes y año del cargo." }));

        if (r.FechaEmision == default)
            return (null, BadRequest(new { mensaje = "Indica la fecha de emisión." }));

        if (r.FechaVencimiento is { } venc && venc < r.FechaEmision)
            return (null, BadRequest(new { mensaje = "El vencimiento no puede ser anterior a la emisión." }));

        if (!await _db.ConceptosCobro.AsNoTracking().AnyAsync(c => c.Id == r.ConceptoCobroId && c.Activo, ct))
            return (null, BadRequest(new { mensaje = "El concepto no existe o está inactivo." }));

        var porTarifa = string.Equals(r.ModoMonto, "tarifa", StringComparison.OrdinalIgnoreCase);

        if (!porTarifa)
        {
            if (r.Monto is not { } monto || monto <= 0 || decimal.Round(monto, 2) != monto)
                return (null, BadRequest(new { mensaje = "Escribe el monto: mayor a cero y con hasta dos decimales." }));

            if (r.MonedaId is not { } monedaId ||
                !await _db.Monedas.AsNoTracking().AnyAsync(m => m.Id == monedaId && m.Activa, ct))
                return (null, BadRequest(new { mensaje = "Elige la moneda." }));
        }

        var tipos = new List<TipoUnidadComercial> { TipoUnidadComercial.Caseta };
        if (r.IncluirComedores) tipos.Add(TipoUnidadComercial.Comedor);
        if (r.IncluirAmbulantes) tipos.Add(TipoUnidadComercial.Ambulante);

        var unidades = _db.UnidadesComerciales.AsNoTracking().Where(u => u.Activa && tipos.Contains(u.Tipo));

        switch ((r.Alcance ?? "todas").Trim().ToLowerInvariant())
        {
            case "bloque":
                if (string.IsNullOrWhiteSpace(r.Bloque))
                    return (null, BadRequest(new { mensaje = "Elige el bloque." }));
                var b = r.Bloque.Trim().ToUpper();
                unidades = unidades.Where(u => u.Bloque != null && u.Bloque.Trim().ToUpper() == b);
                break;

            case "pasillo":
                if (r.PasilloId is not { } pasilloId || pasilloId == Guid.Empty)
                    return (null, BadRequest(new { mensaje = "Elige el pasillo." }));
                unidades = unidades.Where(u => u.PasilloId == pasilloId);
                break;

            case "todas":
                break;

            default:
                return (null, BadRequest(new { mensaje = "Elige a quién se aplica el cargo." }));
        }

        var lista = await unidades
            .Select(u => new { u.Id, u.Codigo, u.Bloque, u.Tipo })
            .ToListAsync(ct);

        if (lista.Count == 0)
            return (null, BadRequest(new { mensaje = "No hay unidades activas con esos datos." }));

        var ids = lista.Select(u => u.Id).ToList();

        // Una deuda anulada también cuenta: anularla no habilita a generarla
        // otra vez (además, el índice único de la base lo impediría).
        var yaTienen = (await _db.Obligaciones.AsNoTracking()
                .Where(o => ids.Contains(o.UnidadComercialId) &&
                            o.ConceptoCobroId == r.ConceptoCobroId &&
                            o.PeriodoAnio == r.Anio &&
                            o.PeriodoMes == r.Mes)
                .Select(o => o.UnidadComercialId)
                .ToListAsync(ct))
            .ToHashSet();

        var tarifas = porTarifa
            ? await _db.TarifasUnidad.AsNoTracking()
                .Where(t => t.ConceptoCobroId == r.ConceptoCobroId && ids.Contains(t.UnidadComercialId))
                .ToDictionaryAsync(t => t.UnidadComercialId, ct)
            : new Dictionary<Guid, TarifaUnidad>();

        var filas = lista
            .Select(u =>
            {
                var tipo = u.Tipo.ToString();

                if (yaTienen.Contains(u.Id))
                    return new FilaCargo(u.Id, u.Codigo, u.Bloque, tipo, "YaTiene", null, null);

                if (!porTarifa)
                    return new FilaCargo(u.Id, u.Codigo, u.Bloque, tipo, "Lista", r.Monto, r.MonedaId);

                return tarifas.TryGetValue(u.Id, out var tarifa) && tarifa.Monto > 0
                    ? new FilaCargo(u.Id, u.Codigo, u.Bloque, tipo, "Lista", tarifa.Monto, tarifa.MonedaId)
                    : new FilaCargo(u.Id, u.Codigo, u.Bloque, tipo, "SinTarifa", null, null);
            })
            .OrderBy(f => f.Estado == "Lista" ? 0 : f.Estado == "SinTarifa" ? 1 : 2)
            .ThenBy(f => f.Codigo.Length)
            .ThenBy(f => f.Codigo)
            .ToList();

        return (filas, null);
    }

    public sealed record TotalMoneda(string Moneda, int Cantidad, decimal Total);

    public sealed record FilaPrevia(Guid UnidadId, string Codigo, string? Bloque, string Tipo, string Estado, decimal? Monto, string Moneda);

    public sealed record PreviaRespuesta(int Listas, int YaTenian, int SinTarifa, List<TotalMoneda> Totales, List<FilaPrevia> Filas);

    private async Task<PreviaRespuesta> RespuestaAsync(List<FilaCargo> filas, CancellationToken ct)
    {
        var monedas = await _db.Monedas.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Codigo, ct);
        string Moneda(Guid? id) => id is { } x && monedas.TryGetValue(x, out var c) ? c : "";

        var totales = filas
            .Where(f => f.Estado == "Lista")
            .GroupBy(f => f.MonedaId)
            .Select(g => new TotalMoneda(Moneda(g.Key), g.Count(), g.Sum(f => f.Monto ?? 0)))
            .ToList();

        return new PreviaRespuesta(
            filas.Count(f => f.Estado == "Lista"),
            filas.Count(f => f.Estado == "YaTiene"),
            filas.Count(f => f.Estado == "SinTarifa"),
            totales,
            filas.Select(f => new FilaPrevia(f.UnidadId, f.Codigo, f.Bloque, f.Tipo, f.Estado, f.Monto, Moneda(f.MonedaId))).ToList());
    }

    // =====================================================================
    // CARGO A UNA UNIDAD
    // =====================================================================

    public sealed record CargoIndividualRequest(
        Guid UnidadComercialId,
        Guid ConceptoCobroId,
        Guid MonedaId,
        decimal Monto,
        DateOnly FechaEmision,
        DateOnly? FechaVencimiento,
        int? Anio,
        int? Mes);

    [HttpPost("cargo")]
    public async Task<IActionResult> CargoIndividual([FromBody] CargoIndividualRequest r, CancellationToken ct)
    {
        if (r.UnidadComercialId == Guid.Empty || r.ConceptoCobroId == Guid.Empty || r.MonedaId == Guid.Empty)
            return BadRequest(new { mensaje = "Elige la caseta, el concepto y la moneda." });

        if (r.Monto <= 0 || decimal.Round(r.Monto, 2) != r.Monto)
            return BadRequest(new { mensaje = "Escribe el monto: mayor a cero y con hasta dos decimales." });

        if ((r.Anio is null) != (r.Mes is null) || r.Mes is < 1 or > 12 || r.Anio is < 2000 or > 2100)
            return BadRequest(new { mensaje = "Indica el mes y el año, o marca que es un cargo eventual." });

        if (r.FechaVencimiento is { } venc && venc < r.FechaEmision)
            return BadRequest(new { mensaje = "El vencimiento no puede ser anterior a la emisión." });

        var unidad = await _db.UnidadesComerciales.AsNoTracking()
            .Where(u => u.Id == r.UnidadComercialId)
            .Select(u => new { u.Codigo, u.Activa })
            .FirstOrDefaultAsync(ct);

        if (unidad is null || !unidad.Activa)
            return BadRequest(new { mensaje = "La caseta no existe o está inactiva." });

        if (!await _db.ConceptosCobro.AsNoTracking().AnyAsync(c => c.Id == r.ConceptoCobroId && c.Activo, ct))
            return BadRequest(new { mensaje = "El concepto no existe o está inactivo." });

        if (!await _db.Monedas.AsNoTracking().AnyAsync(m => m.Id == r.MonedaId && m.Activa, ct))
            return BadRequest(new { mensaje = "La moneda no existe o está inactiva." });

        // Un concepto mensual no se cobra dos veces en el mismo período.
        // Si la deuda de ese período fue anulada, se puede volver a registrar
        // aquí, a mano: la anulada queda tal cual, con su historial. (El cargo
        // mensual a varias casetas, en cambio, no vuelve a generar las anuladas.)
        var estados = r.Anio is null
            ? new List<EstadoObligacion>()
            : await _db.Obligaciones.AsNoTracking()
                .Where(o =>
                    o.UnidadComercialId == r.UnidadComercialId &&
                    o.ConceptoCobroId == r.ConceptoCobroId &&
                    o.PeriodoAnio == r.Anio &&
                    o.PeriodoMes == r.Mes)
                .Select(o => o.Estado)
                .ToListAsync(ct);

        if (estados.Any(e => e != EstadoObligacion.Anulada))
        {
            return Conflict(new
            {
                mensaje = $"La caseta {unidad.Codigo} ya tiene ese concepto para {r.Mes:00}/{r.Anio}. " +
                          "Si el monto está mal, usa «Modificar monto»."
            });
        }

        var reemplazaAnulada = estados.Count > 0;

        var obligacion = new Obligacion(
            r.UnidadComercialId, r.ConceptoCobroId, r.MonedaId, r.Monto,
            r.FechaEmision, r.FechaVencimiento, r.Anio, r.Mes);

        _db.Obligaciones.Add(obligacion);
        await _db.SaveChangesAsync(ct);

        if (reemplazaAnulada)
        {
            HttpContext.Detalle(
                $"Caseta {unidad.Codigo}: se volvió a registrar la deuda de {r.Mes:00}/{r.Anio} " +
                $"(la anterior está anulada y se conserva). Monto {r.Monto:N2}.");
        }

        return Ok(new
        {
            id = obligacion.Id,
            mensaje = reemplazaAnulada
                ? $"Deuda registrada para la caseta {unidad.Codigo}. La deuda anulada de {r.Mes:00}/{r.Anio} se conserva en el historial."
                : $"Deuda registrada para la caseta {unidad.Codigo}."
        });
    }

    // =====================================================================
    // TARIFAS: LA MISMA A VARIAS CASETAS
    // =====================================================================

    public sealed record TarifaMasivaRequest(
        Guid ConceptoCobroId,
        Guid MonedaId,
        decimal Monto,
        string Alcance,
        string? Bloque,
        Guid? PasilloId,
        bool SoloSinTarifa);

    [HttpPost("tarifas-masivo")]
    public async Task<IActionResult> TarifasMasivo([FromBody] TarifaMasivaRequest r, CancellationToken ct)
    {
        if (r.ConceptoCobroId == Guid.Empty || r.MonedaId == Guid.Empty || r.Monto <= 0 || decimal.Round(r.Monto, 2) != r.Monto)
            return BadRequest(new { mensaje = "Elige el concepto, la moneda y un monto mayor a cero." });

        if (!await _db.ConceptosCobro.AsNoTracking().AnyAsync(c => c.Id == r.ConceptoCobroId && c.Activo, ct))
            return BadRequest(new { mensaje = "El concepto no existe o está inactivo." });

        if (!await _db.Monedas.AsNoTracking().AnyAsync(m => m.Id == r.MonedaId && m.Activa, ct))
            return BadRequest(new { mensaje = "La moneda no existe o está inactiva." });

        var casetas = _db.UnidadesComerciales.AsNoTracking()
            .Where(u => u.Activa && u.Tipo == TipoUnidadComercial.Caseta);

        switch ((r.Alcance ?? "todas").Trim().ToLowerInvariant())
        {
            case "bloque" when !string.IsNullOrWhiteSpace(r.Bloque):
                var b = r.Bloque.Trim().ToUpper();
                casetas = casetas.Where(u => u.Bloque != null && u.Bloque.Trim().ToUpper() == b);
                break;
            case "pasillo" when r.PasilloId is { } p && p != Guid.Empty:
                casetas = casetas.Where(u => u.PasilloId == p);
                break;
            case "todas":
                break;
            default:
                return BadRequest(new { mensaje = "Elige a qué casetas se asigna la tarifa." });
        }

        var ids = await casetas.Select(u => u.Id).ToListAsync(ct);

        var existentes = await _db.TarifasUnidad
            .Where(t => t.ConceptoCobroId == r.ConceptoCobroId && ids.Contains(t.UnidadComercialId))
            .ToDictionaryAsync(t => t.UnidadComercialId, ct);

        int creadas = 0, actualizadas = 0, omitidas = 0;

        foreach (var id in ids)
        {
            if (existentes.TryGetValue(id, out var tarifa))
            {
                if (r.SoloSinTarifa)
                {
                    omitidas++;
                    continue;
                }

                tarifa.Actualizar(r.MonedaId, r.Monto);
                actualizadas++;
            }
            else
            {
                _db.TarifasUnidad.Add(new TarifaUnidad(id, r.ConceptoCobroId, r.MonedaId, r.Monto));
                creadas++;
            }
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            mensaje = $"Tarifa asignada: {creadas} nuevas, {actualizadas} actualizadas" +
                      (omitidas > 0 ? $", {omitidas} casetas conservaron su tarifa." : "."),
            creadas,
            actualizadas,
            omitidas
        });
    }
}

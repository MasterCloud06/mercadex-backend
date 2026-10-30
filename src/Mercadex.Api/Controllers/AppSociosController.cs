using System.Security.Claims;
using Mercadex.Api.Seguridad;
using Mercadex.Domain.AppSocios;
using Mercadex.Domain.Caja;
using Mercadex.Domain.Cobranza;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

/// <summary>
/// API de la aplicación móvil de socios. No usa la clave interna del
/// escritorio: se protege con la cuenta del socio (token JWT). Cada
/// socio solo ve las casetas de las que es titular.
/// </summary>
[ApiController]
[Route("api/app")]
public sealed class AppSociosController : ControllerBase
{
    private static readonly TimeSpan ZonaBolivia = TimeSpan.FromHours(-4);

    private readonly MercadexDbContext _db;
    private readonly IConfiguration _config;

    public AppSociosController(MercadexDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public sealed record ActivarRequest(string? Documento, string? Codigo, string? Contrasena);
    public sealed record LoginRequest(string? Documento, string? Contrasena);
    public sealed record CambiarContrasenaRequest(string? Actual, string? Nueva);
    public sealed record ReportarPagoRequest(List<Guid>? ObligacionIds, string? Referencia, string? ComprobanteBase64, string? ComprobanteTipo);

    private const string MensajeCredenciales = "Carnet o contraseña incorrectos.";

    // ===================================================== ACCESO

    [HttpPost("activar")]
    [EnableRateLimiting("app-acceso")]
    public async Task<IActionResult> Activar([FromBody] ActivarRequest r, CancellationToken ct)
    {
        var ahora = DateTimeOffset.UtcNow;
        var documento = r.Documento?.Trim() ?? "";
        var codigo = new string((r.Codigo ?? "").Where(char.IsDigit).ToArray());

        if (documento.Length == 0 || codigo.Length == 0)
            return BadRequest(new { mensaje = "Escribe tu carnet y el código que te dio la caja." });

        var problema = SeguridadApp.ProblemaContrasena(r.Contrasena);
        if (problema != null)
            return BadRequest(new { mensaje = problema });

        var candidatas = await CuentasPorDocumentoAsync(documento, ct);

        foreach (var (cuenta, socio) in candidatas)
        {
            if (!cuenta.Activa || !cuenta.CodigoVigente(ahora))
                continue;

            if (cuenta.EstaBloqueada(ahora))
                return Bloqueada();

            if (SeguridadApp.Verificar(codigo, cuenta.CodigoActivacionHash))
            {
                cuenta.Activar(SeguridadApp.Huella(r.Contrasena!), ahora);
                await _db.SaveChangesAsync(ct);
                HttpContext.Detalle($"{socio.Nombres} {socio.Apellidos} · carnet {documento}");
                return Ok(Sesion(cuenta, socio.Nombres, socio.Apellidos));
            }

            cuenta.RegistrarFallo(ahora);
        }

        await _db.SaveChangesAsync(ct);
        return BadRequest(new { mensaje = "El carnet o el código no son válidos, o el código venció. Pide uno nuevo en la caja." });
    }

    [HttpPost("login")]
    [EnableRateLimiting("app-acceso")]
    public async Task<IActionResult> Login([FromBody] LoginRequest r, CancellationToken ct)
    {
        var ahora = DateTimeOffset.UtcNow;
        var documento = r.Documento?.Trim() ?? "";

        if (documento.Length == 0 || string.IsNullOrEmpty(r.Contrasena))
            return BadRequest(new { mensaje = "Escribe tu carnet y tu contraseña." });

        var candidatas = await CuentasPorDocumentoAsync(documento, ct);

        foreach (var (cuenta, socio) in candidatas)
        {
            if (!cuenta.Activa || !cuenta.TieneContrasena)
                continue;

            if (cuenta.EstaBloqueada(ahora))
                return Bloqueada();

            if (SeguridadApp.Verificar(r.Contrasena, cuenta.ContrasenaHash))
            {
                cuenta.RegistrarAcceso(ahora);
                await _db.SaveChangesAsync(ct);
                return Ok(Sesion(cuenta, socio.Nombres, socio.Apellidos));
            }

            cuenta.RegistrarFallo(ahora);
        }

        await _db.SaveChangesAsync(ct);
        return Unauthorized(new { mensaje = MensajeCredenciales });
    }

    [HttpPost("contrasena")]
    [Authorize(Roles = SeguridadApp.RolSocio)]
    public async Task<IActionResult> CambiarContrasena([FromBody] CambiarContrasenaRequest r, CancellationToken ct)
    {
        var (cuenta, socio, error) = await SesionActualAsync(ct);
        if (error != null) return error;

        if (!SeguridadApp.Verificar(r.Actual ?? "", cuenta!.ContrasenaHash))
            return BadRequest(new { mensaje = "La contraseña actual no es correcta." });

        var problema = SeguridadApp.ProblemaContrasena(r.Nueva);
        if (problema != null)
            return BadRequest(new { mensaje = problema });

        cuenta.CambiarContrasena(SeguridadApp.Huella(r.Nueva!));
        await _db.SaveChangesAsync(ct);

        // La versión de sesión cambió: se entrega un token nuevo.
        return Ok(Sesion(cuenta, socio!.Nombres, socio.Apellidos));
    }

    // ===================================================== CONSULTAS

    [HttpGet("perfil")]
    [Authorize(Roles = SeguridadApp.RolSocio)]
    public async Task<IActionResult> Perfil(CancellationToken ct)
    {
        var (_, socio, error) = await SesionActualAsync(ct);
        if (error != null) return error;

        var unidades = await UnidadesDelSocioAsync(socio!.Id, ct);

        return Ok(new
        {
            nombres = socio.Nombres,
            apellidos = socio.Apellidos,
            documento = socio.DocumentoIdentidad,
            telefono = socio.Telefono,
            unidades = unidades.Select(u => new { id = u.Id, codigo = u.Codigo, bloque = u.Bloque, tipo = u.Tipo })
        });
    }

    [HttpGet("deudas")]
    [Authorize(Roles = SeguridadApp.RolSocio)]
    public async Task<IActionResult> Deudas(CancellationToken ct)
    {
        var (_, socio, error) = await SesionActualAsync(ct);
        if (error != null) return error;

        var unidades = await UnidadesDelSocioAsync(socio!.Id, ct);
        var ids = unidades.Select(u => u.Id).ToList();

        var monedas = await _db.Monedas.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Codigo, ct);

        var obligaciones = await (
                from o in _db.Obligaciones.AsNoTracking()
                join c in _db.ConceptosCobro.AsNoTracking() on o.ConceptoCobroId equals c.Id
                where ids.Contains(o.UnidadComercialId) && o.Estado == EstadoObligacion.Pendiente
                select new
                {
                    o.Id, o.UnidadComercialId, o.MonedaId, o.Monto, o.MontoPagado, o.FechaEmision,
                    o.FechaVencimiento, o.PeriodoAnio, o.PeriodoMes, Concepto = c.Nombre
                })
            .ToListAsync(ct);

        var enRevision = await ObligacionesEnRevisionAsync(ids, ct);

        return Ok(unidades.Select(u => new
        {
            unidadId = u.Id,
            codigo = u.Codigo,
            bloque = u.Bloque,
            deudas = obligaciones
                .Where(o => o.UnidadComercialId == u.Id)
                .OrderBy(o => o.FechaEmision)
                .Select(o => new
                {
                    id = o.Id,
                    concepto = o.Concepto,
                    periodo = Periodo(o.PeriodoAnio, o.PeriodoMes),
                    // Lo que falta pagar (con pagos parciales, menos que el monto).
                    monto = o.Monto - o.MontoPagado,
                    montoOriginal = o.Monto,
                    pagado = o.MontoPagado,
                    monedaId = o.MonedaId,
                    moneda = monedas.GetValueOrDefault(o.MonedaId, "?"),
                    fechaEmision = o.FechaEmision,
                    fechaVencimiento = o.FechaVencimiento,
                    enRevision = enRevision.Contains(o.Id)
                })
        }));
    }

    [HttpGet("recibos")]
    [Authorize(Roles = SeguridadApp.RolSocio)]
    public async Task<IActionResult> Recibos(CancellationToken ct)
    {
        var (_, socio, error) = await SesionActualAsync(ct);
        if (error != null) return error;

        var unidades = await UnidadesDelSocioAsync(socio!.Id, ct);
        var ids = unidades.Select(u => u.Id).ToList();
        var codigoUnidad = unidades.ToDictionary(u => u.Id, u => u.Codigo);
        var monedas = await _db.Monedas.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Codigo, ct);

        var recibos = await (
                from r in _db.Recibos.AsNoTracking()
                join p in _db.Pagos.AsNoTracking() on r.PagoId equals p.Id
                where ids.Contains(r.UnidadComercialId)
                orderby r.FechaEmision descending
                select new
                {
                    r.Id, r.Numero, r.UnidadComercialId, r.FechaEmision, r.Estado,
                    r.MotivoAnulacion, r.FechaAnulacion, r.RegistradoPor, r.CargoEmisor,
                    p.MonedaId, p.Monto, p.FormaPago, p.Referencia
                })
            .Take(100)
            .ToListAsync(ct);

        var reciboIds = recibos.Select(r => r.Id).ToList();
        var detalles = await _db.DetallesRecibo.AsNoTracking()
            .Where(d => reciboIds.Contains(EF.Property<Guid>(d, "ReciboId")))
            .Select(d => new { ReciboId = EF.Property<Guid>(d, "ReciboId"), d.Concepto, d.Monto, d.PeriodoAnio, d.PeriodoMes })
            .ToListAsync(ct);

        return Ok(recibos.Select(r => new
        {
            id = r.Id,
            numero = r.Numero,
            caseta = codigoUnidad.GetValueOrDefault(r.UnidadComercialId, "?"),
            fecha = r.FechaEmision.ToOffset(ZonaBolivia),
            anulado = r.Estado == EstadoRecibo.Anulado,
            // Transparencia con el socio: por qué y cuándo se anuló.
            motivoAnulacion = r.Estado == EstadoRecibo.Anulado ? r.MotivoAnulacion : null,
            fechaAnulacion = r.Estado == EstadoRecibo.Anulado ? r.FechaAnulacion?.ToOffset(ZonaBolivia) : null,
            emitidoPor = string.IsNullOrWhiteSpace(r.RegistradoPor)
                ? null
                : r.RegistradoPor + (string.IsNullOrWhiteSpace(r.CargoEmisor) ? "" : " — " + r.CargoEmisor),
            moneda = monedas.GetValueOrDefault(r.MonedaId, "?"),
            total = r.Monto,
            formaPago = r.FormaPago switch
            {
                FormaPago.Efectivo => "Efectivo",
                FormaPago.QR => "QR",
                _ => "Otros"
            },
            detalle = detalles
                .Where(d => d.ReciboId == r.Id)
                .Select(d => new { concepto = d.Concepto, periodo = Periodo(d.PeriodoAnio, d.PeriodoMes), monto = d.Monto })
        }));
    }

    // ===================================================== PAGAR

    /// <summary>Comunicados vigentes de la directiva (los marcados para la app).</summary>
    [HttpGet("comunicados")]
    [Authorize(Roles = SeguridadApp.RolSocio)]
    public async Task<IActionResult> Comunicados(CancellationToken ct)
    {
        var hoy = ComunicadosController.Hoy();
        var lista = await _db.Comunicados.AsNoTracking()
            .Where(c => c.MostrarEnApp && !c.Archivado && (c.VigenteHasta == null || c.VigenteHasta >= hoy))
            .OrderByDescending(c => c.FechaUtc)
            .Take(30)
            .ToListAsync(ct);

        Response.Headers["Cache-Control"] = "no-store";
        return Ok(lista.Select(c => new
        {
            id = c.Id,
            titulo = c.Titulo,
            texto = c.Texto,
            fecha = c.FechaUtc.ToOffset(TimeSpan.FromHours(-4)),
            autor = c.Autor
        }));
    }

    [HttpGet("datos-pago")]
    [Authorize(Roles = SeguridadApp.RolSocio)]
    public async Task<IActionResult> DatosPago(CancellationToken ct)
    {
        var (_, _, error) = await SesionActualAsync(ct);
        if (error != null) return error;

        var c = await _db.ConfiguracionPagoApp.AsNoTracking().FirstOrDefaultAsync(ct);

        return Ok(new
        {
            disponible = c?.QrImagen != null || c?.NumeroCuenta != null,
            tieneQr = c?.QrImagen != null,
            banco = c?.Banco,
            numeroCuenta = c?.NumeroCuenta,
            titularCuenta = c?.TitularCuenta,
            instrucciones = c?.Instrucciones,
            actualizado = c?.FechaActualizacionUtc
        });
    }

    [HttpGet("datos-pago/qr")]
    [Authorize(Roles = SeguridadApp.RolSocio)]
    public async Task<IActionResult> QrPago(CancellationToken ct)
    {
        var (_, _, error) = await SesionActualAsync(ct);
        if (error != null) return error;

        var c = await _db.ConfiguracionPagoApp.AsNoTracking().FirstOrDefaultAsync(ct);
        if (c?.QrImagen == null)
            return NotFound(new { mensaje = "La asociación todavía no cargó su QR de cobro." });

        Response.Headers["Cache-Control"] = "no-store";
        return File(c.QrImagen, c.QrTipo ?? "image/png");
    }

    [HttpPost("pagos")]
    [Authorize(Roles = SeguridadApp.RolSocio)]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> ReportarPago([FromBody] ReportarPagoRequest r, CancellationToken ct)
    {
        var (_, socio, error) = await SesionActualAsync(ct);
        if (error != null) return error;

        var ids = r.ObligacionIds?.Distinct().ToList() ?? new List<Guid>();
        if (ids.Count == 0)
            return BadRequest(new { mensaje = "Elige al menos una deuda." });

        byte[] comprobante;
        try
        {
            comprobante = Convert.FromBase64String(r.ComprobanteBase64 ?? "");
        }
        catch (FormatException)
        {
            return BadRequest(new { mensaje = "No se pudo leer la foto del comprobante." });
        }

        var tipo = DetectarTipoImagen(comprobante);
        if (tipo == null)
            return BadRequest(new { mensaje = "El comprobante debe ser una foto (JPG o PNG)." });

        var unidades = (await UnidadesDelSocioAsync(socio!.Id, ct)).Select(u => u.Id).ToHashSet();

        var obligaciones = await _db.Obligaciones.AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.UnidadComercialId, o.MonedaId, Saldo = o.Monto - o.MontoPagado, o.Estado })
            .ToListAsync(ct);

        if (obligaciones.Count != ids.Count || obligaciones.Any(o => !unidades.Contains(o.UnidadComercialId)))
            return BadRequest(new { mensaje = "Alguna deuda no corresponde a tus casetas." });

        if (obligaciones.Any(o => o.Estado != EstadoObligacion.Pendiente))
            return Conflict(new { mensaje = "Alguna deuda ya fue pagada. Actualiza la lista." });

        if (obligaciones.Select(o => o.UnidadComercialId).Distinct().Count() > 1)
            return BadRequest(new { mensaje = "Paga las deudas de cada caseta por separado." });

        if (obligaciones.Select(o => o.MonedaId).Distinct().Count() > 1)
            return BadRequest(new { mensaje = "Las deudas en bolivianos y en dólares se pagan por separado." });

        var enRevision = await ObligacionesEnRevisionAsync(new List<Guid> { obligaciones[0].UnidadComercialId }, ct);
        if (ids.Any(enRevision.Contains))
            return Conflict(new { mensaje = "Ya enviaste un comprobante por alguna de estas deudas. Espera la revisión de la caja." });

        var pago = new PagoReportado(
            socio.Id,
            obligaciones[0].UnidadComercialId,
            obligaciones[0].MonedaId,
            obligaciones.Sum(o => o.Saldo),
            ids,
            r.Referencia ?? "",
            comprobante,
            tipo,
            DateTimeOffset.UtcNow);

        _db.PagosReportados.Add(pago);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = pago.Id, mensaje = "Comprobante enviado. La caja lo revisará y verás el recibo aquí." });
    }

    [HttpGet("pagos")]
    [Authorize(Roles = SeguridadApp.RolSocio)]
    public async Task<IActionResult> MisPagos(CancellationToken ct)
    {
        var (_, socio, error) = await SesionActualAsync(ct);
        if (error != null) return error;

        var monedas = await _db.Monedas.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Codigo, ct);
        var codigos = await _db.UnidadesComerciales.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Codigo, ct);

        var pagos = await _db.PagosReportados.AsNoTracking()
            .Where(p => p.SocioId == socio!.Id)
            .OrderByDescending(p => p.FechaReporteUtc)
            .Take(50)
            .Select(p => new
            {
                p.Id, p.UnidadComercialId, p.MonedaId, p.Monto, p.Referencia, p.FechaReporteUtc,
                p.Estado, p.MotivoRechazo, p.NumeroRecibo, Cantidad = p.ObligacionIds.Count
            })
            .ToListAsync(ct);

        return Ok(pagos.Select(p => new
        {
            id = p.Id,
            caseta = codigos.GetValueOrDefault(p.UnidadComercialId, "?"),
            moneda = monedas.GetValueOrDefault(p.MonedaId, "?"),
            monto = p.Monto,
            referencia = p.Referencia,
            fecha = p.FechaReporteUtc.ToOffset(ZonaBolivia),
            deudas = p.Cantidad,
            estado = p.Estado switch
            {
                EstadoPagoReportado.Aprobado => "aprobado",
                EstadoPagoReportado.Rechazado => "rechazado",
                _ => "pendiente"
            },
            motivoRechazo = p.MotivoRechazo,
            numeroRecibo = p.NumeroRecibo
        }));
    }

    // ===================================================== AUXILIARES

    private async Task<List<(CuentaSocioApp Cuenta, Domain.Socios.Socio Socio)>> CuentasPorDocumentoAsync(string documento, CancellationToken ct)
    {
        var normalizado = documento.ToUpperInvariant();

        var filas = await (
                from c in _db.CuentasSocioApp
                join s in _db.Socios on c.SocioId equals s.Id
                where s.Activo && s.DocumentoIdentidad.ToUpper() == normalizado
                select new { c, s })
            .ToListAsync(ct);

        return filas.Select(x => (x.c, x.s)).ToList();
    }

    private async Task<(CuentaSocioApp? Cuenta, Domain.Socios.Socio? Socio, IActionResult? Error)> SesionActualAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(SeguridadApp.ClaimCuenta), out var cuentaId) ||
            !int.TryParse(User.FindFirstValue(SeguridadApp.ClaimVersion), out var version))
            return (null, null, SesionVencida());

        var cuenta = await _db.CuentasSocioApp.FirstOrDefaultAsync(c => c.Id == cuentaId, ct);
        if (cuenta == null || !cuenta.Activa || cuenta.VersionSesion != version || !cuenta.TieneContrasena)
            return (null, null, SesionVencida());

        var socio = await _db.Socios.FirstOrDefaultAsync(s => s.Id == cuenta.SocioId, ct);
        if (socio == null || !socio.Activo)
            return (null, null, SesionVencida());

        return (cuenta, socio, null);
    }

    private async Task<List<(Guid Id, string Codigo, string? Bloque, string Tipo)>> UnidadesDelSocioAsync(Guid socioId, CancellationToken ct)
    {
        var filas = await (
                from a in _db.AsignacionesSocioCaseta.AsNoTracking()
                join u in _db.UnidadesComerciales.AsNoTracking() on a.UnidadComercialId equals u.Id
                where a.SocioId == socioId && a.Activa
                orderby u.Codigo
                select new { u.Id, u.Codigo, u.Bloque, u.Tipo })
            .ToListAsync(ct);

        return filas
            .Select(u => (u.Id, u.Codigo, (string?)u.Bloque, u.Tipo.ToString()))
            .ToList();
    }

    private async Task<HashSet<Guid>> ObligacionesEnRevisionAsync(List<Guid> unidadIds, CancellationToken ct)
    {
        var listas = await _db.PagosReportados.AsNoTracking()
            .Where(p => p.Estado == EstadoPagoReportado.Pendiente && unidadIds.Contains(p.UnidadComercialId))
            .Select(p => p.ObligacionIds)
            .ToListAsync(ct);

        return listas.SelectMany(x => x).ToHashSet();
    }

    private object Sesion(CuentaSocioApp cuenta, string nombres, string apellidos)
    {
        var (token, expira) = SeguridadApp.CrearToken(cuenta, $"{nombres} {apellidos}", _config["MERCADEX_JWT_KEY"]!);
        return new { token, expira, nombres, apellidos };
    }

    private static string? Periodo(int? anio, int? mes) =>
        anio.HasValue && mes.HasValue ? $"{mes:00}/{anio}" : null;

    private static string? DetectarTipoImagen(byte[] datos)
    {
        if (datos.Length > 3 && datos[0] == 0xFF && datos[1] == 0xD8 && datos[2] == 0xFF)
            return "image/jpeg";
        if (datos.Length > 8 && datos[0] == 0x89 && datos[1] == 0x50 && datos[2] == 0x4E && datos[3] == 0x47)
            return "image/png";
        return null;
    }

    private IActionResult SesionVencida() =>
        Unauthorized(new { mensaje = "Tu sesión terminó. Vuelve a ingresar." });

    private IActionResult Bloqueada() =>
        StatusCode(StatusCodes.Status429TooManyRequests, new
        {
            mensaje = "Demasiados intentos. Por seguridad, espera 15 minutos y vuelve a intentar."
        });
}

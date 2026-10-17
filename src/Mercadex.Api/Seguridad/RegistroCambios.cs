using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Mercadex.Domain.Auditoria;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Seguridad;

/// <summary>
/// Registro de cambios (auditoría): quién hizo qué, cuándo y desde dónde.
/// Cada petición que modifica datos y termina bien deja una línea. Los
/// controladores pueden agregar un detalle legible con
/// <see cref="Cambios.Detalle"/>; si no lo hacen, se guarda un resumen de
/// los datos enviados (sin contraseñas, códigos ni archivos).
/// </summary>
public static class Cambios
{
    private const string ClaveDetalle = "mx.cambio.detalle";
    private const string ClaveReferencia = "mx.cambio.referencia";
    private const string ClaveOmitir = "mx.cambio.omitir";

    /// <summary>Texto que verá la directiva en el registro (por ejemplo «Recibo N.º 120 · Caseta A-12»).</summary>
    public static void Detalle(this HttpContext http, string detalle) =>
        http.Items[ClaveDetalle] = detalle;

    public static void Referencia(this HttpContext http, object? referencia) =>
        http.Items[ClaveReferencia] = referencia?.ToString();

    /// <summary>La acción ya dejó su propia línea (o no corresponde registrarla).</summary>
    public static void NoRegistrar(this HttpContext http) =>
        http.Items[ClaveOmitir] = true;

    private static readonly CultureInfo Bolivia = CultureInfo.GetCultureInfo("es-BO");

    public static string Monto(decimal monto, string moneda) =>
        $"{(moneda == "BOB" ? "Bs" : moneda == "USD" ? "$us" : moneda)} {monto.ToString("N2", Bolivia)}";

    internal static string? LeerDetalle(HttpContext http) => http.Items[ClaveDetalle] as string;

    internal static string? LeerReferencia(HttpContext http) => http.Items[ClaveReferencia] as string;

    internal static bool Omitir(HttpContext http) => http.Items.ContainsKey(ClaveOmitir);

    /// <summary>Agrega una línea al registro. Se guarda con el próximo SaveChanges.</summary>
    public static void Agregar(
        MercadexDbContext db,
        HttpContext http,
        string modulo,
        string accion,
        string? detalle,
        string? referencia = null,
        string? usuario = null,
        string? usuarioId = null,
        string? rol = null)
    {
        var sesion = http.User;
        var conSesion = sesion.Identity?.IsAuthenticated == true;

        db.RegistroCambios.Add(new RegistroCambio(
            DateTimeOffset.UtcNow,
            conSesion ? sesion.FindFirstValue(ClaimTypes.NameIdentifier) : usuarioId,
            conSesion ? sesion.Identity!.Name : usuario ?? "Sin sesión",
            conSesion ? sesion.FindFirstValue(ClaimTypes.Role) : rol,
            Origen(http),
            modulo,
            accion,
            detalle,
            referencia,
            http.Connection.RemoteIpAddress?.ToString()));
    }

    /// <summary>
    /// Escritorio, Panel web o App de socios. El navegador siempre envía
    /// el encabezado Origin; el escritorio y la app no.
    /// </summary>
    public static string Origen(HttpContext http)
    {
        if (http.User.IsInRole(SeguridadApp.RolSocio) ||
            http.Request.Path.StartsWithSegments("/api/app"))
            return "App de socios";

        return http.Request.Headers.Origin.Count > 0 ? "Panel web" : "Escritorio";
    }

    // ------------------------------------------------------------------
    // Nombres legibles de cada acción: "Controlador.Acción" → (módulo, acción).
    // ------------------------------------------------------------------

    internal static readonly Dictionary<string, (string Modulo, string Accion)> Nombres = new(StringComparer.Ordinal)
    {
        ["Caja.ProcesarCobro"] = ("Caja", "Cobro registrado"),
        ["Caja.AnularRecibo"] = ("Caja", "Recibo anulado"),
        ["Caja.CrearPago"] = ("Caja", "Pago registrado"),
        ["Caja.CrearRecibo"] = ("Caja", "Recibo emitido"),
        ["ArqueoDiario.RegistrarCierre"] = ("Caja", "Cierre de caja"),
        ["Egresos.Post"] = ("Egresos", "Egreso registrado"),
        ["Egresos.Anular"] = ("Egresos", "Egreso anulado"),

        ["PanelCaja.Aprobar"] = ("Pagos QR", "Pago QR aprobado"),
        ["PanelCaja.Rechazar"] = ("Pagos QR", "Pago QR rechazado"),
        ["PanelCaja.GenerarCodigo"] = ("App de socios", "Código de acceso generado"),
        ["PanelCaja.Desactivar"] = ("App de socios", "Acceso a la app desactivado"),
        ["PanelCaja.GuardarConfiguracion"] = ("App de socios", "Datos de pago QR cambiados"),

        ["AppSocios.Activar"] = ("App de socios", "Socio activó su cuenta"),
        ["AppSocios.CambiarContrasena"] = ("App de socios", "Socio cambió su contraseña"),
        ["AppSocios.ReportarPago"] = ("App de socios", "Socio envió un pago QR"),

        ["Cobranza.Generar"] = ("Obligaciones", "Cargo mensual generado"),
        ["Cobranza.CargoIndividual"] = ("Obligaciones", "Cargo individual"),
        ["Cobranza.TarifasMasivo"] = ("Obligaciones", "Tarifas cambiadas en bloque"),
        ["GeneradorCobranza.Generar"] = ("Obligaciones", "Cobranza generada"),
        ["TarifasUnidad.Guardar"] = ("Obligaciones", "Tarifa de caseta cambiada"),
        ["TarifasUnidad.GenerarMensual"] = ("Obligaciones", "Cargo mensual generado"),
        ["Obligaciones.Crear"] = ("Obligaciones", "Deuda creada"),
        ["Obligaciones.MarcarComoPagada"] = ("Obligaciones", "Deuda marcada como pagada"),
        ["Obligaciones.RestaurarComoPendiente"] = ("Obligaciones", "Deuda vuelta a pendiente"),
        ["CorreccionesObligaciones.CorregirMonto"] = ("Obligaciones", "Monto de deuda corregido"),

        ["ConceptosCobro.Actualizar"] = ("Conceptos de cobro", "Concepto editado"),
        ["ConceptosCobro.Desactivar"] = ("Conceptos de cobro", "Concepto desactivado"),
        ["ConceptosCobro.Activar"] = ("Conceptos de cobro", "Concepto activado"),
        ["CatalogosIngresos.CrearConcepto"] = ("Conceptos de cobro", "Concepto creado"),

        ["Socios.Crear"] = ("Socios", "Socio registrado"),
        ["Socios.Actualizar"] = ("Socios", "Datos del socio editados"),
        ["Socios.Desactivar"] = ("Socios", "Socio dado de baja"),
        ["Socios.Activar"] = ("Socios", "Socio reactivado"),
        ["Kardex.Guardar"] = ("Socios", "Kardex editado"),
        ["KardexDocumentos.Adjuntar"] = ("Socios", "Documento adjuntado al kardex"),
        ["AsignacionesSocioCaseta.Crear"] = ("Socios", "Socio asignado a caseta"),

        ["Casetas.Ubicacion"] = ("Casetas", "Ubicación cambiada"),
        ["Casetas.Titular"] = ("Casetas", "Titular asignado"),
        ["Casetas.QuitarTitular"] = ("Casetas", "Titular quitado"),
        ["Casetas.Desactivar"] = ("Casetas", "Caseta desactivada"),
        ["Casetas.Activar"] = ("Casetas", "Caseta activada"),
        ["UnidadesComerciales.Crear"] = ("Casetas", "Caseta creada"),
        ["UnidadesComerciales.ActualizarUbicacion"] = ("Casetas", "Ubicación cambiada"),
        ["Pasillos.Crear"] = ("Casetas", "Pasillo creado"),
        ["Importacion.Padron"] = ("Casetas", "Padrón importado desde Excel"),

        ["ConceptosCobro.Eliminar"] = ("Conceptos de cobro", "Concepto eliminado"),
        ["MapaMercado.CargarPlano"] = ("Casetas", "Casetas cargadas del plano"),
        ["Comunicados.Crear"] = ("Comunicados", "Comunicado creado"),
        ["Comunicados.Editar"] = ("Comunicados", "Comunicado editado"),
        ["Comunicados.Archivar"] = ("Comunicados", "Comunicado archivado"),
        ["Comunicados.Restaurar"] = ("Comunicados", "Comunicado restaurado"),

        ["Usuarios.Crear"] = ("Usuarios", "Usuario creado"),
        ["Usuarios.Actualizar"] = ("Usuarios", "Usuario editado"),
        ["Usuarios.Restablecer"] = ("Usuarios", "Contraseña restablecida"),
        ["Usuarios.Desactivar"] = ("Usuarios", "Usuario desactivado"),
        ["Usuarios.Activar"] = ("Usuarios", "Usuario activado"),
        ["Auth.CambiarContrasena"] = ("Sesión", "Cambió su contraseña"),
    };

    /// <summary>Acciones que no se registran (inicios de sesión: los registra el propio controlador).</summary>
    internal static readonly HashSet<string> Excluidas = new(StringComparer.Ordinal)
    {
        "Auth.Login", "AppSocios.Login"
    };

    // ------------------------------------------------------------------
    // Resumen automático de los datos enviados.
    // ------------------------------------------------------------------

    private static readonly string[] Sensibles =
    {
        "password", "contrasena", "clave", "token", "comprobante", "qr", "base64",
        "archivo", "imagen", "foto", "contenido", "hash", "firma"
    };

    internal static string? Resumir(IDictionary<string, object?> argumentos)
    {
        var partes = new List<string>();

        foreach (var (nombre, valor) in argumentos)
        {
            if (valor is null || valor is CancellationToken || EsSensible(nombre))
                continue;

            if (EsSimple(valor.GetType()))
            {
                if (valor is not Guid)
                    partes.Add($"{Etiqueta(nombre)}: {Texto(valor)}");
                continue;
            }

            if (valor is IEnumerable and not string)
                continue;

            foreach (var p in valor.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || EsSensible(p.Name) || !EsSimple(p.PropertyType))
                    continue;

                object? v;
                try { v = p.GetValue(valor); }
                catch { continue; }

                if (v is null or Guid || v is string s && string.IsNullOrWhiteSpace(s))
                    continue;

                partes.Add($"{Etiqueta(p.Name)}: {Texto(v)}");
            }
        }

        if (partes.Count == 0)
            return null;

        var texto = string.Join(" · ", partes);
        return texto.Length > 1000 ? texto[..1000] : texto;
    }

    private static bool EsSensible(string nombre)
    {
        var n = nombre.ToLowerInvariant();
        return Sensibles.Any(n.Contains);
    }

    private static bool EsSimple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) ||
               t == typeof(Guid) || t == typeof(DateOnly) || t == typeof(DateTime) ||
               t == typeof(DateTimeOffset);
    }

    private static string Texto(object v) => v switch
    {
        bool b => b ? "sí" : "no",
        decimal d => d.ToString("N2", CultureInfo.GetCultureInfo("es-BO")),
        DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        string s => s.Length > 200 ? s[..200] + "…" : s,
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
    };

    /// <summary>«NombrePagador» → «Nombre pagador».</summary>
    private static string Etiqueta(string nombre)
    {
        var texto = new System.Text.StringBuilder();

        for (var i = 0; i < nombre.Length; i++)
        {
            var c = nombre[i];
            if (i > 0 && char.IsUpper(c))
                texto.Append(' ').Append(char.ToLowerInvariant(c));
            else
                texto.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }

        return texto.ToString();
    }
}

/// <summary>Filtro global que escribe la línea del registro cuando un cambio sale bien.</summary>
public sealed class RegistroCambiosFilter : IAsyncActionFilter
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RegistroCambiosFilter> _log;

    public RegistroCambiosFilter(IServiceScopeFactory scopes, ILogger<RegistroCambiosFilter> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var metodo = http.Request.Method;

        if (HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo) ||
            context.ActionDescriptor is not ControllerActionDescriptor accion)
        {
            await next();
            return;
        }

        var clave = $"{accion.ControllerName}.{accion.ActionName}";

        if (Cambios.Excluidas.Contains(clave))
        {
            await next();
            return;
        }

        // Se resume antes de ejecutar: algunas acciones modifican sus argumentos.
        var resumen = Cambios.Resumir(context.ActionArguments);
        var referencia = new[] { "id", "unidadId", "socioId" }
            .Select(k => context.RouteData.Values.TryGetValue(k, out var v) ? v?.ToString() : null)
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));

        var resultado = await next();

        if (resultado.Exception is not null && !resultado.ExceptionHandled)
            return;

        var estado = resultado.Result switch
        {
            IStatusCodeActionResult r => r.StatusCode ?? 200,
            _ => http.Response.StatusCode
        };

        if (estado is < 200 or >= 300 || Cambios.Omitir(http))
            return;

        var (modulo, texto) = Cambios.Nombres.TryGetValue(clave, out var nombre)
            ? nombre
            : (accion.ControllerName, accion.ActionName);

        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MercadexDbContext>();

            var detalle = Cambios.LeerDetalle(http);
            var refFinal = Cambios.LeerReferencia(http) ?? referencia;

            // Sin detalle propio: se nombra el registro afectado (socio, caseta, concepto…).
            if (detalle is null && Guid.TryParse(refFinal, out var guid))
            {
                var nombreRegistro = await DescribirAsync(db, guid);
                detalle = nombreRegistro is null
                    ? resumen
                    : resumen is null ? nombreRegistro : $"{nombreRegistro} · {resumen}";
            }

            Cambios.Agregar(
                db,
                http,
                modulo,
                texto,
                detalle ?? resumen,
                refFinal);

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // El cambio ya quedó guardado; no se le devuelve error al usuario.
            _log.LogError(ex, "No se pudo escribir el registro de cambios de {Accion}", clave);
        }
    }

    private static async Task<string?> DescribirAsync(MercadexDbContext db, Guid id)
    {
        var socio = await db.Socios.AsNoTracking().Where(x => x.Id == id)
            .Select(x => x.Nombres + " " + x.Apellidos).FirstOrDefaultAsync();
        if (socio is not null)
            return $"Socio {socio}";

        var caseta = await db.UnidadesComerciales.AsNoTracking().Where(x => x.Id == id)
            .Select(x => x.Codigo).FirstOrDefaultAsync();
        if (caseta is not null)
            return $"Caseta {caseta}";

        var concepto = await db.ConceptosCobro.AsNoTracking().Where(x => x.Id == id)
            .Select(x => x.Nombre).FirstOrDefaultAsync();
        if (concepto is not null)
            return $"Concepto {concepto}";

        var deuda = await (
                from o in db.Obligaciones.AsNoTracking()
                join u in db.UnidadesComerciales.AsNoTracking() on o.UnidadComercialId equals u.Id
                join c in db.ConceptosCobro.AsNoTracking() on o.ConceptoCobroId equals c.Id
                where o.Id == id
                select "Caseta " + u.Codigo + " · " + c.Nombre)
            .FirstOrDefaultAsync();

        return deuda;
    }
}

using Mercadex.Api.Seguridad;
using Mercadex.Domain.Comunicacion;
using Mercadex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

/// <summary>
/// Comunicados de la directiva a los socios. Se imprimen desde el
/// escritorio con el membrete de la asociación y, si se marca, se muestran
/// en la app de socios hasta su fecha de vigencia. Leer: todos los roles;
/// crear, editar y archivar: administrador (PermisosPorRolFilter).
/// </summary>
[ApiController]
[Route("api/comunicados")]
public sealed class ComunicadosController : ControllerBase
{
    private static readonly TimeSpan ZonaBolivia = TimeSpan.FromHours(-4);

    private readonly MercadexDbContext _db;

    public ComunicadosController(MercadexDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool archivados = false, CancellationToken ct = default)
    {
        var hoy = Hoy();
        var lista = await _db.Comunicados.AsNoTracking()
            .Where(c => c.Archivado == archivados)
            .OrderByDescending(c => c.FechaUtc)
            .ToListAsync(ct);

        Response.Headers["Cache-Control"] = "no-store";
        return Ok(lista.Select(c => Vista(c, hoy)));
    }

    public sealed record ComunicadoRequest(string? Titulo, string? Texto, bool MostrarEnApp, DateOnly? VigenteHasta);

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] ComunicadoRequest r, CancellationToken ct)
    {
        Comunicado c;
        try
        {
            c = new Comunicado(r.Titulo ?? "", r.Texto ?? "", r.MostrarEnApp, r.VigenteHasta,
                SeguridadUsuarios.NombreResponsable(User), DateTimeOffset.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message.Split(" (Parameter")[0] });
        }

        _db.Comunicados.Add(c);
        await _db.SaveChangesAsync(ct);

        HttpContext.Referencia(c.Id);
        HttpContext.Detalle($"«{c.Titulo}»" + (c.MostrarEnApp ? " · visible en la app" : ""));
        return Ok(Vista(c, Hoy()));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Editar(Guid id, [FromBody] ComunicadoRequest r, CancellationToken ct)
    {
        var c = await _db.Comunicados.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
            return NotFound(new { mensaje = "El comunicado no existe." });

        try
        {
            c.Editar(r.Titulo ?? "", r.Texto ?? "", r.MostrarEnApp, r.VigenteHasta);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message.Split(" (Parameter")[0] });
        }

        await _db.SaveChangesAsync(ct);
        HttpContext.Detalle($"«{c.Titulo}»" + (c.MostrarEnApp ? " · visible en la app" : " · solo impreso"));
        return Ok(Vista(c, Hoy()));
    }

    [HttpPost("{id:guid}/archivar")]
    public Task<IActionResult> Archivar(Guid id, CancellationToken ct) => CambiarArchivo(id, true, ct);

    [HttpPost("{id:guid}/restaurar")]
    public Task<IActionResult> Restaurar(Guid id, CancellationToken ct) => CambiarArchivo(id, false, ct);

    private async Task<IActionResult> CambiarArchivo(Guid id, bool archivar, CancellationToken ct)
    {
        var c = await _db.Comunicados.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
            return NotFound(new { mensaje = "El comunicado no existe." });

        if (archivar) c.Archivar(); else c.Restaurar();
        await _db.SaveChangesAsync(ct);

        HttpContext.Detalle($"«{c.Titulo}»");
        return Ok(new
        {
            mensaje = archivar
                ? $"«{c.Titulo}» se archivó: ya no aparece en la app."
                : $"«{c.Titulo}» volvió a la lista de comunicados."
        });
    }

    internal static DateOnly Hoy() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ZonaBolivia).DateTime);

    internal static object Vista(Comunicado c, DateOnly hoy) => new
    {
        id = c.Id,
        titulo = c.Titulo,
        texto = c.Texto,
        fecha = c.FechaUtc.ToOffset(ZonaBolivia),
        autor = c.Autor,
        mostrarEnApp = c.MostrarEnApp,
        vigenteHasta = c.VigenteHasta,
        archivado = c.Archivado,
        visibleEnApp = c.VisibleEnApp(hoy)
    };
}

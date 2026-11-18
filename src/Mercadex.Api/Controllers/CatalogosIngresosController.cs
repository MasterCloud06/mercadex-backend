using Mercadex.Domain.Caja;
using Mercadex.Domain.Cobranza;
using Mercadex.Infrastructure.Persistence;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mercadex.Api.Controllers;

[ApiController]
[Route("api/ingresos/catalogos")]
public sealed class CatalogosIngresosController : ControllerBase
{
    private readonly MercadexDbContext _context;

    public CatalogosIngresosController(
        MercadexDbContext context)
    {
        _context = context;
    }

    // Consultar formas de pago y conceptos.
    [HttpGet]
    public async Task<IActionResult> Obtener()
    {
        // Formas de pago ofrecidas en caja: Efectivo, QR y Otros.
        // "Transferencia" se conserva solo para cobros historicos
        // (en el arqueo cuenta dentro de "Otros").
        var formasPago = new[]
            {
                new { Id = (int)FormaPago.Efectivo, Nombre = "Efectivo" },
                new { Id = (int)FormaPago.QR, Nombre = "QR" },
                new { Id = (int)FormaPago.Otro, Nombre = "Otros" }
            }
            .ToList();

        var conceptos = await _context.ConceptosCobro
            .AsNoTracking()
            .OrderBy(concepto => concepto.Nombre)
            .Select(concepto => new
            {
                concepto.Id,
                concepto.Codigo,
                concepto.Nombre,
                concepto.Descripcion,
                concepto.Activo
            })
            .ToListAsync();

        return Ok(new
        {
            FormasPago = formasPago,
            Conceptos = conceptos
        });
    }

    // Crear un concepto nuevo.
    // POST: /api/ingresos/catalogos/conceptos
    [HttpPost("conceptos")]
    public async Task<IActionResult> CrearConcepto(
        [FromBody] CrearConceptoCobroRequest solicitud)
    {
        if (string.IsNullOrWhiteSpace(solicitud.Codigo))
        {
            return BadRequest(new
            {
                mensaje = "Introduce el código del concepto."
            });
        }

        if (string.IsNullOrWhiteSpace(solicitud.Nombre))
        {
            return BadRequest(new
            {
                mensaje = "Introduce el nombre del concepto."
            });
        }

        var codigo = solicitud.Codigo
            .Trim()
            .ToUpperInvariant();

        var nombre = solicitud.Nombre.Trim();

        var nombreComparacion = nombre.ToUpperInvariant();

        // Incluye conceptos inactivos para no duplicarlos.
        var codigoExiste = await _context.ConceptosCobro
            .AsNoTracking()
            .AnyAsync(concepto =>
                concepto.Codigo.ToUpper() == codigo);

        if (codigoExiste)
        {
            return Conflict(new
            {
                mensaje =
                    $"Ya existe un concepto con el código {codigo}."
            });
        }

        var nombreExiste = await _context.ConceptosCobro
            .AsNoTracking()
            .AnyAsync(concepto =>
                concepto.Nombre.ToUpper() == nombreComparacion);

        if (nombreExiste)
        {
            return Conflict(new
            {
                mensaje =
                    $"Ya existe un concepto con el nombre {nombre}."
            });
        }

        var conceptoNuevo = new ConceptoCobro(
            codigo,
            nombre,
            solicitud.Descripcion);

        // Número para los reportes: el siguiente libre.
        var ultimo = await _context.ConceptosCobro.AsNoTracking().MaxAsync(c => c.Numero) ?? 0;
        if (ultimo < ConceptoCobro.NumeroMaximo)
            conceptoNuevo.AsignarNumero(ultimo + 1);

        _context.ConceptosCobro.Add(conceptoNuevo);

        await _context.SaveChangesAsync();

        return StatusCode(
            StatusCodes.Status201Created,
            new
            {
                conceptoNuevo.Id,
                conceptoNuevo.Codigo,
                conceptoNuevo.Numero,
                conceptoNuevo.Nombre,
                conceptoNuevo.Descripcion,
                conceptoNuevo.Activo
            });
    }
}

public sealed class CrearConceptoCobroRequest
{
    [System.ComponentModel.DataAnnotations.Required(
        ErrorMessage = "El código es obligatorio.")]
    [System.ComponentModel.DataAnnotations.StringLength(
        50,
        ErrorMessage = "El código no puede superar los 50 caracteres.")]
    public string Codigo { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required(
        ErrorMessage = "El nombre es obligatorio.")]
    [System.ComponentModel.DataAnnotations.StringLength(
        150,
        ErrorMessage = "El nombre no puede superar los 150 caracteres.")]
    public string Nombre { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.StringLength(
        500,
        ErrorMessage = "La descripción no puede superar los 500 caracteres.")]
    public string? Descripcion { get; set; }
}
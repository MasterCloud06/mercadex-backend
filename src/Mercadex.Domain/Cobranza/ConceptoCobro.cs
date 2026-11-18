using Mercadex.Domain.Common;

namespace Mercadex.Domain.Cobranza;

public sealed class ConceptoCobro : Entity
{
    private ConceptoCobro()
    {
    }

    public ConceptoCobro(
        string codigo,
        string nombre,
        string? descripcion = null)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            throw new ArgumentException(
                "El código del concepto es obligatorio.",
                nameof(codigo));
        }

        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ArgumentException(
                "El nombre del concepto es obligatorio.",
                nameof(nombre));
        }

        Codigo = codigo.Trim().ToUpperInvariant();
        Nombre = nombre.Trim();
        Descripcion = LimpiarOpcional(descripcion);

        Activo = true;
    }

    public string Codigo { get; private set; } = string.Empty;

    /// <summary>
    /// Número del concepto en los reportes (0001, 0024…), como en el sistema
    /// anterior. Se asigna solo al crearlo y se puede cambiar.
    /// </summary>
    public int? Numero { get; private set; }

    public const int NumeroMaximo = 9999;

    public void AsignarNumero(int numero)
    {
        if (numero < 1 || numero > NumeroMaximo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numero),
                $"El número del concepto debe estar entre 1 y {NumeroMaximo}.");
        }

        Numero = numero;
    }

    public string Nombre { get; private set; } = string.Empty;

    public string? Descripcion { get; private set; }

    public bool Activo { get; private set; }

    public void CambiarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ArgumentException(
                "El nombre del concepto es obligatorio.",
                nameof(nombre));
        }

        Nombre = nombre.Trim();
    }

    public void CambiarDescripcion(string? descripcion)
    {
        Descripcion = LimpiarOpcional(descripcion);
    }

    public void Activar()
    {
        Activo = true;
    }

    public void Desactivar()
    {
        Activo = false;
    }

    private static string? LimpiarOpcional(string? valor)
    {
        return string.IsNullOrWhiteSpace(valor)
            ? null
            : valor.Trim();
    }
}

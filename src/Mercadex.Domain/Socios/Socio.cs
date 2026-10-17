using Mercadex.Domain.Common;

namespace Mercadex.Domain.Socios;

public sealed class Socio : Entity
{
    private Socio()
    {
    }


    public Socio(
        string nombres,
        string apellidos,
        string documentoIdentidad,
        string? telefono = null)
    {
        ValidarNombres(nombres);
        ValidarApellidos(apellidos);
        ValidarDocumento(documentoIdentidad);


        Nombres = nombres.Trim();

        Apellidos = apellidos.Trim();

        DocumentoIdentidad =
            documentoIdentidad.Trim();

        Telefono =
            LimpiarOpcional(telefono);


        FechaRegistro =
            DateTimeOffset.UtcNow;


        Activo = true;
    }


    // Constructor compatible con pruebas y creación básica
    public Socio(
        string nombres,
        string apellidos)
        : this(
            nombres,
            apellidos,
            "SIN_DOCUMENTO",
            null)
    {
    }


    // Constructor compatible cuando solo se envía teléfono
    public Socio(
        string nombres,
        string apellidos,
        string? telefono)
        : this(
            nombres,
            apellidos,
            "SIN_DOCUMENTO",
            telefono)
    {
    }



    public string Nombres { get; private set; }
        = string.Empty;


    public string Apellidos { get; private set; }
        = string.Empty;


    public string DocumentoIdentidad { get; private set; }
        = string.Empty;


    public string? Telefono { get; private set; }


    public DateTimeOffset FechaRegistro { get; private set; }


    public bool Activo { get; private set; }

    /// <summary>
    /// true: comerciante que NO es socio (comedores y ambulantes del
    /// manual): puede tener unidad, deudas y pagar, pero no tiene Kardex.
    /// false (por defecto): socio de la asociación.
    /// </summary>
    public bool SinAfiliacion { get; private set; }

    public bool EsSocio => !SinAfiliacion;

    /// <summary>Lugar de expedición del carnet (LP, SC, CB…). Opcional; el Kardex lo exige.</summary>
    public string? LugarExpedicion { get; private set; }

    public void DefinirTipo(bool esSocio) => SinAfiliacion = !esSocio;

    public void CambiarLugarExpedicion(string? lugar)
    {
        lugar = lugar?.Trim();
        if (string.IsNullOrEmpty(lugar))
        {
            LugarExpedicion = null;
            return;
        }

        if (lugar.Length > 30)
            throw new ArgumentException("El lugar de expedición es demasiado largo.", nameof(lugar));

        LugarExpedicion = lugar.ToUpperInvariant();
    }


    public string NombreCompleto =>
        $"{Nombres} {Apellidos}";


    /// <summary>Corrige los datos personales del socio (nombre, CI, celular).</summary>
    public void ActualizarDatos(
        string nombres,
        string apellidos,
        string documentoIdentidad,
        string? telefono)
    {
        ValidarNombres(nombres);
        ValidarApellidos(apellidos);
        ValidarDocumento(documentoIdentidad);

        Nombres = nombres.Trim();
        Apellidos = apellidos.Trim();
        DocumentoIdentidad = documentoIdentidad.Trim();
        Telefono = LimpiarOpcional(telefono);
    }


    public void Desactivar()
    {
        Activo = false;
    }


    public void Activar()
    {
        Activo = true;
    }


    private static void ValidarNombres(
        string nombres)
    {
        if (string.IsNullOrWhiteSpace(nombres))
        {
            throw new ArgumentException(
                "Los nombres son obligatorios.",
                nameof(nombres));
        }
    }


    private static void ValidarApellidos(
        string apellidos)
    {
        if (string.IsNullOrWhiteSpace(apellidos))
        {
            throw new ArgumentException(
                "Los apellidos son obligatorios.",
                nameof(apellidos));
        }
    }


    private static void ValidarDocumento(
        string documentoIdentidad)
    {
        if (string.IsNullOrWhiteSpace(documentoIdentidad))
        {
            throw new ArgumentException(
                "El documento de identidad es obligatorio.",
                nameof(documentoIdentidad));
        }
    }


    private static string? LimpiarOpcional(
        string? valor)
    {
        return string.IsNullOrWhiteSpace(valor)
            ? null
            : valor.Trim();
    }
}
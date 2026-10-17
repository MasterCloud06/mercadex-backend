using Mercadex.Domain.Common;

namespace Mercadex.Domain.Comunicacion;

/// <summary>
/// Comunicado de la directiva a los socios: se imprime con el membrete de
/// la asociación y, si se elige, aparece en la app de socios hasta la
/// fecha de vigencia. No se borra: se archiva.
/// </summary>
public sealed class Comunicado : Entity
{
    public const int MaximoTitulo = 150;
    public const int MaximoTexto = 4000;

    private Comunicado()
    {
    }

    public Comunicado(string titulo, string texto, bool mostrarEnApp, DateOnly? vigenteHasta, string? autor, DateTimeOffset fechaUtc)
    {
        FechaUtc = fechaUtc;
        Autor = Recortar(autor, 120);
        Editar(titulo, texto, mostrarEnApp, vigenteHasta);
    }

    public string Titulo { get; private set; } = "";

    public string Texto { get; private set; } = "";

    public DateTimeOffset FechaUtc { get; private set; }

    public string? Autor { get; private set; }

    public bool MostrarEnApp { get; private set; }

    /// <summary>Último día que se ve en la app (inclusive). null: hasta archivarlo.</summary>
    public DateOnly? VigenteHasta { get; private set; }

    public bool Archivado { get; private set; }

    public void Editar(string titulo, string texto, bool mostrarEnApp, DateOnly? vigenteHasta)
    {
        titulo = titulo?.Trim() ?? "";
        texto = texto?.Trim() ?? "";

        if (titulo.Length < 3)
            throw new ArgumentException("Escribe el título del comunicado.", nameof(titulo));
        if (titulo.Length > MaximoTitulo)
            throw new ArgumentException($"El título no puede pasar de {MaximoTitulo} caracteres.", nameof(titulo));
        if (texto.Length < 3)
            throw new ArgumentException("Escribe el texto del comunicado.", nameof(texto));
        if (texto.Length > MaximoTexto)
            throw new ArgumentException($"El texto no puede pasar de {MaximoTexto} caracteres.", nameof(texto));

        Titulo = titulo;
        Texto = texto;
        MostrarEnApp = mostrarEnApp;
        VigenteHasta = vigenteHasta;
    }

    public void Archivar() => Archivado = true;

    public void Restaurar() => Archivado = false;

    /// <summary>Se ve en la app ese día.</summary>
    public bool VisibleEnApp(DateOnly hoy) =>
        MostrarEnApp && !Archivado && (VigenteHasta is null || VigenteHasta >= hoy);

    private static string? Recortar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        texto = texto.Trim();
        return texto.Length > maximo ? texto[..maximo] : texto;
    }
}

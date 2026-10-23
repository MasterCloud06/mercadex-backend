using Mercadex.Domain.Common;

namespace Mercadex.Domain.Cobranza;

public enum TipoAnulacionObligaciones
{
    Individual = 1,
    Masiva = 2
}

/// <summary>
/// Una anulación de deudas (una sola o varias a la vez): quién la hizo,
/// cuándo, con qué filtros y, si se indicó, por qué. Cada deuda anulada queda en
/// <see cref="Detalles"/> con una copia de sus datos. No se borra nada:
/// la deuda sigue en la base con estado Anulada.
/// </summary>
public sealed class AnulacionObligaciones : Entity
{
    public const int MotivoMaximo = 500;

    /// <summary>Lo que queda en el historial cuando no se escribe un motivo (es opcional).</summary>
    public const string SinMotivo = "Sin motivo indicado";
    public const int FiltrosMaximo = 1000;

    private readonly List<DetalleAnulacionObligacion> _detalles = [];

    private AnulacionObligaciones()
    {
    }

    internal AnulacionObligaciones(
        Guid claveOperacion,
        TipoAnulacionObligaciones tipo,
        string? motivo,
        string responsable,
        string? responsableId,
        string? filtros,
        DateTime fechaUtc)
    {
        if (claveOperacion == Guid.Empty)
        {
            throw new ArgumentException(
                "La clave de la operación es obligatoria.",
                nameof(claveOperacion));
        }

        if (!Enum.IsDefined(tipo))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tipo),
                "El tipo de anulación no es válido.");
        }

        if (motivo?.Trim().Length > MotivoMaximo)
        {
            throw new ArgumentException(
                $"El motivo no puede superar los {MotivoMaximo} caracteres.",
                nameof(motivo));
        }

        if (string.IsNullOrWhiteSpace(responsable) ||
            responsable.Trim().Length > 120)
        {
            throw new ArgumentException(
                "El responsable es obligatorio.",
                nameof(responsable));
        }

        if (fechaUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "La fecha debe estar expresada en UTC.",
                nameof(fechaUtc));
        }

        ClaveOperacion = claveOperacion;
        Tipo = tipo;
        Motivo = string.IsNullOrWhiteSpace(motivo) ? SinMotivo : motivo.Trim();
        Responsable = responsable.Trim();
        ResponsableId = string.IsNullOrWhiteSpace(responsableId)
            ? null
            : responsableId.Trim();
        Filtros = string.IsNullOrWhiteSpace(filtros)
            ? null
            : filtros.Trim()[..Math.Min(filtros.Trim().Length, FiltrosMaximo)];
        FechaUtc = fechaUtc;
    }

    /// <summary>
    /// Clave que envía el cliente al confirmar. Si la misma confirmación
    /// llega dos veces (doble clic, reintento), la segunda no anula nada.
    /// </summary>
    public Guid ClaveOperacion { get; private set; }

    public TipoAnulacionObligaciones Tipo { get; private set; }

    public string Motivo { get; private set; } = string.Empty;

    public string Responsable { get; private set; } = string.Empty;

    public string? ResponsableId { get; private set; }

    /// <summary>Filtros usados en una anulación masiva, en texto legible.</summary>
    public string? Filtros { get; private set; }

    public DateTime FechaUtc { get; private set; }

    public int Cantidad { get; private set; }

    public IReadOnlyCollection<DetalleAnulacionObligacion> Detalles =>
        _detalles.AsReadOnly();

    internal void Agregar(DetalleAnulacionObligacion detalle)
    {
        _detalles.Add(detalle);
        Cantidad = _detalles.Count;
    }
}

/// <summary>
/// Una deuda anulada, con la copia de sus datos al momento de anularla.
/// Una deuda solo puede aparecer en una anulación (índice único).
/// </summary>
public sealed class DetalleAnulacionObligacion : Entity
{
    private DetalleAnulacionObligacion()
    {
    }

    internal DetalleAnulacionObligacion(
        Guid anulacionId,
        Obligacion obligacion)
    {
        AnulacionId = anulacionId;
        ObligacionId = obligacion.Id;
        UnidadComercialId = obligacion.UnidadComercialId;
        ConceptoCobroId = obligacion.ConceptoCobroId;
        MonedaId = obligacion.MonedaId;
        Monto = obligacion.Monto;
        PeriodoAnio = obligacion.PeriodoAnio;
        PeriodoMes = obligacion.PeriodoMes;
        EstadoAnterior = obligacion.Estado;
    }

    public Guid AnulacionId { get; private set; }

    public Guid ObligacionId { get; private set; }

    public Guid UnidadComercialId { get; private set; }

    public Guid ConceptoCobroId { get; private set; }

    public Guid MonedaId { get; private set; }

    public decimal Monto { get; private set; }

    public int? PeriodoAnio { get; private set; }

    public int? PeriodoMes { get; private set; }

    public EstadoObligacion EstadoAnterior { get; private set; }
}

/// <summary>
/// Anula una o varias deudas pendientes y deja el registro de la
/// operación. Todas o ninguna: si alguna no está pendiente no se anula
/// ninguna. El controlador comprueba además que no tengan recibos ni
/// comprobantes QR en revisión, y bloquea las filas antes de llamar aquí.
/// </summary>
public static class ServicioAnulacionObligaciones
{
    public static AnulacionObligaciones Anular(
        IEnumerable<Obligacion> obligaciones,
        Guid claveOperacion,
        TipoAnulacionObligaciones tipo,
        string? motivo,
        string responsable,
        string? responsableId,
        string? filtros,
        DateTime fechaUtc)
    {
        ArgumentNullException.ThrowIfNull(obligaciones);

        var lista = obligaciones.ToList();

        if (lista.Count == 0)
        {
            throw new ArgumentException(
                "Debe existir al menos una deuda para anular.",
                nameof(obligaciones));
        }

        if (lista.Any(x => x is null))
        {
            throw new ArgumentException(
                "Todas las deudas deben ser válidas.",
                nameof(obligaciones));
        }

        if (lista.GroupBy(x => x.Id).Any(g => g.Count() > 1))
        {
            throw new InvalidOperationException(
                "La misma deuda no puede anularse dos veces.");
        }

        if (tipo == TipoAnulacionObligaciones.Individual && lista.Count != 1)
        {
            throw new InvalidOperationException(
                "Una anulación individual es de una sola deuda.");
        }

        if (lista.Any(x => !x.EstaPendiente))
        {
            throw new InvalidOperationException(
                "Solamente se pueden anular deudas pendientes.");
        }

        var anulacion = new AnulacionObligaciones(
            claveOperacion,
            tipo,
            motivo,
            responsable,
            responsableId,
            filtros,
            fechaUtc);

        foreach (var obligacion in lista)
        {
            // La copia se toma antes de cambiar el estado.
            anulacion.Agregar(new DetalleAnulacionObligacion(anulacion.Id, obligacion));
            obligacion.Anular();
        }

        return anulacion;
    }
}

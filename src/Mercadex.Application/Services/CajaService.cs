using Mercadex.Application.DTOs;
using Mercadex.Application.DTOs.Caja;
using Mercadex.Application.Interfaces;
using Mercadex.Application.Interfaces.Persistence;
using Mercadex.Domain.Caja;

namespace Mercadex.Application.Services;

public sealed class CajaService : ICajaService
{
    private readonly IPagoRepository _pagoRepository;
    private readonly IReciboRepository _reciboRepository;
    private readonly IObligacionRepository _obligacionRepository;
    private readonly IUnitOfWork _unitOfWork;


    public CajaService(
        IPagoRepository pagoRepository,
        IReciboRepository reciboRepository,
        IObligacionRepository obligacionRepository,
        IUnitOfWork unitOfWork)
    {
        _pagoRepository = pagoRepository;
        _reciboRepository = reciboRepository;
        _obligacionRepository = obligacionRepository;
        _unitOfWork = unitOfWork;
    }


    public Task<PagoDto> RegistrarPagoAsync(
        CrearPagoDto dto)
    {
        return _pagoRepository.CrearAsync(dto);
    }


    public async Task<ReciboDto> GenerarReciboAsync(
        CrearReciboDto dto)
    {
        var recibo = await _reciboRepository
            .CrearAsync(dto);


        foreach (var detalle in dto.Detalles)
        {
            await _obligacionRepository
                .MarcarComoPagadaAsync(
                    detalle.ObligacionId);
        }


        return recibo;
    }


    
public async Task<ResultadoCobroDto> ProcesarCobroAsync(
    ProcesarCobroDto dto,
    string? registradoPor = null,
    string? cargoEmisor = null,
    string? registradoPorId = null)
{
    if (dto.Items is null)
    {
        throw new ArgumentException(
            "Debe seleccionar al menos una obligación.");
    }

    var solicitudes = dto.Items.ToList();

    if (solicitudes.Count == 0)
    {
        throw new ArgumentException(
            "Debe seleccionar al menos una obligación.");
    }

    if (solicitudes
        .GroupBy(x => x.ObligacionId)
        .Any(g => g.Count() > 1))
    {
        throw new InvalidOperationException(
            "No puede seleccionar dos veces la misma obligación.");
    }

    await _unitOfWork.BeginTransactionAsync();

    try
    {
        var obligaciones =
            await _obligacionRepository.ObtenerPorIdsAsync(
                solicitudes.Select(x => x.ObligacionId));

        if (obligaciones.Count != solicitudes.Count)
        {
            throw new InvalidOperationException(
                "No se encontraron todas las obligaciones.");
        }

        if (obligaciones.Any(x =>
            x.UnidadComercialId != dto.UnidadComercialId))
        {
            throw new InvalidOperationException(
                "Las obligaciones no pertenecen a la unidad seleccionada.");
        }

        if (obligaciones.Any(x =>
            x.MonedaId != dto.MonedaId))
        {
            throw new InvalidOperationException(
                "La moneda del pago no coincide con las obligaciones.");
        }

        // Pago QR: el numero de transaccion es obligatorio
        // para poder conciliar con el extracto del banco.
        if (dto.FormaPago == (int)FormaPago.QR &&
            string.IsNullOrWhiteSpace(dto.Referencia))
        {
            throw new ArgumentException(
                "Para un pago QR debe registrar el numero de transaccion.");
        }

        if (!Enum.IsDefined(typeof(FormaPago), dto.FormaPago))
        {
            throw new ArgumentException(
                "La forma de pago no es valida.");
        }

        // Pago total o parcial: cada deuda paga lo indicado o, si no se
        // indica, todo su saldo. El importe debe coincidir con esa suma.
        var items = obligaciones
            .Select(o =>
            {
                var solicitud = solicitudes.First(x => x.ObligacionId == o.Id);
                return new ItemCobro(o, solicitud.NombreConcepto, solicitud.Monto);
            })
            .ToList();

        var totalEsperado = items.Sum(x => x.APagar);

        if (dto.Monto != totalEsperado)
        {
            throw new InvalidOperationException(
                "El importe no coincide con las obligaciones seleccionadas.");
        }

        // El número se asigna en el servidor.
        // El valor NumeroRecibo enviado por el escritorio se ignora.
        var numeroRecibo =
            await _unitOfWork.ObtenerSiguienteNumeroReciboAsync();

        var resultado = ServicioCobro.Registrar(
            numeroRecibo,
            items,
            (FormaPago)dto.FormaPago,
            dto.NombrePagador,
            DateTimeOffset.UtcNow,
            dto.TipoCambio,
            dto.Referencia);

        // Quién registró el cobro (usuario con sesión iniciada).
        resultado.Recibo.AsignarRegistradoPor(registradoPor, cargoEmisor, registradoPorId);
        resultado.Recibo.AsignarObservaciones(dto.Observaciones);

        await _pagoRepository.GuardarAsync(resultado.Pago);

        await _reciboRepository.GuardarAsync(resultado.Recibo);

        await _unitOfWork.CommitAsync();

        return new ResultadoCobroDto(
            new PagoDto(
                resultado.Pago.Id,
                resultado.Pago.MonedaId,
                resultado.Pago.Monto,
                resultado.Pago.FormaPago.ToString(),
                resultado.Pago.NombrePagador,
                resultado.Pago.Referencia,
                resultado.Pago.TipoCambio,
                resultado.Pago.FechaRegistro,
                resultado.Pago.Estado.ToString()),

            new ReciboDto(
                resultado.Recibo.Id,
                resultado.Recibo.Numero,
                resultado.Recibo.UnidadComercialId,
                resultado.Recibo.PagoId,
                resultado.Recibo.FechaEmision,
                resultado.Recibo.Estado.ToString(),
                resultado.Recibo.Total,
                resultado.Recibo.Detalles.Select(d =>
                    new DetalleReciboDto(
                        d.ObligacionId,
                        d.ConceptoCobroId,
                        d.Concepto,
                        d.Monto,
                        d.PeriodoAnio,
                        d.PeriodoMes)))
            {
                RegistradoPor = resultado.Recibo.RegistradoPor,
                CargoEmisor = resultado.Recibo.CargoEmisor,
                Observaciones = resultado.Recibo.Observaciones
            }
        );
    }
    catch
    {
        await _unitOfWork.RollbackAsync();
        throw;
    }
}



    
public async Task<bool> AnularReciboAsync(
    Guid reciboId,
    AnularReciboDto dto,
    string anuladoPor)
{
    ArgumentNullException.ThrowIfNull(dto);
if (string.IsNullOrWhiteSpace(anuladoPor))
{
    throw new ArgumentException(
        "No se pudo identificar al usuario que realiza la anulación.",
        nameof(anuladoPor));
}

anuladoPor = anuladoPor.Trim();

    var motivo = dto.Motivo?.Trim();

    if (string.IsNullOrWhiteSpace(motivo))
    {
        throw new ArgumentException(
            "El motivo de anulacion es obligatorio.");
    }

    if (motivo.Length > 500)
    {
        throw new ArgumentException(
            "El motivo no puede superar 500 caracteres.");
    }

    
    // MERCADEX_ANULACION_AUDITADA_V2
    // Validaciones adicionales del comprobante.

    if (motivo.Length < 5)
    {
        throw new ArgumentException(
            "El motivo debe tener al menos 5 caracteres.");
    }

    if (anuladoPor.Length > 120)
    {
        throw new ArgumentException(
            "El responsable no puede superar 120 caracteres.");
    }
    await _unitOfWork.BeginTransactionAsync();

    try
    {
        // 1. Consultar el recibo original.

        var recibo = await _reciboRepository
            .GetByIdAsync(reciboId);

        if (recibo is null)
        {
            await _unitOfWork.RollbackAsync();
            return false;
        }

        if (!string.Equals(
            recibo.Estado,
            "Emitido",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Solo se pueden anular recibos emitidos.");
        }

        // 2. Validar el pago asociado.

        var pago = await _pagoRepository
            .GetByIdAsync(recibo.PagoId);

        if (pago is null ||
            !string.Equals(
                pago.Estado,
                "Registrado",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "El pago no existe o ya fue anulado.");
        }

        // 3. Verificar todas las obligaciones
        // antes de modificar cualquier registro.

        var detalles = recibo.Detalles.ToList();

        if (detalles.Count == 0 ||
            detalles.Select(d => d.ObligacionId)
                .Distinct()
                .Count() != detalles.Count)
        {
            throw new InvalidOperationException(
                "Los detalles del recibo no son validos.");
        }

        foreach (var detalle in detalles)
        {
            var obligacion = await _obligacionRepository
                .GetByIdAsync(detalle.ObligacionId);

            // Debe tener pagado al menos lo que este recibo pagó de ella
            // (todo, o un abono si fue un pago parcial).
            var pagado = obligacion is null
                ? 0m
                : string.Equals(obligacion.Estado, "Pagada", StringComparison.OrdinalIgnoreCase)
                    ? (obligacion.MontoPagado == 0 ? obligacion.Monto : obligacion.MontoPagado)
                    : string.Equals(obligacion.Estado, "Pendiente", StringComparison.OrdinalIgnoreCase)
                        ? obligacion.MontoPagado
                        : 0m;

            if (obligacion is null ||
                obligacion.UnidadComercialId !=
                    recibo.UnidadComercialId ||
                pagado < detalle.Monto ||
                detalle.Monto <= 0)
            {
                throw new InvalidOperationException(
                    "No se puede restituir una de las " +
                    "obligaciones del recibo.");
            }
        }

        // 4. Anular recibo y pago y restituir deudas.
        // Todos los cambios pertenecen
        // a la misma transaccion.

var reciboAnulado =
    await _reciboRepository.AnularAsync(
        reciboId,
        motivo,
        anuladoPor);

        if (!reciboAnulado)
        {
            throw new InvalidOperationException(
                "No se pudo anular el recibo.");
        }

        var pagoAnulado =
            await _pagoRepository.AnularAsync(
                recibo.PagoId);

        if (!pagoAnulado)
        {
            throw new InvalidOperationException(
                "No se pudo anular el pago asociado.");
        }

        foreach (var detalle in detalles)
        {
            var restaurada =
                await _obligacionRepository
                    .RevertirAbonoAsync(
                        detalle.ObligacionId,
                        detalle.Monto);

            if (!restaurada)
            {
                throw new InvalidOperationException(
                    "No se pudo restituir una deuda.");
            }
        }

        // 5. Confirmar todo junto.

        
        // Registrar comprobante de anulacion.
        // No se realiza Commit adicional.
        // MERCADEX_ANULACION_AUDITADA_V2

        var comprobanteAnulacion =
            new Mercadex.Domain.Caja.ComprobanteAnulacionRecibo(
                recibo.Id,
                Convert.ToInt64(recibo.Numero),
                recibo.UnidadComercialId,
                recibo.PagoId,
                pago.MonedaId,
                Convert.ToDecimal(recibo.Total),
                FechaComprobanteAnulacion.Convertir(
                    recibo.FechaEmision),
                DateTimeOffset.UtcNow,
                motivo,
                anuladoPor);

        await _unitOfWork.RegistrarComprobanteAnulacionAsync(
            comprobanteAnulacion);

        // El Commit existente guardara el comprobante
        // junto con la anulacion del recibo, del pago
        // y la restitucion de las obligaciones.
        await _unitOfWork.CommitAsync();

        return true;
    }
    catch
    {
        // Si algo falla, no se confirma
        // ninguna de las modificaciones.

        await _unitOfWork.RollbackAsync();
        throw;
    }
}

}
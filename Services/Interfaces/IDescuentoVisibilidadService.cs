namespace TheBuryProject.Services.Interfaces;

/// <summary>
/// DESCUENTO-PERMISO-03: decide si un documento o reporte puede entregarse al usuario actual. Un documento
/// que contiene descuentos comerciales manuales (venta, cotización, factura, contrato, documento emitido)
/// solo puede verlo quien tiene <c>ventas.discount</c>: el contenido histórico no se altera ni se oculta
/// parcialmente, simplemente no se entrega. Sin descuentos, el acceso sigue gobernado por los permisos de siempre.
/// Los métodos devuelven <c>true</c> cuando hay que DENEGAR; una entidad inexistente nunca deniega (que responda
/// el NotFound habitual).
/// </summary>
public interface IDescuentoVisibilidadService
{
    /// <summary>El usuario actual tiene el permiso independiente <c>ventas.discount</c>.</summary>
    bool PuedeVerDescuentos { get; }

    Task<bool> DebeDenegarVentaAsync(int ventaId);

    Task<bool> DebeDenegarFacturaAsync(int facturaId);

    Task<bool> DebeDenegarCotizacionAsync(int cotizacionId);

    /// <summary>Documentos emitidos (ids): deniega si alguno está anclado a una venta/cotización con descuentos.</summary>
    Task<bool> DebeDenegarDocumentosAsync(IReadOnlyCollection<int> documentoIds);

    /// <summary>Ids (de los recibidos) que el usuario actual no puede ver: la UI oculta sus accesos; el 403 se mantiene igual.</summary>
    Task<IReadOnlySet<int>> DocumentosRestringidosAsync(IReadOnlyCollection<int> documentoIds);

    Task<bool> DebeDenegarGrupoDocumentosAsync(Guid grupoImpresionId);

    /// <summary>Documentos de un pago de cuota/cobranza (el id es el del pago ancla).</summary>
    Task<bool> DebeDenegarDocumentosDePagoAsync(int pagoId);
}

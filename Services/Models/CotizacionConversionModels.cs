using System.Text.Json.Serialization;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Models;

// COTIZACION-MIVENTA-01 (POC "Mi Venta"): además de crear la Venta (lo que esta clase
// ya hacía), permite encadenar en la MISMA llamada las dos acciones que hoy existen por
// separado en Venta/Edit — VentaController.EjecutarConfirmarYFacturarAsync ya hace
// exactamente esta secuencia (ConfirmarVentaAsync → FacturarVentaAsync) para una venta
// ya creada; acá se reutilizan esos mismos dos métodos de IVentaService, sin reimplementar
// ninguna regla. Ninguno de los dos se ejecuta si el usuario no tiene el permiso real que
// ya exige VentaController (ventas/update para confirmar, ventas/invoice para facturar) —
// ver CotizacionConversionService.ConvertirAVentaAsync.

public sealed class CotizacionConversionPreviewResultado
{
    public bool Convertible { get; init; }
    public List<string> Errores { get; init; } = new();
    public List<string> Advertencias { get; init; } = new();
    public int CotizacionId { get; init; }
    public EstadoCotizacion EstadoCotizacion { get; init; }
    public int? ClienteId { get; init; }
    public bool ClienteFaltante { get; init; }
    public bool CotizacionVencida { get; init; }
    public bool HayCambiosDePrecios { get; init; }
    public bool HayProductosTrazables { get; init; }
    public decimal TotalCotizado { get; init; }

    // Envío separado del total cotizado (no recibe recargo del plan) y total a cobrar resultante.
    public decimal ImporteEnvio { get; init; }
    public decimal TotalACobrar { get; init; }
    public decimal AnticipoCotizado { get; init; }
    public List<CotizacionConversionDetallePreview> Detalles { get; init; } = new();
}

public sealed class CotizacionConversionDetallePreview
{
    public int ProductoId { get; init; }
    public string CodigoProducto { get; init; } = string.Empty;
    public string NombreProducto { get; init; } = string.Empty;
    public int Cantidad { get; init; }
    public decimal PrecioCotizado { get; init; }
    public decimal? PrecioActual { get; init; }
    public bool ProductoActivo { get; init; }
    public bool PrecioCambio { get; init; }
    public bool RequiereUnidadFisica { get; init; }
    public decimal? DiferenciaUnitaria { get; init; }
    public decimal? DiferenciaTotal { get; init; }
    public List<string> Advertencias { get; init; } = new();
}

public sealed class CotizacionConversionRequest
{
    public bool UsarPrecioCotizado { get; init; } = true;
    public bool ConfirmarAdvertencias { get; init; } = false;
    public int? ClienteIdOverride { get; init; }
    public string? ObservacionesAdicionales { get; init; }

    // VENTA-COTIZACION-EXCEPCION-01: mismo mecanismo de excepción documental que ya usa
    // Venta/Create (VentaViewModel.AplicarExcepcionDocumental/MotivoExcepcionDocumentalCreate),
    // aplicado en el único momento de "commit" real que existe para una cotización todavía sin
    // Venta: la conversión. Ver CotizacionConversionService.ConvertirAVentaAsync, que delega la
    // decisión completa (permiso, alcance excepcionable) en IVentaService.
    // AplicarExcepcionDocumentalSiCorresponde — no reimplementa esa regla acá.
    public bool AplicarExcepcionDocumental { get; init; } = false;
    public string? MotivoExcepcionDocumental { get; init; }

    // COTIZACION-MIVENTA-01: si viene en true, tras crear la venta se intenta
    // ConfirmarVentaAsync (y, si Facturar también viene en true, FacturarVentaAsync a
    // continuación) — mismo orden que EjecutarConfirmarYFacturarAsync. Si el estado de la
    // venta no lo permite (crédito personal pendiente de configurar el plan, autorización
    // pendiente, etc.) o el usuario no tiene el permiso correspondiente, no se confirma ni
    // se factura y el resultado lo indica explícitamente — nunca se fuerza ni se asume.
    public bool ConfirmarVenta { get; init; } = false;
    public bool Facturar { get; init; } = false;
    // El <select> del Cotizador manda "A"/"B"/"C" (mismos valores que el <select
    // id="modal-tipo-factura"> de Venta/Create) — el resto del wire de este DTO no tiene
    // convertidor de enum-a-string global, así que se declara acá puntualmente.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoFactura TipoFactura { get; init; } = TipoFactura.B;

    // Envío: mismos campos reales de VentaEnvioViewModel/VentaEnvio. Si no llegan (o llegan
    // vacíos), se completan con el domicilio del Cliente — comportamiento previo, sin cambios.
    public string? EnvioDestinatario { get; init; }
    public string? EnvioTelefono { get; init; }
    public string? EnvioDomicilio { get; init; }
    public string? EnvioLocalidad { get; init; }
    public string? EnvioProvincia { get; init; }
    public string? EnvioCodigoPostal { get; init; }
    public string? EnvioTransportista { get; init; }
    public decimal? EnvioCostoEnvio { get; init; }
    public DateTime? EnvioFechaProgramada { get; init; }
    public string? EnvioObservaciones { get; init; }
}

// COTIZACION-MIVENTA-02 (segunda iteración de la POC "Mi Venta", 2026-09-17): "Confirmar Mi
// Venta" no puede crear la Venta primero y descubrir un bloqueo después — eso es justo lo que
// hacía derivar silenciosamente al wizard. Este preflight es de sólo lectura (no persiste nada,
// no abre transacción) y reutiliza las reglas reales ya existentes: IVentaValidator.ValidarStock
// (misma validación que ConfirmarVentaAsync corre después de crear, acá se corre ANTES sobre una
// Venta en memoria) y ICajaService.ObtenerAperturaActivaParaUsuarioAsync (mismo chequeo que
// AsegurarCajaAbiertaParaUsuarioActualAsync, sin lanzar ni reservar nada). Crédito personal se
// reporta siempre como bloqueo con acción "Continuar con wizard": VentaValidator.
// ValidarEstadoParaConfirmacion sólo acepta Cotización/Presupuesto/PendienteRequisitos, y
// AplicarResultadoValidacionAsync deja cualquier venta de Crédito personal en
// PendienteFinanciacion — confirmar en un solo paso es estructuralmente imposible hasta que el
// plan se configure en Credito/ConfigurarVenta (wizard), no una ambigüedad de negocio.
public sealed class CotizacionMiVentaBloqueo
{
    public string Codigo { get; init; } = string.Empty;
    public string Mensaje { get; init; } = string.Empty;
    public string? AccionSugerida { get; init; }
}

public sealed class CotizacionMiVentaPreflightResultado
{
    public bool Listo { get; init; }
    public bool EsCreditoPersonal { get; init; }
    public List<CotizacionMiVentaBloqueo> Bloqueos { get; init; } = new();
}

// COTIZACION-MIVENTA-02: preview de Subtotal/IVA/alícuotas para el modal "Facturar al
// confirmar" ANTES de que exista una Venta — construido con el mismo cálculo que
// ConvertirAVentaAsync ya usa para armar los VentaDetalle reales (ConstruirDetalles), envuelto
// en FacturaAlicuotaResumenBuilder.Build (el mismo builder que ya usa VentaController.Facturar
// GET). Los números coinciden con Venta/Details porque es literalmente el mismo cálculo sobre
// los mismos datos snapshot de la Cotización, no una segunda implementación en JS.
public sealed class CotizacionFacturaPreviewResultado
{
    public bool Exitoso { get; init; }
    public List<string> Errores { get; init; } = new();
    public decimal Subtotal { get; init; }
    public decimal IVA { get; init; }
    public decimal Total { get; init; }
    public List<FacturaAlicuotaResumenViewModel> ResumenAlicuotas { get; init; } = new();
}

public sealed class CotizacionConversionResultado
{
    public bool Exitoso { get; init; }
    public List<string> Errores { get; init; } = new();
    public List<string> Advertencias { get; init; } = new();
    public int CotizacionId { get; init; }
    public int? VentaId { get; init; }
    public string? NumeroVenta { get; init; }
    public EstadoVenta? EstadoVenta { get; init; }

    // VENTA-COTIZACION-EXCEPCION-01: espejo de venta.TieneExcepcionDocumentalRegistrada — para
    // que el frontend (cotizacion-simulador.js) pueda confirmar que la excepción solicitada
    // efectivamente se autorizó (nunca lo asume del lado del cliente).
    public bool ExcepcionDocumentalAplicada { get; init; }

    // COTIZACION-MIVENTA-01: refleja lo que realmente pasó al intentar encadenar Confirmar/
    // Facturar — nunca lo asume el frontend, siempre viene de VentaService.ConfirmarVentaAsync/
    // FacturarVentaAsync (o de un permiso faltante, ver MensajeConfirmacion).
    public bool VentaConfirmada { get; init; }
    public bool Facturada { get; init; }
    public string? MensajeConfirmacion { get; init; }

    public static CotizacionConversionResultado Fallido(int cotizacionId, IEnumerable<string> errores) =>
        new() { Exitoso = false, CotizacionId = cotizacionId, Errores = errores.ToList() };
}

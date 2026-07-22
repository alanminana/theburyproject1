using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel unificado del detalle de caja (apertura live + cierre read-only).
/// Lo construye <see cref="TheBuryProject.Services.CajaConciliacionBuilder"/> a partir del
/// <see cref="DetallesAperturaViewModel"/> ya poblado por el service: es solo presentación.
/// Separa de forma explícita Vendido / Cobrado / Pendiente / Caja física y expone un
/// libro mayor con saldo esperado acumulado para conciliar a mano cuando la caja no cierra.
/// </summary>
public class CajaConciliacionViewModel
{
    // ── Encabezado ──
    public int AperturaId { get; set; }
    public int? CierreId { get; set; }
    public string NombreCaja { get; set; } = "Caja";
    public string? CodigoCaja { get; set; }
    public string? Ubicacion { get; set; }
    public string? Sucursal { get; set; }

    /// <summary>Turno cerrado: la vista se renderiza en modo solo lectura.</summary>
    public bool EstaCerrada { get; set; }
    public bool EsLectura => EstaCerrada;
    /// <summary>Live + (dueño del turno o admin): habilita Nuevo movimiento / Cerrar caja.</summary>
    public bool PuedeOperar { get; set; }

    public DateTime FechaApertura { get; set; }
    public DateTime? FechaCierre { get; set; }
    public string Responsable { get; set; } = string.Empty;
    public string? UsuarioCierre { get; set; }
    public string? ObservacionesApertura { get; set; }
    public string? ObservacionesCierre { get; set; }
    public DateTime? UltimaActividad { get; set; }
    public TimeSpan? Duracion { get; set; }

    // ── Cards principales ──
    public decimal FondoInicial { get; set; }
    /// <summary>Total facturado/confirmado del turno (incluye crédito personal). No es plata en caja.</summary>
    public decimal TotalVendido { get; set; }
    /// <summary>Dinero efectivamente recibido en el turno (efectivo + digital).</summary>
    public decimal TotalCobrado { get; set; }
    public decimal TotalCobradoEfectivo { get; set; }
    public decimal TotalCobradoDigital { get; set; }
    /// <summary>Vendido todavía no cobrado (crédito personal, cuenta corriente).</summary>
    public decimal TotalPendiente { get; set; }
    public decimal IngresosEfectivo { get; set; }
    public decimal EgresosEfectivo { get; set; }
    /// <summary>Fondo inicial + ingresos efectivo − egresos efectivo.</summary>
    public decimal CajaFisicaEsperada { get; set; }

    // ── Cierre (solo si EstaCerrada) ──
    public decimal EfectivoContado { get; set; }
    public decimal ChequesContados { get; set; }
    public decimal ValesContados { get; set; }
    public decimal MontoEsperadoSistema { get; set; }
    public decimal MontoTotalReal { get; set; }
    public decimal DiferenciaCaja { get; set; }
    public bool TieneDiferencia { get; set; }
    public string? JustificacionDiferencia { get; set; }
    public string? DetalleArqueo { get; set; }

    // ── Colecciones por tab ──
    public List<ResumenMedioConciliacionViewModel> ResumenPorMedio { get; set; } = new();
    public List<VentaTurnoLineaViewModel> Ventas { get; set; } = new();
    public List<MovimientoCajaLineaViewModel> Movimientos { get; set; } = new();
    public List<ConciliacionLineaViewModel> LibroMayor { get; set; } = new();
    public List<VentaTurnoLineaViewModel> VentasSinImpacto { get; set; } = new();
    public List<AuditoriaCajaLineaViewModel> Auditoria { get; set; } = new();

    // ── Derivados de presentación ──
    public IEnumerable<VentaTurnoLineaViewModel> VentasEfectivas => Ventas.Where(v => v.Categoria == VentaTurnoCategoria.Efectiva);
    public IEnumerable<VentaTurnoLineaViewModel> VentasPendientes => Ventas.Where(v => v.Categoria == VentaTurnoCategoria.Pendiente);
    public IEnumerable<VentaTurnoLineaViewModel> VentasRegistro => Ventas.Where(v => v.Categoria == VentaTurnoCategoria.Registro);
    public int CantidadMovimientos => Movimientos.Count;
}

/// <summary>Clasificación de la venta del turno según su estado, para seccionar la vista.</summary>
public enum VentaTurnoCategoria
{
    /// <summary>Confirmada / Facturada / Entregada.</summary>
    Efectiva,
    /// <summary>PendienteRequisitos / PendienteFinanciacion.</summary>
    Pendiente,
    /// <summary>Presupuesto / Cotización / Cancelada (auditoría operativa).</summary>
    Registro
}

/// <summary>
/// Impacto financiero de un movimiento de caja según el medio de pago (spec 5.2): el recargo
/// y la naturaleza del dinero no deben quedar ocultos. Diferencia dinero real en caja, saldo en
/// bancos/billeteras, valores con liquidación posterior y operaciones sin ingreso inmediato.
/// </summary>
public enum CategoriaImpactoCaja
{
    /// <summary>Efectivo: ingreso/egreso real de caja física.</summary>
    CajaFisica,
    /// <summary>Transferencia, Mercado Pago, depósito bancario: saldo en cuentas o billeteras.</summary>
    CuentaBancaria,
    /// <summary>Tarjeta y cheque: valores con acreditación / liquidación posterior.</summary>
    AAcreditar,
    /// <summary>Crédito personal, cuenta corriente: no genera ingreso inmediato en caja.</summary>
    SinIngresoInmediato
}

/// <summary>Fila del resumen por medio de pago: Vendido / Cobrado / Pendiente / impacta caja física.</summary>
public class ResumenMedioConciliacionViewModel
{
    public string MedioPago { get; set; } = string.Empty;
    public string MedioKey { get; set; } = "otro";
    public decimal TotalVendido { get; set; }
    public decimal TotalCobrado { get; set; }
    public decimal TotalPendiente { get; set; }
    public bool ImpactaCajaFisica { get; set; }
}

/// <summary>Línea de venta del turno con cobrado/pendiente derivados de los movimientos de caja.</summary>
public class VentaTurnoLineaViewModel
{
    public int VentaId { get; set; }
    public string Numero { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string Cliente { get; set; } = "Sin cliente";
    public EstadoVenta Estado { get; set; }
    public string EstadoLabel { get; set; } = string.Empty;
    public string EstadoChipClass { get; set; } = "chip chip-neutral";
    public string MedioPago { get; set; } = string.Empty;
    public string MedioKey { get; set; } = "otro";
    public decimal TotalVenta { get; set; }
    public decimal CobradoAhora { get; set; }
    public decimal Pendiente { get; set; }
    public bool ImpactaCajaFisica { get; set; }
    public string? Referencia { get; set; }
    public string? MotivoNoImpacta { get; set; }
    public VentaTurnoCategoria Categoria { get; set; } = VentaTurnoCategoria.Efectiva;
    public bool Cancelada => Estado == EstadoVenta.Cancelada;
}

/// <summary>Movimiento real de caja con Entra/Sale separados (no monto con signo).</summary>
public class MovimientoCajaLineaViewModel
{
    public int MovimientoId { get; set; }
    public DateTime FechaHora { get; set; }
    public bool EsIngreso { get; set; }
    public string TipoLabel { get; set; } = string.Empty;
    public string Concepto { get; set; } = string.Empty;
    public string MedioPago { get; set; } = string.Empty;
    public string MedioKey { get; set; } = "otro";
    public string? Referencia { get; set; }
    /// <summary>URL de navegación de la referencia (venta / crédito), o null si no es navegable.</summary>
    public string? ReferenciaUrl { get; set; }
    public string? Descripcion { get; set; }
    public decimal Entra { get; set; }
    public decimal Sale { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string? Observacion { get; set; }
    public bool ImpactaCajaFisica { get; set; }

    /// <summary>Importe base del movimiento, antes de recargo/descuento del medio de pago.</summary>
    public decimal? ImporteBase { get; set; }
    /// <summary>Recargo del medio de pago aplicado (concepto separado del importe base).</summary>
    public decimal? RecargoMedioPago { get; set; }
    /// <summary>Descuento del medio de pago aplicado (concepto separado del importe base).</summary>
    public decimal? DescuentoMedioPago { get; set; }
    /// <summary>True si el movimiento tiene un recargo o descuento del medio a desglosar.</summary>
    public bool TieneAjusteMedioPago => (RecargoMedioPago ?? 0m) != 0m || (DescuentoMedioPago ?? 0m) != 0m;

    /// <summary>Impacto financiero del movimiento según el medio de pago (lo resuelve el builder).</summary>
    public CategoriaImpactoCaja CategoriaImpacto { get; set; } = CategoriaImpactoCaja.CajaFisica;

    /// <summary>Etiqueta corta de la categoría de impacto, para el chip de la vista.</summary>
    public string CategoriaImpactoLabel => CategoriaImpacto switch
    {
        CategoriaImpactoCaja.CajaFisica => "Caja física",
        CategoriaImpactoCaja.CuentaBancaria => "Banco / billetera",
        CategoriaImpactoCaja.AAcreditar => "A acreditar",
        CategoriaImpactoCaja.SinIngresoInmediato => "Sin ingreso inmediato",
        _ => "Caja física"
    };

    /// <summary>Clase de chip asociada a la categoría de impacto.</summary>
    public string CategoriaImpactoChipClass => CategoriaImpacto switch
    {
        CategoriaImpactoCaja.CajaFisica => "chip-ok",
        CategoriaImpactoCaja.CuentaBancaria => "chip-violet",
        CategoriaImpactoCaja.AAcreditar => "chip-warn",
        CategoriaImpactoCaja.SinIngresoInmediato => "chip-neutral",
        _ => "chip-neutral"
    };

    /// <summary>Estado de acreditación bancaria del movimiento (lo copia el builder desde la entidad).</summary>
    public EstadoAcreditacionMovimientoCaja? EstadoAcreditacion { get; set; }

    /// <summary>
    /// True solo para estados que el operador necesita ver: dinero todavía no confirmado o
    /// rechazado/revertido. Acreditado y NoAplica/null son el camino normal ⇒ no se muestra chip.
    /// </summary>
    public bool MuestraAcreditacion => EstadoAcreditacion is
        EstadoAcreditacionMovimientoCaja.Pendiente
        or EstadoAcreditacionMovimientoCaja.Rechazado
        or EstadoAcreditacionMovimientoCaja.Revertido
        or EstadoAcreditacionMovimientoCaja.Anulado;

    /// <summary>Etiqueta clara del estado de acreditación, para el chip de la vista (spec 5.3).</summary>
    public string AcreditacionLabel => EstadoAcreditacion switch
    {
        EstadoAcreditacionMovimientoCaja.Pendiente => "Pendiente de acreditación",
        EstadoAcreditacionMovimientoCaja.Acreditado => "Acreditado",
        EstadoAcreditacionMovimientoCaja.Rechazado => "Acreditación rechazada",
        EstadoAcreditacionMovimientoCaja.Revertido => "Revertido",
        EstadoAcreditacionMovimientoCaja.Anulado => "Anulado",
        _ => string.Empty
    };

    /// <summary>Clase de chip asociada al estado de acreditación.</summary>
    public string AcreditacionChipClass => EstadoAcreditacion switch
    {
        EstadoAcreditacionMovimientoCaja.Pendiente => "chip-warn",
        EstadoAcreditacionMovimientoCaja.Rechazado => "chip-bad",
        _ => "chip-neutral"
    };
}

/// <summary>Fila del libro mayor de caja: saldo esperado acumulado fila por fila.</summary>
public class ConciliacionLineaViewModel
{
    public int Orden { get; set; }
    public DateTime FechaHora { get; set; }
    public string TipoLabel { get; set; } = string.Empty;
    public string Concepto { get; set; } = string.Empty;
    public string MedioPago { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    /// <summary>URL de navegación de la referencia (venta / crédito), o null si no es navegable.</summary>
    public string? ReferenciaUrl { get; set; }
    public decimal Entra { get; set; }
    public decimal Sale { get; set; }
    /// <summary>Saldo de caja física esperado tras aplicar esta operación (solo cambia si impacta caja).</summary>
    public decimal SaldoEsperado { get; set; }
    public bool ImpactaCajaFisica { get; set; }
    public bool EsApertura { get; set; }

    /// <summary>Importe base del movimiento, antes de recargo/descuento del medio de pago.</summary>
    public decimal? ImporteBase { get; set; }
    /// <summary>Recargo del medio de pago aplicado (concepto separado del importe base).</summary>
    public decimal? RecargoMedioPago { get; set; }
    /// <summary>Descuento del medio de pago aplicado (concepto separado del importe base).</summary>
    public decimal? DescuentoMedioPago { get; set; }
    /// <summary>True si la fila tiene un recargo o descuento del medio a desglosar.</summary>
    public bool TieneAjusteMedioPago => (RecargoMedioPago ?? 0m) != 0m || (DescuentoMedioPago ?? 0m) != 0m;
}

/// <summary>Evento de auditoría del turno (apertura, movimientos, cierre, anulaciones).</summary>
public class AuditoriaCajaLineaViewModel
{
    public DateTime FechaHora { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public string Entidad { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    public string? Detalle { get; set; }
    public decimal? Monto { get; set; }
}

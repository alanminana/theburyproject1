using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.ViewModels.Punitorio
{
    /// <summary>
    /// Proyección de UI read-only para el panel de punitorios de una cuota. Conserva el orden
    /// entregado por <see cref="PunitorioCuotaDetalleResultado"/> y no expone entidades EF,
    /// snapshots ni tokens de concurrencia.
    /// </summary>
    public sealed class CuotaPunitorioDetalleViewModel
    {
        public int CreditoId { get; init; }
        public int CuotaId { get; init; }
        public int NumeroCuota { get; init; }
        public DateOnly FechaVencimiento { get; init; }
        public DateOnly FechaCalculoComercial { get; init; }
        public bool HistorialCompleto { get; init; }
        public string? MotivoHistorialIncompleto { get; init; }
        public CuotaPunitorioResumenViewModel Resumen { get; init; } = new();
        public IReadOnlyList<PunitorioAplicacionHistorialViewModel> Aplicaciones { get; init; } =
            Array.Empty<PunitorioAplicacionHistorialViewModel>();
        public IReadOnlyList<PagoCuotaHistorialViewModel> Pagos { get; init; } =
            Array.Empty<PagoCuotaHistorialViewModel>();
        public CuotaPunitorioAccionesViewModel Acciones { get; init; } = new();

        public static CuotaPunitorioDetalleViewModel Desde(
            PunitorioCuotaDetalleResultado detalle,
            bool puedeAplicar = false,
            bool puedeAnular = false)
        {
            ArgumentNullException.ThrowIfNull(detalle);

            var aplicacionDeReferencia = detalle.AplicacionActiva ?? detalle.AplicacionesHistoricas.FirstOrDefault();
            var acciones = ConstruirAcciones(detalle, puedeAplicar, puedeAnular, aplicacionDeReferencia);

            return new CuotaPunitorioDetalleViewModel
            {
                CreditoId = detalle.CreditoId,
                CuotaId = detalle.CuotaId,
                NumeroCuota = detalle.NumeroCuota,
                FechaVencimiento = detalle.FechaVencimiento,
                FechaCalculoComercial = detalle.FechaCalculoComercial,
                HistorialCompleto = detalle.HistorialCompleto,
                MotivoHistorialIncompleto = detalle.MotivoHistorialIncompleto,
                Resumen = new CuotaPunitorioResumenViewModel
                {
                    CapitalPendiente = detalle.CapitalPendiente,
                    PunitorioCalculadoHoy = detalle.CalculoActual.ImporteCalculado,
                    PunitorioAplicadoPendiente = detalle.CalculoActual.PunitorioAplicadoPendienteReal,
                    EstadoCalculo = EstadoCalculo(detalle.CalculoActual.Estado),
                    EstadoAplicacion = EstadoAplicacion(aplicacionDeReferencia)
                },
                Aplicaciones = detalle.AplicacionesHistoricas
                    .Select(a => new PunitorioAplicacionHistorialViewModel
                    {
                        FechaAplicacion = a.FechaAplicacion,
                        FechaCalculo = a.FechaCalculo,
                        Estado = EstadoAplicacion(a),
                        ImporteTeorico = a.ImporteTeorico,
                        ImportePreviamenteAplicado = a.ImportePreviamenteAplicado,
                        ImporteNuevoAplicado = a.ImporteNuevoAplicado,
                        ImportePagado = a.ImportePagado,
                        ImportePendiente = a.ImportePendiente,
                        MotivoAplicacion = a.MotivoAplicacion,
                        UsuarioAplicacion = a.UsuarioAplicacion,
                        FechaAnulacion = a.FechaAnulacion,
                        MotivoAnulacion = a.MotivoAnulacion,
                        UsuarioAnulacion = a.UsuarioAnulacion
                    })
                    .ToList(),
                Pagos = detalle.Pagos
                    .Select(p => new PagoCuotaHistorialViewModel
                    {
                        FechaPagoComercial = p.FechaPagoComercial,
                        ImporteTotal = p.ImporteTotal,
                        ImporteAplicadoPunitorio = p.ImporteAplicadoPunitorio,
                        ImporteAplicadoCapital = p.ImporteAplicadoCuota,
                        MedioPago = p.MedioPago,
                        EstadoTexto = EstadoPago(p.Estado),
                        OrigenTexto = OrigenPago(p.Origen),
                        MovimientoCajaId = p.MovimientoCajaId,
                        HistorialCompleto = p.HistorialCompleto,
                        MotivoHistorialIncompleto = p.MotivoHistorialIncompleto
                    })
                    .ToList(),
                Acciones = acciones
            };
        }

        private static CuotaPunitorioAccionesViewModel ConstruirAcciones(
            PunitorioCuotaDetalleResultado detalle,
            bool tienePermisoAplicar,
            bool tienePermisoAnular,
            PunitorioAplicacionDetalle? aplicacionDeReferencia)
        {
            var importePreviamenteAplicado = detalle.AplicacionesHistoricas
                .Where(a => a.Estado != EstadoPunitorioAplicado.Anulado)
                .Sum(a => a.ImporteNuevoAplicado);
            var diferencialEstimado = detalle.CalculoActual.ImporteCalculado.HasValue
                ? detalle.CalculoActual.ImporteCalculado.Value - importePreviamenteAplicado
                : (decimal?)null;
            var motivoBloqueoAplicar = MotivoBloqueoAplicar(detalle, diferencialEstimado);

            return new CuotaPunitorioAccionesViewModel
            {
                TienePermisoAplicar = tienePermisoAplicar,
                TienePermisoAnular = tienePermisoAnular,
                PuedeAplicar = tienePermisoAplicar && motivoBloqueoAplicar is null,
                MotivoBloqueoAplicar = motivoBloqueoAplicar,
                ImportePreviamenteAplicado = importePreviamenteAplicado,
                DiferencialEstimado = diferencialEstimado,
                Aplicar = new AplicarPunitorioHttpViewModel
                {
                    CuotaRowVersionBase64 = detalle.CuotaRowVersionBase64
                },
                Anulacion = aplicacionDeReferencia is null
                    ? null
                    : ConstruirAnulacion(aplicacionDeReferencia, tienePermisoAnular)
            };
        }

        private static string? MotivoBloqueoAplicar(
            PunitorioCuotaDetalleResultado detalle,
            decimal? diferencialEstimado)
        {
            if (detalle.AplicacionActiva is not null)
                return "Ya existe una aplicación activa incompatible. Anulala antes de volver a aplicar.";

            if (!detalle.HistorialCompleto)
                return "El historial está incompleto y no permite aplicar un importe con seguridad.";

            var motivoEstado = detalle.CalculoActual.Estado switch
            {
                EstadoCalculoPunitorioDetalle.SinConfiguracion => "No existe una configuración de punitorios vigente.",
                EstadoCalculoPunitorioDetalle.ConfiguracionInactiva => "La configuración vigente está inactiva.",
                EstadoCalculoPunitorioDetalle.DentroDeGracia => "La cuota todavía está dentro del período de gracia.",
                EstadoCalculoPunitorioDetalle.TasaCero => "La tasa vigente es 0%; no hay importe para aplicar.",
                EstadoCalculoPunitorioDetalle.HistorialIncompleto => "El historial incompleto impide calcular un importe autoritativo.",
                EstadoCalculoPunitorioDetalle.SinSaldo => "La cuota no tiene saldo de capital pendiente.",
                EstadoCalculoPunitorioDetalle.EntradaInvalida => "El cálculo actual no es válido.",
                _ => null
            };
            if (motivoEstado is not null)
                return motivoEstado;

            if (detalle.CalculoActual.Estado != EstadoCalculoPunitorioDetalle.Calculado ||
                !detalle.CalculoActual.ImporteCalculado.HasValue)
            {
                return "El cálculo actual no permite aplicar un punitorio.";
            }

            if (diferencialEstimado is null or <= 0m)
                return "No hay un diferencial nuevo para aplicar.";

            if (string.IsNullOrWhiteSpace(detalle.CuotaRowVersionBase64))
                return "El token de concurrencia no está disponible. Actualizá el panel.";

            return null;
        }

        private static PunitorioAnulacionViewModel ConstruirAnulacion(
            PunitorioAplicacionDetalle aplicacion,
            bool tienePermisoAnular)
        {
            var motivoBloqueo = MotivoBloqueoAnular(aplicacion);
            return new PunitorioAnulacionViewModel
            {
                PunitorioAplicadoId = aplicacion.PunitorioAplicadoId,
                PuedeAnular = tienePermisoAnular && motivoBloqueo is null,
                MotivoBloqueo = motivoBloqueo,
                ImporteAplicado = aplicacion.ImporteAplicado,
                ImportePagado = aplicacion.ImportePagado,
                ImportePendiente = aplicacion.ImportePendiente,
                FechaAplicacion = aplicacion.FechaAplicacion,
                MotivoOriginal = aplicacion.MotivoAplicacion,
                UsuarioOriginal = aplicacion.UsuarioAplicacion,
                Form = new AnularPunitorioHttpViewModel
                {
                    PunitorioAplicadoRowVersionBase64 = aplicacion.RowVersionBase64
                }
            };
        }

        private static string? MotivoBloqueoAnular(PunitorioAplicacionDetalle aplicacion)
        {
            if (aplicacion.Estado == EstadoPunitorioAplicado.Anulado)
                return "La aplicación ya fue anulada.";
            if (aplicacion.Estado == EstadoPunitorioAplicado.Pagado)
                return "La aplicación está pagada y no puede anularse.";
            if (aplicacion.Estado != EstadoPunitorioAplicado.Aplicado || !aplicacion.EsActiva)
                return "La aplicación ya no está activa.";
            if (!aplicacion.ImportePagado.HasValue || !aplicacion.ImportePendiente.HasValue)
                return "El progreso de cobro no es reconstruible; no se puede anular con seguridad.";
            if (aplicacion.ImportePagado.Value > 0m)
                return "La aplicación tiene un pago parcial y no puede anularse.";
            if (aplicacion.ImportePendiente.Value <= 0m)
                return "La aplicación no tiene saldo pendiente para anular.";
            if (string.IsNullOrWhiteSpace(aplicacion.RowVersionBase64))
                return "El token de concurrencia no está disponible. Actualizá el panel.";

            return null;
        }

        private static EstadoPunitorioVisualViewModel EstadoCalculo(EstadoCalculoPunitorioDetalle estado) => estado switch
        {
            EstadoCalculoPunitorioDetalle.Calculado => new("Calculado", "calculate", "chip-info"),
            EstadoCalculoPunitorioDetalle.TasaCero => new("Tasa 0%", "percent", "chip-neutral"),
            EstadoCalculoPunitorioDetalle.SinConfiguracion => new("Sin configuración", "settings_off", "chip-neutral"),
            EstadoCalculoPunitorioDetalle.ConfiguracionInactiva => new("Configuración inactiva", "pause_circle", "chip-warn"),
            EstadoCalculoPunitorioDetalle.DentroDeGracia => new("Dentro de gracia", "schedule", "chip-info"),
            EstadoCalculoPunitorioDetalle.HistorialIncompleto => new("Historial incompleto", "warning", "chip-warn"),
            EstadoCalculoPunitorioDetalle.SinSaldo => new("Sin saldo", "check_circle", "chip-ok"),
            _ => new("No calculable", "error", "chip-bad")
        };

        private static EstadoPunitorioVisualViewModel EstadoAplicacion(PunitorioAplicacionDetalle? aplicacion)
        {
            if (aplicacion is null)
                return new EstadoPunitorioVisualViewModel("Sin aplicación", "remove_circle_outline", "chip-neutral");

            if (aplicacion.Estado == EstadoPunitorioAplicado.Aplicado &&
                aplicacion.ImportePagado is > 0m &&
                aplicacion.ImportePendiente is > 0m)
            {
                return new EstadoPunitorioVisualViewModel(
                    "Aplicado parcialmente pagado", "pending_actions", "chip-warn");
            }

            return aplicacion.Estado switch
            {
                EstadoPunitorioAplicado.Aplicado => new("Aplicado pendiente", "pending", "chip-warn"),
                EstadoPunitorioAplicado.Pagado => new("Pagado", "paid", "chip-ok"),
                EstadoPunitorioAplicado.Anulado => new("Anulado", "block", "chip-bad"),
                EstadoPunitorioAplicado.Revertido => new("Revertido", "undo", "chip-neutral"),
                _ => new("Sin información suficiente", "help", "chip-neutral")
            };
        }

        private static string EstadoPago(EstadoPagoCuota estado) => estado switch
        {
            EstadoPagoCuota.Aplicado => "Aplicado",
            EstadoPagoCuota.Anulado => "Anulado",
            EstadoPagoCuota.Revertido => "Revertido",
            _ => "Sin información suficiente"
        };

        private static string OrigenPago(OrigenPagoCuota origen) => origen switch
        {
            OrigenPagoCuota.RegistradoPorSistema => "Registrado por sistema",
            OrigenPagoCuota.BackfillMovimientoCaja => "Reconstruido desde caja",
            OrigenPagoCuota.BackfillIncompleto => "Histórico incompleto",
            OrigenPagoCuota.Reversion => "Reversión",
            _ => "Sin información suficiente"
        };
    }

    public sealed class CuotaPunitorioResumenViewModel
    {
        public decimal CapitalPendiente { get; init; }
        public decimal? PunitorioCalculadoHoy { get; init; }
        public decimal? PunitorioAplicadoPendiente { get; init; }

        /// <summary>
        /// Total actualmente cobrable: capital pendiente más el punitorio ya aplicado pendiente.
        /// El cálculo informativo no aplicado queda deliberadamente excluido. Si el pendiente
        /// aplicado histórico no es reconstruible, el total tampoco se inventa.
        /// </summary>
        public decimal? TotalCobrableActual => PunitorioAplicadoPendiente.HasValue
            ? CapitalPendiente + PunitorioAplicadoPendiente.Value
            : null;

        public EstadoPunitorioVisualViewModel EstadoCalculo { get; init; } =
            new("Sin configuración", "settings_off", "chip-neutral");
        public EstadoPunitorioVisualViewModel EstadoAplicacion { get; init; } =
            new("Sin aplicación", "remove_circle_outline", "chip-neutral");
    }

    public sealed class PunitorioAplicacionHistorialViewModel
    {
        public DateTime FechaAplicacion { get; init; }
        public DateOnly FechaCalculo { get; init; }
        public EstadoPunitorioVisualViewModel Estado { get; init; } =
            new("Sin información suficiente", "help", "chip-neutral");
        public decimal? ImporteTeorico { get; init; }
        public decimal? ImportePreviamenteAplicado { get; init; }
        public decimal ImporteNuevoAplicado { get; init; }
        public decimal? ImportePagado { get; init; }
        public decimal? ImportePendiente { get; init; }
        public string MotivoAplicacion { get; init; } = string.Empty;
        public string UsuarioAplicacion { get; init; } = string.Empty;
        public DateTime? FechaAnulacion { get; init; }
        public string? MotivoAnulacion { get; init; }
        public string? UsuarioAnulacion { get; init; }
    }

    public sealed class PagoCuotaHistorialViewModel
    {
        public DateOnly FechaPagoComercial { get; init; }
        public decimal ImporteTotal { get; init; }
        public decimal? ImporteAplicadoPunitorio { get; init; }
        public decimal? ImporteAplicadoCapital { get; init; }
        public string? MedioPago { get; init; }
        public string EstadoTexto { get; init; } = string.Empty;
        public string OrigenTexto { get; init; } = string.Empty;
        public int? MovimientoCajaId { get; init; }
        public bool HistorialCompleto { get; init; }
        public string? MotivoHistorialIncompleto { get; init; }
    }

    public sealed class CuotaPunitorioAccionesViewModel
    {
        public bool TienePermisoAplicar { get; init; }
        public bool TienePermisoAnular { get; init; }
        public bool PuedeAplicar { get; init; }
        public string? MotivoBloqueoAplicar { get; init; }
        public decimal ImportePreviamenteAplicado { get; init; }
        public decimal? DiferencialEstimado { get; init; }
        public AplicarPunitorioHttpViewModel Aplicar { get; init; } = new();
        public PunitorioAnulacionViewModel? Anulacion { get; init; }
    }

    public sealed class PunitorioAnulacionViewModel
    {
        public int PunitorioAplicadoId { get; init; }
        public bool PuedeAnular { get; init; }
        public string? MotivoBloqueo { get; init; }
        public decimal ImporteAplicado { get; init; }
        public decimal? ImportePagado { get; init; }
        public decimal? ImportePendiente { get; init; }
        public DateTime FechaAplicacion { get; init; }
        public string MotivoOriginal { get; init; } = string.Empty;
        public string UsuarioOriginal { get; init; } = string.Empty;
        public AnularPunitorioHttpViewModel Form { get; init; } = new();
    }

    public sealed record EstadoPunitorioVisualViewModel(string Texto, string Icono, string CssClass);
}

using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.ViewModels
{
    public class ConfiguracionCreditoVentaViewModel
    {
        [Required]
        public int CreditoId { get; set; }

        public int? VentaId { get; set; }

        /// <summary>
        /// RowVersion vigente de la venta (base64), sólo para el fragmento embebido del
        /// wizard: le permite mantener sincronizado el RowVersion del formulario externo
        /// (#venta-form) cada vez que este configurador modifica la venta (crédito
        /// configurado, contrato generado), evitando un 409 de concurrencia falso en el
        /// guardado final por un RowVersion desactualizado. No participa del POST de
        /// configuración de crédito.
        /// </summary>
        public string? VentaRowVersionBase64 { get; set; }

        [Display(Name = "Cliente")]
        public string ClienteNombre { get; set; } = string.Empty;

        public int ClienteId { get; set; }

        [Display(Name = "Número de Crédito")]
        public string? NumeroCredito { get; set; }

        /// <summary>
        /// LEGADO (ML6) — SIN autoridad sobre el porcentaje financiero. La UI de Configurar Venta
        /// ya no ofrece elegir la fuente (Global/Manual/Cliente/Perfil/Producto); el controller
        /// siempre la fija en <see cref="FuenteConfiguracionCredito.Global"/> en el GET. Se
        /// conserva la propiedad y el binding sólo por compatibilidad: el porcentaje sale siempre
        /// del plan de cuotas resuelto por el servidor (<see cref="CantidadCuotas"/>), nunca de
        /// este campo. <see cref="Services.CreditoConfiguracionVentaService"/> sigue leyéndola
        /// para decisiones no financieras (origen de gastos administrativos por defecto).
        /// </summary>
        [Display(Name = "Fuente de Configuración")]
        public FuenteConfiguracionCredito FuenteConfiguracion { get; set; } = FuenteConfiguracionCredito.Global;

        /// <summary>
        /// LEGADO (ML6) — SIN autoridad sobre el porcentaje financiero. La UI de Configurar Venta
        /// ya no ofrece un selector de método; el controller siempre lo fija en
        /// <see cref="MetodoCalculoCredito.Global"/> en el GET y lo transporta como hidden field
        /// para que el binding requerido (<c>[Required]</c>) del POST siga funcionando. Sigue
        /// influyendo únicamente el rango de cuotas permitido
        /// (<see cref="Services.Interfaces.IConfiguracionPagoService.ResolverRangoCuotasAsync"/>),
        /// nunca el porcentaje: eso sale siempre del plan de cuotas.
        /// </summary>
        [Display(Name = "Método de cálculo")]
        [Required(ErrorMessage = "Debe seleccionar un método de cálculo")]
        public MetodoCalculoCredito? MetodoCalculo { get; set; }

        /// <summary>
        /// LEGADO (ML6) — la UI ya no ofrece seleccionar un perfil de crédito para esta pantalla
        /// (Configurar Venta siempre opera con <see cref="MetodoCalculo"/> = Global). Sin efecto
        /// sobre el porcentaje financiero.
        /// </summary>
        public int? PerfilCreditoSeleccionadoId { get; set; }

        [Display(Name = "Monto del Crédito")]
        public decimal Monto { get; set; }

        /// <summary>
        /// Anticipo opcional. Si vacío, se normaliza a 0 en el backend.
        /// </summary>
        [Display(Name = "Anticipo")]
        [Range(0, double.MaxValue, ErrorMessage = "El anticipo no puede ser negativo")]
        public decimal? Anticipo { get; set; }

        [Display(Name = "Monto financiado")]
        public decimal MontoFinanciado { get; set; }

        [Display(Name = "Cantidad de cuotas")]
        [Range(1, 120, ErrorMessage = "La cantidad de cuotas debe estar entre 1 y 120")]
        public int CantidadCuotas { get; set; } = 1;

        /// <summary>
        /// LEGADO (ML6) — SIN autoridad. Es el porcentaje de recargo total del plan seleccionado,
        /// nunca una tasa mensual (el nombre de la propiedad es histórico). El GET ya no la
        /// precarga (queda null) y la vista la muestra sólo read-only, actualizada por la preview
        /// del servidor (<c>/Credito/SimularPlanVenta</c>) — no hay ningún input editable que la
        /// postee. Se conserva la propiedad por compatibilidad de binding; el POST resuelve el
        /// porcentaje real siempre desde el plan de cuotas, ignorando cualquier valor entrante.
        /// </summary>
        [Display(Name = "Tasa mensual (%)")]
        [Range(0, 100, ErrorMessage = "La tasa no puede ser negativa ni superar el 100%")]
        public decimal? TasaMensual { get; set; }

        /// <summary>
        /// Gastos administrativos opcionales. Si vacío, se normaliza a 0.
        /// </summary>
        [Display(Name = "Gastos administrativos")]
        [Range(0, 1000000, ErrorMessage = "El valor debe ser mayor o igual a 0")]
        public decimal? GastosAdministrativos { get; set; }

        [Display(Name = "Fecha de primera cuota")]
        [DataType(DataType.Date)]
        [Required(ErrorMessage = "Debe indicar la fecha de la primera cuota")]
        public DateTime? FechaPrimeraCuota { get; set; }

        public bool CreditoEstaConfigurado { get; set; }

        /// <summary>
        /// F2 (Micro-lote 6): el operador decide en la configuración cobrar la primera cuota al
        /// confirmar la venta. Solo aplica cuando la primera cuota vence en la fecha comercial actual.
        /// </summary>
        [Display(Name = "Cobrar la primera cuota al confirmar la venta")]
        public bool CobrarPrimeraCuota { get; set; }

        /// <summary>Medio de pago del cobro inmediato de la primera cuota.</summary>
        [Display(Name = "Medio de pago de la primera cuota")]
        [StringLength(30)]
        public string? MedioPagoPrimeraCuota { get; set; }

        public bool ContratoGenerado { get; set; }

        public bool PlantillaActivaDisponible { get; set; }

        public int CuotasMinPermitidas { get; set; } = 1;

        public int CuotasMaxPermitidas { get; set; } = 120;

        public int? MaxCuotasCreditoProducto { get; set; }

        public string? RestriccionCreditoProductoDescripcion { get; set; }

        public int MaxCuotasBase { get; set; } = 120;

        public int? ProductoIdRestrictivo { get; set; }

        public string? ProductoRestrictivoNombre { get; set; }

        public ClienteConfigCreditoVentaViewModel ClienteConfigPersonalizada { get; set; } = new();

        /// <summary>
        /// Atajo de vista: los productos de la venta no comparten ninguna cantidad de cuotas.
        /// </summary>
        public bool SinPlanesCompatibles => ClienteConfigPersonalizada.SinPlanesCompatibles;

        public List<PerfilCreditoActivoViewModel> PerfilesActivos { get; set; } = new();

        public bool PuedeGenerarContrato => VentaId.HasValue && CreditoEstaConfigurado && !ContratoGenerado && PlantillaActivaDisponible;

        /// <summary>
        /// Datos contractuales (Cliente/Crédito/Garante/Plantilla) que
        /// ContratoVentaCreditoService.GenerarAsync exigiría y todavía faltan — poblado sólo
        /// cuando <see cref="PuedeGenerarContrato"/> es true (ver
        /// CreditoController.PoblarFaltantesContratoAsync). Vacía si no falta nada: el botón
        /// real de "Generar contrato" se muestra sólo en ese caso.
        /// </summary>
        public List<string> DatosContractualesFaltantes { get; set; } = new();

        public bool ContratoListoParaGenerar => PuedeGenerarContrato && DatosContractualesFaltantes.Count == 0;
    }

    public class ClienteConfigCreditoVentaViewModel
    {
        public bool TieneTasaPersonalizada { get; set; }
        public decimal? TasaPersonalizada { get; set; }
        public decimal? GastosPersonalizados { get; set; }
        public int? CuotasMaximas { get; set; }
        public int? CuotasMinimas { get; set; }
        public decimal TasaGlobal { get; set; }
        public decimal GastosGlobales { get; set; }
        public bool TienePerfilPreferido { get; set; }
        public int? PerfilPreferidoId { get; set; }
        public string? PerfilNombre { get; set; }
        public decimal? PerfilTasa { get; set; }
        public decimal? PerfilGastos { get; set; }
        public int? PerfilMinCuotas { get; set; }
        public int? PerfilMaxCuotas { get; set; }
        public bool TieneConfiguracionCliente { get; set; }
        public decimal? MontoMinimo { get; set; }
        public decimal? MontoMaximo { get; set; }
        public int? MaxCuotasCreditoProducto { get; set; }
        public string? RestriccionCreditoProductoDescripcion { get; set; }
        public int MaxCuotasBase { get; set; } = 120;
        public int? ProductoIdRestrictivo { get; set; }
        public string? ProductoRestrictivoNombre { get; set; }

        /// <summary>
        /// Planes de cuotas efectivos de la venta: intersección de los productos financiados.
        /// Cuando hay planes, las cuotas seleccionables del camino global surgen de esta lista.
        /// Vacía significa "no rige tabla de planes", NO "sin planes compatibles": para eso está
        /// <see cref="SinPlanesCompatibles"/>.
        /// </summary>
        public IReadOnlyList<PlanCuotaCreditoPersonal> CuotasHabilitadas { get; set; } =
            Array.Empty<PlanCuotaCreditoPersonal>();

        /// <summary>
        /// Los productos de la venta no comparten ninguna cantidad de cuotas: la venta no puede
        /// financiarse con Crédito Personal y el formulario debe bloquear el avance.
        /// </summary>
        public bool SinPlanesCompatibles { get; set; }

        /// <summary>Mensaje funcional que explica por qué no hay planes compatibles.</summary>
        public string? MotivoSinPlanes { get; set; }
    }

    public class PerfilCreditoActivoViewModel
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public decimal TasaMensual { get; set; }
        public decimal GastosAdministrativos { get; set; }
        public int MinCuotas { get; set; }
        public int MaxCuotas { get; set; }
    }
}

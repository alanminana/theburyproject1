using System.ComponentModel.DataAnnotations;
using System.Linq;
using TheBuryProject.Models.Enums;
using TheBuryProject.Validation;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para crear y editar clientes
    /// NOTA: Los checkboxes de documentación fueron eliminados
    /// La documentación se sube a través de la sección de Documentación
    /// </summary>
    public class ClienteViewModel
    {
        private string? _cuilCuit;

        public int Id { get; set; }

        [Required(ErrorMessage = "El tipo de documento es requerido")]
        public string TipoDocumento { get; set; } = "DNI";

        [Required(ErrorMessage = "El número de documento es requerido")]
        [StringLength(20)]
        [DocumentoArgentino(nameof(TipoDocumento))]
        public string NumeroDocumento { get; set; } = string.Empty;

        [Required(ErrorMessage = "El apellido es requerido")]
        [StringLength(100)]
        [SoloLetras(MinLength = 2)]
        public string Apellido { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre es requerido")]
        [StringLength(100)]
        [SoloLetras(MinLength = 2)]
        public string Nombre { get; set; } = string.Empty;

        public string? NombreCompleto { get; set; }
        public int? Edad { get; set; }

        [DataType(DataType.Date)]
        public DateTime? FechaNacimiento { get; set; }

        public string? EstadoCivil { get; set; }

        // CUIL/CUIT dedicado (para consultas BCRA)
        [StringLength(11)]
        [CuilCuitArgentino]
        [CuilCoincideConDni(nameof(NumeroDocumento))]
        [Display(Name = "CUIL/CUIT")]
        public string? CuilCuit
        {
            get => _cuilCuit;
            set => _cuilCuit = NormalizeDigitsOrNull(value);
        }

        // Campos BCRA (solo lectura, se rellenan desde el servicio)
        public int? SituacionCrediticiaBcra { get; set; }
        public string? SituacionCrediticiaDescripcion { get; set; }
        public string? SituacionCrediticiaPeriodo { get; set; }
        public DateTime? SituacionCrediticiaUltimaConsultaUtc { get; set; }
        public bool? SituacionCrediticiaConsultaOk { get; set; }

        // Datos de cónyuge (opcionales)
        [StringLength(200)]
        [SoloLetras(MinLength = 2)]
        public string? ConyugeNombreCompleto { get; set; }

        [StringLength(20)]
        public string? ConyugeTipoDocumento { get; set; }

        [StringLength(20)]
        [DocumentoArgentino(nameof(ConyugeTipoDocumento))]
        public string? ConyugeNumeroDocumento { get; set; }

        [StringLength(20)]
        [TelefonoArgentino]
        public string? ConyugeTelefono { get; set; }

        [Range(0, 999999999.99)]
        public decimal? ConyugeSueldo { get; set; }

        [Required(ErrorMessage = "El teléfono es requerido")]
        [StringLength(20)]
        [TelefonoArgentino]
        public string Telefono { get; set; } = string.Empty;

        [StringLength(20)]
        [TelefonoArgentino]
        public string? TelefonoAlternativo { get; set; }

        [EmailAddress(ErrorMessage = "Email inválido")]
        [StringLength(100)]
        public string? Email { get; set; }

        [Required(ErrorMessage = "El domicilio es requerido")]
        [StringLength(200)]
        public string Domicilio { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Localidad { get; set; }

        [StringLength(100)]
        public string? Provincia { get; set; }

        [StringLength(10)]
        [CodigoPostalArgentino]
        public string? CodigoPostal { get; set; }

        // Datos laborales
        [StringLength(200)]
        public string? Empleador { get; set; }

        [StringLength(100)]
        public string? TipoEmpleo { get; set; }

        [Range(0, 999999999.99)]
        public decimal? Sueldo { get; set; }

        [StringLength(20)]
        [TelefonoArgentino]
        public string? TelefonoLaboral { get; set; }

        /// <summary>
        /// Indica si el cliente presentó recibo de sueldo (compatibilidad con la entidad)
        /// </summary>
        public bool TieneReciboSueldo { get; set; } = false;

        [StringLength(50)]
        public string? TiempoTrabajo { get; set; }

        // Control de riesgo
        
        /// <summary>
        /// Nivel de riesgo crediticio (1-5)
        /// </summary>
        [Display(Name = "Calificación Crediticia")]
        public NivelRiesgoCredito NivelRiesgo { get; set; } = NivelRiesgoCredito.AprobadoCondicional;

        /// <summary>
        /// Puntaje numérico derivado del nivel de riesgo (para compatibilidad)
        /// </summary>
        public decimal PuntajeRiesgo { get; set; } = 6.0m;

        public bool Activo { get; set; } = true;

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Configuración personalizada de crédito
        
        [Display(Name = "Perfil de Crédito Preferido")]
        public int? PerfilCreditoPreferidoId { get; set; }

        [Display(Name = "Tasa de Interés Mensual (%)")]
        [Range(0, 100, ErrorMessage = "La tasa debe estar entre 0% y 100%")]
        public decimal? TasaInteresMensualPersonalizada { get; set; }

        [Display(Name = "Gastos Administrativos (%)")]
        [Range(0, 100, ErrorMessage = "Los gastos deben estar entre 0% y 100%")]
        public decimal? GastosAdministrativosPersonalizados { get; set; }

        [Display(Name = "Cuotas Máximas")]
        [Range(1, 120, ErrorMessage = "Las cuotas deben estar entre 1 y 120")]
        public int? CuotasMaximasPersonalizadas { get; set; }

        [Display(Name = "Monto Mínimo ($)")]
        [Range(0, 9999999999.99, ErrorMessage = "El monto mínimo debe ser positivo")]
        public decimal? MontoMinimoPersonalizado { get; set; }

        [Display(Name = "Monto Máximo ($)")]
        [Range(0, 9999999999.99, ErrorMessage = "El monto máximo debe ser positivo")]
        public decimal? MontoMaximoPersonalizado { get; set; }

        // Garante
        public int? GaranteId { get; set; }

        // Historial crediticio (solo lectura)
        public int CreditosTotales { get; set; }
        public int CreditosActivos { get; set; }
        public int CuotasImpagas { get; set; }
        public decimal? MontoAdeudado { get; set; }

        // Scoring de comportamiento (solo lectura, se rellena/recalcula desde el servicio)
        public int PuntajeCliente { get; set; } = 1;
        public int AntiguedadDias { get; set; }
        public DateTime? UltimaVentaFecha { get; set; }
        public int CantidadComprasCliente { get; set; }
        public int CreditosEnTermino { get; set; }
        public int CreditosConAtraso { get; set; }

        /// <summary>True si existe historial de repago evaluable. Derivado, no se mapea.</summary>
        public bool TieneHistorialCredito => CreditosEnTermino > 0 || CreditosConAtraso > 0;

        /// <summary>True si tiene historial y no registra créditos con atraso. Derivado, no se mapea.</summary>
        public bool PagaCreditosEnTermino => TieneHistorialCredito && CreditosConAtraso == 0;

        // ALIASES PARA COMPATIBILIDAD CON CREATE.CSHTML
        // (que usa LugarTrabajo, IngresoMensual, TelefonoTrabajo)
        public string? LugarTrabajo 
        { 
            get => Empleador; 
            set => Empleador = value; 
        }

        public decimal? IngresoMensual 
        { 
            get => Sueldo; 
            set => Sueldo = value; 
        }

        public string? TelefonoTrabajo 
        { 
            get => TelefonoLaboral; 
            set => TelefonoLaboral = value; 
        }

        // Alias para Domicilio
        public string? Direccion => Domicilio;

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        private static string? NormalizeDigitsOrNull(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var digits = new string(value.Where(char.IsDigit).ToArray());
            return string.IsNullOrWhiteSpace(digits) ? null : digits;
        }
    }
}

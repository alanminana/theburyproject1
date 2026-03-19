using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa un cliente del sistema que puede solicitar créditos.
    /// La documentación principal se gestiona en la tabla DocumentoCliente.
    /// Además existen flags históricos (servicios/impuesto/veraz) que se persisten por compatibilidad.
    /// </summary>
    public class Cliente : AuditableEntity
    {
        // Datos Personales
        [Required]
        [StringLength(20)]
        public string TipoDocumento { get; set; } = "DNI"; // DNI, CUIL, CUIT

        [Required]
        [StringLength(20)]
        public string NumeroDocumento { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Apellido { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(200)]
        public string? NombreCompleto { get; set; }

        public DateTime? FechaNacimiento { get; set; }

        [StringLength(50)]
        public string? EstadoCivil { get; set; } // Soltero, Casado, Divorciado, Viudo

        // Datos de Cónyuge (opcionales)
        [StringLength(200)]
        public string? ConyugeNombreCompleto { get; set; }

        [StringLength(20)]
        public string? ConyugeTipoDocumento { get; set; } // DNI, CUIL, CUIT

        [StringLength(20)]
        public string? ConyugeNumeroDocumento { get; set; }

        [StringLength(20)]
        public string? ConyugeTelefono { get; set; }

        public decimal? ConyugeSueldo { get; set; }

        // Datos de Contacto
        [Required]
        [StringLength(20)]
        public string Telefono { get; set; } = string.Empty;

        [StringLength(20)]
        public string? TelefonoAlternativo { get; set; }

        [EmailAddress]
        [StringLength(100)]
        public string? Email { get; set; }

        // Domicilio (columna real en DB)
        [Required]
        [StringLength(200)]
        public string Domicilio { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Localidad { get; set; }

        [StringLength(100)]
        public string? Provincia { get; set; }

        [StringLength(10)]
        public string? CodigoPostal { get; set; }

        // Datos Laborales
        [StringLength(200)]
        public string? Empleador { get; set; }

        [StringLength(100)]
        public string? TipoEmpleo { get; set; } // Relación de dependencia, Autónomo, Monotributista

        // Columna real en DB (según tu AppDbContext y logs)
        public decimal? Sueldo { get; set; }

        [StringLength(20)]
        public string? TelefonoLaboral { get; set; }

        /// <summary>
        /// Esta columna SÍ existe en la base y se sigue usando.
        /// </summary>
        public bool TieneReciboSueldo { get; set; } = false;

        // Control de Riesgo
        /// <summary>
        /// Nivel de riesgo crediticio (1-5). 
        /// 1=Rechazado, 2=Rechazado(Revisar), 3=Aprobado Condicional, 4=Aprobado Limitado, 5=Aprobado Total
        /// </summary>
        public NivelRiesgoCredito NivelRiesgo { get; set; } = NivelRiesgoCredito.AprobadoCondicional;

        /// <summary>
        /// Puntaje de riesgo numérico para cálculos y compatibilidad (derivado del NivelRiesgo * 2).
        /// Rango efectivo: 2, 4, 6, 8, 10
        /// </summary>
        public decimal PuntajeRiesgo { get; set; } = 6.0m; // Valor por defecto = NivelRiesgo.AprobadoCondicional (3) * 2

        // Aptitud Crediticia (semáforo)
        /// <summary>
        /// Estado de aptitud crediticia actual: Apto, NoApto, RequiereAutorizacion
        /// Se actualiza automáticamente al evaluar al cliente.
        /// </summary>
        public EstadoCrediticioCliente EstadoCrediticio { get; set; } = EstadoCrediticioCliente.NoEvaluado;

        /// <summary>
        /// Límite de crédito asignado manualmente al cliente (cupo máximo).
        /// Si es null, no tiene límite asignado y debe configurarse.
        /// </summary>
        public decimal? LimiteCredito { get; set; }

        /// <summary>
        /// Motivo descriptivo si el cliente no está apto para crédito.
        /// </summary>
        [StringLength(500)]
        public string? MotivoNoApto { get; set; }

        /// <summary>
        /// Fecha de la última evaluación de aptitud crediticia.
        /// </summary>
        public DateTime? FechaUltimaEvaluacion { get; set; }

        // ==========================
        // Configuración de Crédito Personal (valores personalizados por cliente)
        // ==========================

        // ==========================
        // CONFIGURACIÓN DE CRÉDITO PERSONAL (TAREA 6 + TAREA 8)
        // ==========================

        /// <summary>
        /// Perfil de crédito preferido para este cliente (TAREA 8.1.1)
        /// Si se especifica, se usarán los valores del perfil como defaults
        /// </summary>
        public int? PerfilCreditoPreferidoId { get; set; }

        /// <summary>
        /// Navegación al perfil de crédito preferido
        /// </summary>
        public virtual PerfilCredito? PerfilCreditoPreferido { get; set; }

        /// <summary>
        /// Tasa de interés mensual personalizada para este cliente (%). 
        /// Si es null, se usa la tasa del perfil preferido o la global del sistema.
        /// </summary>
        public decimal? TasaInteresMensualPersonalizada { get; set; }

        /// <summary>
        /// Porcentaje de gastos administrativos personalizados para este cliente (%).
        /// Si es null, se usan los gastos del perfil preferido o los globales del sistema.
        /// </summary>
        public decimal? GastosAdministrativosPersonalizados { get; set; }

        /// <summary>
        /// Cantidad máxima de cuotas permitidas para este cliente.
        /// Si es null, se usa el límite del perfil preferido o el global del sistema (generalmente 24).
        /// </summary>
        public int? CuotasMaximasPersonalizadas { get; set; }

        /// <summary>
        /// Monto mínimo de crédito permitido para este cliente.
        /// Si es null, se usa el mínimo global del sistema.
        /// </summary>
        public decimal? MontoMinimoPersonalizado { get; set; }

        /// <summary>
        /// Monto máximo de crédito permitido para este cliente.
        /// Si es null, se usa LimiteCredito o el máximo global.
        /// </summary>
        public decimal? MontoMaximoPersonalizado { get; set; }

        // CUIL/CUIT dedicado (para consultas BCRA)
        [StringLength(11)]
        [RegularExpression(@"^\d{11}$", ErrorMessage = "El CUIL/CUIT debe tener exactamente 11 dígitos numéricos")]
        public string? CuilCuit { get; set; }

        // Cache de consulta BCRA Central de Deudores
        public int? SituacionCrediticiaBcra { get; set; }

        [StringLength(100)]
        public string? SituacionCrediticiaDescripcion { get; set; }

        [StringLength(10)]
        public string? SituacionCrediticiaPeriodo { get; set; }

        public DateTime? SituacionCrediticiaUltimaConsultaUtc { get; set; }

        public bool? SituacionCrediticiaConsultaOk { get; set; }

        // Estado
        public bool Activo { get; set; } = true;

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // ==========================
        // NO persistidos (evitan Invalid column name)
        // ==========================

        /// <summary>
        /// Alias/compatibilidad (tu DB no tiene columna 'Direccion').
        /// Usa Domicilio como fuente.
        /// </summary>
        [NotMapped]
        public string? Direccion
        {
            get => Domicilio;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                    Domicilio = value;
            }
        }

        /// <summary>
        /// Tu DB no tiene columna 'IngresosMensuales'.
        /// Si querés compatibilidad, lo tratamos como alias de Sueldo.
        /// </summary>
        [NotMapped]
        public decimal? IngresosMensuales
        {
            get => Sueldo;
            set => Sueldo = value;
        }

        // Flags de documentación y servicios (mapeados en la base según migraciones).
        public bool TieneImpuesto { get; set; } = false;
        public bool TieneServicioAgua { get; set; } = false;
        public bool TieneServicioGas { get; set; } = false;
        public bool TieneServicioLuz { get; set; } = false;
        public bool TieneVeraz { get; set; } = false;

        // Garante asociado (si el cliente tiene un garante asignado)
        public int? GaranteId { get; set; }

        // Navigation Properties
        public virtual Garante? Garante { get; set; }
        public virtual ICollection<Credito> Creditos { get; set; } = new List<Credito>();
        public virtual ICollection<Garante> ComoGarante { get; set; } = new List<Garante>();
        public virtual ICollection<DocumentoCliente> Documentos { get; set; } = new List<DocumentoCliente>();
        public virtual ClienteCreditoConfiguracion? CreditoConfiguracion { get; set; }
        public virtual ICollection<ClientePuntajeHistorial> PuntajeHistorial { get; set; } = new List<ClientePuntajeHistorial>();

    }
}

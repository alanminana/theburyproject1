using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Define el método de cálculo para configurar un crédito personal
    /// TAREA 9: Reemplaza FuenteConfiguracionCredito con opciones más claras
    /// </summary>
    public enum MetodoCalculoCredito
    {
        /// <summary>
        /// Usa la mejor opción disponible automáticamente:
        /// 1. Configuración personalizada del cliente
        /// 2. Perfil preferido del cliente
        /// 3. Defaults globales del sistema
        /// </summary>
        [Display(Name = "🤖 Automático (Por Cliente)")]
        AutomaticoPorCliente = 0,

        /// <summary>
        /// Usa valores de un perfil de crédito específico
        /// El operador selecciona qué perfil aplicar
        /// </summary>
        [Display(Name = "📋 Usar Perfil")]
        UsarPerfil = 1,

        /// <summary>
        /// Usa configuración personalizada específica del cliente
        /// Solo disponible si el cliente tiene valores configurados
        /// </summary>
        [Display(Name = "👤 Usar Cliente")]
        UsarCliente = 2,

        /// <summary>
        /// Usa valores globales por defecto del sistema
        /// </summary>
        [Display(Name = "🌍 Global (Sistema)")]
        Global = 3,

        /// <summary>
        /// Permite edición manual completa sin precarga
        /// Habilita edición total de todos los campos
        /// </summary>
        [Display(Name = "✏️ Manual")]
        Manual = 4
    }
}

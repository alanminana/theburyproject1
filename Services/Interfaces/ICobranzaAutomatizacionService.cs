using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio de automatización de cobranza por tramos.
    /// - Basado en configuración
    /// - Ejecuta acciones según días de atraso
    /// - Sin lógica en controladores
    /// </summary>
    public interface ICobranzaAutomatizacionService
    {
        /// <summary>
        /// Genera los tramos de cobranza basados en la configuración actual.
        /// </summary>
        /// <param name="config">Configuración de mora</param>
        /// <returns>Lista de tramos ordenados por días</returns>
        IReadOnlyList<TramoCobranza> GenerarTramos(ConfiguracionMora config);

        /// <summary>
        /// Determina el tramo aplicable para una cantidad de días de atraso.
        /// </summary>
        /// <param name="diasAtraso">Días de atraso de la cuota/alerta</param>
        /// <param name="tramos">Tramos configurados</param>
        /// <returns>Tramo aplicable o null si no hay ninguno</returns>
        TramoCobranza? ObtenerTramo(int diasAtraso, IReadOnlyList<TramoCobranza> tramos);

        /// <summary>
        /// Determina las acciones pendientes para una alerta según su tramo.
        /// Función pura: no modifica datos.
        /// </summary>
        /// <param name="alerta">Alerta a evaluar</param>
        /// <param name="config">Configuración de mora</param>
        /// <param name="fechaCalculo">Fecha de referencia (default: hoy)</param>
        /// <returns>Lista de acciones a ejecutar</returns>
        IReadOnlyList<AccionAutomatica> DeterminarAcciones(
            AlertaCobranza alerta,
            ConfiguracionMora config,
            DateTime? fechaCalculo = null);

        /// <summary>
        /// Procesa todas las alertas activas ejecutando acciones por tramos.
        /// Este método SÍ modifica datos (no es puro).
        /// </summary>
        /// <param name="alertasActivas">Alertas a procesar</param>
        /// <param name="config">Configuración de mora</param>
        /// <param name="fechaCalculo">Fecha de referencia (default: hoy)</param>
        /// <returns>Resultado del procesamiento</returns>
        Task<ResultadoProcesamientoTramos> ProcesarAlertasAsync(
            IEnumerable<AlertaCobranza> alertasActivas,
            ConfiguracionMora config,
            DateTime? fechaCalculo = null);

        /// <summary>
        /// Escala la prioridad de una alerta si corresponde según configuración.
        /// </summary>
        /// <param name="alerta">Alerta a evaluar</param>
        /// <param name="config">Configuración</param>
        /// <returns>Nueva prioridad (o la actual si no debe cambiar)</returns>
        PrioridadAlerta CalcularPrioridad(AlertaCobranza alerta, ConfiguracionMora config);

        /// <summary>
        /// Determina si un cliente debe ser bloqueado según configuración.
        /// </summary>
        /// <param name="diasAtraso">Días de atraso</param>
        /// <param name="cuotasVencidas">Cantidad de cuotas vencidas</param>
        /// <param name="montoMora">Monto total de mora</param>
        /// <param name="config">Configuración</param>
        /// <returns>True si debe bloquearse</returns>
        bool DebeBloquearCliente(
            int diasAtraso,
            int cuotasVencidas,
            decimal montoMora,
            ConfiguracionMora config);
    }
}

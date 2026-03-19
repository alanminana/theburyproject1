using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Motor de cálculo de mora.
    /// - Idempotente: mismo input + fecha = mismo output
    /// - Puro: no modifica entidades ni base de datos
    /// - Sin notificaciones: solo cálculo
    /// - Basado en configuración: usa ConfiguracionMora
    /// </summary>
    public interface ICalculoMoraService
    {
        /// <summary>
        /// Calcula la mora para una cuota específica.
        /// </summary>
        /// <param name="cuota">Cuota a evaluar</param>
        /// <param name="configuracion">Configuración de mora a aplicar</param>
        /// <param name="fechaCalculo">Fecha de referencia para el cálculo (default: hoy)</param>
        /// <returns>Resultado con detalle de la mora calculada</returns>
        CalculoMoraResult CalcularMoraCuota(
            Cuota cuota,
            ConfiguracionMora configuracion,
            DateTime? fechaCalculo = null);

        /// <summary>
        /// Calcula la mora para múltiples cuotas.
        /// </summary>
        /// <param name="cuotas">Lista de cuotas a evaluar</param>
        /// <param name="configuracion">Configuración de mora a aplicar</param>
        /// <param name="fechaCalculo">Fecha de referencia para el cálculo (default: hoy)</param>
        /// <returns>Resultado consolidado con detalle por cuota</returns>
        CalculoMoraResult CalcularMoraCuotas(
            IEnumerable<Cuota> cuotas,
            ConfiguracionMora configuracion,
            DateTime? fechaCalculo = null);

        /// <summary>
        /// Calcula la mora acumulada de un crédito (todas sus cuotas vencidas).
        /// </summary>
        /// <param name="credito">Crédito con sus cuotas cargadas</param>
        /// <param name="configuracion">Configuración de mora a aplicar</param>
        /// <param name="fechaCalculo">Fecha de referencia para el cálculo (default: hoy)</param>
        /// <returns>Resultado consolidado del crédito</returns>
        CalculoMoraResult CalcularMoraCredito(
            Credito credito,
            ConfiguracionMora configuracion,
            DateTime? fechaCalculo = null);

        /// <summary>
        /// Determina si una cuota está vencida considerando días de gracia.
        /// </summary>
        /// <param name="cuota">Cuota a evaluar</param>
        /// <param name="diasGracia">Días de gracia configurados</param>
        /// <param name="fechaCalculo">Fecha de referencia (default: hoy)</param>
        /// <returns>True si la cuota está vencida</returns>
        bool EstaVencida(Cuota cuota, int diasGracia, DateTime? fechaCalculo = null);

        /// <summary>
        /// Calcula los días de atraso efectivos (descontando días de gracia).
        /// </summary>
        /// <param name="fechaVencimiento">Fecha de vencimiento de la cuota</param>
        /// <param name="diasGracia">Días de gracia configurados</param>
        /// <param name="fechaCalculo">Fecha de referencia (default: hoy)</param>
        /// <returns>Días de atraso efectivos (mínimo 0)</returns>
        int CalcularDiasAtrasoEfectivos(
            DateTime fechaVencimiento,
            int diasGracia,
            DateTime? fechaCalculo = null);
    }
}

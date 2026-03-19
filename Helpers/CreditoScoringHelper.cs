using TheBuryProject.ViewModels;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Contiene m�todos para c�lculo de score crediticio
    /// </summary>
    public static class CreditoScoringHelper
    {
        private const int SCORE_BASE = 500;
        private const int SCORE_MIN = 300;
        private const int SCORE_MAX = 850;
        private const int PUNTOS_POR_DOCUMENTO = 50;
        private const int PUNTOS_ANTIGUEDAD_ANIO = 100;
        private const int PUNTOS_ANTIGUEDAD_MES = 50;
        private const decimal FACTOR_ENDEUDAMIENTO = 5m;

        /// <summary>
        /// Calcula un score crediticio simplificado
        /// </summary>
        public static int CalcularScoreCrediticio(ClienteDetalleViewModel modelo)
        {
            int score = SCORE_BASE;

            // Puntos por documentaci�n verificada
            score += AnadirPuntosDocumentacion(modelo);

            // Penalizaci�n por endeudamiento
            score -= ObtenerPenalizacionEndeudamiento(modelo);

            // Bonificaci�n por antig�edad laboral
            score += ObtenerBonificacionAntiguedad(modelo);

            return Math.Max(SCORE_MIN, Math.Min(SCORE_MAX, score));
        }

        /// <summary>
        /// Calcula puntos adicionales por documentaci�n verificada
        /// </summary>
        private static int AnadirPuntosDocumentacion(ClienteDetalleViewModel modelo)
        {
            return modelo.Documentos.Count(d => d.Estado == EstadoDocumento.Verificado) * PUNTOS_POR_DOCUMENTO;
        }

        /// <summary>
        /// Obtiene la penalizaci�n por endeudamiento alto
        /// </summary>
        private static int ObtenerPenalizacionEndeudamiento(ClienteDetalleViewModel modelo)
        {
            if (!modelo.CreditosActivos.Any() || modelo.Cliente.IngresoMensual == null || modelo.Cliente.IngresoMensual <= 0)
                return 0;

            var cuotaMensualActual = modelo.CreditosActivos
                .Where(c => c.CantidadCuotas > 0)
                .Sum(c => c.MontoTotal / c.CantidadCuotas);
            var endeudamiento = (cuotaMensualActual / modelo.Cliente.IngresoMensual.Value) * 100;

            if (endeudamiento > 40)
                return (int)((endeudamiento - 40) * FACTOR_ENDEUDAMIENTO);

            return 0;
        }

        /// <summary>
        /// Obtiene bonificaci�n por antig�edad laboral
        /// </summary>
        private static int ObtenerBonificacionAntiguedad(ClienteDetalleViewModel modelo)
        {
            if (string.IsNullOrEmpty(modelo.Cliente.TiempoTrabajo))
                return 0;

            if (modelo.Cliente.TiempoTrabajo.Contains("año"))
                return PUNTOS_ANTIGUEDAD_ANIO;

            if (modelo.Cliente.TiempoTrabajo.Contains("mes"))
                return PUNTOS_ANTIGUEDAD_MES;

            return 0;
        }
    }
}
using TheBuryProject.ViewModels;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Contiene m�todos para evaluaci�n de capacidad crediticia
    /// </summary>
    public static class EvaluacionCrediticiaHelper
    {
        private const decimal CAPACIDAD_PAGO_PORCENTAJE = 0.30m;
        private const int CUOTAS_ESTIMADAS = 12;
        private const double PORCENTAJE_ENDEUDAMIENTO_ALTO = 40.0;
        private const double PORCENTAJE_ENDEUDAMIENTO_CRITICO = 60.0;
        private const int SCORE_BAJO = 500;
        private const int SCORE_MINIMO_REQUISITOS = 400;
        private const double PORCENTAJE_MAXIMO_REQUISITOS = 50.0;

        /// <summary>
        /// Calcula la capacidad financiera del cliente
        /// </summary>
        public static void CalcularCapacidadFinanciera(
            EvaluacionCreditoResult evaluacion,
            ClienteDetalleViewModel modelo)
        {
            var creditosVigentes = modelo.CreditosActivos
                .Where(c => c.Estado == EstadoCredito.Activo || c.Estado == EstadoCredito.Aprobado)
                .ToList();

            evaluacion.IngresosMensuales = modelo.Cliente.IngresoMensual ?? 0;
            evaluacion.DeudaActual = creditosVigentes.Sum(c => c.SaldoPendiente);
            evaluacion.CapacidadPagoMensual = evaluacion.IngresosMensuales * CAPACIDAD_PAGO_PORCENTAJE;

            if (evaluacion.IngresosMensuales > 0)
            {
                var cuotaMensualActual = creditosVigentes
                    .Where(c => c.CantidadCuotas > 0)
                    .Sum(c => c.MontoTotal / c.CantidadCuotas);
                evaluacion.PorcentajeEndeudamiento = (double)(cuotaMensualActual / evaluacion.IngresosMensuales * 100);
            }

            evaluacion.MontoMaximoDisponible = evaluacion.CapacidadPagoMensual * CUOTAS_ESTIMADAS;
        }

        /// <summary>
        /// Determina si requiere garante seg�n criterios
        /// </summary>
        public static bool DeterminarRequiereGarante(EvaluacionCreditoResult evaluacion)
        {
            return evaluacion.PorcentajeEndeudamiento > PORCENTAJE_ENDEUDAMIENTO_ALTO ||
                   evaluacion.ScoreCrediticio < SCORE_BAJO ||
                   !evaluacion.TieneDocumentosCompletos;
        }

        /// <summary>
        /// Determina si puede aprobarse con excepci�n
        /// </summary>
        public static bool DeterminarPuedeAprobarConExcepcion(EvaluacionCreditoResult evaluacion)
        {
            return !evaluacion.CumpleRequisitos &&
                   evaluacion.IngresosMensuales > 0 &&
                   evaluacion.PorcentajeEndeudamiento < PORCENTAJE_ENDEUDAMIENTO_CRITICO;
        }

        /// <summary>
        /// Verifica si cumple todos los requisitos m�nimos
        /// </summary>
        public static bool VerificaCumplimientoRequisitos(EvaluacionCreditoResult evaluacion)
        {
            return evaluacion.TieneDocumentosCompletos &&
                   evaluacion.PorcentajeEndeudamiento < PORCENTAJE_MAXIMO_REQUISITOS &&
                   evaluacion.ScoreCrediticio >= SCORE_MINIMO_REQUISITOS &&
                   (!evaluacion.RequiereGarante || evaluacion.TieneGarante);
        }
    }
}
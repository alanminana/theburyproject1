using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para mostrar resultado de evaluación crediticia
    /// </summary>
    public class EvaluacionCreditoViewModel
    {
        public int Id { get; set; }
        public int CreditoId { get; set; }
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;

        // Resultado General
        public ResultadoEvaluacion Resultado { get; set; }
        public decimal PuntajeFinal { get; set; } // 0-100
        public DateTime FechaEvaluacion { get; set; }

        // Datos de Entrada
        public decimal MontoSolicitado { get; set; }
        public decimal PuntajeRiesgoCliente { get; set; }
        public decimal? SueldoCliente { get; set; }
        public decimal? RelacionCuotaIngreso { get; set; }

        // Evaluaciones Individuales
        public List<ReglaEvaluacionViewModel> Reglas { get; set; } = new();

        // Resúmenes Booleanos
        public bool TieneDocumentacionCompleta { get; set; }
        public bool TieneIngresosSuficientes { get; set; }
        public bool TieneBuenHistorial { get; set; }
        public bool TieneGarante { get; set; }

        // Explicaciones
        public string? Motivo { get; set; }
        public string? Observaciones { get; set; }

        // Propiedades Calculadas para UI
        public string ResultadoTexto => Resultado switch
        {
            ResultadoEvaluacion.Aprobado => "Aprobado",
            ResultadoEvaluacion.RequiereAnalisis => "Requiere Análisis",
            ResultadoEvaluacion.Rechazado => "Rechazado",
            _ => "Desconocido"
        };

        public string ColorSemaforo => Resultado switch
        {
            ResultadoEvaluacion.Aprobado => "success",
            ResultadoEvaluacion.RequiereAnalisis => "warning",
            ResultadoEvaluacion.Rechazado => "danger",
            _ => "secondary"
        };

        public string IconoSemaforo => Resultado switch
        {
            ResultadoEvaluacion.Aprobado => "bi-check-circle-fill",
            ResultadoEvaluacion.RequiereAnalisis => "bi-exclamation-triangle-fill",
            ResultadoEvaluacion.Rechazado => "bi-x-circle-fill",
            _ => "bi-question-circle"
        };
    }

   
}

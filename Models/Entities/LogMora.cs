using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Registro de ejecuciones del proceso de mora
    /// </summary>
    public class LogMora  : AuditableEntity
    {
        public DateTime FechaEjecucion { get; set; }
        public int CuotasProcesadas { get; set; }
        public int AlertasGeneradas { get; set; }
        public bool Exitoso { get; set; }
        public string? Mensaje { get; set; }
        public string? DetalleError { get; set; }

        // Propiedades adicionales para estadísticas
        public int CuotasConMora { get; set; }
        public decimal TotalMora { get; set; }
        public decimal TotalRecargosAplicados { get; set; }
        public int Errores { get; set; }
        public TimeSpan DuracionEjecucion { get; set; }
    }
}

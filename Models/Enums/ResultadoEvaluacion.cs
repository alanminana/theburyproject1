namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Resultado de la evaluación crediticia (semáforo)
    /// </summary>
    public enum ResultadoEvaluacion
    {
        Rechazado = 0,      // Rojo - No se debe aprobar
        RequiereAnalisis = 1, // Amarillo - Revisar manualmente
        Aprobado = 2        // Verde - Aprobar automáticamente
    }
}
namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Estados de verificación de un documento
    /// </summary>
    public enum EstadoDocumento
    {
        Pendiente = 1,      // Subido, esperando revisión
        Verificado = 2,     // Revisado y aprobado
        Rechazado = 3,      // Revisado y rechazado
        Vencido = 4         // Documento expirado
    }
}
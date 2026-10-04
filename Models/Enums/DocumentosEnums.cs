namespace TheBuryProject.Models.Enums
{
    /// <summary>Estado de un documento emitido por el motor documental.</summary>
    public enum EstadoDocumentoGenerado
    {
        /// <summary>Emitido, no requiere firma o ya no la espera.</summary>
        Generado = 1,
        PendienteFirma = 2,
        Firmado = 3,
        /// <summary>Anulado por un usuario (queda como histórico, con motivo).</summary>
        Cancelado = 4,
        /// <summary>Reemplazado por una regeneración explícita (el original queda como histórico).</summary>
        Reemplazado = 5
    }

    public enum CategoriaDocumento
    {
        Comercial = 1,
        Legal = 2,
        Financiero = 3,
        Logistico = 4,
        Otro = 9
    }
}

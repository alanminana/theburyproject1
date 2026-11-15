namespace TheBuryProject.Models.Enums
{
    /// <summary>Tipo de referencia informativa de un cliente.</summary>
    public enum TipoReferenciaCliente
    {
        Personal = 1,
        Laboral = 2,
        Comercial = 3
    }

    /// <summary>Resultado de la verificación de una referencia (registro administrativo).</summary>
    public enum EstadoVerificacionReferencia
    {
        Pendiente = 1,
        Verificada = 2,
        NoLocalizada = 3,
        Rechazada = 4
    }
}

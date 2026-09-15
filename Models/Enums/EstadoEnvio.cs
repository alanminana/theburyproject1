namespace TheBuryProject.Models.Enums
{
    public enum EstadoEnvio
    {
        Pendiente = 0,      // Registrado, todavía no se empezó a preparar
        Preparando = 1,     // En preparación / embalaje
        Despachado = 2,     // Salió del depósito hacia el transportista
        EnCamino = 3,       // En tránsito hacia el cliente
        Entregado = 4,      // Entregado al cliente
        Fallido = 5,        // Intento de entrega fallido (requiere MotivoNoEntrega)
        Cancelado = 6       // Envío cancelado
    }
}

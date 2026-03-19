namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Estados de una orden de compra
    /// </summary>
    public enum EstadoOrdenCompra
    {
        Borrador = 0,
        Enviada = 1,
        Confirmada = 2,
        EnTransito = 3,
        Recibida = 4,
        Cancelada = 5
    }
}

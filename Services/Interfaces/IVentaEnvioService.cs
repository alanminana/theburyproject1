using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Resultado de un intento de cambio de estado de envío: nunca lanza para una
    /// transición inválida, la rechaza de forma controlada (fail-closed).
    /// </summary>
    public class CambiarEstadoEnvioResultado
    {
        public bool Exitoso { get; set; }
        public string? Error { get; set; }
        public VentaEnvio? Envio { get; set; }

        public static CambiarEstadoEnvioResultado Ok(VentaEnvio envio) =>
            new() { Exitoso = true, Envio = envio };

        public static CambiarEstadoEnvioResultado Fallido(string error) =>
            new() { Exitoso = false, Error = error };
    }

    /// <summary>
    /// Gestión del envío 1:1 de una venta: alta/baja/edición de los datos de entrega
    /// (invocado desde VentaService al crear/editar la venta) y transiciones de estado
    /// (invocado desde VentaController). El costo de envío es informativo: este servicio
    /// nunca toca Venta.Total, caja, stock ni crédito.
    /// </summary>
    public interface IVentaEnvioService
    {
        Task<VentaEnvio?> GetByVentaIdAsync(int ventaId);

        /// <summary>
        /// Envíos que todavía no llegaron a un estado terminal (Entregado/Cancelado),
        /// para la pestaña "Envíos pendientes" del Centro de Ventas.
        /// </summary>
        Task<List<VentaEnvio>> GetPendientesAsync();

        Task<CambiarEstadoEnvioResultado> CambiarEstadoAsync(
            int ventaId,
            EstadoEnvio nuevoEstado,
            string? motivo,
            string? usuario);

        /// <summary>
        /// True si nuevoEstado es alcanzable desde estadoActual según la máquina de
        /// estados del envío. No escribe nada, sólo consulta la regla.
        /// </summary>
        bool EsTransicionValida(EstadoEnvio estadoActual, EstadoEnvio nuevoEstado);
    }
}

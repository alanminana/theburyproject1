using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>Precio global vigente de un servicio de venta (envío o armado).</summary>
    public sealed record ServicioVentaPrecioInfo(TipoServicioVenta Tipo, decimal Precio, bool Activo);

    /// <summary>Comando de alta/edición del precio global de un servicio.</summary>
    public sealed record ServicioVentaPrecioComando(TipoServicioVenta Tipo, decimal Precio, bool Activo);

    /// <summary>
    /// Administración y consulta de los precios fijos globales de Envío Ciudad/Rural y Armado N.º 1..6.
    /// </summary>
    public interface IServicioVentaPrecioService
    {
        /// <summary>Siempre devuelve los 8 servicios (los que no tienen fila valen 0, activos).</summary>
        Task<IReadOnlyList<ServicioVentaPrecioInfo>> ListarAsync();

        /// <summary>Upsert de los precios enviados. Rechaza tipos inválidos y precios negativos.</summary>
        Task GuardarAsync(IEnumerable<ServicioVentaPrecioComando> comandos);
    }
}

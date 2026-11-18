using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// D-03 Tesorería administrativa: catálogo configurable de orígenes de fondos y resumen de egresos por origen. Separada de la Caja de
    /// ventas: no abre ni cierra cajas, no registra movimientos de Caja y no calcula saldos (solo identifica y suma los egresos
    /// administrativos vigentes: pagos y anticipos a proveedores no anulados).
    /// </summary>
    public interface ITesoreriaService
    {
        Task<List<OrigenFondosItem>> ListarOrigenesAsync(bool soloActivos = false);
        Task<OrigenFondosItem?> ObtenerOrigenAsync(int id);
        /// <summary>Alta (Id nulo) o edición (con RowVersion) de un origen. Código único sin distinguir mayúsculas ni espacios.</summary>
        Task<ResultadoCxp> GuardarOrigenAsync(OrigenFondosRequest request, string usuario);
        /// <summary>Baja lógica; un origen con pagos o anticipos no se borra (se desactiva).</summary>
        Task<ResultadoCxp> EliminarOrigenAsync(int id, string usuario);
        Task<ResumenTesoreria> ObtenerResumenAsync(DateTime? desde, DateTime? hasta);
    }
}

using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    public interface IContratoVentaCreditoService
    {
        Task<ContratoVentaCreditoValidacionResult> ValidarDatosParaGenerarAsync(int ventaId);
        Task<ContratoVentaCredito> GenerarAsync(int ventaId, string usuario);
        Task<ContratoVentaCredito> GenerarPdfAsync(int ventaId, string usuario);
        Task<ContratoVentaCreditoPdfArchivo?> ObtenerPdfAsync(int ventaId);
        Task<bool> ExisteContratoGeneradoAsync(int ventaId);
        Task<ContratoVentaCredito?> ObtenerContratoPorVentaAsync(int ventaId);
        Task<ContratoVentaCredito?> ObtenerContratoPorCreditoAsync(int creditoId);
    }
}

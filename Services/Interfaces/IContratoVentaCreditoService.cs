using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    public interface IContratoVentaCreditoService
    {
        Task<ContratoVentaCreditoValidacionResult> ValidarDatosParaGenerarAsync(int ventaId);

        /// <summary>
        /// Sólo los datos contractuales del Cliente (Nombre/Apellido/Documento/Domicilio/
        /// Localidad/Teléfono) — sin Venta ni Crédito, para poder avisar ANTES de que existan
        /// (ej. Cotizador/Mi Venta, donde el cliente ya está persistido pero la venta todavía
        /// no). Pura, sin acceso a datos: reutiliza la misma regla que
        /// <see cref="ValidarDatosParaGenerarAsync"/> aplica sobre el Cliente.
        /// </summary>
        ContratoVentaCreditoValidacionResult ValidarDatosClienteParaContrato(Cliente? cliente);
        Task<ContratoVentaCredito> GenerarAsync(int ventaId, string usuario);
        Task<ContratoVentaCredito> GenerarPdfAsync(int ventaId, string usuario);
        Task<ContratoVentaCreditoPdfArchivo?> ObtenerPdfAsync(int ventaId);
        Task<bool> ExisteContratoGeneradoAsync(int ventaId);
        Task<bool> ExistePlantillaActivaAsync();
        Task<ContratoVentaCredito?> ObtenerContratoPorVentaAsync(int ventaId);
        Task<ContratoVentaCredito?> ObtenerContratoPorCreditoAsync(int creditoId);
    }
}

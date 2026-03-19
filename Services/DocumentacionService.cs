using System;
using Microsoft.Extensions.Logging;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class DocumentacionService : IDocumentacionService
    {
        private readonly IDocumentoClienteService _documentoClienteService;
        private readonly ICreditoService _creditoService;
        private readonly IVentaService _ventaService;
        private readonly ILogger<DocumentacionService> _logger;

        public DocumentacionService(
            IDocumentoClienteService documentoClienteService,
            ICreditoService creditoService,
            IVentaService ventaService,
            ILogger<DocumentacionService> logger)
        {
            _documentoClienteService = documentoClienteService;
            _creditoService = creditoService;
            _ventaService = ventaService;
            _logger = logger;
        }

        public async Task<DocumentacionCreditoResultado> ProcesarDocumentacionVentaAsync(int ventaId, bool crearCreditoSiCompleta = true)
        {
            var venta = await _ventaService.GetByIdAsync(ventaId)
                ?? throw new InvalidOperationException($"Venta {ventaId} no encontrada");

            var documentacion = await _documentoClienteService.ValidarDocumentacionObligatoriaAsync(venta.ClienteId);

            if (!documentacion.Completa)
            {
                _logger.LogWarning(
                    "Documentación incompleta para cliente {ClienteId}. Faltantes: {Faltantes}",
                    venta.ClienteId,
                    documentacion.DescripcionFaltantes);

                return new DocumentacionCreditoResultado
                {
                    DocumentacionCompleta = false,
                    ClienteId = venta.ClienteId,
                    VentaId = venta.Id,
                    MensajeFaltantes = documentacion.DescripcionFaltantes
                };
            }

            int? creditoId = venta.CreditoId;
            var creditoCreado = false;

            if (!creditoId.HasValue && crearCreditoSiCompleta)
            {
                var credito = await _creditoService.CreatePendienteConfiguracionAsync(venta.ClienteId, venta.Total);
                await _ventaService.AsociarCreditoAVentaAsync(venta.Id, credito.Id);
                creditoId = credito.Id;
                creditoCreado = true;
            }

            return new DocumentacionCreditoResultado
            {
                DocumentacionCompleta = true,
                ClienteId = venta.ClienteId,
                VentaId = venta.Id,
                CreditoId = creditoId,
                CreditoCreado = creditoCreado
            };
        }
    }
}

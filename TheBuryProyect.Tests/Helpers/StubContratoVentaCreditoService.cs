using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Helpers;

public sealed class StubContratoVentaCreditoService : IContratoVentaCreditoService
{
    private readonly bool _existeContratoGenerado;

    public StubContratoVentaCreditoService(bool existeContratoGenerado = true)
    {
        _existeContratoGenerado = existeContratoGenerado;
    }

    public Task<ContratoVentaCreditoValidacionResult> ValidarDatosParaGenerarAsync(int ventaId)
        => Task.FromResult(new ContratoVentaCreditoValidacionResult());

    public Task<ContratoVentaCredito> GenerarAsync(int ventaId, string usuario)
        => throw new NotImplementedException();

    public Task<ContratoVentaCredito> GenerarPdfAsync(int ventaId, string usuario)
        => throw new NotImplementedException();

    public Task<ContratoVentaCreditoPdfArchivo?> ObtenerPdfAsync(int ventaId)
        => Task.FromResult<ContratoVentaCreditoPdfArchivo?>(null);

    public Task<bool> ExisteContratoGeneradoAsync(int ventaId)
        => Task.FromResult(_existeContratoGenerado);

    public Task<bool> ExistePlantillaActivaAsync()
        => Task.FromResult(true);

    public Task<ContratoVentaCredito?> ObtenerContratoPorVentaAsync(int ventaId)
        => Task.FromResult<ContratoVentaCredito?>(null);

    public Task<ContratoVentaCredito?> ObtenerContratoPorCreditoAsync(int creditoId)
        => Task.FromResult<ContratoVentaCredito?>(null);
}

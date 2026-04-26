using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Infrastructure;

/// <summary>
/// Stub mínimo de IPrecioService para tests que instancian ProductoService directamente
/// pero no ejercitan lógica de precios de lista.
/// GetListaPredeterminadaAsync devuelve null → BuscarParaVentaAsync usa PrecioVenta del producto.
/// </summary>
public sealed class StubPrecioService : IPrecioService
{
    public Task<List<ListaPrecio>> GetAllListasAsync(bool soloActivas = true)
        => Task.FromResult(new List<ListaPrecio>());

    public Task<ListaPrecio?> GetListaByIdAsync(int id)
        => Task.FromResult<ListaPrecio?>(null);

    public Task<ListaPrecio?> GetListaPredeterminadaAsync()
        => Task.FromResult<ListaPrecio?>(null);

    public Task<ListaPrecio> CreateListaAsync(ListaPrecio lista)
        => Task.FromResult(lista);

    public Task<ListaPrecio> UpdateListaAsync(ListaPrecio lista, byte[] rowVersion)
        => Task.FromResult(lista);

    public Task<bool> DeleteListaAsync(int id, byte[] rowVersion)
        => Task.FromResult(false);

    public Task<ProductoPrecioLista?> GetPrecioVigenteAsync(int productoId, int listaId, DateTime? fecha = null)
        => Task.FromResult<ProductoPrecioLista?>(null);

    public Task<Dictionary<int, ProductoPrecioLista>> GetPreciosVigentesBatchAsync(
        IEnumerable<int> productoIds, int listaId, DateTime? fecha = null)
        => Task.FromResult(new Dictionary<int, ProductoPrecioLista>());

    public Task<List<ProductoPrecioLista>> GetPreciosProductoAsync(int productoId, DateTime? fecha = null)
        => Task.FromResult(new List<ProductoPrecioLista>());

    public Task<List<ProductoPrecioLista>> GetHistorialPreciosAsync(int productoId, int listaId)
        => Task.FromResult(new List<ProductoPrecioLista>());

    public Task<ProductoPrecioLista> SetPrecioManualAsync(int productoId, int listaId, decimal precio,
        decimal costo, DateTime? vigenciaDesde = null, string? notas = null)
        => throw new NotImplementedException();

    public Task<decimal> CalcularPrecioAutomaticoAsync(int productoId, int listaId, decimal costo)
        => Task.FromResult(costo);

    public Task<ResultadoAplicacionPrecios> AplicarCambioPrecioDirectoAsync(AplicarCambioPrecioDirectoViewModel model)
        => throw new NotImplementedException();

    public Task<List<CambioPrecioEvento>> GetCambioPrecioEventosAsync(int take = 200)
        => Task.FromResult(new List<CambioPrecioEvento>());

    public Task<List<CambioPrecioDetalle>> GetCambiosPrecioProductoAsync(int productoId, int take = 50)
        => Task.FromResult(new List<CambioPrecioDetalle>());

    public Task<Dictionary<int, UltimoCambioProductoResumen>> GetUltimoCambioPorProductosAsync(IEnumerable<int> productoIds)
        => Task.FromResult(new Dictionary<int, UltimoCambioProductoResumen>());

    public Task<CambioPrecioEvento?> GetCambioPrecioEventoAsync(int eventoId)
        => Task.FromResult<CambioPrecioEvento?>(null);

    public Task<(bool Exitoso, string Mensaje, int? EventoReversionId)> RevertirCambioPrecioEventoAsync(int eventoId)
        => throw new NotImplementedException();

    public Task<PriceChangeBatch> SimularCambioMasivoAsync(string nombre, TipoCambio tipoCambio,
        TipoAplicacion tipoAplicacion, decimal valorCambio, List<int> listasIds,
        List<int>? categoriaIds = null, List<int>? marcaIds = null, List<int>? productoIds = null)
        => throw new NotImplementedException();

    public Task<PriceChangeBatch?> GetSimulacionAsync(int batchId)
        => Task.FromResult<PriceChangeBatch?>(null);

    public Task<List<PriceChangeItem>> GetItemsSimulacionAsync(int batchId, int skip = 0, int take = 50)
        => Task.FromResult(new List<PriceChangeItem>());

    public Task<List<int>> GetBatchIdsByProductoAsync(int productoId)
        => Task.FromResult(new List<int>());

    public Task<PriceChangeBatch> AprobarBatchAsync(int batchId, string aprobadoPor, byte[] rowVersion, string? notas = null)
        => throw new NotImplementedException();

    public Task<PriceChangeBatch> RechazarBatchAsync(int batchId, string rechazadoPor, byte[] rowVersion, string motivo)
        => throw new NotImplementedException();

    public Task<PriceChangeBatch> CancelarBatchAsync(int batchId, string canceladoPor, byte[] rowVersion, string? motivo = null)
        => throw new NotImplementedException();

    public Task<bool> RequiereAutorizacionAsync(int batchId)
        => Task.FromResult(false);

    public Task<PriceChangeBatch> AplicarBatchAsync(int batchId, string aplicadoPor, byte[] rowVersion, DateTime? fechaVigencia = null)
        => throw new NotImplementedException();

    public Task<PriceChangeBatch> RevertirBatchAsync(int batchId, string revertidoPor, byte[] rowVersion, string motivo)
        => throw new NotImplementedException();

    public Task<List<PriceChangeBatch>> GetBatchesAsync(EstadoBatch? estado = null,
        DateTime? fechaDesde = null, DateTime? fechaHasta = null, int skip = 0, int take = 50)
        => Task.FromResult(new List<PriceChangeBatch>());

    public Task<Dictionary<string, object>> GetEstadisticasBatchAsync(int batchId)
        => Task.FromResult(new Dictionary<string, object>());

    public Task<byte[]> ExportarHistorialPreciosAsync(List<int> productoIds, DateTime fechaDesde, DateTime fechaHasta)
        => Task.FromResult(Array.Empty<byte>());

    public Task<(bool esValido, string? mensaje)> ValidarMargenMinimoAsync(decimal precio, decimal costo, int listaId)
        => Task.FromResult((true, (string?)null));

    public decimal CalcularMargen(decimal precio, decimal costo)
        => costo == 0 ? 0 : (precio - costo) / costo * 100;

    public decimal AplicarRedondeo(decimal precio, string? reglaRedondeo = null)
        => precio;
}

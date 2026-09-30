using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Unit;

/// <summary>Precios globales de envío/armado en memoria para tests del calculador de cotizaciones.</summary>
public sealed class FakeServicioVentaPrecioService : IServicioVentaPrecioService
{
    private readonly Dictionary<TipoServicioVenta, ServicioVentaPrecioInfo> _precios = new();

    public FakeServicioVentaPrecioService(params (TipoServicioVenta Tipo, decimal Precio, bool Activo)[] precios)
    {
        foreach (var p in precios)
            _precios[p.Tipo] = new ServicioVentaPrecioInfo(p.Tipo, p.Precio, p.Activo);
    }

    public Task<IReadOnlyList<ServicioVentaPrecioInfo>> ListarAsync()
    {
        IReadOnlyList<ServicioVentaPrecioInfo> lista = Enum.GetValues<TipoServicioVenta>()
            .Select(t => _precios.TryGetValue(t, out var info) ? info : new ServicioVentaPrecioInfo(t, 0m, true))
            .ToList();
        return Task.FromResult(lista);
    }

    public Task GuardarAsync(IEnumerable<ServicioVentaPrecioComando> comandos) => Task.CompletedTask;
}

using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Helpers;

/// <summary>
/// Construye los datos read-only para los ViewBags de CreditoController (formularios Create/Edit).
/// Centraliza las consultas de catálogo para evitar duplicación entre los GETs y los paths de
/// retorno por error en Create POST / Edit POST.
/// </summary>
public class CreditoViewBagBuilder
{
    private readonly IClienteLookupService _clienteLookup;
    private readonly IProductoService _productoService;
    private readonly ILogger<CreditoViewBagBuilder> _logger;

    public CreditoViewBagBuilder(
        IClienteLookupService clienteLookup,
        IProductoService productoService,
        ILogger<CreditoViewBagBuilder> logger)
    {
        _clienteLookup = clienteLookup;
        _productoService = productoService;
        _logger = logger;
    }

    /// <summary>
    /// Carga los ViewBag necesarios para los formularios Create y Edit de Crédito.
    /// Los valores se asignan directamente sobre el <paramref name="viewBag"/> del controller.
    /// </summary>
    public async Task CargarAsync(
        dynamic viewBag,
        int? clienteIdSeleccionado = null,
        int? garanteIdSeleccionado = null)
    {
        _logger.LogInformation("CreditoViewBagBuilder.CargarAsync ClienteId:{ClienteId} GaranteId:{GaranteId}",
            clienteIdSeleccionado, garanteIdSeleccionado);

        var clientes = await _clienteLookup.GetClientesSelectListAsync(clienteIdSeleccionado);
        viewBag.Clientes = new SelectList(clientes, "Value", "Text", clienteIdSeleccionado?.ToString());

        var garantes = await _clienteLookup.GetClientesSelectListAsync(garanteIdSeleccionado);
        viewBag.Garantes = new SelectList(garantes, "Value", "Text", garanteIdSeleccionado?.ToString());

        var productos = await _productoService.SearchAsync(soloActivos: true, orderBy: "nombre");
        viewBag.Productos = new SelectList(
            productos
                .Where(p => p.StockActual > 0)
                .Select(p => new
                {
                    p.Id,
                    Detalle = $"{p.Codigo} - {p.Nombre} (Stock: {p.StockActual}) - ${p.PrecioVenta:N2}"
                }),
            "Id",
            "Detalle");
    }
}

using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Helpers;

/// <summary>
/// Construye los datos read-only para los ViewBags de VentaController (formularios Create/Edit).
/// Centraliza las consultas de catálogo para evitar duplicación entre Create GET, Edit GET y
/// los paths de retorno por error en Create POST / Edit POST.
/// </summary>
public class VentaViewBagBuilder
{
    private readonly IClienteLookupService _clienteLookup;
    private readonly IProductoService _productoService;
    private readonly ICreditoService _creditoService;
    private readonly IConfiguracionPagoService _configuracionPagoService;
    private readonly IUsuarioService _usuarioService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<VentaViewBagBuilder> _logger;

    public VentaViewBagBuilder(
        IClienteLookupService clienteLookup,
        IProductoService productoService,
        ICreditoService creditoService,
        IConfiguracionPagoService configuracionPagoService,
        IUsuarioService usuarioService,
        ICurrentUserService currentUser,
        ILogger<VentaViewBagBuilder> logger)
    {
        _clienteLookup = clienteLookup;
        _productoService = productoService;
        _creditoService = creditoService;
        _configuracionPagoService = configuracionPagoService;
        _usuarioService = usuarioService;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Carga todos los ViewBag necesarios para los formularios Create y Edit de Venta.
    /// Los valores se asignan directamente sobre el <paramref name="viewBag"/> del controller.
    /// </summary>
    public async Task CargarAsync(
        dynamic viewBag,
        int? clienteIdSeleccionado = null,
        IEnumerable<int>? productoIdsIncluidos = null,
        string? vendedorUserIdSeleccionado = null)
    {
        var creditosCount = 0;
        var vendedoresCount = 0;

        // Clientes
        var clientes = await _clienteLookup.GetClientesSelectListAsync(clienteIdSeleccionado);
        viewBag.Clientes = new SelectList(clientes, "Value", "Text", clienteIdSeleccionado?.ToString());

        // Productos: se incluyen todos los activos con stock > 0,
        // más los ya presentes en la venta aunque tengan stock 0.
        var productos = await _productoService.SearchAsync(soloActivos: true, orderBy: "nombre");

        var productoIdsIncluidosSet = productoIdsIncluidos != null
            ? new HashSet<int>(productoIdsIncluidos)
            : null;

        viewBag.Productos = new SelectList(
            productos
                .Where(p => p.StockActual > 0 || (productoIdsIncluidosSet != null && productoIdsIncluidosSet.Contains(p.Id)))
                .Select(p => new
                {
                    p.Id,
                    Detalle = $"{p.Codigo} - {p.Nombre} (Stock: {p.StockActual}) - ${p.PrecioVenta:N2}"
                }),
            "Id",
            "Detalle");

        // Filtros derivados de los productos cargados
        var categoriasFiltro = productos
            .Where(p => p.Categoria != null)
            .Select(p => p.Categoria!)
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .OrderBy(c => c.Nombre)
            .Select(c => new { c.Id, c.Nombre })
            .ToList();

        var marcasFiltro = productos
            .Where(p => p.Marca != null)
            .Select(p => p.Marca!)
            .GroupBy(m => m.Id)
            .Select(g => g.First())
            .OrderBy(m => m.Nombre)
            .Select(m => new { m.Id, m.Nombre })
            .ToList();

        viewBag.CategoriasFiltro = new SelectList(categoriasFiltro, "Id", "Nombre");
        viewBag.MarcasFiltro = new SelectList(marcasFiltro, "Id", "Nombre");
        viewBag.TiposPago = EnumHelper.GetSelectList<TipoPago>();

        // Créditos disponibles del cliente seleccionado
        if (clienteIdSeleccionado.HasValue)
        {
            var creditosDisponibles = (await _creditoService.GetByClienteIdAsync(clienteIdSeleccionado.Value))
                .Where(c => (c.Estado == EstadoCredito.Activo || c.Estado == EstadoCredito.Aprobado)
                            && c.SaldoPendiente > 0)
                .OrderByDescending(c => c.FechaAprobacion ?? DateTime.MinValue)
                .Select(c => new
                {
                    c.Id,
                    Detalle = $"{c.Numero} - Saldo: ${c.SaldoPendiente:N2}"
                })
                .ToList();

            viewBag.Creditos = new SelectList(creditosDisponibles, "Id", "Detalle");
            creditosCount = creditosDisponibles.Count;
        }
        else
        {
            viewBag.Creditos = new SelectList(Enumerable.Empty<SelectListItem>());
        }

        // Tarjetas activas
        var tarjetas = await _configuracionPagoService.GetTarjetasActivasAsync();
        var tarjetasDisponibles = tarjetas
            .Select(t => new
            {
                t.Id,
                Detalle = $"{t.NombreTarjeta} ({t.TipoTarjeta})"
            })
            .ToList();
        viewBag.Tarjetas = new SelectList(tarjetasDisponibles, "Id", "Detalle");

        // Vendedores (solo para roles con capacidad de delegar)
        var puedeDelegarVendedor = _currentUser.IsInRole(Roles.SuperAdmin) ||
                                   _currentUser.IsInRole(Roles.Administrador) ||
                                   _currentUser.IsInRole(Roles.Gerente);
        viewBag.PuedeDelegarVendedor = puedeDelegarVendedor;

        if (puedeDelegarVendedor)
        {
            var vendedores = await _usuarioService.GetUsuariosPorRolAsync(Roles.Vendedor);
            viewBag.Vendedores = new SelectList(vendedores, "Id", "UserName", vendedorUserIdSeleccionado);
            vendedoresCount = vendedores.Count;
        }

        _logger.LogDebug(
            "VentaViewBagBuilder.CargarAsync ClienteId:{ClienteId} Clientes:{Clientes} ProductosTotal:{ProductosTotal} ProductosIncluidos:{ProductosIncluidos} Creditos:{Creditos} Tarjetas:{Tarjetas} Vendedores:{Vendedores}",
            clienteIdSeleccionado,
            clientes.Count(),
            productos.Count(),
            productoIdsIncluidos?.Distinct().Count() ?? 0,
            creditosCount,
            tarjetasDisponibles.Count,
            vendedoresCount);
    }
}

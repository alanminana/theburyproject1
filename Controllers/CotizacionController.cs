using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Controllers;

[Authorize]
[PermisoRequerido(Modulo = ModuloCotizaciones, Accion = AccionVer)]
public sealed class CotizacionController : Controller
{
    private const string ModuloCotizaciones = "cotizaciones";
    private const string AccionVer = "view";

    private readonly ICotizacionService _cotizacionService;
    private readonly IProductoService _productoService;
    private readonly IClienteService _clienteService;
    private readonly ILogger<CotizacionController> _logger;

    public CotizacionController(
        ICotizacionService cotizacionService,
        IProductoService productoService,
        IClienteService clienteService,
        ILogger<CotizacionController> logger)
    {
        _cotizacionService = cotizacionService;
        _productoService = productoService;
        _clienteService = clienteService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View("Index_tw");
    }

    [HttpGet]
    public async Task<IActionResult> Listado(
        string? busqueda = null,
        EstadoCotizacion? estado = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _cotizacionService.ListarAsync(
            new CotizacionFiltros
            {
                Busqueda = busqueda,
                Estado = estado,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                Page = page < 1 ? 1 : page,
                PageSize = 25
            },
            cancellationToken);

        ViewBag.Busqueda = busqueda;
        ViewBag.Estado = estado;
        ViewBag.FechaDesde = fechaDesde;
        ViewBag.FechaHasta = fechaHasta;
        return View("Listado_tw", resultado);
    }

    [HttpGet]
    public async Task<IActionResult> Detalles(int id, CancellationToken cancellationToken = default)
    {
        var cotizacion = await _cotizacionService.ObtenerAsync(id, cancellationToken);
        if (cotizacion is null)
            return NotFound();

        return View("Detalles_tw", cotizacion);
    }

    [HttpGet]
    public async Task<IActionResult> BuscarProductos(string term, int take = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(term))
                return Ok(Array.Empty<object>());

            var limite = Math.Clamp(take, 1, 30);
            var productos = await _productoService.BuscarParaVentaAsync(
                term.Trim(),
                limite,
                soloConStock: false);

            cancellationToken.ThrowIfCancellationRequested();

            return Ok(productos.Select(p => new
            {
                id = p.Id,
                codigo = p.Codigo,
                nombre = p.Nombre,
                descripcion = p.Descripcion,
                categoria = p.Categoria,
                marca = p.Marca,
                stockActual = p.StockActual,
                precioVenta = p.PrecioVenta,
                requiereNumeroSerie = p.RequiereNumeroSerie,
                codigoExacto = p.CodigoExacto
            }));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al buscar productos para cotizacion con termino {Term}", term);
            return StatusCode(500, new { error = "No se pudieron buscar productos para cotizacion." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ProductoResumen(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            if (id <= 0)
                return BadRequest(new { error = "El identificador de producto debe ser valido." });

            var producto = await _productoService.ObtenerPrecioVigenteParaVentaAsync(id);
            cancellationToken.ThrowIfCancellationRequested();

            if (producto is null)
                return NotFound(new { error = "Producto no encontrado o inactivo." });

            return Ok(new
            {
                id = producto.ProductoId,
                codigo = producto.Codigo,
                nombre = producto.Nombre,
                precioVenta = producto.PrecioVenta,
                stockActual = producto.StockActual
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener producto {ProductoId} para cotizacion", id);
            return StatusCode(500, new { error = "No se pudo obtener el producto para cotizacion." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> BuscarClientes(string term, int take = 15, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(term))
                return Ok(Array.Empty<object>());

            var limite = Math.Clamp(take, 1, 30);
            var clientes = await _clienteService.SearchAsync(
                searchTerm: term.Trim(),
                soloActivos: true,
                orderBy: "nombre");

            cancellationToken.ThrowIfCancellationRequested();

            return Ok(clientes.Take(limite).Select(c => new
            {
                id = c.Id,
                nombre = c.Nombre,
                apellido = c.Apellido,
                tipoDocumento = c.TipoDocumento,
                numeroDocumento = c.NumeroDocumento,
                display = c.ToDisplayName()
            }));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al buscar clientes para cotizacion con termino {Term}", term);
            return StatusCode(500, new { error = "No se pudieron buscar clientes para cotizacion." });
        }
    }
}

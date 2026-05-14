using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "movimientos", Accion = "view")]
    public class MovimientoStockController : Controller
    {
        private readonly IMovimientoStockService _movimientoStockService;
        private readonly IProductoService _productoService;
        private readonly IMapper _mapper;
        private readonly ILogger<MovimientoStockController> _logger;
        private readonly ICurrentUserService _currentUser;

        public MovimientoStockController(
            IMovimientoStockService movimientoStockService,
            IProductoService productoService,
            IMapper mapper,
            ILogger<MovimientoStockController> logger,
            ICurrentUserService currentUser)
        {
            _movimientoStockService = movimientoStockService;
            _productoService = productoService;
            _mapper = mapper;
            _logger = logger;
            _currentUser = currentUser;
        }

        #region Vistas — Index / Kardex / Crear ajuste

        // GET: MovimientoStock
        public async Task<IActionResult> Index(MovimientoStockFilterViewModel filter)
        {
            try
            {
                var movimientos = await _movimientoStockService.SearchAsync(
                    productoId: filter.ProductoId,
                    tipo: filter.Tipo,
                    fechaDesde: filter.FechaDesde,
                    fechaHasta: filter.FechaHasta,
                    orderBy: filter.OrderBy,
                    orderDirection: filter.OrderDirection);

                var viewModels = _mapper.Map<IEnumerable<MovimientoStockViewModel>>(movimientos);

                filter.Movimientos = viewModels;
                filter.TotalResultados = viewModels.Count();

                var productos = await _productoService.GetAllAsync();
                ViewBag.Productos = new SelectList(productos.OrderBy(p => p.Nombre), "Id", "Nombre", filter.ProductoId);
                ViewBag.Tipos = new SelectList(Enum.GetValues(typeof(TipoMovimiento))); // mantiene coherencia con la vista

                return View("Index_tw", filter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener movimientos de stock");
                TempData["Error"] = "Error al cargar los movimientos de stock";
                return View("Index_tw", new MovimientoStockFilterViewModel());
            }
        }

        [HttpGet]
        public async Task<IActionResult> ListJson(int? productoId, string? tipo, DateTime? fechaDesde, DateTime? fechaHasta, string? busqueda)
        {
            try
            {
                TipoMovimiento? tipoEnum = Enum.TryParse<TipoMovimiento>(tipo, true, out var t) ? t : null;

                var movimientos = await _movimientoStockService.SearchAsync(
                    productoId: productoId,
                    tipo: tipoEnum,
                    fechaDesde: fechaDesde,
                    fechaHasta: fechaHasta,
                    orderBy: "fecha",
                    orderDirection: "desc");

                var list = _mapper.Map<IEnumerable<MovimientoStockViewModel>>(movimientos).ToList();

                // Filter by product name/code if busqueda is provided
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    var q = busqueda.Trim().ToLowerInvariant();
                    list = list.Where(m =>
                        (m.ProductoNombre ?? "").ToLowerInvariant().Contains(q) ||
                        (m.ProductoCodigo ?? "").ToLowerInvariant().Contains(q)
                    ).ToList();
                }

                var entradas = list.Where(m => m.Tipo == TipoMovimiento.Entrada).Sum(m => m.Cantidad);
                var salidas = list.Where(m => m.Tipo == TipoMovimiento.Salida).Sum(m => Math.Abs(m.Cantidad));
                var ajustes = list.Count(m => m.Tipo == TipoMovimiento.Ajuste);

                return Json(new
                {
                    total = list.Count,
                    entradas,
                    salidas,
                    ajustes,
                    items = list.Select(m => new
                    {
                        id = m.Id,
                        productoId = m.ProductoId,
                        fecha = m.Fecha.ToString("dd MMM yyyy"),
                        hora = m.Fecha.ToString("HH:mm:ss"),
                        tipo = m.Tipo.ToString(),
                        tipoNombre = m.TipoNombre ?? m.Tipo.ToString(),
                        productoNombre = m.ProductoNombre,
                        productoCodigo = m.ProductoCodigo,
                        cantidad = m.Cantidad,
                        costoUnitarioAlMomento = m.CostoUnitarioAlMomento,
                        costoTotalAlMomento = m.CostoTotalAlMomento,
                        fuenteCosto = m.FuenteCosto,
                        referencia = m.Referencia,
                        usuario = m.CreatedBy,
                        stockAnterior = m.StockAnterior,
                        stockNuevo = m.StockNuevo
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener movimientos JSON");
                return Json(new { total = 0, entradas = 0m, salidas = 0m, ajustes = 0, items = Array.Empty<object>() });
            }
        }

        // GET: MovimientoStock/Kardex/5
        public async Task<IActionResult> Kardex(int id)
        {
            try
            {
                var producto = await _productoService.GetByIdAsync(id);
                if (producto == null)
                {
                    TempData["Error"] = "Producto no encontrado";
                    return RedirectToAction("Index", "Producto");
                }

                var movimientos = await _movimientoStockService.GetByProductoIdAsync(id);
                var viewModels = _mapper.Map<IEnumerable<MovimientoStockViewModel>>(movimientos);

                ViewBag.Producto = producto;
                return View("Kardex_tw", viewModels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener kardex del producto {ProductoId}", id);
                TempData["Error"] = "Error al cargar el kardex";
                return RedirectToAction("Index", "Producto");
            }
        }

        // =========================
        //  NUEVO/ACTUALIZADO: AJUSTES
        // =========================

        // GET: MovimientoStock/Create
        public async Task<IActionResult> Create(int? productoId)
        {
            try
            {
                var viewModel = new AjusteStockViewModel();

                if (productoId.HasValue)
                {
                    var producto = await _productoService.GetByIdAsync(productoId.Value);
                    if (producto != null)
                    {
                        viewModel.ProductoId = producto.Id;
                        viewModel.ProductoNombre = producto.Nombre;
                        viewModel.ProductoCodigo = producto.Codigo;
                        viewModel.StockActual = producto.StockActual;
                    }
                }

                var productos = await _productoService.GetAllAsync();
                ViewBag.Productos = new SelectList(
                    productos.Where(p => p.Activo).OrderBy(p => p.Nombre),
                    "Id",
                    "Nombre",
                    productoId);

                ViewBag.Tipos = new SelectList(Enum.GetValues(typeof(TipoMovimiento)));

                return View("Create_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el formulario de ajuste de stock");
                TempData["Error"] = "Error al cargar el formulario";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: MovimientoStock/Create
        [HttpPost]
        [ValidateAntiForgeryToken] // protege el POST con anti-CSRF
        public async Task<IActionResult> Create(AjusteStockViewModel viewModel)
        {
            try
            {
                // Validación de modelo
                if (!ModelState.IsValid)
                {
                    var productosInvalid = await _productoService.GetAllAsync();
                    ViewBag.Productos = new SelectList(
                        productosInvalid.Where(p => p.Activo).OrderBy(p => p.Nombre),
                        "Id",
                        "Nombre",
                        viewModel.ProductoId);

                    // RE-llenar tipos al volver a la vista con errores
                    ViewBag.Tipos = new SelectList(Enum.GetValues(typeof(TipoMovimiento)));

                    return View("Create_tw", viewModel);
                }

                // Validación de existencia de producto antes de registrar
                var producto = await _productoService.GetByIdAsync(viewModel.ProductoId);
                if (producto == null)
                {
                    ModelState.AddModelError(nameof(viewModel.ProductoId), "Producto inexistente.");
                    var productosMissing = await _productoService.GetAllAsync();
                    ViewBag.Productos = new SelectList(
                        productosMissing.Where(p => p.Activo).OrderBy(p => p.Nombre),
                        "Id",
                        "Nombre",
                        viewModel.ProductoId);
                    ViewBag.Tipos = new SelectList(Enum.GetValues(typeof(TipoMovimiento)));
                    return View("Create_tw", viewModel);
                }

                var usuarioActual = _currentUser.GetUsername();
                // Registrar el ajuste en servicio de dominio
                await _movimientoStockService.RegistrarAjusteAsync(
                    viewModel.ProductoId,
                    viewModel.Tipo,
                    viewModel.Cantidad,
                    viewModel.Referencia,
                    viewModel.Motivo,
                    usuarioActual);  // ← NUEVO PARÁMETRO

                TempData["Success"] = "Ajuste de stock registrado exitosamente";
                return RedirectToAction(nameof(Kardex), new { id = viewModel.ProductoId });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                var productos = await _productoService.GetAllAsync();
                ViewBag.Productos = new SelectList(
                    productos.Where(p => p.Activo).OrderBy(p => p.Nombre),
                    "Id",
                    "Nombre",
                    viewModel.ProductoId);
                ViewBag.Tipos = new SelectList(Enum.GetValues(typeof(TipoMovimiento)));
                return View("Create_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar ajuste de stock");
                ModelState.AddModelError("", "Error al registrar el ajuste de stock");
                var productos = await _productoService.GetAllAsync();
                ViewBag.Productos = new SelectList(
                    productos.Where(p => p.Activo).OrderBy(p => p.Nombre),
                    "Id",
                    "Nombre",
                    viewModel.ProductoId);
                ViewBag.Tipos = new SelectList(Enum.GetValues(typeof(TipoMovimiento)));
                return View("Create_tw", viewModel);
            }
        }

        #endregion

        #region API — Info de producto / Búsqueda

        // GET API: MovimientoStock/GetProductoInfo/5
        [HttpGet]
        [Produces("application/json")] // negociación de contenido JSON
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // evita cachear stock
        public async Task<IActionResult> GetProductoInfo(int id)
        {
            try
            {
                var producto = await _productoService.GetByIdAsync(id);
                if (producto == null)
                {
                    return NotFound();
                }

                // Respuesta 200 OK con JSON tipado
                return Ok(new
                {
                    id = producto.Id,
                    nombre = producto.Nombre,
                    codigo = producto.Codigo,
                    stockActual = producto.StockActual,
                    stockMinimo = producto.StockMinimo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener info del producto {ProductoId}", id);
                return BadRequest("Error al obtener información del producto");
            }
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<IActionResult> BuscarProductos(string term, int take = 20)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(term))
                    return Ok(new List<object>());

                var limite = Math.Clamp(take, 1, 30);
                var termino = term.Trim();

                var productos = await _productoService.SearchAsync(
                    searchTerm: termino,
                    soloActivos: true,
                    orderBy: "nombre");

                var resultado = productos
                    .Take(limite)
                    .Select(p => new
                    {
                        id = p.Id,
                        codigo = p.Codigo,
                        nombre = p.Nombre,
                        descripcion = p.Descripcion,
                        stockActual = p.StockActual
                    })
                    .ToList();

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar productos para ajuste de stock con término {Term}", term);
                return StatusCode(500, new { error = "No se pudo buscar productos" });
            }
        }

        #endregion
    }
}

using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionVer)]
    public class OrdenCompraController : Controller
    {
        private const string ModuloCompras = "ordenescompra";
        private const string AccionVer = "view";
        private const string AccionCrear = "create";
        private const string AccionActualizar = "update";
        private const string AccionRecepcionar = "receive";
        private const string AccionCancelar = "cancel";

        private readonly IOrdenCompraService _ordenCompraService;
        private readonly IProveedorService _proveedorService;
        private readonly IProductoService _productoService;
        private readonly IMapper _mapper;
        private readonly ILogger<OrdenCompraController> _logger;

        public OrdenCompraController(
            IOrdenCompraService ordenCompraService,
            IProveedorService proveedorService,
            IProductoService productoService,
            IMapper mapper,
            ILogger<OrdenCompraController> logger)
        {
            _ordenCompraService = ordenCompraService;
            _proveedorService = proveedorService;
            _productoService = productoService;
            _mapper = mapper;
            _logger = logger;
        }

        #region CRUD — Index / Detalle / Crear / Editar / Eliminar

        // GET: OrdenCompra
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionVer)]
        public async Task<IActionResult> Index(OrdenCompraFilterViewModel filter)
        {
            try
            {
                var ordenes = await _ordenCompraService.SearchAsync(
                    searchTerm: filter.SearchTerm,
                    proveedorId: filter.ProveedorId,
                    estado: filter.Estado,
                    fechaDesde: filter.FechaDesde,
                    fechaHasta: filter.FechaHasta,
                    orderBy: filter.OrderBy,
                    orderDirection: filter.OrderDirection);

                var viewModels = _mapper.Map<IEnumerable<OrdenCompraViewModel>>(ordenes);

                // Cargar proveedores para el filtro
                var proveedores = await _proveedorService.GetAllAsync();
                ViewBag.Proveedores = new SelectList(proveedores, "Id", "RazonSocial", filter.ProveedorId);
                ViewBag.Estados = new SelectList(Enum.GetValues(typeof(EstadoOrdenCompra)));
                ViewBag.Filter = filter;
                ViewBag.Ordenes = viewModels;
                ViewBag.TotalOrdenes = viewModels.Count();
                ViewBag.TotalValorizado = viewModels.Sum(o => o.Total);

                return View("Index_tw", filter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener las órdenes de compra");
                TempData["Error"] = "Error al cargar las órdenes de compra";
                return View("Index_tw", filter);
            }
        }

        // GET: OrdenCompra/Details/5
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionVer)]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var orden = await _ordenCompraService.GetByIdAsync(id);
                if (orden == null)
                {
                    TempData["Error"] = "Orden de compra no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                var viewModel = _mapper.Map<OrdenCompraViewModel>(orden);
                return View("Details_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener la orden de compra {Id}", id);
                TempData["Error"] = "Error al cargar los detalles de la orden";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: OrdenCompra/Create
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionCrear)]
        public async Task<IActionResult> Create()
        {
            try
            {
                await CargarDatosSelectListsAsync();

                // Generar número de orden automáticamente
                var numeroOrden = await _ordenCompraService.GenerarNumeroOrdenAsync();

                var viewModel = new OrdenCompraViewModel
                {
                    Numero = numeroOrden,
                    FechaEmision = DateTime.Today,
                    Estado = EstadoOrdenCompra.Borrador
                };

                return View("Create_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el formulario de creaci�n");
                TempData["Error"] = "Error al cargar el formulario";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: OrdenCompra/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionCrear)]
        public async Task<IActionResult> Create(OrdenCompraViewModel viewModel)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    await CargarDatosSelectListsAsync(viewModel.ProveedorId);
                    return View("Create_tw", viewModel);
                }

                // Validar que tenga al menos un detalle
                if (viewModel.Detalles == null || !viewModel.Detalles.Any())
                {
                    ModelState.AddModelError("", "Debe agregar al menos un producto a la orden");
                    await CargarDatosSelectListsAsync(viewModel.ProveedorId);
                    return View("Create_tw", viewModel);
                }

                var orden = _mapper.Map<OrdenCompra>(viewModel);
                await _ordenCompraService.CreateAsync(orden);

                TempData["Success"] = $"Orden de compra {orden.Numero} creada exitosamente";
                return RedirectToAction(nameof(Details), new { id = orden.Id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                await CargarDatosSelectListsAsync(viewModel.ProveedorId);
                return View("Create_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear la orden de compra");
                ModelState.AddModelError("", "Error al crear la orden de compra");
                await CargarDatosSelectListsAsync(viewModel.ProveedorId);
                return View("Create_tw", viewModel);
            }
        }

        // GET: OrdenCompra/Edit/5
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionActualizar)]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var orden = await _ordenCompraService.GetByIdAsync(id);
                if (orden == null)
                {
                    TempData["Error"] = "Orden de compra no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                // No permitir editar �rdenes recibidas o canceladas
                if (orden.Estado == EstadoOrdenCompra.Recibida || orden.Estado == EstadoOrdenCompra.Cancelada)
                {
                    TempData["Error"] = "No se puede editar una orden recibida o cancelada";
                    return RedirectToAction(nameof(Details), new { id });
                }

                TempData["Error"] = "La edición de órdenes de compra todavía no está disponible.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar la orden de compra {Id} para edici�n", id);
                TempData["Error"] = "Error al cargar la orden para edici�n";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: OrdenCompra/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionActualizar)]
        public IActionResult Edit(int id, OrdenCompraViewModel viewModel)
        {
            TempData["Error"] = "La edición de órdenes de compra todavía no está disponible.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // GET: OrdenCompra/Delete/5
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionCancelar)]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var orden = await _ordenCompraService.GetByIdAsync(id);
                if (orden == null)
                {
                    TempData["Error"] = "Orden de compra no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                if (orden.Estado == EstadoOrdenCompra.Recibida || orden.Estado == EstadoOrdenCompra.EnTransito)
                {
                    TempData["Error"] = "No se puede eliminar una orden en tránsito o recibida";
                    return RedirectToAction(nameof(Details), new { id });
                }

                var viewModel = _mapper.Map<OrdenCompraViewModel>(orden);
                return View("Delete_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar la orden de compra {Id} para eliminaci�n", id);
                TempData["Error"] = "Error al cargar la orden";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: OrdenCompra/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionCancelar)]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                await _ordenCompraService.DeleteAsync(id);
                TempData["Success"] = "Orden de compra eliminada exitosamente";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Delete), new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar la orden de compra {Id}", id);
                TempData["Error"] = "Error al eliminar la orden de compra";
                return RedirectToAction(nameof(Delete), new { id });
            }
        }

        #endregion

        #region Operaciones especiales — Cambiar estado / Recepcionar

        // POST: OrdenCompra/CambiarEstado
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionActualizar)]
        public async Task<IActionResult> CambiarEstado(int id, EstadoOrdenCompra nuevoEstado)
        {
            try
            {
                var resultado = await _ordenCompraService.CambiarEstadoAsync(id, nuevoEstado);
                if (resultado)
                {
                    TempData["Success"] = $"Estado cambiado a {nuevoEstado} exitosamente";
                }
                else
                {
                    TempData["Error"] = "No se pudo cambiar el estado";
                }

                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar el estado de la orden {Id}", id);
                TempData["Error"] = "Error al cambiar el estado";
                return RedirectToAction(nameof(Details), new { id });
            }
        }
        // GET: OrdenCompra/Recepcionar/5
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionRecepcionar)]
        public async Task<IActionResult> Recepcionar(int id)
        {
            try
            {
                var orden = await _ordenCompraService.GetByIdAsync(id);

                if (orden == null)
                {
                    TempData["Error"] = "Orden no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                // Solo se puede recepcionar si est� confirmada o en tr�nsito
                if (orden.Estado != EstadoOrdenCompra.Confirmada &&
                    orden.Estado != EstadoOrdenCompra.EnTransito)
                {
                    TempData["Error"] = "Solo se pueden recepcionar �rdenes confirmadas o en tr�nsito";
                    return RedirectToAction(nameof(Details), new { id });
                }

                // Mapear a ViewModel
                var viewModel = _mapper.Map<OrdenCompraViewModel>(orden);

                return View("Recepcionar_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar orden para recepci�n {Id}", id);
                TempData["Error"] = "Error al cargar la orden";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: OrdenCompra/Recepcionar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloCompras, Accion = AccionRecepcionar)]
        public async Task<IActionResult> Recepcionar(int id, byte[] rowVersion, List<RecepcionDetalleViewModel> detalles)
        {
            try
            {
                // Validar que al menos se haya recepcionado algo
                if (detalles == null || !detalles.Any(d => d.CantidadARecepcionar > 0))
                {
                    TempData["Error"] = "Debe recepcionar al menos un producto";
                    return RedirectToAction(nameof(Recepcionar), new { id });
                }

                await _ordenCompraService.RecepcionarAsync(id, rowVersion, detalles);

                TempData["Success"] = "Mercader�a recepcionada exitosamente";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Recepcionar), new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al recepcionar orden {Id}", id);
                TempData["Error"] = "Error al recepcionar mercader�a";
                return RedirectToAction(nameof(Recepcionar), new { id });
            }
        }
        #endregion

        #region Métodos auxiliares

        // Helper: Cargar datos para los SelectLists
        private async Task CargarDatosSelectListsAsync(int? proveedorIdSeleccionado = null)
        {
            var proveedores = await _proveedorService.SearchAsync(soloActivos: true);
            ViewBag.Proveedores = new SelectList(proveedores, "Id", "RazonSocial", proveedorIdSeleccionado);

            var productos = await _productoService.SearchAsync(soloActivos: true);
            ViewBag.Productos = new SelectList(productos, "Id", "Nombre");
            ViewBag.ProductosJson = productos.Select(p => new { p.Id, p.Nombre, p.Codigo, p.PrecioCompra }).ToList();

            ViewBag.Estados = new SelectList(Enum.GetValues(typeof(EstadoOrdenCompra)));
        }

        #endregion
    }
}

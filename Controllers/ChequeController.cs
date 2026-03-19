using AutoMapper;
using System.Linq;
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
    [PermisoRequerido(Modulo = "cheques", Accion = "view")]
    public class ChequeController : Controller
    {
        private readonly IChequeService _chequeService;
        private readonly IProveedorService _proveedorService;
        private readonly IOrdenCompraService _ordenCompraService;
        private readonly IMapper _mapper;
        private readonly ILogger<ChequeController> _logger;

        public ChequeController(
            IChequeService chequeService,
            IProveedorService proveedorService,
            IOrdenCompraService ordenCompraService,
            IMapper mapper,
            ILogger<ChequeController> logger)
        {
            _chequeService = chequeService;
            _proveedorService = proveedorService;
            _ordenCompraService = ordenCompraService;
            _mapper = mapper;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> OrdenesPorProveedor(int proveedorId)
        {
            var ordenes = await _ordenCompraService.GetByProveedorIdAsync(proveedorId);

            var resultado = ordenes.Select(o => new
            {
                id = o.Id,
                numero = o.Numero
            });

            return Json(resultado);
        }

        // GET: Cheque
        public async Task<IActionResult> Index(ChequeFilterViewModel filter)
        {
            try
            {
                var cheques = await _chequeService.SearchAsync(
                    searchTerm: filter.SearchTerm,
                    proveedorId: filter.ProveedorId,
                    estado: filter.Estado,
                    fechaEmisionDesde: filter.FechaEmisionDesde,
                    fechaEmisionHasta: filter.FechaEmisionHasta,
                    fechaVencimientoDesde: filter.FechaVencimientoDesde,
                    fechaVencimientoHasta: filter.FechaVencimientoHasta,
                    soloVencidos: filter.SoloVencidos,
                    soloPorVencer: filter.SoloPorVencer,
                    orderBy: filter.OrderBy,
                    orderDirection: filter.OrderDirection);

                var viewModels = _mapper.Map<IEnumerable<ChequeViewModel>>(cheques);

                // Cargar proveedores para el filtro
                var proveedores = await _proveedorService.GetAllAsync();
                ViewBag.Proveedores = new SelectList(proveedores, "Id", "RazonSocial", filter.ProveedorId);
                ViewBag.Estados = new SelectList(Enum.GetValues(typeof(EstadoCheque)));

                ViewBag.Filter = filter;

                // Contar cheques vencidos y por vencer
                var chequesVencidos = await _chequeService.GetVencidosAsync();
                var chequesPorVencer = await _chequeService.GetPorVencerAsync();
                ViewBag.TotalVencidos = chequesVencidos.Count();
                ViewBag.TotalPorVencer = chequesPorVencer.Count();

                return View("Index_tw", viewModels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener los cheques");
                TempData["Error"] = "Error al cargar los cheques";
                return View("Index_tw", new List<ChequeViewModel>());
            }
        }

        // GET: Cheque/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var cheque = await _chequeService.GetByIdAsync(id);
                if (cheque == null)
                {
                    TempData["Error"] = "Cheque no encontrado";
                    return RedirectToAction(nameof(Index));
                }

                var viewModel = _mapper.Map<ChequeViewModel>(cheque);
                return View("Details_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener el cheque {Id}", id);
                TempData["Error"] = "Error al cargar los detalles del cheque";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Cheque/Create
        public async Task<IActionResult> Create(int? proveedorId = null, int? ordenCompraId = null)
        {
            try
            {
                await CargarDatosSelectListsAsync(proveedorId, ordenCompraId);

                var viewModel = new ChequeViewModel
                {
                    FechaEmision = DateTime.Today,
                    Estado = EstadoCheque.Emitido
                };

                if (proveedorId.HasValue)
                {
                    viewModel.ProveedorId = proveedorId.Value;
                }

                if (ordenCompraId.HasValue)
                {
                    viewModel.OrdenCompraId = ordenCompraId.Value;
                }

                return View("Create_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el formulario de creación");
                TempData["Error"] = "Error al cargar el formulario";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Cheque/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ChequeViewModel viewModel)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    await CargarDatosSelectListsAsync(viewModel.ProveedorId, viewModel.OrdenCompraId);
                    return View("Create_tw", viewModel);
                }

                var cheque = _mapper.Map<Cheque>(viewModel);
                await _chequeService.CreateAsync(cheque);

                TempData["Success"] = $"Cheque {cheque.Numero} creado exitosamente";
                return RedirectToAction(nameof(Details), new { id = cheque.Id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                await CargarDatosSelectListsAsync(viewModel.ProveedorId, viewModel.OrdenCompraId);
                return View("Create_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear el cheque");
                ModelState.AddModelError("", "Error al crear el cheque");
                await CargarDatosSelectListsAsync(viewModel.ProveedorId, viewModel.OrdenCompraId);
                return View("Create_tw", viewModel);
            }
        }

        // GET: Cheque/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var cheque = await _chequeService.GetByIdAsync(id);
                if (cheque == null)
                {
                    TempData["Error"] = "Cheque no encontrado";
                    return RedirectToAction(nameof(Index));
                }

                // No permitir editar cheques cobrados o depositados
                if (cheque.Estado == EstadoCheque.Cobrado || cheque.Estado == EstadoCheque.Depositado)
                {
                    TempData["Error"] = "No se puede editar un cheque cobrado o depositado";
                    return RedirectToAction(nameof(Details), new { id });
                }

                var viewModel = _mapper.Map<ChequeViewModel>(cheque);
                await CargarDatosSelectListsAsync(viewModel.ProveedorId, viewModel.OrdenCompraId);

                return View("Edit_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el cheque {Id} para edición", id);
                TempData["Error"] = "Error al cargar el cheque para edición";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Cheque/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ChequeViewModel viewModel)
        {
            if (id != viewModel.Id)
            {
                return NotFound();
            }

            try
            {
                if (!ModelState.IsValid)
                {
                    await CargarDatosSelectListsAsync(viewModel.ProveedorId, viewModel.OrdenCompraId);
                    return View("Edit_tw", viewModel);
                }

                var cheque = _mapper.Map<Cheque>(viewModel);
                await _chequeService.UpdateAsync(cheque);

                TempData["Success"] = "Cheque actualizado exitosamente";
                return RedirectToAction(nameof(Details), new { id = cheque.Id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                await CargarDatosSelectListsAsync(viewModel.ProveedorId, viewModel.OrdenCompraId);
                return View("Edit_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar el cheque {Id}", id);
                ModelState.AddModelError("", "Error al actualizar el cheque");
                await CargarDatosSelectListsAsync(viewModel.ProveedorId, viewModel.OrdenCompraId);
                return View("Edit_tw", viewModel);
            }
        }

        // GET: Cheque/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var cheque = await _chequeService.GetByIdAsync(id);
                if (cheque == null)
                {
                    TempData["Error"] = "Cheque no encontrado";
                    return RedirectToAction(nameof(Index));
                }

                var viewModel = _mapper.Map<ChequeViewModel>(cheque);
                return View("Delete_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el cheque {Id} para eliminación", id);
                TempData["Error"] = "Error al cargar el cheque";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Cheque/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                await _chequeService.DeleteAsync(id);
                TempData["Success"] = "Cheque eliminado exitosamente";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Delete), new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar el cheque {Id}", id);
                TempData["Error"] = "Error al eliminar el cheque";
                return RedirectToAction(nameof(Delete), new { id });
            }
        }

        // POST: Cheque/CambiarEstado
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int id, EstadoCheque nuevoEstado)
        {
            try
            {
                var resultado = await _chequeService.CambiarEstadoAsync(id, nuevoEstado);
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
                _logger.LogError(ex, "Error al cambiar el estado del cheque {Id}", id);
                TempData["Error"] = "Error al cambiar el estado";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        // Helper: Cargar datos para los SelectLists
        private async Task CargarDatosSelectListsAsync(int? proveedorIdSeleccionado = null, int? ordenCompraIdSeleccionado = null)
        {
            var proveedores = await _proveedorService.SearchAsync(soloActivos: true);
            ViewBag.Proveedores = new SelectList(proveedores, "Id", "RazonSocial", proveedorIdSeleccionado);

            ViewBag.Estados = new SelectList(Enum.GetValues(typeof(EstadoCheque)));

            // Cargar órdenes de compra del proveedor seleccionado si aplica
            if (proveedorIdSeleccionado.HasValue)
            {
                var ordenes = await _ordenCompraService.GetByProveedorIdAsync(proveedorIdSeleccionado.Value);
                ViewBag.OrdenesCompra = new SelectList(ordenes, "Id", "Numero", ordenCompraIdSeleccionado);
            }
            else
            {
                ViewBag.OrdenesCompra = new SelectList(Enumerable.Empty<OrdenCompra>(), "Id", "Numero");
            }
        }
    }
}
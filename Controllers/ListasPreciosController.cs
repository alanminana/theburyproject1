using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers;

/// <summary>
/// Controlador para gestión de listas de precios
/// </summary>
[Authorize]
[PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionVer)]
public class ListasPreciosController : Controller
{
    private const string ModuloPrecios = "precios";
    private const string AccionVer = "view";
    private const string AccionCrear = "create";
    private const string AccionActualizar = "update";
    private const string AccionEliminar = "delete";

    private readonly IPrecioService _precioService;
    private readonly ILogger<ListasPreciosController> _logger;

    public ListasPreciosController(
        IPrecioService precioService,
        ILogger<ListasPreciosController> logger)
    {
        _precioService = precioService;
        _logger = logger;
    }

    // ============================================
    // INDEX - Listar todas las listas de precios
    // ============================================

    /// <summary>
    /// Muestra todas las listas de precios
    /// </summary>
    public async Task<IActionResult> Index(bool incluirInactivas = false)
    {
        try
        {
            var listas = await _precioService.GetAllListasAsync(soloActivas: !incluirInactivas);

            var viewModel = listas.Select(l => new ListaPrecioViewModel
            {
                Id = l.Id,
                Nombre = l.Nombre,
                Codigo = l.Codigo,
                Tipo = l.Tipo,
                TipoDisplay = l.Tipo.ToString(),
                EsPredeterminada = l.EsPredeterminada,
                MargenPorcentaje = l.MargenPorcentaje,
                RecargoPorcentaje = l.RecargoPorcentaje,
                CantidadCuotas = l.CantidadCuotas,
                Activa = l.Activa,
                Descripcion = l.Descripcion
            }).ToList();

            ViewBag.IncluirInactivas = incluirInactivas;
            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener listas de precios");
            TempData["Error"] = "Error al cargar las listas de precios.";
            return View(new List<ListaPrecioViewModel>());
        }
    }

    // ============================================
    // DETAILS - Ver detalle de una lista
    // ============================================

    /// <summary>
    /// Muestra los detalles de una lista de precios
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        try
        {
            var lista = await _precioService.GetListaByIdAsync(id);
            if (lista == null)
            {
                TempData["Error"] = "Lista de precios no encontrada.";
                return RedirectToAction(nameof(Index));
            }

            var viewModel = new ListaPrecioViewModel
            {
                Id = lista.Id,
                Nombre = lista.Nombre,
                Codigo = lista.Codigo,
                Tipo = lista.Tipo,
                TipoDisplay = lista.Tipo.ToString(),
                EsPredeterminada = lista.EsPredeterminada,
                MargenPorcentaje = lista.MargenPorcentaje,
                RecargoPorcentaje = lista.RecargoPorcentaje,
                MargenMinimoPorcentaje = lista.MargenMinimoPorcentaje,
                CantidadCuotas = lista.CantidadCuotas,
                ReglaRedondeo = lista.ReglaRedondeo,
                ReglasJson = lista.ReglasJson,
                Activa = lista.Activa,
                Descripcion = lista.Descripcion,
                Notas = lista.Notas
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener detalles de lista {ListaId}", id);
            TempData["Error"] = "Error al cargar los detalles de la lista.";
            return RedirectToAction(nameof(Index));
        }
    }

    // ============================================
    // CREATE - Crear nueva lista
    // ============================================

    /// <summary>
    /// Muestra el formulario para crear una nueva lista de precios
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionCrear)]
    public IActionResult Create()
    {
        var viewModel = new CrearListaPrecioViewModel
        {
            Tipo = TipoListaPrecio.Contado,
            MargenPorcentaje = 30,
            RecargoPorcentaje = 0,
            MargenMinimoPorcentaje = 15,
            CantidadCuotas = 1,
            Activa = true,
            EsPredeterminada = false
        };

        return View(viewModel);
    }

    /// <summary>
    /// Procesa la creación de una nueva lista de precios
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionCrear)]
    public async Task<IActionResult> Create(CrearListaPrecioViewModel viewModel)
    {
        if (!ModelState.IsValid)
        {
            return View(viewModel);
        }

        try
        {
            var lista = new ListaPrecio
            {
                Nombre = viewModel.Nombre,
                Codigo = viewModel.Codigo,
                Tipo = viewModel.Tipo,
                EsPredeterminada = viewModel.EsPredeterminada,
                MargenPorcentaje = viewModel.MargenPorcentaje,
                RecargoPorcentaje = viewModel.RecargoPorcentaje ?? 0,
                MargenMinimoPorcentaje = viewModel.MargenMinimoPorcentaje ?? 0,
                CantidadCuotas = viewModel.CantidadCuotas ?? 1,
                ReglaRedondeo = viewModel.ReglaRedondeo,
                ReglasJson = viewModel.ReglasJson,
                Activa = viewModel.Activa,
                Descripcion = viewModel.Descripcion,
                Notas = viewModel.Notas
            };

            var listaCreada = await _precioService.CreateListaAsync(lista);

            TempData["Success"] = $"Lista de precios '{listaCreada.Nombre}' creada exitosamente.";
            return RedirectToAction(nameof(Details), new { id = listaCreada.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear lista de precios");
            ModelState.AddModelError("", "Error al crear la lista de precios: " + ex.Message);
            return View(viewModel);
        }
    }

    // ============================================
    // EDIT - Editar lista existente
    // ============================================

    /// <summary>
    /// Muestra el formulario para editar una lista de precios
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionActualizar)]
    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var lista = await _precioService.GetListaByIdAsync(id);
            if (lista == null)
            {
                TempData["Error"] = "Lista de precios no encontrada.";
                return RedirectToAction(nameof(Index));
            }

            var viewModel = new EditarListaPrecioViewModel
            {
                Id = lista.Id,
                RowVersion = lista.RowVersion,
                Nombre = lista.Nombre,
                Codigo = lista.Codigo,
                Tipo = lista.Tipo,
                EsPredeterminada = lista.EsPredeterminada,
                MargenPorcentaje = lista.MargenPorcentaje,
                RecargoPorcentaje = lista.RecargoPorcentaje,
                MargenMinimoPorcentaje = lista.MargenMinimoPorcentaje,
                CantidadCuotas = lista.CantidadCuotas,
                ReglaRedondeo = lista.ReglaRedondeo,
                ReglasJson = lista.ReglasJson,
                Activa = lista.Activa,
                Descripcion = lista.Descripcion,
                Notas = lista.Notas
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar lista para edición {ListaId}", id);
            TempData["Error"] = "Error al cargar la lista de precios.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Procesa la edición de una lista de precios
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionActualizar)]
    public async Task<IActionResult> Edit(int id, EditarListaPrecioViewModel viewModel)
    {
        if (id != viewModel.Id)
        {
            TempData["Error"] = "ID de lista no coincide.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            return View(viewModel);
        }

        try
        {
            var lista = await _precioService.GetListaByIdAsync(id);
            if (lista == null)
            {
                TempData["Error"] = "Lista de precios no encontrada.";
                return RedirectToAction(nameof(Index));
            }

            // Actualizar propiedades
            lista.Nombre = viewModel.Nombre;
            lista.Codigo = viewModel.Codigo;
            lista.Tipo = viewModel.Tipo;
            lista.EsPredeterminada = viewModel.EsPredeterminada;
            lista.MargenPorcentaje = viewModel.MargenPorcentaje;
            lista.RecargoPorcentaje = viewModel.RecargoPorcentaje ?? 0;
            lista.MargenMinimoPorcentaje = viewModel.MargenMinimoPorcentaje ?? 0;
            lista.CantidadCuotas = viewModel.CantidadCuotas ?? 1;
            lista.ReglaRedondeo = viewModel.ReglaRedondeo;
            lista.ReglasJson = viewModel.ReglasJson;
            lista.Activa = viewModel.Activa;
            lista.Descripcion = viewModel.Descripcion;
            lista.Notas = viewModel.Notas;

            await _precioService.UpdateListaAsync(lista, viewModel.RowVersion);

            TempData["Success"] = $"Lista de precios '{lista.Nombre}' actualizada exitosamente.";
            return RedirectToAction(nameof(Details), new { id = lista.Id });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            TempData["Error"] = "La lista fue modificada por otro usuario. Recargue la página e intente nuevamente.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar lista de precios {ListaId}", id);
            ModelState.AddModelError("", "Error al actualizar la lista: " + ex.Message);
            return View(viewModel);
        }
    }

    // ============================================
    // DELETE - Eliminar lista (soft delete)
    // ============================================

    /// <summary>
    /// Muestra confirmación para eliminar una lista de precios
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionEliminar)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var lista = await _precioService.GetListaByIdAsync(id);
            if (lista == null)
            {
                TempData["Error"] = "Lista de precios no encontrada.";
                return RedirectToAction(nameof(Index));
            }

            var viewModel = new ListaPrecioViewModel
            {
                Id = lista.Id,
                RowVersion = lista.RowVersion,
                Nombre = lista.Nombre,
                Codigo = lista.Codigo,
                Tipo = lista.Tipo,
                TipoDisplay = lista.Tipo.ToString(),
                EsPredeterminada = lista.EsPredeterminada,
                Activa = lista.Activa,
                Descripcion = lista.Descripcion
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar lista para eliminación {ListaId}", id);
            TempData["Error"] = "Error al cargar la lista de precios.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Procesa la eliminación (soft delete) de una lista de precios
    /// </summary>
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionEliminar)]
    public async Task<IActionResult> DeleteConfirmed(int id, byte[] rowVersion)
    {
        try
        {
            var result = await _precioService.DeleteListaAsync(id, rowVersion);

            if (result)
            {
                TempData["Success"] = "Lista de precios eliminada exitosamente.";
            }
            else
            {
                TempData["Error"] = "No se pudo eliminar la lista de precios.";
            }
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            TempData["Error"] = "La lista fue modificada por otro usuario. Recargue la página e intente nuevamente.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar lista de precios {ListaId}", id);
            TempData["Error"] = "Error al eliminar la lista: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
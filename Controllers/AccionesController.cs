using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers;

/// <summary>
/// Controller para gestión de acciones del sistema RBAC
/// </summary>
[Authorize]
[PermisoRequerido(Modulo = "acciones", Accion = "view")]
public class AccionesController : Controller
{
    private readonly IRolService _rolService;
    private readonly ILogger<AccionesController> _logger;

    private string CurrentUserName => User.Identity?.Name ?? "Sistema";

    private string? GetSafeReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    private IActionResult RedirectToReturnUrlOrIndex(string? returnUrl)
    {
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null ? LocalRedirect(safeReturnUrl) : RedirectToAction(nameof(Index));
    }

    private IActionResult RedirectToReturnUrlOrDetails(int id, string? returnUrl)
    {
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null ? LocalRedirect(safeReturnUrl) : RedirectToAction(nameof(Details), new { id });
    }

    public AccionesController(
        IRolService rolService,
        ILogger<AccionesController> logger)
    {
        _rolService = rolService;
        _logger = logger;
    }

    /// <summary>
    /// Lista todas las acciones del sistema
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? returnUrl)
    {
        try
        {
            var modulos = await _rolService.GetAllModulosAsync();

            var acciones = modulos
                .SelectMany(m => m.Acciones.Select(a => new AccionViewModel
                {
                    Id = a.Id,
                    Nombre = a.Nombre,
                    Clave = a.Clave,
                    Descripcion = a.Descripcion,
                    ModuloId = m.Id,
                    ModuloNombre = m.Nombre,
                    Activo = a.Activa
                }))
                .OrderBy(a => a.ModuloNombre)
                .ThenBy(a => a.Nombre)
                .ToList();

            return View(acciones);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener lista de acciones");
            TempData["Error"] = "Error al cargar las acciones";
            return View(new List<AccionViewModel>());
        }
    }

    /// <summary>
    /// Muestra detalles de una acción
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        try
        {
            var accion = await _rolService.GetAccionByIdAsync(id);
            if (accion == null)
            {
                return NotFound();
            }

            var modulo = await _rolService.GetModuloByIdAsync(accion.ModuloId);
            if (modulo == null)
            {
                return NotFound();
            }

            var viewModel = new AccionDetalleViewModel
            {
                Id = accion.Id,
                Nombre = accion.Nombre,
                Clave = accion.Clave,
                Descripcion = accion.Descripcion,
                ModuloId = modulo.Id,
                ModuloNombre = modulo.Nombre,
                ModuloClave = modulo.Clave,
                Activo = accion.Activa,
                CreatedAt = accion.CreatedAt
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener detalles de la acción {AccionId}", id);
            TempData["Error"] = "Error al cargar los detalles de la acción";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Muestra formulario para crear una nueva acción
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "acciones", Accion = "create")]
    public async Task<IActionResult> Create(string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);
        await CargarModulosEnViewBag();
        return View(new CrearAccionViewModel());
    }

    /// <summary>
    /// Crea una nueva acción
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "acciones", Accion = "create")]
    public async Task<IActionResult> Create(CrearAccionViewModel model, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            await CargarModulosEnViewBag();
            return View(model);
        }

        try
        {
            var accion = new AccionModulo
            {
                Nombre = model.Nombre,
                Clave = model.Clave,
                Descripcion = model.Descripcion,
                ModuloId = model.ModuloId,
                Activa = model.Activo
            };

            await _rolService.CreateAccionAsync(accion);

            _logger.LogInformation("Acción creada: {AccionNombre} por usuario {User}",
                model.Nombre, User.Identity?.Name);
            TempData["Success"] = $"Acción '{model.Nombre}' creada exitosamente";
            return RedirectToReturnUrlOrIndex(returnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear acción {AccionNombre}", model.Nombre);
            ModelState.AddModelError(string.Empty, "Error al crear la acción");
            await CargarModulosEnViewBag();
        }

        return View(model);
    }

    /// <summary>
    /// Muestra formulario para editar una acción
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "acciones", Accion = "update")]
    public async Task<IActionResult> Edit(int id, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        try
        {
            var accion = await _rolService.GetAccionByIdAsync(id);
            if (accion == null)
            {
                return NotFound();
            }

            var viewModel = new EditarAccionViewModel
            {
                Id = accion.Id,
                Nombre = accion.Nombre,
                Clave = accion.Clave,
                Descripcion = accion.Descripcion,
                ModuloId = accion.ModuloId,
                Activo = accion.Activa
            };

            await CargarModulosEnViewBag();
            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar formulario de edición para acción {AccionId}", id);
            TempData["Error"] = "Error al cargar el formulario de edición";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Actualiza una acción
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "acciones", Accion = "update")]
    public async Task<IActionResult> Edit(EditarAccionViewModel model, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            await CargarModulosEnViewBag();
            return View(model);
        }

        try
        {
            var accion = await _rolService.GetAccionByIdAsync(model.Id);
            if (accion == null)
            {
                return NotFound();
            }

            accion.Nombre = model.Nombre;
            accion.Clave = model.Clave;
            accion.Descripcion = model.Descripcion;
            accion.ModuloId = model.ModuloId;
            accion.Activa = model.Activo;
            accion.UpdatedBy = CurrentUserName;

            var actualizada = await _rolService.UpdateAccionAsync(accion, CurrentUserName);
            if (!actualizada)
            {
                TempData["Error"] = "No se pudo actualizar la acción";
                await CargarModulosEnViewBag();
                return View(model);
            }

            _logger.LogInformation("Acción actualizada: {AccionId} por usuario {User}",
                model.Id, CurrentUserName);
            TempData["Success"] = $"Acción '{model.Nombre}' actualizada exitosamente";
            return RedirectToReturnUrlOrDetails(model.Id, returnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar acción {AccionId}", model.Id);
            ModelState.AddModelError(string.Empty, "Error al actualizar la acción");
            await CargarModulosEnViewBag();
        }

        return View(model);
    }

    /// <summary>
    /// Muestra formulario para confirmar eliminación de acción
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "acciones", Accion = "delete")]
    public async Task<IActionResult> Delete(int id, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        try
        {
            var accion = await _rolService.GetAccionByIdAsync(id);
            if (accion == null)
            {
                return NotFound();
            }

            var modulo = await _rolService.GetModuloByIdAsync(accion.ModuloId);
            var viewModel = new EliminarAccionViewModel
            {
                Id = accion.Id,
                Nombre = accion.Nombre,
                Clave = accion.Clave,
                ModuloNombre = modulo?.Nombre ?? "Desconocido"
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar formulario de eliminación para acción {AccionId}", id);
            TempData["Error"] = "Error al cargar el formulario de eliminación";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Elimina una acción
    /// </summary>
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "acciones", Accion = "delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, string? returnUrl)
    {
        try
        {
            var accion = await _rolService.GetAccionByIdAsync(id);
            if (accion == null)
            {
                TempData["Error"] = "Acción no encontrada";
                return RedirectToAction(nameof(Index));
            }

            var eliminada = await _rolService.DeleteAccionAsync(id, CurrentUserName);

            if (eliminada)
            {
                _logger.LogInformation("Acción eliminada: {AccionId} por usuario {User}",
                    id, CurrentUserName);
                TempData["Success"] = "Acción eliminada exitosamente";
            }
            else
            {
                TempData["Error"] = "No se pudo eliminar la acción";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar acción {AccionId}", id);
            TempData["Error"] = "Error al eliminar la acción";
        }

        return RedirectToReturnUrlOrIndex(returnUrl);
    }

    /// <summary>
    /// Carga la lista de módulos en ViewBag para dropdowns
    /// </summary>
    private async Task CargarModulosEnViewBag()
    {
        var modulos = await _rolService.GetAllModulosAsync();
        ViewBag.Modulos = new SelectList(modulos.OrderBy(m => m.Nombre), "Id", "Nombre");
    }
}
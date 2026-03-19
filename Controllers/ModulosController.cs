using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers;

/// <summary>
/// Controller para gestión de módulos del sistema RBAC
/// </summary>
[Authorize]
[PermisoRequerido(Modulo = "modulos", Accion = "view")]
public class ModulosController : Controller
{
    private readonly IRolService _rolService;
    private readonly ILogger<ModulosController> _logger;

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

    public ModulosController(
        IRolService rolService,
        ILogger<ModulosController> logger)
    {
        _rolService = rolService;
        _logger = logger;
    }

    /// <summary>
    /// Lista todos los módulos del sistema
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? returnUrl)
    {
        try
        {
            var modulos = await _rolService.GetAllModulosAsync();

            var viewModel = modulos.Select(m => new ModuloViewModel
            {
                Id = m.Id,
                Nombre = m.Nombre,
                Clave = m.Clave,
                Categoria = m.Categoria,
                Icono = m.Icono,
                CantidadAcciones = m.Acciones.Count,
                Activo = m.Activo
            }).ToList();

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener lista de módulos");
            TempData["Error"] = "Error al cargar los módulos";
            return View(new List<ModuloViewModel>());
        }
    }

    /// <summary>
    /// Muestra detalles de un módulo con sus acciones
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        try
        {
            var modulo = await _rolService.GetModuloByIdAsync(id);
            if (modulo == null)
            {
                return NotFound();
            }

            var viewModel = new ModuloDetalleViewModel
            {
                Id = modulo.Id,
                Nombre = modulo.Nombre,
                Clave = modulo.Clave,
                Descripcion = modulo.Descripcion,
                Categoria = modulo.Categoria,
                Icono = modulo.Icono,
                Orden = modulo.Orden,
                Activo = modulo.Activo,
                CreatedAt = modulo.CreatedAt,
                Acciones = modulo.Acciones.Select(a => new AccionViewModel
                {
                    Id = a.Id,
                    Nombre = a.Nombre,
                    Clave = a.Clave,
                    Descripcion = a.Descripcion,
                    Activo = a.Activa
                }).ToList()
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener detalles del módulo {ModuloId}", id);
            TempData["Error"] = "Error al cargar los detalles del módulo";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Muestra formulario para crear un nuevo módulo
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "modulos", Accion = "create")]
    public IActionResult Create(string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);
        return View(new CrearModuloViewModel());
    }

    /// <summary>
    /// Crea un nuevo módulo
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "modulos", Accion = "create")]
    public async Task<IActionResult> Create(CrearModuloViewModel model, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var modulo = new ModuloSistema
            {
                Nombre = model.Nombre,
                Clave = model.Clave,
                Descripcion = model.Descripcion,
                Categoria = model.Categoria,
                Icono = model.Icono ?? "bi-square",
                Orden = model.Orden,
                Activo = model.Activo
            };

            await _rolService.CreateModuloAsync(modulo);

            _logger.LogInformation("Módulo creado: {ModuloNombre} por usuario {User}",
                model.Nombre, User.Identity?.Name);
            TempData["Success"] = $"Módulo '{model.Nombre}' creado exitosamente";
            return RedirectToReturnUrlOrIndex(returnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear módulo {ModuloNombre}", model.Nombre);
            ModelState.AddModelError(string.Empty, "Error al crear el módulo");
        }

        return View(model);
    }

    /// <summary>
    /// Muestra formulario para editar un módulo
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "modulos", Accion = "update")]
    public async Task<IActionResult> Edit(int id, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        try
        {
            var modulo = await _rolService.GetModuloByIdAsync(id);
            if (modulo == null)
            {
                return NotFound();
            }

            var viewModel = new EditarModuloViewModel
            {
                Id = modulo.Id,
                Nombre = modulo.Nombre,
                Clave = modulo.Clave,
                Descripcion = modulo.Descripcion,
                Categoria = modulo.Categoria,
                Icono = modulo.Icono,
                Orden = modulo.Orden,
                Activo = modulo.Activo
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar formulario de edición para módulo {ModuloId}", id);
            TempData["Error"] = "Error al cargar el formulario de edición";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Actualiza un módulo
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "modulos", Accion = "update")]
    public async Task<IActionResult> Edit(EditarModuloViewModel model, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var modulo = await _rolService.GetModuloByIdAsync(model.Id);
            if (modulo == null)
            {
                return NotFound();
            }

            modulo.Nombre = model.Nombre;
            modulo.Clave = model.Clave;
            modulo.Descripcion = model.Descripcion;
            modulo.Categoria = model.Categoria;
            modulo.Icono = model.Icono;
            modulo.Orden = model.Orden;
            modulo.Activo = model.Activo;

            var actualizado = await _rolService.UpdateModuloAsync(modulo, User.Identity?.Name);

            if (!actualizado)
            {
                TempData["Error"] = "No se pudo actualizar el módulo";
                return View(model);
            }

            _logger.LogInformation("Módulo actualizado: {ModuloId} por usuario {User}",
                model.Id, User.Identity?.Name);
            TempData["Success"] = $"Módulo '{model.Nombre}' actualizado exitosamente";
            return RedirectToReturnUrlOrDetails(model.Id, returnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar módulo {ModuloId}", model.Id);
            ModelState.AddModelError(string.Empty, "Error al actualizar el módulo");
        }

        return View(model);
    }

    /// <summary>
    /// Muestra formulario para confirmar eliminación de módulo
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "modulos", Accion = "delete")]
    public async Task<IActionResult> Delete(int id, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        try
        {
            var modulo = await _rolService.GetModuloByIdAsync(id);
            if (modulo == null)
            {
                return NotFound();
            }

            var viewModel = new EliminarModuloViewModel
            {
                Id = modulo.Id,
                Nombre = modulo.Nombre,
                Clave = modulo.Clave,
                CantidadAcciones = modulo.Acciones.Count
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar formulario de eliminación para módulo {ModuloId}", id);
            TempData["Error"] = "Error al cargar el formulario de eliminación";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Elimina un módulo
    /// </summary>
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "modulos", Accion = "delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, string? returnUrl)
    {
        try
        {
            var modulo = await _rolService.GetModuloByIdAsync(id);
            if (modulo == null)
            {
                TempData["Error"] = "Módulo no encontrado";
                return RedirectToAction(nameof(Index));
            }

            var eliminado = await _rolService.DeleteModuloAsync(id, User.Identity?.Name);

            if (!eliminado)
            {
                TempData["Error"] = "No se pudo eliminar el módulo";
                return RedirectToAction(nameof(Index));
            }

            _logger.LogInformation("Módulo eliminado: {ModuloId} por usuario {User}",
                id, User.Identity?.Name);
            TempData["Success"] = "Módulo eliminado exitosamente";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar módulo {ModuloId}", id);
            TempData["Error"] = "Error al eliminar el módulo";
        }

        return RedirectToReturnUrlOrIndex(returnUrl);
    }
}
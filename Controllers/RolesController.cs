using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers;

/// <summary>
/// Controller para gestión de roles y permisos del sistema
/// </summary>
[Authorize]
[PermisoRequerido(Modulo = "roles", Accion = "view")]
public class RolesController : Controller
{
    private readonly IRolService _rolService;
    private readonly ILogger<RolesController> _logger;

    private IActionResult RedirectToReturnUrlOrIndex(string? returnUrl)
    {
        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null
            ? LocalRedirect(safeReturnUrl)
            : RedirectToAction("Index", "Seguridad", new { tab = "roles" })!;
    }

    private IActionResult RedirectToReturnUrlOrDetails(string id, string? returnUrl)
    {
        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null
            ? LocalRedirect(safeReturnUrl)
            : RedirectToAction("RolDetails", "Seguridad", new { id, returnUrl = safeReturnUrl })!;
    }

    public RolesController(
        IRolService rolService,
        ILogger<RolesController> logger)
    {
        _rolService = rolService;
        _logger = logger;
    }

    /// <summary>
    /// Lista todos los roles del sistema
    /// </summary>
    [HttpGet]
    public IActionResult Index(string? returnUrl) => RedirectToReturnUrlOrIndex(returnUrl);

    /// <summary>
    /// Muestra detalles de un rol con sus permisos
    /// </summary>
    [HttpGet]
    public IActionResult Details(string id, string? returnUrl)
    {
        if (string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        return RedirectToAction("RolDetails", "Seguridad", new
        {
            id,
            returnUrl = Url.GetSafeReturnUrl(returnUrl)
        })!;
    }

    /// <summary>
    /// Muestra formulario para crear un nuevo rol
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "roles", Accion = "create")]
    public IActionResult Create(string? returnUrl) => RedirectToReturnUrlOrIndex(returnUrl);

    /// <summary>
    /// Crea un nuevo rol
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "create")]
    public async Task<IActionResult> Create(CrearRolViewModel model, string? returnUrl)
    {
        ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            return View("Create_tw", model);
        }

        try
        {
            var result = await _rolService.CreateRoleAsync(model.Nombre);

            if (result.Succeeded)
            {
                _logger.LogInformation("Rol creado: {RoleName} por usuario {User}",
                    model.Nombre, User.Identity?.Name);
                TempData["Success"] = $"Rol '{model.Nombre}' creado exitosamente";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear rol {RoleName}", model.Nombre);
            ModelState.AddModelError(string.Empty, "Error al crear el rol");
        }

        return View("Create_tw", model);
    }

    /// <summary>
    /// Muestra formulario para editar un rol
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "roles", Accion = "update")]
    public IActionResult Edit(string id, string? returnUrl)
    {
        if (string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        return RedirectToReturnUrlOrDetails(id, returnUrl);
    }

    /// <summary>
    /// Actualiza un rol
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "update")]
    public async Task<IActionResult> Edit(EditarRolViewModel model, string? returnUrl)
    {
        ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            return View("Edit_tw", model);
        }

        try
        {
            var result = await _rolService.UpdateRoleAsync(model.Id, model.Nombre);

            if (result.Succeeded)
            {
                _logger.LogInformation("Rol actualizado: {RoleId} -> {NuevoNombre} por usuario {User}",
                    model.Id, model.Nombre, User.Identity?.Name);
                TempData["Success"] = $"Rol '{model.Nombre}' actualizado exitosamente";
                return RedirectToReturnUrlOrDetails(model.Id, returnUrl);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar rol {RoleId}", model.Id);
            ModelState.AddModelError(string.Empty, "Error al actualizar el rol");
        }

        return View("Edit_tw", model);
    }

    /// <summary>
    /// Muestra formulario para confirmar eliminación de rol
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "roles", Accion = "delete")]
    public IActionResult Delete(string id, string? returnUrl)
    {
        if (string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        return RedirectToReturnUrlOrDetails(id, returnUrl);
    }

    /// <summary>
    /// Elimina un rol
    /// </summary>
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "delete")]
    public async Task<IActionResult> DeleteConfirmed(string id, string? returnUrl)
    {
        try
        {
            var result = await _rolService.DeleteRoleAsync(id);

            if (result.Succeeded)
            {
                _logger.LogInformation("Rol eliminado: {RoleId} por usuario {User}",
                    id, User.Identity?.Name);
                TempData["Success"] = "Rol eliminado exitosamente";
            }
            else
            {
                TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar rol {RoleId}", id);
            TempData["Error"] = "Error al eliminar el rol";
        }

        return RedirectToReturnUrlOrIndex(returnUrl);
    }

    /// <summary>
    /// Muestra interfaz para asignar permisos a un rol
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "roles", Accion = "assignpermissions")]
    public IActionResult AsignarPermisos(string id, string? returnUrl)
    {
        if (string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
        return RedirectToAction("Index", "Seguridad", new
        {
            tab = "permisos-rol",
            roleId = id,
            returnUrl = safeReturnUrl
        })!;
    }

    /// <summary>
    /// Asigna/remueve un permiso de un rol (AJAX)
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "assignpermissions")]
    public async Task<IActionResult> TogglePermiso(string roleId, int moduloId, int accionId, bool asignar)
    {
        try
        {
            if (asignar)
            {
                await _rolService.AssignPermissionToRoleAsync(roleId, moduloId, accionId);
            }
            else
            {
                await _rolService.RemovePermissionFromRoleAsync(roleId, moduloId, accionId);
            }

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al toggle permiso para rol {RoleId}", roleId);
            return Json(new { success = false, message = "Error al modificar el permiso" });
        }
    }

    /// <summary>
    /// Muestra usuarios en un rol
    /// </summary>
    [HttpGet]
    public IActionResult Usuarios(string id, string? returnUrl)
    {
        if (string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        return RedirectToReturnUrlOrDetails(id, returnUrl);
    }
}

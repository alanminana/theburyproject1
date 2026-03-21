using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers;

[Authorize]
public class SeguridadController : Controller
{
    private static readonly PermissionMatrixColumnDefinition[] PermissionMatrixColumns =
    [
        new("view", "View", true, ["view"]),
        new("create", "Create", true, ["create"]),
        new("edit", "Edit", true, ["update", "edit"]),
        new("delete", "Delete", true, ["delete"]),
        new("approve", "Approve", true, ["approve", "authorize"]),
        new("export", "Export", false, ["export"]),
        new("print", "Print", false, ["print"]),
        new("duplicate", "Duplicate", false, ["duplicate"]),
        new("assign", "Assign", false, ["assignpermissions", "assignroles", "assign"]),
        new("revoke", "Revoke", false, ["revoke"]),
        new("block", "Block", false, ["block"]),
        new("resetpass", "ResetPass", false, ["resetpassword", "resetpass"])
    ];

    private readonly AppDbContext _context;
    private readonly IRolService _rolService;
    private readonly IUsuarioService _usuarioService;
    private readonly ISeguridadAuditoriaService _seguridadAuditoria;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SeguridadController> _logger;

    public SeguridadController(
        AppDbContext context,
        IRolService rolService,
        IUsuarioService usuarioService,
        ISeguridadAuditoriaService seguridadAuditoria,
        UserManager<ApplicationUser> userManager,
        ILogger<SeguridadController> logger)
    {
        _context = context;
        _rolService = rolService;
        _usuarioService = usuarioService;
        _seguridadAuditoria = seguridadAuditoria;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? tab,
        bool mostrarInactivos = false,
        string? roleId = null,
        string? buscarModulo = null,
        string? grupo = null,
        string? usuario = null,
        string? modulo = null,
        string? accion = null,
        DateOnly? desde = null,
        DateOnly? hasta = null)
    {
        var activeTab = NormalizeTab(tab);
        var now = DateTimeOffset.UtcNow;
        var roles = await _rolService.GetAllRolesAsync();
        var roleMetadata = await _context.RolMetadatas
            .AsNoTracking()
            .ToDictionaryAsync(m => m.RoleId);

        var viewModel = new SeguridadIndexViewModel
        {
            ActiveTab = activeTab,
            UsuariosActivos = await _context.Users.AsNoTracking().CountAsync(u => u.Activo),
            UsuariosBloqueados = await _context.Users.AsNoTracking().CountAsync(u =>
                u.LockoutEnabled &&
                u.LockoutEnd.HasValue &&
                u.LockoutEnd > now),
            RolesActivos = roles.Count(role =>
                !roleMetadata.TryGetValue(role.Id, out var metadata) || metadata.Activo),
            PermisosAsignados = await _context.RolPermisos
                .AsNoTracking()
                .CountAsync(rp => !rp.IsDeleted),
            UsuariosTab = activeTab == "usuarios"
                ? await BuildUsuariosTabViewModelAsync(mostrarInactivos)
                : null,
            RolesTab = activeTab == "roles"
                ? await BuildRolesTabViewModelAsync(roles, roleMetadata)
                : null,
            PermisosRolTab = activeTab == "permisos-rol"
                ? await BuildPermisosRolViewModelAsync(roleId, buscarModulo, grupo, roles, roleMetadata)
                : null,
            AuditoriaTab = activeTab == "auditoria"
                ? await BuildAuditoriaViewModelAsync(usuario, modulo, accion, desde, hasta)
                : null
        };

        return View(viewModel);
    }

    [HttpGet]
    [PermisoRequerido(Modulo = "usuarios", Accion = "update")]
    public async Task<IActionResult> EditUsuario(string id, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        try
        {
            var model = await BuildEditUsuarioViewModelAsync(id);
            if (model == null)
            {
                return NotFound();
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            return View("EditUsuario_tw", model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar la edición de seguridad para usuario {UserId}", id);
            TempData["Error"] = "Error al cargar la edición del usuario.";
            return RedirectToAction(nameof(Index), new { tab = "usuarios" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "usuarios", Accion = "update")]
    public async Task<IActionResult> EditUsuario(SeguridadEditarUsuarioViewModel model, string? returnUrl)
    {
        ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

        model.RolesSeleccionados = model.RolesSeleccionados
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (model.RowVersion is null || model.RowVersion.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "No se recibió la versión de fila (RowVersion). Recargá la pantalla e intentá nuevamente.");
        }

        await PopulateEditUsuarioOptionsAsync(model);

        if (!ModelState.IsValid)
        {
            return View("EditUsuario_tw", model);
        }

        try
        {
            var sucursal = await _context.GetSucursalAsync(model.SucursalId);
            if (model.SucursalId.HasValue && sucursal == null)
            {
                ModelState.AddModelError(nameof(model.SucursalId), "La sucursal seleccionada no existe o está inactiva.");
            }

            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null)
            {
                return NotFound();
            }

            var existingByUserName = await _userManager.FindByNameAsync(model.UserName);
            if (existingByUserName != null && existingByUserName.Id != model.Id)
            {
                ModelState.AddModelError(nameof(model.UserName), $"El nombre de usuario '{model.UserName}' ya está en uso.");
            }

            var existingByEmail = await _userManager.FindByEmailAsync(model.Email);
            if (existingByEmail != null && existingByEmail.Id != model.Id)
            {
                ModelState.AddModelError(nameof(model.Email), $"El email '{model.Email}' ya está en uso.");
            }

            var invalidRoles = new List<string>();
            foreach (var roleName in model.RolesSeleccionados)
            {
                if (!await _rolService.RoleExistsAsync(roleName))
                {
                    invalidRoles.Add(roleName);
                }
            }

            if (invalidRoles.Any())
            {
                ModelState.AddModelError(nameof(model.RolesSeleccionados),
                    $"Roles inválidos: {string.Join(", ", invalidRoles)}");
            }

            if (!ModelState.IsValid)
            {
                return View("EditUsuario_tw", model);
            }

            var result = await _usuarioService.UpdateUsuarioAsync(new UsuarioUpdateRequest
            {
                UserId = model.Id,
                UserName = model.UserName,
                Email = model.Email,
                Nombre = model.Nombre,
                Apellido = model.Apellido,
                Telefono = model.Telefono,
                SucursalId = sucursal?.Id,
                SucursalNombre = sucursal?.Nombre,
                Activo = model.Activo,
                RolesDeseados = model.RolesSeleccionados,
                RowVersion = model.RowVersion!,
                EditadoPor = User.Identity?.Name
            });

            if (!result.Ok)
            {
                if (result.ConcurrencyConflict)
                {
                    _logger.LogWarning("Conflicto de concurrencia al editar usuario {UserId} desde Seguridad", model.Id);
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error);
                }

                return View("EditUsuario_tw", model);
            }

            _logger.LogInformation("Usuario editado desde Seguridad: {UserId} por {Admin}",
                model.Id, User.Identity?.Name);
            TempData["Success"] = $"Usuario '{model.UserName}' actualizado exitosamente.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Editar",
                $"Usuario \"{model.UserName}\"",
                $"Edición de usuario. Roles: {(model.RolesSeleccionados.Count > 0 ? string.Join(", ", model.RolesSeleccionados) : "sin roles")} · Sucursal: {sucursal?.Nombre ?? "sin sucursal"} · Estado: {(model.Activo ? "activo" : "inactivo")}.");

            var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
            if (safeReturnUrl != null)
            {
                return LocalRedirect(safeReturnUrl);
            }

            return RedirectToAction(nameof(Index), new { tab = "usuarios" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al editar usuario {UserId} desde Seguridad", model.Id);
            ModelState.AddModelError(string.Empty, "Error al actualizar el usuario.");
            return View("EditUsuario_tw", model);
        }
    }

    [HttpGet]
    [PermisoRequerido(Modulo = "roles", Accion = "view")]
    public async Task<IActionResult> RolDetails(string id, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        try
        {
            var model = await BuildRoleDetailsViewModelAsync(id);
            if (model == null)
            {
                return NotFound();
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            return View("RolDetails_tw", model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar detalle de rol {RoleId}", id);
            TempData["Error"] = "Error al cargar el detalle del rol.";
            return RedirectToAction(nameof(Index), new { tab = "roles" });
        }
    }

    [HttpGet]
    [PermisoRequerido(Modulo = "roles", Accion = "create")]
    public IActionResult CreateRol(string? returnUrl)
    {
        ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
        return PartialView("_CreateRoleModal_tw", new CrearRolViewModel { Activo = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "create")]
    public async Task<IActionResult> CreateRol([FromForm] CrearRolViewModel model, string? returnUrl)
    {
        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
        NormalizeRoleCreateModel(model);
        if (string.IsNullOrWhiteSpace(model.Nombre))
        {
            ModelState.AddModelError(nameof(model.Nombre), "El nombre del rol es requerido.");
        }

        if (!ModelState.IsValid)
        {
            return this.JsonModelErrors();
        }

        if (await _rolService.RoleExistsAsync(model.Nombre))
        {
            ModelState.AddModelError(nameof(model.Nombre), $"El rol '{model.Nombre}' ya existe.");
            return this.JsonModelErrors();
        }

        try
        {
            var (ok, error, roleId, roleName) = await _rolService.CreateRoleWithMetadataAsync(
                model.Nombre, model.Descripcion, model.Activo);

            if (!ok)
            {
                ModelState.AddModelError(string.Empty, error ?? "Error al crear el rol.");
                return this.JsonModelErrors();
            }

            _logger.LogInformation("Rol creado desde Seguridad: {RoleName} por {Admin}", roleName, User.Identity?.Name);
            TempData["Success"] = $"Rol '{roleName}' creado correctamente.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Crear",
                $"Rol \"{roleName}\"",
                $"Rol creado con estado {(model.Activo ? "activo" : "inactivo")}.");
            return Json(new
            {
                success = true,
                redirectUrl = safeReturnUrl ?? Url.Action(nameof(Index), new { tab = "roles" })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear rol {RoleName} desde Seguridad", model.Nombre);
            ModelState.AddModelError(string.Empty, "Error al crear el rol.");
            return this.JsonModelErrors();
        }
    }

    [HttpGet]
    [PermisoRequerido(Modulo = "roles", Accion = "update")]
    public async Task<IActionResult> EditRol(string id, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        var role = await _rolService.GetRoleByIdAsync(id);
        if (role == null)
        {
            return NotFound();
        }

        var metadata = await _rolService.GetRoleMetadataAsync(role.Id);
        ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

        return PartialView("_EditRoleModal_tw", new EditarRolViewModel
        {
            Id = role.Id,
            Nombre = role.Name ?? string.Empty,
            Descripcion = metadata?.Descripcion ?? RolMetadataDefaults.GetDescripcion(role.Name),
            Activo = metadata?.Activo ?? true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "update")]
    public async Task<IActionResult> EditRol([FromForm] EditarRolViewModel model, string? returnUrl)
    {
        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
        NormalizeRoleEditModel(model);
        if (string.IsNullOrWhiteSpace(model.Nombre))
        {
            ModelState.AddModelError(nameof(model.Nombre), "El nombre del rol es requerido.");
        }

        if (!ModelState.IsValid)
        {
            return this.JsonModelErrors();
        }

        try
        {
            var (ok, error, roleName) = await _rolService.UpdateRoleWithMetadataAsync(
                model.Id, model.Nombre, model.Descripcion, model.Activo);

            if (!ok)
            {
                ModelState.AddModelError(string.Empty, error ?? "Error al actualizar el rol.");
                return this.JsonModelErrors();
            }

            _logger.LogInformation("Rol editado desde Seguridad: {RoleId} por {Admin}", model.Id, User.Identity?.Name);
            TempData["Success"] = $"Rol '{roleName}' actualizado correctamente.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Editar",
                $"Rol \"{roleName}\"",
                $"Rol actualizado. Estado: {(model.Activo ? "activo" : "inactivo")}.");
            return Json(new
            {
                success = true,
                redirectUrl = safeReturnUrl ?? Url.Action(nameof(Index), new { tab = "roles" })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al editar rol {RoleId} desde Seguridad", model.Id);
            ModelState.AddModelError(string.Empty, "Error al actualizar el rol.");
            return this.JsonModelErrors();
        }
    }

    [HttpGet]
    [PermisoRequerido(Modulo = "roles", Accion = "create")]
    public async Task<IActionResult> DuplicateRol(string id, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        var role = await _rolService.GetRoleByIdAsync(id);
        if (role == null)
        {
            return NotFound();
        }

        var metadata = await _rolService.GetRoleMetadataAsync(role.Id);
        ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

        return PartialView("_DuplicateRoleModal_tw", new DuplicarRolViewModel
        {
            RolOrigenId = role.Id,
            RolOrigenNombre = role.Name ?? string.Empty,
            Nombre = await BuildDuplicateRoleNameAsync(role.Name),
            Descripcion = metadata?.Descripcion,
            Activo = metadata?.Activo ?? true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "create")]
    public async Task<IActionResult> DuplicateRol([FromForm] DuplicarRolViewModel model, string? returnUrl)
    {
        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
        model.Nombre = (model.Nombre ?? string.Empty).Trim();
        model.Descripcion = NormalizeNullable(model.Descripcion);
        if (string.IsNullOrWhiteSpace(model.Nombre))
        {
            ModelState.AddModelError(nameof(model.Nombre), "El nombre del rol es requerido.");
        }

        if (!ModelState.IsValid)
        {
            return this.JsonModelErrors();
        }

        try
        {
            var (ok, error, roleId, roleName, permisosCopiados) = await _rolService.DuplicateRoleAsync(
                model.RolOrigenId, model.Nombre, model.Descripcion, model.Activo);

            if (!ok)
            {
                ModelState.AddModelError(string.Empty, error ?? "Error al duplicar el rol.");
                return this.JsonModelErrors();
            }

            _logger.LogInformation("Rol duplicado desde Seguridad: {SourceRoleId} -> {NewRole} por {Admin}",
                model.RolOrigenId, roleName, User.Identity?.Name);
            TempData["Success"] = $"Rol '{roleName}' duplicado correctamente.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Crear",
                $"Rol \"{roleName}\"",
                $"Rol duplicado desde \"{model.RolOrigenId}\" con {permisosCopiados} permisos.");
            return Json(new
            {
                success = true,
                redirectUrl = safeReturnUrl ?? Url.Action(nameof(Index), new { tab = "roles" })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al duplicar rol {RoleId} desde Seguridad", model.RolOrigenId);
            ModelState.AddModelError(string.Empty, "Error al duplicar el rol.");
            return this.JsonModelErrors();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "update")]
    public async Task<IActionResult> ToggleRolActivo(string id, bool activo, string? returnUrl)
    {
        try
        {
            var roleName = await _rolService.ToggleRoleActivoAsync(id, activo);
            if (roleName == null)
            {
                TempData["Error"] = "Rol no encontrado.";
                return RedirectToAction(nameof(Index), new { tab = "roles" });
            }

            var actionLabel = activo ? "activado" : "desactivado";
            _logger.LogInformation("Rol {RoleId} {ActionLabel} desde Seguridad por {Admin}", id, actionLabel, User.Identity?.Name);
            TempData["Success"] = $"Rol '{roleName}' {actionLabel} correctamente.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cambiar estado del rol {RoleId}", id);
            TempData["Error"] = "Error al actualizar el estado del rol.";
        }

        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null
            ? LocalRedirect(safeReturnUrl)
            : RedirectToAction(nameof(Index), new { tab = "roles" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "delete")]
    public async Task<IActionResult> DeleteRol(string id, string? returnUrl)
    {
        try
        {
            var result = await _rolService.DeleteRoleAsync(id);
            if (result.Succeeded)
            {
                TempData["Success"] = "Rol eliminado correctamente.";
            }
            else
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar rol {RoleId}", id);
            TempData["Error"] = "Error al eliminar el rol.";
        }

        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null
            ? LocalRedirect(safeReturnUrl)
            : RedirectToAction(nameof(Index), new { tab = "roles" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "assignpermissions")]
    public async Task<IActionResult> SavePermisosRol(string roleId, List<int>? accionIds, string? returnUrl)
    {
        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);

        if (string.IsNullOrWhiteSpace(roleId))
        {
            TempData["Error"] = "Debés seleccionar un rol.";
            return RedirectToAction(nameof(Index), new { tab = "permisos-rol" });
        }

        var role = await _rolService.GetRoleByIdAsync(roleId);
        if (role == null)
        {
            TempData["Error"] = "Rol no encontrado.";
            return RedirectToAction(nameof(Index), new { tab = "permisos-rol" });
        }

        try
        {
            var (ok, error, permisosAsignados) = await _rolService.SyncPermisosForRoleAsync(roleId, accionIds ?? []);

            if (!ok)
            {
                TempData["Error"] = error ?? "Error al guardar los permisos del rol.";
            }
            else
            {
                _logger.LogInformation("Permisos guardados para rol {RoleId} por {Admin}", roleId, User.Identity?.Name);
                TempData["Success"] = $"Permisos actualizados para '{role.Name}'.";
                await _seguridadAuditoria.RegistrarEventoAsync(
                    "Seguridad",
                    "Permisos",
                    $"Rol \"{role.Name}\"",
                    $"{permisosAsignados} permisos guardados manualmente.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar permisos del rol {RoleId}", roleId);
            TempData["Error"] = "Error al guardar los permisos del rol.";
        }

        return safeReturnUrl != null
            ? LocalRedirect(safeReturnUrl)
            : RedirectToAction(nameof(Index), new { tab = "permisos-rol", roleId });
    }

    [HttpGet]
    [PermisoRequerido(Modulo = "roles", Accion = "assignpermissions")]
    public async Task<IActionResult> CopyPermisosRol(string roleId, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(roleId))
        {
            return NotFound();
        }

        var role = await _rolService.GetRoleByIdAsync(roleId);
        if (role == null)
        {
            return NotFound();
        }

        ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

        return PartialView("_CopyPermisosRolModal_tw", new CopiarPermisosRolViewModel
        {
            RolDestinoId = role.Id,
            RolDestinoNombre = role.Name ?? string.Empty,
            RolesDisponibles = (await GetRoleSelectorItemsAsync())
                .Where(r => r.Id != role.Id)
                .ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "assignpermissions")]
    public async Task<IActionResult> CopyPermisosRol([FromForm] CopiarPermisosRolViewModel model, string? returnUrl)
    {
        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);

        if (string.IsNullOrWhiteSpace(model.RolDestinoId))
        {
            ModelState.AddModelError(nameof(model.RolDestinoId), "Rol destino inválido.");
        }

        if (string.Equals(model.RolDestinoId, model.RolOrigenId, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.RolOrigenId), "El rol origen debe ser distinto del rol actual.");
        }

        if (!ModelState.IsValid)
        {
            return this.JsonModelErrors();
        }

        var targetRole = await _rolService.GetRoleByIdAsync(model.RolDestinoId);
        var sourceRole = await _rolService.GetRoleByIdAsync(model.RolOrigenId);

        if (targetRole == null || sourceRole == null)
        {
            return Json(new { success = false, errors = new[] { "No se encontraron los roles seleccionados." } });
        }

        try
        {
            var (ok, error, permisosCopiados) = await _rolService.CopyPermisosFromRoleAsync(sourceRole.Id, targetRole.Id);

            if (!ok)
            {
                return Json(new { success = false, errors = new[] { error ?? "Error al copiar permisos entre roles." } });
            }

            _logger.LogInformation("Permisos copiados de {SourceRole} a {TargetRole} por {Admin}",
                sourceRole.Name, targetRole.Name, User.Identity?.Name);

            TempData["Success"] = $"Permisos copiados desde '{sourceRole.Name}' hacia '{targetRole.Name}'.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Permisos",
                $"Rol \"{targetRole.Name}\"",
                $"Permisos copiados desde \"{sourceRole.Name}\".");
            return Json(new
            {
                success = true,
                redirectUrl = safeReturnUrl ?? Url.Action(nameof(Index), new { tab = "permisos-rol", roleId = targetRole.Id })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al copiar permisos entre roles {SourceRoleId} -> {TargetRoleId}", model.RolOrigenId, model.RolDestinoId);
            return Json(new { success = false, errors = new[] { "Error al copiar permisos entre roles." } });
        }
    }

    [HttpGet]
    [PermisoRequerido(Modulo = "roles", Accion = "view")]
    public async Task<IActionResult> Auditoria(
        string? usuario,
        string? modulo,
        string? accion,
        DateOnly? desde,
        DateOnly? hasta)
        => View("Auditoria_tw", await BuildAuditoriaViewModelAsync(usuario, modulo, accion, desde, hasta));

    private async Task<SeguridadAuditoriaViewModel> BuildAuditoriaViewModelAsync(
        string? usuario,
        string? modulo,
        string? accion,
        DateOnly? desde,
        DateOnly? hasta)
    {
        var registrosBase = _context.SeguridadEventosAuditoria
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(usuario))
        {
            registrosBase = registrosBase.Where(r => r.UsuarioNombre == usuario);
        }

        if (!string.IsNullOrWhiteSpace(modulo))
        {
            registrosBase = registrosBase.Where(r => r.Modulo == modulo);
        }

        if (!string.IsNullOrWhiteSpace(accion))
        {
            registrosBase = registrosBase.Where(r => r.Accion == accion);
        }

        if (desde.HasValue)
        {
            var desdeDate = desde.Value.ToDateTime(TimeOnly.MinValue);
            registrosBase = registrosBase.Where(r => r.FechaEvento >= desdeDate);
        }

        if (hasta.HasValue)
        {
            var hastaDate = hasta.Value.ToDateTime(TimeOnly.MaxValue);
            registrosBase = registrosBase.Where(r => r.FechaEvento <= hastaDate);
        }

        var viewModel = new SeguridadAuditoriaViewModel
        {
            UsuarioSeleccionado = usuario,
            ModuloSeleccionado = modulo,
            AccionSeleccionada = accion,
            Desde = desde,
            Hasta = hasta,
            Usuarios = await _context.SeguridadEventosAuditoria
                .AsNoTracking()
                .Select(r => r.UsuarioNombre)
                .Distinct()
                .OrderBy(u => u)
                .ToListAsync(),
            Modulos = await _context.SeguridadEventosAuditoria
                .AsNoTracking()
                .Select(r => r.Modulo)
                .Distinct()
                .OrderBy(m => m)
                .ToListAsync(),
            Acciones = await _context.SeguridadEventosAuditoria
                .AsNoTracking()
                .Select(r => r.Accion)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync(),
            Registros = await registrosBase
                .OrderByDescending(r => r.FechaEvento)
                .Select(r => new RegistroAuditoriaViewModel
                {
                    FechaHora = r.FechaEvento,
                    Usuario = r.UsuarioNombre,
                    Accion = r.Accion,
                    Modulo = r.Modulo,
                    Entidad = r.Entidad,
                    Detalle = r.Detalle ?? string.Empty
                })
                .ToListAsync()
        };
        
        return viewModel;
    }

    private static string NormalizeTab(string? tab)
    {
        return (tab ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "roles" => "roles",
            "permisos-rol" => "permisos-rol",
            "auditoria" => "auditoria",
            _ => "usuarios"
        };
    }

    private async Task<SeguridadUsuariosTabViewModel> BuildUsuariosTabViewModelAsync(bool mostrarInactivos)
    {
        var query = _context.Users.AsQueryable();

        if (!mostrarInactivos)
        {
            query = query.Where(u => u.Activo);
        }

        var users = await query
            .OrderBy(u => u.UserName)
            .ToListAsync();

        var sucursales = await _context.GetSucursalOptionsAsync();
        var sucursalesLookup = sucursales.ToDictionary(s => s.Id, s => s.Nombre);

        var rolesLookup = await _context.UserRoles
            .Join(_context.Roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, r.Name })
            .GroupBy(x => x.UserId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Name!).ToList());

        var usuarios = users.Select(user => new UsuarioViewModel
        {
            Id = user.Id,
            Email = user.Email!,
            UserName = user.UserName!,
            EmailConfirmed = user.EmailConfirmed,
            LockoutEnabled = user.LockoutEnabled,
            LockoutEnd = user.LockoutEnd,
            Roles = rolesLookup.GetValueOrDefault(user.Id, new List<string>()),
            Activo = user.Activo,
            NombreCompleto = user.NombreCompleto,
            SucursalId = user.SucursalId,
            Sucursal = user.SucursalId.HasValue && sucursalesLookup.TryGetValue(user.SucursalId.Value, out var sucursalNombre)
                ? sucursalNombre
                : user.Sucursal,
            UltimoAcceso = user.UltimoAcceso,
            FechaCreacion = user.FechaCreacion
        }).ToList();

        return new SeguridadUsuariosTabViewModel
        {
            Usuarios = usuarios,
            MostrarInactivos = mostrarInactivos,
            AllRoles = usuarios
                .SelectMany(u => u.Roles)
                .Distinct()
                .OrderBy(r => r)
                .ToList(),
            AllSucursales = sucursales
        };
    }

    private async Task<SeguridadRolesTabViewModel> BuildRolesTabViewModelAsync(
        IReadOnlyCollection<IdentityRole>? roles = null,
        IReadOnlyDictionary<string, RolMetadata>? roleMetadata = null)
    {
        var roleList = roles?.ToList() ?? await _rolService.GetAllRolesAsync();
        var metadataLookup = roleMetadata ?? await _context.RolMetadatas
            .AsNoTracking()
            .ToDictionaryAsync(m => m.RoleId);

        var userCounts = await _context.UserRoles
            .AsNoTracking()
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);

        var activeUserCounts = await (
            from ur in _context.UserRoles.AsNoTracking()
            join u in _context.Users.AsNoTracking().Where(user => user.Activo)
                on ur.UserId equals u.Id
            group ur by ur.RoleId into grouped
            select new { RoleId = grouped.Key, Count = grouped.Count() }
        ).ToDictionaryAsync(x => x.RoleId, x => x.Count);

        var permissionCounts = await _context.RolPermisos
            .AsNoTracking()
            .Where(rp => !rp.IsDeleted)
            .GroupBy(rp => rp.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);

        return new SeguridadRolesTabViewModel
        {
            Roles = roleList
                .Select(role =>
                {
                    metadataLookup.TryGetValue(role.Id, out var metadata);

                    return new RolViewModel
                    {
                        Id = role.Id,
                        Nombre = role.Name ?? string.Empty,
                        Descripcion = metadata?.Descripcion ?? RolMetadataDefaults.GetDescripcion(role.Name),
                        Activo = metadata?.Activo ?? true,
                        CantidadUsuarios = userCounts.GetValueOrDefault(role.Id),
                        CantidadUsuariosActivos = activeUserCounts.GetValueOrDefault(role.Id),
                        CantidadPermisos = permissionCounts.GetValueOrDefault(role.Id)
                    };
                })
                .OrderByDescending(r => r.Activo)
                .ThenBy(r => r.Nombre)
                .ToList()
        };
    }

    private async Task<SeguridadPermisosRolViewModel> BuildPermisosRolViewModelAsync(
        string? roleId,
        string? buscarModulo,
        string? grupo,
        IReadOnlyCollection<IdentityRole>? roles = null,
        IReadOnlyDictionary<string, RolMetadata>? roleMetadata = null)
    {
        var roleList = roles?.ToList() ?? await _rolService.GetAllRolesAsync();
        var metadataLookup = roleMetadata ?? await _context.RolMetadatas
            .AsNoTracking()
            .ToDictionaryAsync(m => m.RoleId);

        var modulos = await _rolService.GetAllModulosAsync();
        var columns = PermissionMatrixColumns
            .Where(column => column.Required || modulos.Any(modulo =>
                modulo.Acciones.Any(accion => column.Aliases.Contains(Canon(accion.Clave), StringComparer.OrdinalIgnoreCase))))
            .Select(column => new SeguridadPermisosRolColumnViewModel
            {
                Key = column.Key,
                Label = column.Label,
                Required = column.Required
            })
            .ToList();

        var selectedRole = !string.IsNullOrWhiteSpace(roleId)
            ? roleList.FirstOrDefault(role => role.Id == roleId)
            : null;

        var permisosSeleccionados = selectedRole == null
            ? []
            : await _rolService.GetPermissionsForRoleAsync(selectedRole.Id);
        var accionesSeleccionadas = permisosSeleccionados
            .Select(p => p.AccionId)
            .ToHashSet();

        var filas = modulos
            .OrderBy(m => m.Categoria)
            .ThenBy(m => m.Orden)
            .Select(modulo => new SeguridadPermisosRolRowViewModel
            {
                ModuloId = modulo.Id,
                ModuloNombre = modulo.Nombre,
                ModuloClave = modulo.Clave,
                Grupo = modulo.Categoria ?? "General",
                Descripcion = modulo.Descripcion,
                Celdas = columns
                    .Select(column => BuildPermisoRolCell(modulo, column.Key, accionesSeleccionadas))
                    .ToList()
            })
            .ToList();

        return new SeguridadPermisosRolViewModel
        {
            RolSeleccionadoId = selectedRole?.Id,
            RolSeleccionadoNombre = selectedRole?.Name,
            BuscarModulo = buscarModulo,
            GrupoSeleccionado = grupo,
            Roles = roleList
                .Select(role =>
                {
                    metadataLookup.TryGetValue(role.Id, out var metadata);
                    return new SeguridadRolSelectorItemViewModel
                    {
                        Id = role.Id,
                        Nombre = role.Name ?? string.Empty,
                        Activo = metadata?.Activo ?? true
                    };
                })
                .OrderByDescending(role => role.Activo)
                .ThenBy(role => role.Nombre)
                .ToList(),
            Grupos = filas
                .Select(f => f.Grupo)
                .Distinct()
                .OrderBy(g => g)
                .ToList(),
            Columnas = columns,
            Filas = filas
        };
    }

    private async Task<SeguridadEditarUsuarioViewModel?> BuildEditUsuarioViewModelAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var model = new SeguridadEditarUsuarioViewModel
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Nombre = user.Nombre,
            Apellido = user.Apellido,
            Telefono = user.Telefono ?? user.PhoneNumber,
            RolesSeleccionados = roles.ToList(),
            SucursalId = user.SucursalId,
            Activo = user.Activo,
            RowVersion = user.RowVersion
        };

        await PopulateEditUsuarioOptionsAsync(model);
        return model;
    }

    private async Task PopulateEditUsuarioOptionsAsync(SeguridadEditarUsuarioViewModel model)
    {
        var allRoles = await _rolService.GetAllRolesAsync();
        model.AllRoles = allRoles
            .Select(r => r.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        model.AllSucursales = await _context.GetSucursalOptionsAsync();
    }

    private async Task<RolDetalleViewModel?> BuildRoleDetailsViewModelAsync(string id)
    {
        var role = await _rolService.GetRoleByIdAsync(id);
        if (role == null)
        {
            return null;
        }

        var metadata = await _rolService.GetRoleMetadataAsync(role.Id);
        var permisos = await _rolService.GetPermissionsForRoleAsync(id);
        var usuarios = await _rolService.GetUsersInRoleAsync(role.Name ?? string.Empty, includeInactive: true);

        return new RolDetalleViewModel
        {
            Id = role.Id,
            Nombre = role.Name ?? string.Empty,
            Descripcion = metadata?.Descripcion ?? RolMetadataDefaults.GetDescripcion(role.Name),
            Activo = metadata?.Activo ?? true,
            CantidadUsuarios = usuarios.Count,
            CantidadUsuariosActivos = usuarios.Count(u => u.Activo),
            Permisos = permisos.Select(p => new PermisoViewModel
            {
                Id = p.Id,
                ModuloNombre = p.Modulo.Nombre,
                AccionNombre = p.Accion.Nombre,
                ClaimValue = p.ClaimValue
            }).ToList(),
            Usuarios = usuarios.Select(u => new UsuarioBasicoViewModel
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                UserName = u.UserName ?? string.Empty,
                NombreCompleto = u.NombreCompleto ?? string.Empty,
                UltimoAcceso = u.UltimoAcceso,
                Activo = u.Activo,
                EmailConfirmed = u.EmailConfirmed
            }).ToList()
        };
    }


    private static void NormalizeRoleCreateModel(CrearRolViewModel model)
    {
        model.Nombre = (model.Nombre ?? string.Empty).Trim();
        model.Descripcion = NormalizeNullable(model.Descripcion);
    }

    private static void NormalizeRoleEditModel(EditarRolViewModel model)
    {
        model.Nombre = (model.Nombre ?? string.Empty).Trim();
        model.Descripcion = NormalizeNullable(model.Descripcion);
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Canon(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private SeguridadPermisosRolCellViewModel BuildPermisoRolCell(
        ModuloSistema modulo,
        string columnKey,
        HashSet<int> accionesSeleccionadas)
    {
        var definition = PermissionMatrixColumns.First(column => column.Key == columnKey);
        var accion = modulo.Acciones
            .OrderBy(a => a.Orden)
            .FirstOrDefault(a => definition.Aliases.Contains(Canon(a.Clave), StringComparer.OrdinalIgnoreCase));

        if (accion == null)
        {
            return new SeguridadPermisosRolCellViewModel
            {
                ColumnKey = columnKey,
                Disponible = false
            };
        }

        return new SeguridadPermisosRolCellViewModel
        {
            ColumnKey = columnKey,
            AccionId = accion.Id,
            AccionClave = accion.Clave,
            AccionNombre = accion.Nombre,
            Disponible = true,
            Seleccionado = accionesSeleccionadas.Contains(accion.Id)
        };
    }

    private async Task<List<SeguridadRolSelectorItemViewModel>> GetRoleSelectorItemsAsync()
    {
        var roles = await _rolService.GetAllRolesAsync();
        var metadataLookup = await _context.RolMetadatas
            .AsNoTracking()
            .ToDictionaryAsync(m => m.RoleId);

        return roles
            .Select(role =>
            {
                metadataLookup.TryGetValue(role.Id, out var metadata);
                return new SeguridadRolSelectorItemViewModel
                {
                    Id = role.Id,
                    Nombre = role.Name ?? string.Empty,
                    Activo = metadata?.Activo ?? true
                };
            })
            .OrderByDescending(role => role.Activo)
            .ThenBy(role => role.Nombre)
            .ToList();
    }

    private async Task<string> BuildDuplicateRoleNameAsync(string? sourceRoleName)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceRoleName)
            ? "Rol copia"
            : $"{sourceRoleName.Trim()} Copia";

        var existingNames = await _context.Roles
            .AsNoTracking()
            .Select(r => r.Name ?? string.Empty)
            .ToListAsync();
        var existingNamesSet = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingNamesSet.Contains(baseName))
        {
            return baseName;
        }

        var suffix = 2;
        var candidate = $"{baseName} {suffix}";
        while (existingNamesSet.Contains(candidate))
        {
            suffix++;
            candidate = $"{baseName} {suffix}";
        }

        return candidate;
    }

    private sealed record PermissionMatrixColumnDefinition(
        string Key,
        string Label,
        bool Required,
        string[] Aliases);
}

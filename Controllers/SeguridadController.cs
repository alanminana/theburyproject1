using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Filters;
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
    private readonly ISeguridadAuditoriaService _seguridadAuditoria;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SeguridadController> _logger;

    public SeguridadController(
        AppDbContext context,
        IRolService rolService,
        ISeguridadAuditoriaService seguridadAuditoria,
        UserManager<ApplicationUser> userManager,
        ILogger<SeguridadController> logger)
    {
        _context = context;
        _rolService = rolService;
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

            ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);
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
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

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
            var sucursal = await GetSucursalAsync(model.SucursalId);
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

            var currentRoles = await _userManager.GetRolesAsync(user);
            var rolesToRemove = currentRoles.Except(model.RolesSeleccionados, StringComparer.OrdinalIgnoreCase).ToList();
            var rolesToAdd = model.RolesSeleccionados.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();

            var wasActive = user.Activo;
            user.UserName = model.UserName;
            user.Email = model.Email;
            user.Nombre = string.IsNullOrWhiteSpace(model.Nombre) ? null : model.Nombre.Trim();
            user.Apellido = string.IsNullOrWhiteSpace(model.Apellido) ? null : model.Apellido.Trim();
            user.Telefono = string.IsNullOrWhiteSpace(model.Telefono) ? null : model.Telefono.Trim();
            user.PhoneNumber = user.Telefono;
            user.SucursalId = sucursal?.Id;
            user.Sucursal = sucursal?.Nombre;
            user.Activo = model.Activo;

            if (wasActive && !model.Activo)
            {
                user.FechaDesactivacion = DateTime.UtcNow;
                user.DesactivadoPor = User.Identity?.Name;
                user.MotivoDesactivacion = "Desactivado desde la edición de Seguridad.";
            }
            else if (!wasActive && model.Activo)
            {
                user.FechaDesactivacion = null;
                user.DesactivadoPor = null;
                user.MotivoDesactivacion = null;
            }

            _context.Entry(user).Property(u => u.RowVersion).OriginalValue = model.RowVersion!;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                if (updateResult.Errors.Any(error =>
                    string.Equals(error.Code, "ConcurrencyFailure", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Conflicto de concurrencia al editar usuario {UserId} desde Seguridad", model.Id);
                    ModelState.AddModelError(string.Empty, "El usuario fue modificado por otro usuario. Recargá los datos antes de guardar nuevamente.");
                    return View("EditUsuario_tw", model);
                }

                foreach (var error in updateResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View("EditUsuario_tw", model);
            }

            if (rolesToRemove.Any())
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    foreach (var error in removeResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    return View("EditUsuario_tw", model);
                }
            }

            if (rolesToAdd.Any())
            {
                var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
                if (!addResult.Succeeded)
                {
                    foreach (var error in addResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    return View("EditUsuario_tw", model);
                }
            }

            _logger.LogInformation("Usuario editado desde Seguridad: {UserId} por {Admin}",
                model.Id, User.Identity?.Name);
            TempData["Success"] = $"Usuario '{model.UserName}' actualizado exitosamente.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Editar",
                $"Usuario \"{model.UserName}\"",
                $"Edición de usuario. Roles: {(model.RolesSeleccionados.Count > 0 ? string.Join(", ", model.RolesSeleccionados) : "sin roles")} · Sucursal: {sucursal?.Nombre ?? "sin sucursal"} · Estado: {(model.Activo ? "activo" : "inactivo")}.");

            var safeReturnUrl = GetSafeReturnUrl(returnUrl);
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

            ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);
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
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);
        return PartialView("_CreateRoleModal_tw", new CrearRolViewModel { Activo = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "create")]
    public async Task<IActionResult> CreateRol([FromForm] CrearRolViewModel model, string? returnUrl)
    {
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);
        NormalizeRoleCreateModel(model);
        if (string.IsNullOrWhiteSpace(model.Nombre))
        {
            ModelState.AddModelError(nameof(model.Nombre), "El nombre del rol es requerido.");
        }

        if (!ModelState.IsValid)
        {
            return JsonModelErrors();
        }

        if (await _rolService.RoleExistsAsync(model.Nombre))
        {
            ModelState.AddModelError(nameof(model.Nombre), $"El rol '{model.Nombre}' ya existe.");
            return JsonModelErrors();
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var createResult = await _rolService.CreateRoleAsync(model.Nombre);
            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return JsonModelErrors();
            }

            var role = await _rolService.GetRoleByNameAsync(model.Nombre);
            if (role == null)
            {
                ModelState.AddModelError(string.Empty, "No se pudo recuperar el rol creado.");
                return JsonModelErrors();
            }

            var metadata = await EnsureRoleMetadataAsync(role.Id, role.Name);
            metadata.Descripcion = string.IsNullOrWhiteSpace(model.Descripcion)
                ? RolMetadataDefaults.GetDescripcion(role.Name)
                : model.Descripcion;
            metadata.Activo = model.Activo;
            metadata.IsDeleted = false;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Rol creado desde Seguridad: {RoleName} por {Admin}", role.Name, User.Identity?.Name);
            TempData["Success"] = $"Rol '{role.Name}' creado correctamente.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Crear",
                $"Rol \"{role.Name}\"",
                $"Rol creado con estado {(model.Activo ? "activo" : "inactivo")}.");
            return Json(new
            {
                success = true,
                redirectUrl = safeReturnUrl ?? Url.Action(nameof(Index), new { tab = "roles" })
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error al crear rol {RoleName} desde Seguridad", model.Nombre);
            ModelState.AddModelError(string.Empty, "Error al crear el rol.");
            return JsonModelErrors();
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

        var metadata = await GetRoleMetadataAsync(role.Id);
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

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
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);
        NormalizeRoleEditModel(model);
        if (string.IsNullOrWhiteSpace(model.Nombre))
        {
            ModelState.AddModelError(nameof(model.Nombre), "El nombre del rol es requerido.");
        }

        if (!ModelState.IsValid)
        {
            return JsonModelErrors();
        }

        var role = await _rolService.GetRoleByIdAsync(model.Id);
        if (role == null)
        {
            return Json(new { success = false, errors = new[] { "Rol no encontrado." } });
        }

        var existingRole = await _rolService.GetRoleByNameAsync(model.Nombre);
        if (existingRole != null && existingRole.Id != model.Id)
        {
            ModelState.AddModelError(nameof(model.Nombre), $"El rol '{model.Nombre}' ya existe.");
            return JsonModelErrors();
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var updateResult = await _rolService.UpdateRoleAsync(model.Id, model.Nombre);
            if (!updateResult.Succeeded)
            {
                foreach (var error in updateResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return JsonModelErrors();
            }

            var metadata = await EnsureRoleMetadataAsync(model.Id, model.Nombre);
            metadata.Descripcion = string.IsNullOrWhiteSpace(model.Descripcion)
                ? RolMetadataDefaults.GetDescripcion(model.Nombre)
                : model.Descripcion;
            metadata.Activo = model.Activo;
            metadata.IsDeleted = false;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Rol editado desde Seguridad: {RoleId} por {Admin}", model.Id, User.Identity?.Name);
            TempData["Success"] = $"Rol '{model.Nombre}' actualizado correctamente.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Editar",
                $"Rol \"{model.Nombre}\"",
                $"Rol actualizado. Estado: {(model.Activo ? "activo" : "inactivo")}.");
            return Json(new
            {
                success = true,
                redirectUrl = safeReturnUrl ?? Url.Action(nameof(Index), new { tab = "roles" })
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error al editar rol {RoleId} desde Seguridad", model.Id);
            ModelState.AddModelError(string.Empty, "Error al actualizar el rol.");
            return JsonModelErrors();
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

        var metadata = await GetRoleMetadataAsync(role.Id);
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

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
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);
        model.Nombre = (model.Nombre ?? string.Empty).Trim();
        model.Descripcion = NormalizeNullable(model.Descripcion);
        if (string.IsNullOrWhiteSpace(model.Nombre))
        {
            ModelState.AddModelError(nameof(model.Nombre), "El nombre del rol es requerido.");
        }

        if (!ModelState.IsValid)
        {
            return JsonModelErrors();
        }

        var sourceRole = await _rolService.GetRoleByIdAsync(model.RolOrigenId);
        if (sourceRole == null)
        {
            return Json(new { success = false, errors = new[] { "Rol origen no encontrado." } });
        }

        if (await _rolService.RoleExistsAsync(model.Nombre))
        {
            ModelState.AddModelError(nameof(model.Nombre), $"El rol '{model.Nombre}' ya existe.");
            return JsonModelErrors();
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var createResult = await _rolService.CreateRoleAsync(model.Nombre);
            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return JsonModelErrors();
            }

            var newRole = await _rolService.GetRoleByNameAsync(model.Nombre);
            if (newRole == null)
            {
                return Json(new { success = false, errors = new[] { "No se pudo recuperar el rol duplicado." } });
            }

            var metadata = await EnsureRoleMetadataAsync(newRole.Id, newRole.Name);
            metadata.Descripcion = string.IsNullOrWhiteSpace(model.Descripcion)
                ? RolMetadataDefaults.GetDescripcion(newRole.Name)
                : model.Descripcion;
            metadata.Activo = model.Activo;
            metadata.IsDeleted = false;

            var permisosOrigen = await _rolService.GetPermissionsForRoleAsync(sourceRole.Id);
            var permisos = permisosOrigen
                .Select(p => (p.ModuloId, p.AccionId))
                .Distinct()
                .ToList();

            if (permisos.Count > 0)
            {
                await _rolService.AssignMultiplePermissionsAsync(newRole.Id, permisos);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Rol duplicado desde Seguridad: {SourceRole} -> {NewRole} por {Admin}",
                sourceRole.Name, newRole.Name, User.Identity?.Name);
            TempData["Success"] = $"Rol '{newRole.Name}' duplicado correctamente.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Crear",
                $"Rol \"{newRole.Name}\"",
                $"Rol duplicado desde \"{sourceRole.Name}\" con {permisos.Count} permisos.");
            return Json(new
            {
                success = true,
                redirectUrl = safeReturnUrl ?? Url.Action(nameof(Index), new { tab = "roles" })
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error al duplicar rol {RoleId} desde Seguridad", model.RolOrigenId);
            ModelState.AddModelError(string.Empty, "Error al duplicar el rol.");
            return JsonModelErrors();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "update")]
    public async Task<IActionResult> ToggleRolActivo(string id, bool activo, string? returnUrl)
    {
        try
        {
            var role = await _rolService.GetRoleByIdAsync(id);
            if (role == null)
            {
                TempData["Error"] = "Rol no encontrado.";
                return RedirectToAction(nameof(Index), new { tab = "roles" });
            }

            var metadata = await EnsureRoleMetadataAsync(role.Id, role.Name);
            metadata.Activo = activo;
            metadata.IsDeleted = false;

            await _context.SaveChangesAsync();

            var actionLabel = activo ? "activado" : "desactivado";
            _logger.LogInformation("Rol {RoleId} {ActionLabel} desde Seguridad por {Admin}", id, actionLabel, User.Identity?.Name);
            TempData["Success"] = $"Rol '{role.Name}' {actionLabel} correctamente.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cambiar estado del rol {RoleId}", id);
            TempData["Error"] = "Error al actualizar el estado del rol.";
        }

        var safeReturnUrl = GetSafeReturnUrl(returnUrl);
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

        var safeReturnUrl = GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null
            ? LocalRedirect(safeReturnUrl)
            : RedirectToAction(nameof(Index), new { tab = "roles" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "roles", Accion = "assignpermissions")]
    public async Task<IActionResult> SavePermisosRol(string roleId, List<int>? accionIds, string? returnUrl)
    {
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);

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

        accionIds ??= [];

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var acciones = await _context.AccionesModulo
                .AsNoTracking()
                .Where(a => accionIds.Contains(a.Id) && !a.IsDeleted && a.Activa)
                .Select(a => new { a.Id, a.ModuloId })
                .ToListAsync();

            await _rolService.ClearPermissionsForRoleAsync(roleId);

            if (acciones.Count > 0)
            {
                await _rolService.AssignMultiplePermissionsAsync(
                    roleId,
                    acciones.Select(a => (a.ModuloId, a.Id)).ToList());
            }

            await transaction.CommitAsync();

            _logger.LogInformation("Permisos guardados para rol {RoleId} por {Admin}", roleId, User.Identity?.Name);
            TempData["Success"] = $"Permisos actualizados para '{role.Name}'.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Permisos",
                $"Rol \"{role.Name}\"",
                $"{acciones.Count} permisos guardados manualmente.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
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

        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

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
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);

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
            return JsonModelErrors();
        }

        var targetRole = await _rolService.GetRoleByIdAsync(model.RolDestinoId);
        var sourceRole = await _rolService.GetRoleByIdAsync(model.RolOrigenId);

        if (targetRole == null || sourceRole == null)
        {
            return Json(new { success = false, errors = new[] { "No se encontraron los roles seleccionados." } });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var permisosOrigen = await _rolService.GetPermissionsForRoleAsync(sourceRole.Id);
            await _rolService.ClearPermissionsForRoleAsync(targetRole.Id);

            if (permisosOrigen.Count > 0)
            {
                await _rolService.AssignMultiplePermissionsAsync(
                    targetRole.Id,
                    permisosOrigen.Select(p => (p.ModuloId, p.AccionId)).Distinct().ToList());
            }

            await transaction.CommitAsync();

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
            await transaction.RollbackAsync();
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

        var sucursales = await GetSucursalOptionsAsync();
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

    private string? GetSafeReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;

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

        model.AllSucursales = await GetSucursalOptionsAsync();
    }

    private Task<List<SucursalOptionViewModel>> GetSucursalOptionsAsync()
    {
        return _context.Sucursales
            .AsNoTracking()
            .Where(s => s.Activa)
            .OrderBy(s => s.Nombre)
            .Select(s => new SucursalOptionViewModel
            {
                Id = s.Id,
                Nombre = s.Nombre
            })
            .ToListAsync();
    }

    private async Task<Sucursal?> GetSucursalAsync(int? sucursalId)
    {
        if (!sucursalId.HasValue)
        {
            return null;
        }

        return await _context.Sucursales
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sucursalId.Value && s.Activa);
    }

    private async Task<RolDetalleViewModel?> BuildRoleDetailsViewModelAsync(string id)
    {
        var role = await _rolService.GetRoleByIdAsync(id);
        if (role == null)
        {
            return null;
        }

        var metadata = await GetRoleMetadataAsync(role.Id);
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

    private async Task<RolMetadata?> GetRoleMetadataAsync(string roleId)
    {
        return await _context.RolMetadatas
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoleId == roleId);
    }

    private async Task<RolMetadata> EnsureRoleMetadataAsync(string roleId, string? roleName)
    {
        var metadata = await _context.RolMetadatas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.RoleId == roleId);

        if (metadata != null)
        {
            if (metadata.IsDeleted)
            {
                metadata.IsDeleted = false;
            }

            if (string.IsNullOrWhiteSpace(metadata.Descripcion))
            {
                metadata.Descripcion = RolMetadataDefaults.GetDescripcion(roleName);
            }

            return metadata;
        }

        metadata = new RolMetadata
        {
            RoleId = roleId,
            Descripcion = RolMetadataDefaults.GetDescripcion(roleName),
            Activo = true,
            IsDeleted = false
        };

        _context.RolMetadatas.Add(metadata);
        return metadata;
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

    private JsonResult JsonModelErrors()
    {
        var errors = ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "No se pudo completar la operación." : e.ErrorMessage)
            .Distinct()
            .ToArray();

        return Json(new
        {
            success = false,
            errors = errors.Length > 0 ? errors : new[] { "No se pudo completar la operación." }
        });
    }

    private sealed record PermissionMatrixColumnDefinition(
        string Key,
        string Label,
        bool Required,
        string[] Aliases);
}

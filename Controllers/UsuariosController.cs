using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheBuryProject.Data;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers;

/// <summary>
/// Controller para gestión de usuarios del sistema
/// </summary>
[Authorize]
[PermisoRequerido(Modulo = "usuarios", Accion = "view")]
public class UsuariosController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _context;
    private readonly IRolService _rolService;
    private readonly ISeguridadAuditoriaService _seguridadAuditoria;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UsuariosController> _logger;
    private readonly IdentityOptions _identityOptions;

    private IActionResult RedirectToReturnUrlOrIndex(string? returnUrl)
    {
        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null ? LocalRedirect(safeReturnUrl) : RedirectToAction("Index", "Seguridad", new { tab = "usuarios" });
    }

    private IActionResult RedirectToReturnUrlOrDetails(string id, string? returnUrl)
    {
        var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null ? LocalRedirect(safeReturnUrl) : RedirectToAction(nameof(Details), new { id });
    }

    public UsuariosController(
        UserManager<ApplicationUser> userManager,
        AppDbContext context,
        IRolService rolService,
        ISeguridadAuditoriaService seguridadAuditoria,
        ICurrentUserService currentUser,
        ILogger<UsuariosController> logger,
        IOptions<IdentityOptions> identityOptions)
    {
        _userManager = userManager;
        _context = context;
        _rolService = rolService;
        _seguridadAuditoria = seguridadAuditoria;
        _currentUser = currentUser;
        _logger = logger;
        _identityOptions = identityOptions.Value;
    }

    /// <summary>
    /// Muestra detalles de un usuario
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Details(string id, string? returnUrl)
    {
        if (string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

        try
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var permisos = await _rolService.GetUserEffectivePermissionsAsync(id);

            var viewModel = new UsuarioDetalleViewModel
            {
                Id = user.Id,
                Email = user.Email!,
                UserName = user.UserName!,
                EmailConfirmed = user.EmailConfirmed,
                LockoutEnabled = user.LockoutEnabled,
                LockoutEnd = user.LockoutEnd,
                Roles = roles.ToList(),
                Permisos = permisos,
                Activo = user.Activo
            };

            return View("Details_tw", viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener detalles del usuario {UserId}", id);
            TempData["Error"] = "Error al cargar los detalles del usuario";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Devuelve el partial del modal para crear usuario (AJAX)
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "usuarios", Accion = "create")]
    public async Task<IActionResult> Create()
    {
        await CargarRolesEnViewBag();
        await CargarSucursalesEnViewBag();
        return PartialView("_CreateModal_tw", new CrearUsuarioViewModel());
    }

    /// <summary>
    /// Crea un nuevo usuario via AJAX y devuelve JSON
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "usuarios", Accion = "create")]
    public async Task<IActionResult> Create([FromForm] CrearUsuarioViewModel model)
    {
        model.RolesSeleccionados = model.RolesSeleccionados
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!ModelState.IsValid)
            return this.JsonModelErrors();

        try
        {
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
                return Json(new
                {
                    success = false,
                    errors = invalidRoles.Select(r => $"El rol '{r}' no existe.").ToList()
                });
            }

            var sucursal = await _context.GetSucursalAsync(model.SucursalId);
            if (model.SucursalId.HasValue && sucursal == null)
            {
                return Json(new { success = false, errors = new[] { "La sucursal seleccionada no existe o está inactiva." } });
            }

            // Verificar duplicados
            if (await _userManager.FindByNameAsync(model.UserName) != null)
                return Json(new { success = false, errors = new[] { $"El nombre de usuario '{model.UserName}' ya está en uso." } });

            if (await _userManager.FindByEmailAsync(model.Email) != null)
                return Json(new { success = false, errors = new[] { $"El email '{model.Email}' ya está en uso." } });

            var user = new ApplicationUser
            {
                UserName = model.UserName,
                Email = model.Email,
                EmailConfirmed = model.EmailConfirmed,
                Nombre = model.Nombre,
                Apellido = model.Apellido,
                Telefono = model.Telefono,
                PhoneNumber = model.Telefono,
                SucursalId = sucursal?.Id,
                Sucursal = sucursal?.Nombre,
                Activo = model.Activo,
                FechaCreacion = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                if (model.RolesSeleccionados.Any())
                {
                    var roleResult = await _userManager.AddToRolesAsync(user, model.RolesSeleccionados);
                    if (!roleResult.Succeeded)
                    {
                        await _userManager.DeleteAsync(user);
                        return Json(new
                        {
                            success = false,
                            errors = roleResult.Errors.Select(e => e.Description).ToList()
                        });
                    }
                }

                _logger.LogInformation("Usuario creado: {Email} por {Admin}",
                    model.Email, _currentUser.GetUsername());
                TempData["Success"] = $"Usuario '{model.UserName}' creado exitosamente.";
                await _seguridadAuditoria.RegistrarEventoAsync(
                    "Seguridad",
                    "Crear",
                    $"Usuario \"{model.UserName}\"",
                    $"Alta de usuario con roles: {(model.RolesSeleccionados.Count > 0 ? string.Join(", ", model.RolesSeleccionados) : "sin roles")} · Sucursal: {sucursal?.Nombre ?? "sin sucursal"}.");
                return Json(new { success = true });
            }

            return Json(new { success = false, errors = result.Errors.Select(e => e.Description).ToList() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear usuario {Email}", model.Email);
            return Json(new { success = false, errors = new[] { "Error interno al crear el usuario." } });
        }
    }

    /// <summary>
    /// Desactiva rápidamente un usuario (soft delete)
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "usuarios", Accion = "delete")]
    public async Task<IActionResult> Desactivar(string id, string? returnUrl)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }

            if (!user.Activo)
            {
                TempData["Info"] = "El usuario ya estaba inactivo";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }

            user.Activo = false;
            user.FechaDesactivacion = DateTime.UtcNow;
            user.DesactivadoPor = _currentUser.GetUsername();
            user.MotivoDesactivacion = "Desactivación rápida desde la grilla de usuarios.";

            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                _logger.LogInformation("Usuario desactivado rápidamente: {UserId} por usuario {User}",
                    id, _currentUser.GetUsername());
                TempData["Success"] = $"Usuario '{user.UserName}' desactivado exitosamente.";
            }
            else
            {
                TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al desactivar rápidamente usuario {UserId}", id);
            TempData["Error"] = "Error al desactivar el usuario";
        }

        return RedirectToReturnUrlOrIndex(returnUrl);
    }

    /// <summary>
    /// Muestra formulario para desactivar lógicamente un usuario
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "usuarios", Accion = "delete")]
    public async Task<IActionResult> Delete(string id, string? returnUrl)
    {
        if (string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

        try
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);

            var viewModel = new EliminarUsuarioViewModel
            {
                Id = user.Id,
                Email = user.Email!,
                UserName = user.UserName!,
                Roles = roles.ToList()
            };

            return View("Delete_tw", viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar formulario de desactivación para usuario {UserId}", id);
            TempData["Error"] = "Error al cargar el formulario de desactivación";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Desactiva un usuario (soft delete)
    /// </summary>
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "usuarios", Accion = "delete")]
    public async Task<IActionResult> DeleteConfirmed(string id, string? returnUrl, string? motivo)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado";
                return RedirectToAction(nameof(Index));
            }

            // Soft delete: marcar como inactivo en lugar de eliminar
            user.Activo = false;
            user.FechaDesactivacion = DateTime.UtcNow;
            user.DesactivadoPor = _currentUser.GetUsername();
            user.MotivoDesactivacion = motivo;

            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                _logger.LogInformation("Usuario desactivado: {UserId} por usuario {User}. Motivo: {Motivo}",
                    id, _currentUser.GetUsername(), motivo ?? "No especificado");
                TempData["Success"] = "Usuario desactivado exitosamente. El usuario no podrá iniciar sesión, pero se mantiene el historial de ventas y auditorías.";
            }
            else
            {
                TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al desactivar usuario {UserId}", id);
            TempData["Error"] = "Error al desactivar el usuario";
        }

        return RedirectToReturnUrlOrIndex(returnUrl);
    }

    /// <summary>
    /// Reactiva un usuario previamente desactivado
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "usuarios", Accion = "delete")] // Mismo permiso que desactivar
    public async Task<IActionResult> Reactivar(string id, string? returnUrl)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado";
                return RedirectToAction(nameof(Index));
            }

            if (user.Activo)
            {
                TempData["Warning"] = "El usuario ya está activo";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }

            // Reactivar usuario
            user.Activo = true;
            user.FechaDesactivacion = null;
            user.DesactivadoPor = null;
            user.MotivoDesactivacion = null;

            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                _logger.LogInformation("Usuario reactivado: {UserId} por usuario {User}",
                    id, _currentUser.GetUsername());
                TempData["Success"] = "Usuario reactivado exitosamente";
            }
            else
            {
                TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al reactivar usuario {UserId}", id);
            TempData["Error"] = "Error al reactivar el usuario";
        }

        return RedirectToReturnUrlOrIndex(returnUrl);
    }

    /// <summary>
    /// Devuelve el partial del modal para cambiar contraseña (AJAX)
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "usuarios", Accion = "resetpassword")]
    public async Task<IActionResult> CambiarPassword(string id)
    {
        if (string.IsNullOrEmpty(id))
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        var viewModel = new CambiarPasswordUsuarioViewModel
        {
            UserId = user.Id,
            UserName = user.UserName!,
            RequiredLength = _identityOptions.Password.RequiredLength,
            RequireUppercase = _identityOptions.Password.RequireUppercase,
            RequireLowercase = _identityOptions.Password.RequireLowercase,
            RequireDigit = _identityOptions.Password.RequireDigit,
            RequireNonAlphanumeric = _identityOptions.Password.RequireNonAlphanumeric
        };

        return PartialView("_CambiarPasswordModal_tw", viewModel);
    }

    /// <summary>
    /// Cambia la contraseña de un usuario via AJAX y devuelve JSON
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "usuarios", Accion = "resetpassword")]
    public async Task<IActionResult> CambiarPassword([FromForm] CambiarPasswordUsuarioViewModel model)
    {
        foreach (var error in ValidatePasswordPolicy(model.NewPassword))
        {
            ModelState.AddModelError(nameof(model.NewPassword), error);
        }

        if (!ModelState.IsValid)
            return this.JsonModelErrors();

        try
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
                return Json(new { success = false, errors = new[] { "Usuario no encontrado." } });

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

            if (result.Succeeded)
            {
                _logger.LogInformation("Contraseña cambiada para usuario {UserId} por {User}",
                    model.UserId, _currentUser.GetUsername());
                await _seguridadAuditoria.RegistrarEventoAsync(
                    "Seguridad",
                    "Reset Password",
                    $"Usuario \"{user.UserName}\"",
                    "Reseteo manual de contraseña desde modal de Seguridad.");
                return Json(new { success = true });
            }

            return Json(new { success = false, errors = result.Errors.Select(e => e.Description).ToList() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cambiar contraseña de usuario {UserId}", model.UserId);
            return Json(new { success = false, errors = new[] { "Error al cambiar la contraseña." } });
        }
    }

    private IEnumerable<string> ValidatePasswordPolicy(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            yield break;
        }

        var policy = _identityOptions.Password;

        if (password.Length < policy.RequiredLength)
        {
            yield return $"La contraseña debe tener al menos {policy.RequiredLength} caracteres.";
        }

        if (policy.RequireUppercase && !password.Any(char.IsUpper))
        {
            yield return "La contraseña debe incluir al menos una letra mayúscula.";
        }

        if (policy.RequireLowercase && !password.Any(char.IsLower))
        {
            yield return "La contraseña debe incluir al menos una letra minúscula.";
        }

        if (policy.RequireDigit && !password.Any(char.IsDigit))
        {
            yield return "La contraseña debe incluir al menos un número.";
        }

        if (policy.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
        {
            yield return "La contraseña debe incluir al menos un símbolo.";
        }
    }

    /// <summary>
    /// Confirma el email de un usuario manualmente (sin token)
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "usuarios", Accion = "update")]
    public async Task<IActionResult> ConfirmarEmail(string id, string? returnUrl)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (user.EmailConfirmed)
            {
                TempData["Info"] = "El email ya estaba confirmado";
                return RedirectToReturnUrlOrDetails(id, returnUrl);
            }

            // Generar token y confirmar email
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var result = await _userManager.ConfirmEmailAsync(user, token);

            if (result.Succeeded)
            {
                _logger.LogInformation("Email confirmado para usuario {UserId} por {Admin}",
                    id, _currentUser.GetUsername());
                TempData["Success"] = $"Email confirmado exitosamente para {user.Email}. Ahora puede iniciar sesión.";
            }
            else
            {
                TempData["Error"] = "Error al confirmar el email: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al confirmar email de usuario {UserId}", id);
            TempData["Error"] = "Error al confirmar el email";
        }

        return RedirectToReturnUrlOrDetails(id, returnUrl);
    }

    /// <summary>
    /// Carga la lista de roles en ViewBag
    /// </summary>
    private async Task CargarRolesEnViewBag()
    {
        var roles = await _rolService.GetAllRolesAsync();
        ViewBag.Roles = roles.Select(r => r.Name).ToList();
    }

    private async Task CargarSucursalesEnViewBag()
    {
        var sucursales = await _context.GetSucursalOptionsAsync();
        ViewBag.Sucursales = sucursales;
    }

    /// <summary>
    /// Devuelve el partial del modal para bloquear usuario (AJAX)
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = "usuarios", Accion = "update")]
    public async Task<IActionResult> Bloquear(string id, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        var viewModel = new BloquearUsuarioViewModel
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            ReturnUrl = Url.GetSafeReturnUrl(returnUrl)
        };

        return PartialView("_BloquearUsuarioModal_tw", viewModel);
    }

    /// <summary>
    /// Bloquea un usuario con motivo y fecha opcional
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "usuarios", Accion = "update")]
    public async Task<IActionResult> Bloquear([FromForm] BloquearUsuarioViewModel model)
    {
        if (model.BloqueadoHasta.HasValue && model.BloqueadoHasta.Value <= DateTime.UtcNow)
        {
            ModelState.AddModelError(nameof(model.BloqueadoHasta), "La fecha de bloqueo debe ser posterior a la fecha actual.");
        }

        if (!ModelState.IsValid)
            return this.JsonModelErrors();

        try
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
                return Json(new { success = false, errors = new[] { "Usuario no encontrado." } });

            if (!user.Activo)
                return Json(new { success = false, errors = new[] { "No se puede bloquear un usuario inactivo." } });

            var lockoutEnd = model.BloqueadoHasta.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(model.BloqueadoHasta.Value, DateTimeKind.Local))
                : DateTimeOffset.MaxValue;

            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, lockoutEnd);

            var bloqueoLabel = model.BloqueadoHasta.HasValue
                ? $"hasta {model.BloqueadoHasta.Value:dd/MM/yyyy HH:mm}"
                : "por tiempo indefinido";

            _logger.LogInformation(
                "Usuario {UserId} bloqueado por {Admin}. Motivo: {Motivo}. Bloqueado hasta: {BloqueadoHasta}",
                model.UserId,
                _currentUser.GetUsername(),
                model.MotivoBloqueo,
                model.BloqueadoHasta?.ToString("O") ?? "indefinido");

            TempData["Success"] = $"Usuario {user.UserName} bloqueado correctamente {bloqueoLabel}.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Bloquear",
                $"Usuario \"{user.UserName}\"",
                $"Motivo: {model.MotivoBloqueo}. Vigencia: {bloqueoLabel}.");
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al bloquear usuario {UserId}", model.UserId);
            return Json(new { success = false, errors = new[] { "Error al bloquear el usuario." } });
        }
    }

    /// <summary>
    /// Desbloquea un usuario (remueve lockout)
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "usuarios", Accion = "update")]
    public async Task<IActionResult> Desbloquear(string id, string? returnUrl)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            await _userManager.SetLockoutEndDateAsync(user, null);

            _logger.LogInformation("Usuario {UserId} desbloqueado por {Admin}", id, _currentUser.GetUsername());
            TempData["Success"] = $"Usuario {user.UserName} desbloqueado correctamente.";
            await _seguridadAuditoria.RegistrarEventoAsync(
                "Seguridad",
                "Desbloquear",
                $"Usuario \"{user.UserName}\"",
                "Desbloqueo manual de usuario.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al desbloquear usuario {UserId}", id);
            TempData["Error"] = "Error al desbloquear el usuario.";
        }
        return RedirectToReturnUrlOrIndex(returnUrl);
    }

    /// <summary>
    /// Acción masiva sobre múltiples usuarios
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAction(string ids, string accion, string? rol, int? sucursalId, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(ids))
        {
            TempData["Error"] = "No se seleccionaron usuarios.";
            return RedirectToReturnUrlOrIndex(returnUrl);
        }

        if (string.IsNullOrWhiteSpace(accion))
        {
            TempData["Error"] = "No se indicó ninguna acción masiva.";
            return RedirectToReturnUrlOrIndex(returnUrl);
        }

        var permisoRequerido = accion switch
        {
            "activar" or "desactivar" => (modulo: "usuarios", accion: "delete"),
            "bloquear" or "cambiarSucursal" => (modulo: "usuarios", accion: "update"),
            "asignarRol" => (modulo: "usuarios", accion: "assignroles"),
            _ => default
        };

        if (string.IsNullOrWhiteSpace(permisoRequerido.modulo) || !_currentUser.HasPermission(permisoRequerido.modulo, permisoRequerido.accion))
        {
            TempData["Error"] = "No tenés permisos para ejecutar esta acción masiva.";
            return RedirectToReturnUrlOrIndex(returnUrl);
        }

        if (accion == "asignarRol")
        {
            rol = rol?.Trim();
            if (string.IsNullOrWhiteSpace(rol))
            {
                TempData["Error"] = "Debés seleccionar un rol para la asignación masiva.";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }

            if (!await _rolService.RoleExistsAsync(rol))
            {
                TempData["Error"] = $"El rol '{rol}' no existe.";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        Sucursal? sucursalDestino = null;
        if (accion == "cambiarSucursal")
        {
            if (!sucursalId.HasValue)
            {
                TempData["Error"] = "Debés indicar una sucursal para el cambio masivo.";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }

            sucursalDestino = await _context.GetSucursalAsync(sucursalId);
            if (sucursalDestino == null)
            {
                TempData["Error"] = "La sucursal seleccionada no existe o está inactiva.";
                return RedirectToReturnUrlOrIndex(returnUrl);
            }
        }

        var userIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = 0;
        var errores = new List<string>();

        try
        {
            foreach (var uid in userIds)
            {
                var user = await _userManager.FindByIdAsync(uid);
                if (user == null) continue;

                switch (accion)
                {
                    case "activar":
                        if (user.Activo)
                        {
                            continue;
                        }

                        user.Activo = true;
                        user.FechaDesactivacion = null;
                        user.DesactivadoPor = null;
                        user.MotivoDesactivacion = null;

                        var activarResult = await _userManager.UpdateAsync(user);
                        if (activarResult.Succeeded)
                        {
                            count++;
                        }
                        else
                        {
                            errores.Add($"{user.UserName}: {string.Join(", ", activarResult.Errors.Select(e => e.Description))}");
                        }
                        break;

                    case "desactivar":
                        if (!user.Activo)
                        {
                            continue;
                        }

                        user.Activo = false;
                        user.FechaDesactivacion = DateTime.UtcNow;
                        user.DesactivadoPor = _currentUser.GetUsername();
                        user.MotivoDesactivacion = "Desactivación masiva desde Seguridad.";

                        var desactivarResult = await _userManager.UpdateAsync(user);
                        if (desactivarResult.Succeeded)
                        {
                            count++;
                        }
                        else
                        {
                            errores.Add($"{user.UserName}: {string.Join(", ", desactivarResult.Errors.Select(e => e.Description))}");
                        }
                        break;

                    case "bloquear":
                        if (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
                        {
                            continue;
                        }

                        await _userManager.SetLockoutEnabledAsync(user, true);
                        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                        count++;
                        break;

                    case "asignarRol":
                        if (rol != null && !await _userManager.IsInRoleAsync(user, rol))
                        {
                            var addToRoleResult = await _userManager.AddToRoleAsync(user, rol);
                            if (addToRoleResult.Succeeded)
                            {
                                count++;
                            }
                            else
                            {
                                errores.Add($"{user.UserName}: {string.Join(", ", addToRoleResult.Errors.Select(e => e.Description))}");
                            }
                        }
                        break;

                    case "cambiarSucursal":
                        if (sucursalDestino != null && user.SucursalId == sucursalDestino.Id)
                        {
                            continue;
                        }

                        user.SucursalId = sucursalDestino?.Id;
                        user.Sucursal = sucursalDestino?.Nombre;
                        var sucursalResult = await _userManager.UpdateAsync(user);
                        if (sucursalResult.Succeeded)
                        {
                            count++;
                        }
                        else
                        {
                            errores.Add($"{user.UserName}: {string.Join(", ", sucursalResult.Errors.Select(e => e.Description))}");
                        }
                        break;

                    default:
                        TempData["Error"] = $"Acción '{accion}' no reconocida.";
                        return RedirectToReturnUrlOrIndex(returnUrl);
                }
            }

            var accionLabel = accion switch
            {
                "activar" => "activados",
                "desactivar" => "desactivados",
                "bloquear" => "bloqueados",
                "asignarRol" => $"actualizados con el rol '{rol}'",
                "cambiarSucursal" => $"movidos a la sucursal '{sucursalDestino?.Nombre}'",
                _ => accion
            };

            _logger.LogInformation("Acción masiva '{Accion}' ejecutada sobre {Count} usuarios por {Admin}",
                accion, count, _currentUser.GetUsername());

            if (count > 0)
            {
                TempData["Success"] = $"{count} usuario(s) {accionLabel} correctamente.";
            }
            else if (!errores.Any())
            {
                TempData["Error"] = "La acción masiva no produjo cambios sobre los usuarios seleccionados.";
            }

            if (errores.Any())
            {
                var resumen = errores.Count <= 3
                    ? string.Join(" | ", errores)
                    : $"Se produjeron errores en {errores.Count} usuario(s).";
                TempData["Error"] = resumen;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en acción masiva '{Accion}'", accion);
            TempData["Error"] = "Error al ejecutar la acción masiva.";
        }

        return RedirectToReturnUrlOrIndex(returnUrl);
    }
}

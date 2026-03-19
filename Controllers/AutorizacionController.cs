using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers;

/// <summary>
/// Controlador para gestión de autorizaciones y umbrales
/// </summary>
[Authorize]
[PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.Ver)]
public class AutorizacionController : Controller
{
    private readonly IAutorizacionService _autorizacionService;
    private readonly UserManager<ApplicationUser> _userManager;

    private string? GetSafeReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    private IActionResult RedirectToReturnUrlOrIndex(string? returnUrl)
    {
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null ? LocalRedirect(safeReturnUrl) : RedirectToAction(nameof(Index));
    }

    private IActionResult RedirectToReturnUrlOrSolicitudes(string? returnUrl)
    {
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);
        return safeReturnUrl != null ? LocalRedirect(safeReturnUrl) : RedirectToAction(nameof(Solicitudes));
    }

    public AutorizacionController(
        IAutorizacionService autorizacionService,
        UserManager<ApplicationUser> userManager)
    {
        _autorizacionService = autorizacionService;
        _userManager = userManager;
    }

    #region Umbrales

    /// <summary>
    /// Lista de todos los umbrales configurados
    /// </summary>    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.GestionarUmbrales)]
    public async Task<IActionResult> Index(string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        var umbrales = await _autorizacionService.ObtenerTodosUmbralesAsync();

        var viewModel = new UmbralesListViewModel
        {
            Umbrales = umbrales,
            UmbralesPorRol = umbrales
                .GroupBy(u => u.Rol)
                .ToDictionary(g => g.Key, g => g.ToList())
        };

        return View(viewModel);
    }

    /// <summary>
    /// Formulario para crear nuevo umbral
    /// </summary>    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.GestionarUmbrales)]
    [HttpGet]
    public IActionResult CrearUmbral(string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);
        ViewBag.Roles = new List<string> { Roles.Administrador, Roles.Gerente, Roles.Vendedor, Roles.Contador };
        return View(new UmbralAutorizacionViewModel());
    }

    /// <summary>
    /// Procesar creación de umbral
    /// </summary>    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.GestionarUmbrales)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearUmbral(UmbralAutorizacionViewModel model, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            ViewBag.Roles = new List<string> { Roles.Administrador, Roles.Gerente, Roles.Vendedor, Roles.Contador };
            return View(model);
        }

        try
        {
            var umbral = new UmbralAutorizacion
            {
                Rol = model.Rol,
                TipoUmbral = model.TipoUmbral,
                ValorMaximo = model.ValorMaximo,
                Descripcion = model.Descripcion,
                Activo = model.Activo
            };

            await _autorizacionService.CrearUmbralAsync(umbral);
            TempData["Success"] = "Umbral creado exitosamente";
            return RedirectToReturnUrlOrIndex(returnUrl);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.Roles = new List<string> { Roles.Administrador, Roles.Gerente, Roles.Vendedor, Roles.Contador };
            return View(model);
        }
    }

    /// <summary>
    /// Formulario para editar umbral
    /// </summary>    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.GestionarUmbrales)]
    [HttpGet]
    public async Task<IActionResult> EditarUmbral(int id, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        var umbral = await _autorizacionService.ObtenerUmbralAsync(id);
        if (umbral == null)
        {
            TempData["Error"] = "Umbral no encontrado";
            return RedirectToReturnUrlOrIndex(returnUrl);
        }

        var viewModel = new UmbralAutorizacionViewModel
        {
            Id = umbral.Id,
            Rol = umbral.Rol,
            TipoUmbral = umbral.TipoUmbral,
            ValorMaximo = umbral.ValorMaximo,
            Descripcion = umbral.Descripcion,
            Activo = umbral.Activo
        };

        ViewBag.Roles = new List<string> { Roles.Administrador, Roles.Gerente, Roles.Vendedor, Roles.Contador };
        return View(viewModel);
    }

    /// <summary>
    /// Procesar edición de umbral
    /// </summary>    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.GestionarUmbrales)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarUmbral(UmbralAutorizacionViewModel model, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            ViewBag.Roles = new List<string> { Roles.Administrador, Roles.Gerente, Roles.Vendedor, Roles.Contador };
            return View(model);
        }

        try
        {
            var umbral = new UmbralAutorizacion
            {
                Id = model.Id,
                ValorMaximo = model.ValorMaximo,
                Descripcion = model.Descripcion,
                Activo = model.Activo
            };

            await _autorizacionService.ActualizarUmbralAsync(umbral);
            TempData["Success"] = "Umbral actualizado exitosamente";
            return RedirectToReturnUrlOrIndex(returnUrl);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.Roles = new List<string> { Roles.Administrador, Roles.Gerente, Roles.Vendedor, Roles.Contador };
            return View(model);
        }
    }

    /// <summary>
    /// Eliminar umbral
    /// </summary>    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.GestionarUmbrales)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarUmbral(int id, string? returnUrl)
    {
        try
        {
            await _autorizacionService.EliminarUmbralAsync(id);
            TempData["Success"] = "Umbral eliminado exitosamente";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al eliminar umbral: {ex.Message}";
        }

        return RedirectToReturnUrlOrIndex(returnUrl);
    }

    #endregion

    #region Solicitudes

    /// <summary>
    /// Lista de solicitudes de autorización
    /// </summary>    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.Ver)]
    public async Task<IActionResult> Solicitudes(string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        var usuario = await _userManager.GetUserAsync(User);
        var todasSolicitudes = await _autorizacionService.ObtenerTodasSolicitudesAsync();
        var misSolicitudes = await _autorizacionService.ObtenerSolicitudesPorUsuarioAsync(usuario?.UserName ?? "");

        var viewModel = new SolicitudesListViewModel
        {
            Pendientes = todasSolicitudes.Where(s => s.Estado == EstadoSolicitud.Pendiente).ToList(),
            Resueltas = todasSolicitudes.Where(s => s.Estado != EstadoSolicitud.Pendiente).ToList(),
            MisSolicitudes = misSolicitudes,
            TotalPendientes = todasSolicitudes.Count(s => s.Estado == EstadoSolicitud.Pendiente),
            TotalAprobadas = todasSolicitudes.Count(s => s.Estado == EstadoSolicitud.Aprobada),
            TotalRechazadas = todasSolicitudes.Count(s => s.Estado == EstadoSolicitud.Rechazada)
        };

        return View(viewModel);
    }

    /// <summary>
    /// Ver detalles de una solicitud
    /// </summary>
    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.Ver)]
    public async Task<IActionResult> DetallesSolicitud(int id, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        var solicitud = await _autorizacionService.ObtenerSolicitudAsync(id);
        if (solicitud == null)
        {
            TempData["Error"] = "Solicitud no encontrada";
            return RedirectToAction(nameof(Solicitudes));
        }

        var viewModel = new GestionarSolicitudViewModel
        {
            Id = solicitud.Id,
            UsuarioSolicitante = solicitud.UsuarioSolicitante,
            RolSolicitante = solicitud.RolSolicitante,
            TipoUmbral = solicitud.TipoUmbral,
            ValorSolicitado = solicitud.ValorSolicitado,
            ValorPermitido = solicitud.ValorPermitido,
            TipoOperacion = solicitud.TipoOperacion,
            ReferenciaOperacionId = solicitud.ReferenciaOperacionId,
            Justificacion = solicitud.Justificacion,
            Estado = solicitud.Estado,
            FechaSolicitud = solicitud.CreatedAt,
            ComentarioResolucion = solicitud.ComentarioResolucion
        };

        return View(viewModel);
    }

    /// <summary>
    /// Formulario para crear nueva solicitud
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.Ver)]
    public IActionResult CrearSolicitud(string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);
        return View(new CrearSolicitudAutorizacionViewModel());
    }

    /// <summary>
    /// Procesar creación de solicitud
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.Ver)]
    public async Task<IActionResult> CrearSolicitud(CrearSolicitudAutorizacionViewModel model, string? returnUrl)
    {
        ViewData["ReturnUrl"] = GetSafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var usuario = await _userManager.GetUserAsync(User);
            var roles = await _userManager.GetRolesAsync(usuario!);
            var rol = roles.FirstOrDefault(r => Roles.GetAllRoles().Contains(r)) ?? Roles.Vendedor;

            var solicitud = new SolicitudAutorizacion
            {
                UsuarioSolicitante = usuario?.UserName ?? "Desconocido",
                RolSolicitante = rol,
                TipoUmbral = model.TipoUmbral,
                ValorSolicitado = model.ValorSolicitado,
                ValorPermitido = model.ValorPermitido,
                TipoOperacion = model.TipoOperacion,
                ReferenciaOperacionId = model.ReferenciaOperacionId,
                Justificacion = model.Justificacion
            };

            await _autorizacionService.CrearSolicitudAsync(solicitud);
            TempData["Success"] = "Solicitud de autorización creada exitosamente. Aguarde aprobación de un superior.";
            return RedirectToReturnUrlOrSolicitudes(returnUrl);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Error al crear solicitud: {ex.Message}");
            return View(model);
        }
    }

    /// <summary>
    /// Aprobar solicitud
    /// </summary>    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.Aprobar)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AprobarSolicitud(int id, string? comentario, string? returnUrl)
    {
        try
        {
            var usuario = await _userManager.GetUserAsync(User);
            await _autorizacionService.AprobarSolicitudAsync(id, usuario?.UserName ?? Roles.Administrador, comentario);
            TempData["Success"] = "Solicitud aprobada exitosamente";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al aprobar solicitud: {ex.Message}";
        }

        return RedirectToReturnUrlOrSolicitudes(returnUrl);
    }

    /// <summary>
    /// Rechazar solicitud
    /// </summary>    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.Rechazar)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RechazarSolicitud(int id, string comentario, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(comentario))
        {
            TempData["Error"] = "Debe proporcionar un comentario para rechazar la solicitud";
            return RedirectToAction(nameof(DetallesSolicitud), new { id, returnUrl });
        }

        try
        {
            var usuario = await _userManager.GetUserAsync(User);
            await _autorizacionService.RechazarSolicitudAsync(id, usuario?.UserName ?? Roles.Administrador, comentario);
            TempData["Success"] = "Solicitud rechazada";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al rechazar solicitud: {ex.Message}";
        }

        return RedirectToReturnUrlOrSolicitudes(returnUrl);
    }

    /// <summary>
    /// Cancelar solicitud propia
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = AutorizacionesConstants.Modulo, Accion = AutorizacionesConstants.Acciones.Ver)]
    public async Task<IActionResult> CancelarSolicitud(int id, string? returnUrl)
    {
        try
        {
            await _autorizacionService.CancelarSolicitudAsync(id);
            TempData["Success"] = "Solicitud cancelada";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al cancelar solicitud: {ex.Message}";
        }

        return RedirectToReturnUrlOrSolicitudes(returnUrl);
    }

    #endregion
}

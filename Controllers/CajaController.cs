// FILE: Controllers/CajaController.cs
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// Controlador para gestión de cajas y arqueos
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "caja", Accion = "view")]
    public class CajaController : Controller
    {
        private readonly ICajaService _cajaService;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<CajaController> _logger;
        private readonly IMapper _mapper;

        public CajaController(
            ICajaService cajaService,
            ICurrentUserService currentUser,
            ILogger<CajaController> logger,
            IMapper mapper)
        {
            _cajaService = cajaService;
            _currentUser = currentUser;
            _logger = logger;
            _mapper = mapper;
        }

        #region CRUD de Cajas

        /// <summary>
        /// Módulo principal de cajas: muestra cajas activas/inactivas y aperturas abiertas
        /// </summary>
        public async Task<IActionResult> Index()
        {
            var cajas = await _cajaService.ObtenerTodasCajasAsync();
            var aperturasAbiertas = await _cajaService.ObtenerAperturasAbiertasAsync();

            var viewModel = new CajasListViewModel
            {
                CajasActivas = cajas.Where(c => c.Activa).ToList(),
                CajasInactivas = cajas.Where(c => !c.Activa).ToList(),
                AperturasAbiertas = aperturasAbiertas
            };

            ViewBag.CurrentUser = _currentUser.GetUsername();
            ViewBag.EsAdmin = _currentUser.IsInRole("SuperAdmin");

            return View("Index_tw", viewModel);
        }

        public IActionResult Create()
        {
            return View("Create_tw");
        }

        public IActionResult CreatePartial()
        {
            return PartialView("_CreateModal_tw", new CajaViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CajaViewModel model)
        {
            var isAjax = Request.Headers.XRequestedWith == "XMLHttpRequest";

            if (!ModelState.IsValid)
            {
                if (isAjax)
                    return Json(new { ok = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
                return View("Create_tw", model);
            }

            try
            {
                await _cajaService.CrearCajaAsync(model);
                if (isAjax)
                    return Json(new { ok = true, entity = new { id = model.Id, codigo = model.Codigo, nombre = model.Nombre, sucursal = model.Sucursal, ubicacion = model.Ubicacion, activa = model.Activa } });
                TempData["Success"] = "Caja creada exitosamente";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear caja");
                if (isAjax)
                    return Json(new { ok = false, errors = new[] { ex.Message } });
                TempData["Error"] = ex.Message;
                return View("Create_tw", model);
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            var caja = await _cajaService.ObtenerCajaPorIdAsync(id);
            if (caja == null)
            {
                TempData["Error"] = "Caja no encontrada";
                return RedirectToAction(nameof(Index));
            }

            // Usar AutoMapper
            var model = _mapper.Map<CajaViewModel>(caja);

            return View("Edit_tw", model);
        }

        public async Task<IActionResult> EditPartial(int id)
        {
            var caja = await _cajaService.ObtenerCajaPorIdAsync(id);
            if (caja == null)
                return NotFound();

            var model = _mapper.Map<CajaViewModel>(caja);
            return PartialView("_EditModal_tw", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CajaViewModel model)
        {
            var isAjax = Request.Headers.XRequestedWith == "XMLHttpRequest";

            if (id != model.Id)
            {
                if (isAjax)
                    return Json(new { ok = false, errors = new[] { "Caja no encontrada" } });
                TempData["Error"] = "Caja no encontrada";
                return RedirectToAction(nameof(Index));
            }

            var rowVersion = model.RowVersion;
            if (rowVersion is null || rowVersion.Length == 0)
            {
                var msg = "No se recibió la versión de fila (RowVersion). Recargá la página e intentá nuevamente.";
                if (isAjax)
                    return Json(new { ok = false, errors = new[] { msg } });
                ModelState.AddModelError("", msg);
                return View("Edit_tw", model);
            }

            if (!ModelState.IsValid)
            {
                if (isAjax)
                    return Json(new { ok = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
                return View("Edit_tw", model);
            }

            try
            {
                await _cajaService.ActualizarCajaAsync(id, model);
                if (isAjax)
                    return Json(new { ok = true, entity = new { id = model.Id, codigo = model.Codigo, nombre = model.Nombre, sucursal = model.Sucursal, ubicacion = model.Ubicacion, activa = model.Activa } });
                TempData["Success"] = "Caja actualizada exitosamente";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar caja");
                if (isAjax)
                    return Json(new { ok = false, errors = new[] { ex.Message } });
                TempData["Error"] = ex.Message;
                return View("Edit_tw", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, byte[]? rowVersion)
        {
            try
            {
                if (rowVersion is null || rowVersion.Length == 0)
                {
                    TempData["Error"] = "No se recibió la versión de fila (RowVersion). Recargá la página e intentá nuevamente.";
                    return RedirectToAction(nameof(Index));
                }

                await _cajaService.EliminarCajaAsync(id, rowVersion);
                TempData["Success"] = "Caja eliminada exitosamente";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar caja");
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Apertura de Caja

        public async Task<IActionResult> Abrir(int? cajaId)
        {
            var cajas = await SetCajasActivasSelectListAsync(cajaId);

            var model = new AbrirCajaViewModel();
            if (cajaId.HasValue)
            {
                model.CajaId = cajaId.Value;
                var caja = cajas.FirstOrDefault(c => c.Id == cajaId.Value);
                if (caja != null)
                {
                    model.CajaNombre = caja.Nombre;
                    model.CajaCodigo = caja.Codigo;
                }
            }

            return View("Abrir_tw", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Abrir(AbrirCajaViewModel model)
        {
            if (!ModelState.IsValid)
            {
                _ = await SetCajasActivasSelectListAsync(model.CajaId);
                return View("Abrir_tw", model);
            }

            try
            {
                var apertura = await _cajaService.AbrirCajaAsync(model, _currentUser.GetUsername());

                TempData["Success"] = $"Caja abierta exitosamente con ${model.MontoInicial:N2}";
                return RedirectToAction(nameof(DetallesApertura), new { id = apertura.Id });
            }
            catch (InvalidOperationException ex)
            {
                // Ej: "La caja ya tiene una apertura activa"
                _logger.LogWarning(ex, "Validación al abrir caja");
                TempData["Error"] = ex.Message;
                _ = await SetCajasActivasSelectListAsync(model.CajaId);
                return View("Abrir_tw", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al abrir caja");
                _ = await SetCajasActivasSelectListAsync(model.CajaId);
                TempData["Error"] = "Error al abrir la caja";
                return View("Abrir_tw", model);
            }
        }

        #endregion

        #region Movimientos

        public async Task<IActionResult> RegistrarMovimiento(int aperturaId)
        {
            var apertura = await _cajaService.ObtenerAperturaPorIdAsync(aperturaId);
            if (apertura == null)
            {
                TempData["Error"] = "Apertura no encontrada";
                return RedirectToAction(nameof(Index));
            }

            var saldo = await _cajaService.CalcularSaldoActualAsync(aperturaId);

            var model = new MovimientoCajaViewModel
            {
                AperturaCajaId = aperturaId,
                CajaNombre = apertura.Caja.Nombre,
                SaldoActual = saldo
            };

            return View("RegistrarMovimiento_tw", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarMovimiento(MovimientoCajaViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await TryPopulateMovimientoContextAsync(model);
                return View("RegistrarMovimiento_tw", model);
            }

            try
            {
                await _cajaService.RegistrarMovimientoAsync(model, _currentUser.GetUsername());

                TempData["Success"] = "Movimiento registrado exitosamente";
                return RedirectToAction(nameof(DetallesApertura), new { id = model.AperturaCajaId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar movimiento");
                await TryPopulateMovimientoContextAsync(model);
                TempData["Error"] = ex.Message;
                return View("RegistrarMovimiento_tw", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "caja", Accion = "edit")]
        public async Task<IActionResult> AcreditarMovimiento(int movimientoId, int aperturaId)
        {
            try
            {
                await _cajaService.AcreditarMovimientoAsync(movimientoId, _currentUser.GetUsername());
                TempData["Success"] = "Movimiento acreditado correctamente.";
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Validación al acreditar movimiento {MovimientoId}", movimientoId);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al acreditar movimiento {MovimientoId}", movimientoId);
                TempData["Error"] = "Error al acreditar el movimiento.";
            }

            return RedirectToAction(nameof(DetallesApertura), new { id = aperturaId });
        }

        #endregion

        #region Cierre de Caja

        public async Task<IActionResult> Cerrar(int aperturaId, string? returnUrl = null)
        {
            try
            {
                var detalles = await _cajaService.ObtenerDetallesAperturaAsync(aperturaId);

                ViewBag.ReturnUrl = returnUrl;

                var model = new CerrarCajaViewModel
                {
                    AperturaCajaId = aperturaId,
                    MontoInicialSistema = detalles.Apertura.MontoInicial,
                    TotalIngresosSistema = detalles.TotalIngresos,
                    TotalEgresosSistema = detalles.TotalEgresos,
                    MontoEsperadoSistema = detalles.SaldoActual,
                    CajaNombre = detalles.Apertura.Caja.Nombre,
                    FechaApertura = detalles.Apertura.FechaApertura,
                    UsuarioApertura = detalles.Apertura.UsuarioApertura,
                    Movimientos = detalles.Movimientos
                };

                return View("Cerrar_tw", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar formulario de cierre");
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cerrar(CerrarCajaViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                await TryPopulateCerrarModelAsync(model);
                ViewBag.ReturnUrl = returnUrl;
                return View("Cerrar_tw", model);
            }

            try
            {
                var cierre = await _cajaService.CerrarCajaAsync(model, _currentUser.GetUsername());

                if (cierre.TieneDiferencia)
                {
                    TempData["Warning"] = $"Caja cerrada con diferencia de ${cierre.Diferencia:N2}";
                }
                else
                {
                    TempData["Success"] = "Caja cerrada exitosamente sin diferencias";
                }

                var safeReturnUrl = (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                    ? returnUrl
                    : null;

                return RedirectToAction(nameof(DetallesCierre), new { id = cierre.Id, returnUrl = safeReturnUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cerrar caja");
                await TryPopulateCerrarModelAsync(model);
                ViewBag.ReturnUrl = returnUrl;
                TempData["Error"] = ex.Message;
                return View("Cerrar_tw", model);
            }
        }

        #endregion

        #region Detalles y Reportes

        public async Task<IActionResult> DetallesApertura(int id)
        {
            try
            {
                var detalles = await _cajaService.ObtenerDetallesAperturaAsync(id);

                ViewBag.CurrentUser = _currentUser.GetUsername();
                ViewBag.EsAdmin = _currentUser.IsInRole("SuperAdmin");

                return View("DetallesApertura_tw", detalles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar detalles de apertura");
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        public async Task<IActionResult> DetallesCierre(int id, string? returnUrl = null)
        {
            var cierre = await _cajaService.ObtenerCierrePorIdAsync(id);
            if (cierre == null)
            {
                TempData["Error"] = "Cierre no encontrado";
                return RedirectToAction(nameof(Historial));
            }

            ViewBag.ReturnUrl = returnUrl;

            return View("DetallesCierre_tw", cierre);
        }

        /// <summary>
        /// Historial de cierres de caja con filtros
        /// </summary>
        public async Task<IActionResult> Historial(int? cajaId, DateTime? fechaDesde, DateTime? fechaHasta)
        {
            var viewModel = await _cajaService.ObtenerEstadisticasCierresAsync(cajaId, fechaDesde, fechaHasta);

            await SetHistorialFiltersAsync(cajaId, fechaDesde, fechaHasta);

            return View("Historial_tw", viewModel);
        }

        #endregion

        #region Helpers

        private async Task<List<Caja>> SetCajasActivasSelectListAsync(int? selectedId)
        {
            var cajas = await _cajaService.ObtenerTodasCajasAsync();
            ViewBag.Cajas = new SelectList(cajas.Where(c => c.Activa), "Id", "Nombre", selectedId);
            return cajas;
        }

        private async Task SetHistorialFiltersAsync(int? cajaId, DateTime? fechaDesde, DateTime? fechaHasta)
        {
            var cajas = await _cajaService.ObtenerTodasCajasAsync();
            ViewBag.Cajas = new SelectList(cajas, "Id", "Nombre", cajaId);
            ViewBag.FechaDesde = fechaDesde;
            ViewBag.FechaHasta = fechaHasta;
        }

        private async Task TryPopulateMovimientoContextAsync(MovimientoCajaViewModel model)
        {
            var apertura = await _cajaService.ObtenerAperturaPorIdAsync(model.AperturaCajaId);
            if (apertura != null)
            {
                model.CajaNombre = apertura.Caja.Nombre;
                model.SaldoActual = await _cajaService.CalcularSaldoActualAsync(model.AperturaCajaId);
            }
        }

        private async Task TryPopulateCerrarModelAsync(CerrarCajaViewModel model)
        {
            var detalles = await _cajaService.ObtenerDetallesAperturaAsync(model.AperturaCajaId);
            model.CajaNombre = detalles.Apertura.Caja.Nombre;
            model.FechaApertura = detalles.Apertura.FechaApertura;
            model.UsuarioApertura = detalles.Apertura.UsuarioApertura;
            model.Movimientos = detalles.Movimientos;
        }

        #endregion
    }
}

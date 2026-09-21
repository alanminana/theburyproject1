// FILE: Controllers/CajaController.cs
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
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
        private readonly ICajaVendedorService _cajaVendedorService;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<CajaController> _logger;
        private readonly IMapper _mapper;
        private readonly AppDbContext _context;
        private readonly IRelojComercial _relojComercial;

        public CajaController(
            ICajaService cajaService,
            ICajaVendedorService cajaVendedorService,
            ICurrentUserService currentUser,
            ILogger<CajaController> logger,
            IMapper mapper,
            AppDbContext context,
            IRelojComercial relojComercial)
        {
            _cajaService = cajaService;
            _cajaVendedorService = cajaVendedorService;
            _currentUser = currentUser;
            _logger = logger;
            _mapper = mapper;
            _context = context;
            _relojComercial = relojComercial;
        }

        #region CRUD de Cajas

        /// <summary>
        /// Módulo principal de cajas: muestra cajas activas/inactivas y aperturas abiertas
        /// </summary>
        public async Task<IActionResult> Index()
        {
            var cajas = await _cajaService.ObtenerTodasCajasAsync();
            var aperturasAbiertas = await _cajaService.ObtenerAperturasAbiertasAsync();

            var hoyComercial = _relojComercial.HoyComercial;
            var zonaComercial = _relojComercial.ZonaComercial;

            var viewModel = new CajasListViewModel
            {
                CajasActivas = cajas.Where(c => c.Activa).ToList(),
                CajasInactivas = cajas.Where(c => !c.Activa).ToList(),
                AperturasAbiertas = aperturasAbiertas,
                // Efectivo esperado calculado por el backend (misma logica que el detalle/cierre)
                ResumenFisicoPorApertura = aperturasAbiertas.ToDictionary(
                    a => a.Id,
                    a => Services.CajaService.CalcularResumenFisico(a)),
                // Aperturas de un día comercial anterior: bloqueadas para operar hasta cerrarlas.
                AperturasVencidasIds = aperturasAbiertas
                    .Where(a => Services.CajaService.EsAperturaVencida(a, hoyComercial, zonaComercial))
                    .Select(a => a.Id)
                    .ToHashSet()
            };

            ViewBag.CurrentUser = _currentUser.GetUsername();
            ViewBag.EsAdmin = EsAdminCaja();
            // Cajas que el usuario puede operar (padrón): la vista oculta "Abrir" en las demás.
            ViewBag.CajasOperables = (await _cajaVendedorService.ObtenerCajaIdsDeUsuarioAsync(_currentUser.GetUserId())).ToHashSet();

            return View("Index_tw", viewModel);
        }

        [PermisoRequerido(Modulo = "caja", Accion = "create")]
        public IActionResult Create()
        {
            return View("Create_tw");
        }

        [PermisoRequerido(Modulo = "caja", Accion = "create")]
        public IActionResult CreatePartial()
        {
            return PartialView("_CreateModal_tw", new CajaViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "caja", Accion = "create")]
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

        [PermisoRequerido(Modulo = "caja", Accion = "update")]
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

        [PermisoRequerido(Modulo = "caja", Accion = "update")]
        public async Task<IActionResult> EditPartial(int id)
        {
            var caja = await _cajaService.ObtenerCajaPorIdAsync(id);
            if (caja == null)
                return NotFound();

            var model = _mapper.Map<CajaViewModel>(caja);
            model.VendedoresDisponibles = await _cajaVendedorService.ObtenerVendedoresDisponiblesAsync();
            model.CajerosDisponibles = await _cajaVendedorService.ObtenerCajerosDisponiblesAsync();
            model.VendedorIds = await _cajaVendedorService.ObtenerVendedorIdsAsignadosAsync(id);
            return PartialView("_EditModal_tw", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "caja", Accion = "update")]
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

                // Padrón de vendedores: solo cuando el formulario lo gestionó (modal de edición).
                if (model.VendedoresGestionados)
                {
                    await _cajaVendedorService.AsignarVendedoresAsync(id, model.VendedorIds ?? new List<string>(), _currentUser.GetUsername());
                }

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
        [PermisoRequerido(Modulo = "caja", Accion = "delete")]
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
            var disponibles = await SetCajasActivasSelectListAsync(cajaId);

            var model = new AbrirCajaViewModel();
            if (cajaId.HasValue)
            {
                var caja = disponibles.FirstOrDefault(c => c.Id == cajaId.Value);
                if (caja == null)
                {
                    // Caja con turno abierto, inactiva o fuera del padrón: no se preselecciona.
                    TempData["Error"] = "La caja indicada no está disponible para abrir.";
                }
                else
                {
                    model.CajaId = caja.Id;
                    model.CajaNombre = caja.Nombre;
                    model.CajaCodigo = caja.Codigo;

                    // Fondo inicial por defecto = último efectivo con el que cerró la caja (editable).
                    model.MontoInicial = await _cajaService.ObtenerUltimoEfectivoCierreAsync(caja.Id) ?? 0m;
                }
            }

            return View("Abrir_tw", model);
        }

        /// <summary>
        /// Devuelve el efectivo del último cierre de la caja para prellenar el fondo inicial
        /// cuando el usuario elige la caja desde el selector.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> UltimoEfectivoCierre(int cajaId)
        {
            if (cajaId <= 0)
            {
                return Json(new { monto = 0m });
            }

            var monto = await _cajaService.ObtenerUltimoEfectivoCierreAsync(cajaId) ?? 0m;
            return Json(new { monto });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "caja", Accion = "open")]
        public async Task<IActionResult> Abrir(AbrirCajaViewModel model)
        {
            if (!ModelState.IsValid)
            {
                _ = await SetCajasActivasSelectListAsync(model.CajaId);
                return View("Abrir_tw", model);
            }

            if (!await PuedeOperarCajaAsync(model.CajaId))
            {
                TempData["Error"] = MensajeSinPermisoOperarCaja;
                return RedirectToAction(nameof(Index));
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

            if (!await PuedeOperarCajaAsync(apertura.CajaId))
            {
                TempData["Error"] = MensajeSinPermisoOperarCaja;
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
        [PermisoRequerido(Modulo = "caja", Accion = "movements")]
        public async Task<IActionResult> RegistrarMovimiento(MovimientoCajaViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await TryPopulateMovimientoContextAsync(model);
                return View("RegistrarMovimiento_tw", model);
            }

            if (!await PuedeOperarAperturaAsync(model.AperturaCajaId))
            {
                TempData["Error"] = MensajeSinPermisoOperarCaja;
                return RedirectToAction(nameof(Index));
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
            if (!await PuedeOperarAperturaAsync(aperturaId))
            {
                TempData["Error"] = MensajeSinPermisoOperarCaja;
                return RedirectToAction(nameof(DetallesApertura), new { id = aperturaId });
            }

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
                if (!await PuedeOperarAperturaAsync(aperturaId))
                {
                    TempData["Error"] = MensajeSinPermisoOperarCaja;
                    return RedirectToAction(nameof(Index));
                }

                var detalles = await _cajaService.ObtenerDetallesAperturaAsync(aperturaId);

                ViewBag.ReturnUrl = returnUrl;

                var model = new CerrarCajaViewModel
                {
                    AperturaCajaId = aperturaId,
                    MontoInicialSistema = detalles.Apertura.MontoInicial,
                    TotalIngresosSistema = detalles.TotalIngresosFisicos,
                    TotalEgresosSistema = detalles.TotalEgresosFisicos,
                    MontoEsperadoSistema = detalles.CajaFisicaEsperada,
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
        [PermisoRequerido(Modulo = "caja", Accion = "close")]
        public async Task<IActionResult> Cerrar(CerrarCajaViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                await TryPopulateCerrarModelAsync(model);
                ViewBag.ReturnUrl = returnUrl;
                return View("Cerrar_tw", model);
            }

            if (!await PuedeOperarAperturaAsync(model.AperturaCajaId))
            {
                TempData["Error"] = MensajeSinPermisoOperarCaja;
                return RedirectToAction(nameof(Index));
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
            catch (DiferenciaCajaSinJustificacionException ex)
            {
                // Rechazo de validación esperado (ya registrado por el servicio): error pegado al campo.
                ModelState.AddModelError(nameof(model.JustificacionDiferencia), ex.Message);
                await TryPopulateCerrarModelAsync(model);
                ViewBag.ReturnUrl = returnUrl;
                return View("Cerrar_tw", model);
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
                var puedeOperar = await PuedeOperarAperturaAsync(id);
                var cuotaCreditoMap = await ObtenerMapaCuotaCreditoAsync(detalles.Movimientos);

                var viewModel = CajaConciliacionBuilder.Build(detalles, detalles.Apertura.Cierre, puedeOperar, cuotaCreditoMap);

                return View("DetallesApertura_tw", viewModel);
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

            // El detalle operativo del turno (movimientos, ventas y mercadería) se reutiliza
            // del mismo cálculo que la apertura para que el cierre muestre la misma trazabilidad.
            var detalle = await _cajaService.ObtenerDetallesAperturaAsync(cierre.AperturaCajaId);

            ViewBag.ReturnUrl = returnUrl;

            var cuotaCreditoMap = await ObtenerMapaCuotaCreditoAsync(detalle.Movimientos);

            return View("DetallesCierre_tw", CajaConciliacionBuilder.Build(detalle, cierre, puedeOperar: false, cuotaCreditoMap));
        }

        /// <summary>
        /// Mapa cuotaId ⇒ creditoId para los cobros de cuota del turno: el builder lo usa para
        /// linkear esas referencias (que guardan el id de la cuota) al detalle del crédito.
        /// </summary>
        private async Task<IReadOnlyDictionary<int, int>> ObtenerMapaCuotaCreditoAsync(
            IEnumerable<MovimientoCaja>? movimientos)
        {
            var cuotaIds = (movimientos ?? Enumerable.Empty<MovimientoCaja>())
                .Where(m => m.Concepto == ConceptoMovimientoCaja.CobroCuota
                         && m.ReferenciaId.HasValue
                         && !m.IsDeleted)
                .Select(m => m.ReferenciaId!.Value)
                .Distinct()
                .ToList();

            if (cuotaIds.Count == 0)
                return new Dictionary<int, int>();

            return await _context.Cuotas
                .AsNoTracking()
                .Where(c => cuotaIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.CreditoId);
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

        /// <summary>
        /// Capacidad de administracion de caja para la UI: roles administrativos
        /// (SuperAdmin/Administrador) o cualquier usuario con permiso caja/update.
        /// Refleja "el admin o el usuario que tenga los permisos" y se mantiene
        /// alineada con el gating por accion de los POST de escritura (create/update/delete).
        /// </summary>
        private bool EsAdminCaja() =>
            _currentUser.IsInRole(Roles.SuperAdmin)
            || _currentUser.IsInRole(Roles.Administrador)
            || _currentUser.HasPermission("caja", "update");

        /// <summary>
        /// Supervisor de caja: roles administrativos que pueden operar cualquier caja sin estar
        /// en el padrón ("a excepción del admin"). Es el bypass del enforcement de operación.
        /// </summary>
        private bool EsSupervisorCaja() =>
            _currentUser.IsInRole(Roles.SuperAdmin)
            || _currentUser.IsInRole(Roles.Administrador);

        /// <summary>
        /// Enforcement de operación: el usuario puede operar la caja si es supervisor (admin)
        /// o si está asignado a su padrón. Estricto: caja sin padrón ⇒ solo admin.
        /// </summary>
        private async Task<bool> PuedeOperarCajaAsync(int cajaId)
        {
            if (EsSupervisorCaja())
            {
                return true;
            }

            return await _cajaVendedorService.EsMiembroAsync(cajaId, _currentUser.GetUserId());
        }

        /// <summary>Variante por apertura: resuelve la caja de la apertura y aplica <see cref="PuedeOperarCajaAsync"/>.</summary>
        private async Task<bool> PuedeOperarAperturaAsync(int aperturaId)
        {
            var apertura = await _cajaService.ObtenerAperturaPorIdAsync(aperturaId);
            if (apertura == null)
            {
                // Apertura inexistente: dejar que el flujo normal devuelva "no encontrada".
                return true;
            }

            return await PuedeOperarCajaAsync(apertura.CajaId);
        }

        private const string MensajeSinPermisoOperarCaja =
            "No estás habilitado para operar esta caja. Pedí al administrador que te asigne a ella.";

        /// <summary>
        /// Carga en ViewBag.Cajas solo las cajas que se pueden abrir ahora: activas, sin turno abierto
        /// y, para no supervisores, dentro de su padrón. Devuelve esa misma lista.
        /// </summary>
        private async Task<List<Caja>> SetCajasActivasSelectListAsync(int? selectedId)
        {
            var cajas = await _cajaService.ObtenerTodasCajasAsync();
            var activas = cajas.Where(c => c.Activa).AsEnumerable();

            // Mismo criterio que AbrirCajaAsync ("La caja ya tiene una apertura activa").
            var cajaIdsConTurnoAbierto = (await _cajaService.ObtenerAperturasAbiertasAsync())
                .Select(a => a.CajaId)
                .ToHashSet();
            activas = activas.Where(c => !cajaIdsConTurnoAbierto.Contains(c.Id));

            // Enforcement: un usuario no supervisor solo puede abrir las cajas de su padrón.
            if (!EsSupervisorCaja())
            {
                var cajasHabilitadas = (await _cajaVendedorService.ObtenerCajaIdsDeUsuarioAsync(_currentUser.GetUserId())).ToHashSet();
                activas = activas.Where(c => cajasHabilitadas.Contains(c.Id));
            }

            var disponibles = activas.ToList();
            ViewBag.Cajas = new SelectList(disponibles, "Id", "Nombre", selectedId);
            return disponibles;
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
            model.MontoInicialSistema = detalles.Apertura.MontoInicial;
            model.TotalIngresosSistema = detalles.TotalIngresosFisicos;
            model.TotalEgresosSistema = detalles.TotalEgresosFisicos;
            model.MontoEsperadoSistema = detalles.CajaFisicaEsperada;
            model.CajaNombre = detalles.Apertura.Caja.Nombre;
            model.FechaApertura = detalles.Apertura.FechaApertura;
            model.UsuarioApertura = detalles.Apertura.UsuarioApertura;
            model.Movimientos = detalles.Movimientos;
        }

        #endregion
    }
}

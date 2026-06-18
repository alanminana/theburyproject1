using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Modules.MercadoLibre.Exceptions;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;
using TheBuryProject.Modules.MercadoLibre.ViewModels;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Modules.MercadoLibre.Controllers
{
    /// <summary>
    /// Módulo MercadoLibre: conexión OAuth, prueba de conexión e importación/
    /// administración de publicaciones existentes.
    /// No crea publicaciones nuevas (fuera del MVP).
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "mercadolibre", Accion = "view")]
    public class MercadoLibreController : Controller
    {
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreAccountService _accountService;
        private readonly IMercadoLibreListingService _listingService;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IMercadoLibreSyncService _syncService;
        private readonly IMercadoLibreOrderService _orderService;
        private readonly IMercadoLibreListingAdminService _listingAdminService;
        private readonly IMercadoLibrePriceBatchService _priceBatchService;
        private readonly IMercadoLibrePublicacionService _publicacionService;
        private readonly IMercadoLibreCategoriaService _categoriaService;
        private readonly IMercadoLibreCategoryCatalogService _catalogService;
        private readonly IMercadoLibreCategoryCatalogImportService _catalogImportService;
        private readonly IMercadoLibreDashboardService _dashboardService;
        private readonly IMercadoLibreQuestionService _questionService;
        private readonly IMercadoLibreMessageService _messageService;
        private readonly ICatalogLookupService _catalogLookupService;
        private readonly IClienteLookupService _clienteLookupService;
        private readonly IFileStorageService _fileStorage;
        private readonly IMapper _mapper;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly ILogger<MercadoLibreController> _logger;

        /// <summary>Extensiones de imagen aceptadas para borradores ML (subconjunto validado con magic bytes).</summary>
        private static readonly HashSet<string> ExtensionesImagenMl =
            new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

        public MercadoLibreController(
            IMercadoLibreAuthService authService,
            IMercadoLibreAccountService accountService,
            IMercadoLibreListingService listingService,
            IMercadoLibreConfiguracionService configuracionService,
            IMercadoLibreSyncService syncService,
            IMercadoLibreOrderService orderService,
            IMercadoLibreListingAdminService listingAdminService,
            IMercadoLibrePriceBatchService priceBatchService,
            IMercadoLibrePublicacionService publicacionService,
            IMercadoLibreCategoriaService categoriaService,
            IMercadoLibreCategoryCatalogService catalogService,
            IMercadoLibreCategoryCatalogImportService catalogImportService,
            IMercadoLibreDashboardService dashboardService,
            IMercadoLibreQuestionService questionService,
            IMercadoLibreMessageService messageService,
            ICatalogLookupService catalogLookupService,
            IClienteLookupService clienteLookupService,
            IFileStorageService fileStorage,
            IMapper mapper,
            IHostEnvironment hostEnvironment,
            ILogger<MercadoLibreController> logger)
        {
            _authService = authService;
            _accountService = accountService;
            _listingService = listingService;
            _configuracionService = configuracionService;
            _syncService = syncService;
            _orderService = orderService;
            _listingAdminService = listingAdminService;
            _priceBatchService = priceBatchService;
            _publicacionService = publicacionService;
            _categoriaService = categoriaService;
            _catalogService = catalogService;
            _catalogImportService = catalogImportService;
            _dashboardService = dashboardService;
            _questionService = questionService;
            _messageService = messageService;
            _catalogLookupService = catalogLookupService;
            _clienteLookupService = clienteLookupService;
            _fileStorage = fileStorage;
            _mapper = mapper;
            _hostEnvironment = hostEnvironment;
            _logger = logger;
        }

        // GET: /MercadoLibre
        public async Task<IActionResult> Index()
        {
            var cuentas = await _accountService.GetCuentasAsync();

            var viewModel = new MercadoLibreConexionViewModel
            {
                ModuloConfigurado = _authService.EstaConfigurado,
                Cuentas = _mapper.Map<List<MercadoLibreCuentaViewModel>>(cuentas)
            };

            return View(viewModel);
        }

        // GET: /MercadoLibre/Dashboard — panel operativo del canal (Fase 17).
        // Centro de operación: KPIs, alertas y accesos. Solo lectura.
        public async Task<IActionResult> Dashboard()
        {
            var viewModel = await _dashboardService.GetDashboardAsync(HttpContext.RequestAborted);
            return View(viewModel);
        }

        // POST: /MercadoLibre/Conectar — redirige al consentimiento de Mercado Libre
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "connect")]
        public IActionResult Conectar()
        {
            try
            {
                var url = _authService.BuildAuthorizationUrl();
                return Redirect(url);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: /MercadoLibre/OAuthCallback?code=...&state=...
        // Debe coincidir con la Redirect URI registrada en Mercado Libre Developers.
        [HttpGet]
        public async Task<IActionResult> OAuthCallback(string? code, string? state, string? error, string? error_description)
        {
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("Callback OAuth de Mercado Libre devolvió error: {Error}", error);
                TempData["Error"] = $"Mercado Libre rechazó la autorización: {error_description ?? error}";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["Error"] = "El callback de Mercado Libre no incluyó el parámetro code.";
                return RedirectToAction(nameof(Index));
            }

            if (!_authService.ValidarState(state))
            {
                _logger.LogWarning("Callback OAuth de Mercado Libre con state inválido o expirado");
                TempData["Error"] = "El state del callback es inválido o expiró. Reintentar la conexión desde el ERP.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var cuenta = await _authService.HandleOAuthCallbackAsync(code);
                TempData["Success"] = $"Cuenta {cuenta.Nickname} conectada correctamente.";
            }
            catch (MercadoLibreApiException ex)
            {
                TempData["Error"] = $"No se pudo completar la conexión: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /MercadoLibre/ProbarConexion
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProbarConexion(int id)
        {
            var (ok, mensaje) = await _accountService.ProbarConexionAsync(id);

            if (ok)
                TempData["Success"] = mensaje;
            else
                TempData["Error"] = mensaje;

            return RedirectToAction(nameof(Index));
        }

        // POST: /MercadoLibre/Desconectar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "connect")]
        public async Task<IActionResult> Desconectar(int id)
        {
            await _accountService.DesconectarAsync(id);
            TempData["Success"] = "Cuenta desconectada. Los tokens fueron eliminados.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /MercadoLibre/Listings?filtro=vinculadas|sin-vincular
        public async Task<IActionResult> Listings(string? filtro = null)
        {
            var listings = await _listingService.GetListingsAsync(filtro);

            var (_, _, productos) = await _catalogLookupService.GetCategoriasMarcasYProductosAsync();

            ViewBag.Filtro = filtro;
            ViewBag.ProductosPickerJson = System.Text.Json.JsonSerializer.Serialize(
                productos.OrderBy(p => p.Nombre).Select(p => new
                {
                    id = p.Id,
                    codigo = p.Codigo,
                    nombre = p.Nombre
                }));

            return View(listings);
        }

        // POST: /MercadoLibre/ImportarListings
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> ImportarListings(int accountId)
        {
            try
            {
                var resultado = await _listingService.ImportarPublicacionesAsync(accountId);

                TempData["Success"] =
                    $"Importación completada: {resultado.TotalEncontradas} encontradas, " +
                    $"{resultado.Creadas} nuevas, {resultado.Actualizadas} actualizadas" +
                    (resultado.Errores > 0 ? $", {resultado.Errores} con error." : ".");
            }
            catch (MercadoLibreApiException ex)
            {
                TempData["Error"] = $"La importación falló: {ex.Message}";
            }

            return RedirectToAction(nameof(Listings));
        }

        // POST: /MercadoLibre/SyncPreview — preview de push stock/precio (no toca nada)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> SyncPreview(int[] listingIds, string tipo = "StockYPrecio")
        {
            if (listingIds is null || listingIds.Length == 0)
            {
                TempData["Error"] = "Seleccioná al menos una publicación para sincronizar.";
                return RedirectToAction(nameof(Listings));
            }

            var tipoSync = ParseTipoSync(tipo);
            var preview = await _syncService.PrepararPreviewAsync(listingIds, tipoSync);

            return View(preview);
        }

        // POST: /MercadoLibre/SyncAplicar — aplica (o simula) el push
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> SyncAplicar(int[] listingIds, string tipo, bool confirmarReal = false)
        {
            if (listingIds is null || listingIds.Length == 0)
            {
                TempData["Error"] = "No hay publicaciones para sincronizar.";
                return RedirectToAction(nameof(Listings));
            }

            var tipoSync = ParseTipoSync(tipo);
            var usuario = User.Identity?.Name ?? "Sistema";

            var resultado = await _syncService.AplicarAsync(listingIds, tipoSync, confirmarReal, usuario);

            var prefijo = resultado.FueSimulado ? "SIMULACIÓN: " : "";
            var resumen = $"{prefijo}{resultado.Exitosos} sincronizadas, {resultado.Fallidos} con error, {resultado.Omitidos} omitidas.";

            if (resultado.Fallidos > 0)
                TempData["Error"] = resumen + " Ver detalle en el log de sincronización.";
            else
                TempData["Success"] = resumen;

            return RedirectToAction(nameof(Listings));
        }

        private static Services.Interfaces.MercadoLibreSyncTipo ParseTipoSync(string tipo)
            => tipo?.ToLowerInvariant() switch
            {
                "stock" => Services.Interfaces.MercadoLibreSyncTipo.Stock,
                "precio" => Services.Interfaces.MercadoLibreSyncTipo.Precio,
                _ => Services.Interfaces.MercadoLibreSyncTipo.StockYPrecio
            };

        // GET: /MercadoLibre/Configuracion — configuración del canal ML (vive junto a las de Ventas)
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "config")]
        public async Task<IActionResult> Configuracion()
        {
            var viewModel = await _configuracionService.GetViewModelAsync();
            ViewBag.Clientes = await _clienteLookupService.GetClientesSelectListAsync(viewModel.ClienteMercadoLibreId);
            ViewBag.CatalogoEstado = await CargarCatalogoEstadoAsync();
            return View(viewModel);
        }

        /// <summary>Estado del catálogo local + ruta sugerida para el input de importación.</summary>
        private async Task<MercadoLibreCatalogoEstadoVm> CargarCatalogoEstadoAsync()
        {
            var estado = await _catalogService.GetEstadoAsync();
            estado.RutaSugerida = string.IsNullOrWhiteSpace(estado.SourceFilePath)
                ? @"E:\theburyproject1\_ml-cache\mla_categories_with_attributes.json.gz"
                : estado.SourceFilePath;
            return estado;
        }

        // POST: /MercadoLibre/ImportarCatalogo — importa el catálogo desde un archivo LOCAL.
        // Solo admin (permiso config) o entorno Development. No sube el archivo: lee la ruta.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "config")]
        public async Task<IActionResult> ImportarCatalogo(string rutaArchivo)
        {
            if (!_hostEnvironment.IsDevelopment() && !User.IsInRole("Administrador"))
            {
                TempData["Error"] = "La importación del catálogo está restringida a administradores.";
                return RedirectToAction(nameof(Configuracion));
            }

            if (string.IsNullOrWhiteSpace(rutaArchivo))
            {
                TempData["Error"] = "Indicá la ruta local del archivo de categorías.";
                return RedirectToAction(nameof(Configuracion));
            }

            rutaArchivo = rutaArchivo.Trim().Trim('"');

            if (!System.IO.File.Exists(rutaArchivo))
            {
                TempData["Error"] = $"No existe el archivo: {rutaArchivo}";
                return RedirectToAction(nameof(Configuracion));
            }

            try
            {
                var resultado = await _catalogImportService.ImportFromFileAsync(rutaArchivo, ct: HttpContext.RequestAborted);

                if (resultado.Ok)
                    TempData["Success"] =
                        $"Catálogo importado: {resultado.ImportedCategories} categorías " +
                        $"({resultado.LeafCategories} hojas, {resultado.ListingAllowedCategories} publicables), " +
                        $"{resultado.ImportedAttributes} atributos en {resultado.DurationMs} ms.";
                else
                    TempData["Error"] = $"No se pudo importar el catálogo: {resultado.Error}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importando el catálogo ML desde {Ruta}", rutaArchivo);
                TempData["Error"] = $"Error importando el catálogo: {ex.Message}";
            }

            return RedirectToAction(nameof(Configuracion));
        }

        // POST: /MercadoLibre/Configuracion
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "config")]
        public async Task<IActionResult> Configuracion(MercadoLibreConfiguracionViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                var recargado = await _configuracionService.GetViewModelAsync();
                viewModel.CuentasDisponibles = recargado.CuentasDisponibles;
                viewModel.ListasPreciosDisponibles = recargado.ListasPreciosDisponibles;
                viewModel.SucursalesDisponibles = recargado.SucursalesDisponibles;
                ViewBag.Clientes = await _clienteLookupService.GetClientesSelectListAsync(viewModel.ClienteMercadoLibreId);
                return View(viewModel);
            }

            try
            {
                await _configuracionService.GuardarAsync(viewModel, User.Identity?.Name ?? "Sistema");
                TempData["Success"] = "Configuración de Mercado Libre guardada.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Configuracion));
        }

        // ------------------------------------------------------------------
        // ABM de publicaciones (Fase E) + calculadora (Fase G)
        // ------------------------------------------------------------------

        // GET: /MercadoLibre/Listing/5
        public async Task<IActionResult> Listing(int id)
        {
            var detalle = await _listingAdminService.GetDetalleAsync(id);

            if (detalle is null)
                return NotFound();

            var (_, _, productos) = await _catalogLookupService.GetCategoriasMarcasYProductosAsync();
            ViewBag.ProductosPickerJson = System.Text.Json.JsonSerializer.Serialize(
                productos.OrderBy(p => p.Nombre).Select(p => new
                {
                    id = p.Id,
                    codigo = p.Codigo,
                    nombre = p.Nombre
                }));

            var configListing = await _configuracionService.GetAsync();
            ViewBag.Preguntas = await _questionService.GetPreguntasPorListingAsync(id, HttpContext.RequestAborted);
            ViewBag.PuedeSimularPregunta = _hostEnvironment.IsDevelopment() || configListing.ModoSimulacion;
            ViewBag.ModoSimulacionPreguntas = configListing.ModoSimulacion;
            return View(detalle);
        }

        // POST: /MercadoLibre/ListingCambiarEstado — pausar | reactivar | finalizar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> ListingCambiarEstado(int id, string accion, bool confirmarReal = false)
        {
            var (ok, mensaje) = await _listingAdminService.CambiarEstadoAsync(
                id, accion, confirmarReal, User.Identity?.Name ?? "Sistema");

            TempData[ok ? "Success" : "Error"] = mensaje;
            return RedirectToAction(nameof(Listing), new { id });
        }

        // POST: /MercadoLibre/ListingEditar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> ListingEditar(
            int id, string? titulo, decimal? precio, int? stock, string? sellerSku, bool confirmarReal = false)
        {
            var (ok, mensaje) = await _listingAdminService.EditarAsync(
                id, titulo, precio, stock, sellerSku, confirmarReal, User.Identity?.Name ?? "Sistema");

            TempData[ok ? "Success" : "Error"] = mensaje;
            return RedirectToAction(nameof(Listing), new { id });
        }

        // POST: /MercadoLibre/ListingEditarDescripcion
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> ListingEditarDescripcion(int id, string descripcion, bool confirmarReal = false)
        {
            if (string.IsNullOrWhiteSpace(descripcion))
            {
                TempData["Error"] = "La descripción no puede estar vacía.";
                return RedirectToAction(nameof(Listing), new { id });
            }

            var (ok, mensaje) = await _listingAdminService.EditarDescripcionAsync(
                id, descripcion.Trim(), confirmarReal, User.Identity?.Name ?? "Sistema");

            TempData[ok ? "Success" : "Error"] = mensaje;
            return RedirectToAction(nameof(Listing), new { id });
        }

        // POST: /MercadoLibre/ListingEditarCategoria
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> ListingEditarCategoria(int id, string? categoryId, bool confirmarReal = false)
        {
            var (ok, mensaje) = await _listingAdminService.EditarCategoriaAsync(
                id, categoryId, confirmarReal, User.Identity?.Name ?? "Sistema");

            TempData[ok ? "Success" : "Error"] = mensaje;
            return RedirectToAction(nameof(Listing), new { id });
        }

        // ------------------------------------------------------------------
        // Aumentos masivos de precio (Fase I)
        // ------------------------------------------------------------------

        // GET: /MercadoLibre/Aumentos
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> Aumentos()
        {
            var batches = await _priceBatchService.GetBatchesAsync();
            return View(batches);
        }

        // GET: /MercadoLibre/AumentoNuevo
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> AumentoNuevo()
        {
            await CargarAumentoNuevoAsync();

            var hayVinculadas = !(ViewBag.NoHayPublicacionesVinculadas as bool? ?? false);
            return View(new MercadoLibrePriceBatchRequest
            {
                Origen = Entities.MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl,
                SoloVinculadas = hayVinculadas
            });
        }

        // POST: /MercadoLibre/AumentoSimular
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> AumentoSimular(MercadoLibrePriceBatchRequest request)
        {
            if (!ModelState.IsValid)
            {
                await CargarAumentoNuevoAsync();
                return View(nameof(AumentoNuevo), request);
            }

            var batchId = await _priceBatchService.SimularAsync(request, User.Identity?.Name ?? "Sistema");

            TempData["Success"] = "Simulación creada. Revisá el preview antes de aplicar.";
            return RedirectToAction(nameof(Aumento), new { id = batchId });
        }

        // GET: /MercadoLibre/Aumento/5
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> Aumento(int id)
        {
            var batch = await _priceBatchService.GetBatchAsync(id);

            if (batch is null)
                return NotFound();

            return View(batch);
        }

        // POST: /MercadoLibre/AumentoAplicar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> AumentoAplicar(int id, bool confirmarReal = false)
        {
            var (ok, mensaje) = await _priceBatchService.AplicarAsync(id, confirmarReal, User.Identity?.Name ?? "Sistema");
            TempData[ok ? "Success" : "Error"] = mensaje;
            return RedirectToAction(nameof(Aumento), new { id });
        }

        // POST: /MercadoLibre/AumentoRevertir
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> AumentoRevertir(int id, string? motivo, bool confirmarReal = false)
        {
            var (ok, mensaje) = await _priceBatchService.RevertirAsync(id, motivo, confirmarReal, User.Identity?.Name ?? "Sistema");
            TempData[ok ? "Success" : "Error"] = mensaje;
            return RedirectToAction(nameof(Aumento), new { id });
        }

        // POST: /MercadoLibre/AumentoCancelar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> AumentoCancelar(int id)
        {
            try
            {
                await _priceBatchService.CancelarAsync(id, User.Identity?.Name ?? "Sistema");
                TempData["Success"] = "Lote cancelado.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Aumentos));
        }

        // ------------------------------------------------------------------
        // Órdenes (Fases C, D y H)
        // ------------------------------------------------------------------

        // GET: /MercadoLibre/Ordenes?filtro=pendientes|venta-creada|liquidadas|devoluciones
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> Ordenes(string? filtro = null)
        {
            ViewBag.Filtro = filtro;
            var config = await _configuracionService.GetAsync();
            ViewBag.PuedeGenerarOrdenSimulada = _hostEnvironment.IsDevelopment() || config.ModoSimulacion;
            ViewBag.PuedeGenerarOrdenOperativaSimulada = _hostEnvironment.IsDevelopment() || config.ModoSimulacion;
            ViewBag.ModoSimulacion = config.ModoSimulacion;
            var ordenes = await _orderService.GetOrdenesAsync(filtro);
            return View(ordenes);
        }

        // GET: /MercadoLibre/Orden/5
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> Orden(int id)
        {
            var orden = await _orderService.GetOrdenAsync(id);

            if (orden is null)
                return NotFound();

            ViewBag.PuedeSimularEnvio = _hostEnvironment.IsDevelopment() || orden.ModoSimulacion;
            ViewBag.PuedeSimularClaim = _hostEnvironment.IsDevelopment() || orden.ModoSimulacion;
            ViewBag.Mensajes = await _messageService.GetMensajesPorOrdenAsync(id, HttpContext.RequestAborted);
            ViewBag.PuedeSimularMensaje = _hostEnvironment.IsDevelopment() || orden.ModoSimulacion;
            ViewBag.ModoSimulacionMensajes = orden.ModoSimulacion;
            return View(orden);
        }

        // POST: /MercadoLibre/OrdenGenerarSimulada
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenGenerarSimulada()
        {
            var config = await _configuracionService.GetAsync();
            var permitirPorDevelopment = _hostEnvironment.IsDevelopment();

            if (!permitirPorDevelopment && !config.ModoSimulacion)
            {
                TempData["Error"] = "La generacion de ordenes QA solo esta habilitada en Development o con ModoSimulacion=true.";
                return RedirectToAction(nameof(Ordenes));
            }

            var resultado = await _orderService.CrearOrdenSimuladaAsync(
                User.Identity?.Name ?? "Sistema",
                permitirPorDevelopment);

            TempData[resultado.Ok ? "Success" : "Error"] = resultado.Mensaje;

            if (resultado.Ok && resultado.OrderId.HasValue)
                return RedirectToAction(nameof(Orden), new { id = resultado.OrderId.Value });

            return RedirectToAction(nameof(Ordenes));
        }

        // POST: /MercadoLibre/OrdenGenerarOperativaSimulada
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenGenerarOperativaSimulada()
        {
            var config = await _configuracionService.GetAsync();
            var permitirPorDevelopment = _hostEnvironment.IsDevelopment();

            if (!permitirPorDevelopment && !config.ModoSimulacion)
            {
                TempData["Error"] = "La generacion de ordenes operativas simuladas solo esta habilitada en Development o con ModoSimulacion=true.";
                return RedirectToAction(nameof(Ordenes));
            }

            var resultado = await _orderService.CrearOrdenOperativaSimuladaAsync(
                User.Identity?.Name ?? "Sistema",
                permitirPorDevelopment);

            TempData[resultado.Ok ? "Success" : "Error"] = resultado.Mensaje;

            if (resultado.Ok && resultado.OrderId.HasValue)
                return RedirectToAction(nameof(Orden), new { id = resultado.OrderId.Value });

            return RedirectToAction(nameof(Ordenes));
        }

        // POST: /MercadoLibre/OrdenesImportarRecientes
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenesImportarRecientes()
        {
            var config = await _configuracionService.GetAsync();

            if (config.AccountId is null)
            {
                TempData["Error"] = "Configurá la cuenta de Mercado Libre antes de importar órdenes.";
                return RedirectToAction(nameof(Ordenes));
            }

            try
            {
                var importadas = await _orderService.ImportarOrdenesRecientesAsync(config.AccountId.Value);
                TempData["Success"] = $"Importación completada: {importadas} órdenes procesadas.";
            }
            catch (MercadoLibreApiException ex)
            {
                TempData["Error"] = $"La importación de órdenes falló: {ex.Message}";
            }

            return RedirectToAction(nameof(Ordenes));
        }

        // POST: /MercadoLibre/OrdenCrearVenta
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenCrearVenta(int id)
        {
            try
            {
                var resultado = await _orderService.CrearVentaInternaAsync(id, User.Identity?.Name ?? "Sistema");

                if (resultado.VentaCreada)
                    TempData["Success"] = $"Venta {resultado.VentaNumero} creada y stock descontado.";
                else
                    TempData["Error"] = resultado.Mensaje ?? "No se pudo crear la venta.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Orden), new { id });
        }

        // POST: /MercadoLibre/OrdenLiquidar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenLiquidar(int id, decimal netoReal)
        {
            try
            {
                await _orderService.RegistrarLiquidacionAsync(id, netoReal, User.Identity?.Name ?? "Sistema");
                TempData["Success"] = $"Liquidación registrada en caja por {netoReal:N2}.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Orden), new { id });
        }

        // POST: /MercadoLibre/OrdenDevolucion
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenDevolucion(int id, Entities.MercadoLibreDevolucionEstado decision, string? nota)
        {
            try
            {
                await _orderService.DecidirDevolucionAsync(id, decision, nota, User.Identity?.Name ?? "Sistema");

                TempData["Success"] = decision == Entities.MercadoLibreDevolucionEstado.StockReingresado
                    ? "Devolución resuelta: stock reingresado."
                    : $"Devolución resuelta como {decision}.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Orden), new { id });
        }

        // POST: /MercadoLibre/OrdenSimularClaim
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenSimularClaim(int id, Entities.MercadoLibreClaimTipo tipo, string? motivo)
        {
            var config = await _configuracionService.GetAsync();
            var permitirPorDevelopment = _hostEnvironment.IsDevelopment();

            if (!permitirPorDevelopment && !config.ModoSimulacion)
            {
                TempData["Error"] = "La simulacion de reclamos/devoluciones solo esta habilitada en Development o con ModoSimulacion=true.";
                return RedirectToAction(nameof(Orden), new { id });
            }

            try
            {
                var resultado = await _orderService.SimularClaimAsync(
                    id,
                    tipo,
                    motivo,
                    User.Identity?.Name ?? "Sistema",
                    permitirPorDevelopment);

                TempData[resultado.Ok ? "Success" : "Error"] = resultado.Mensaje;
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Orden), new { id });
        }

        // POST: /MercadoLibre/OrdenResolverClaim
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenResolverClaim(
            int id,
            int claimId,
            Entities.MercadoLibreClaimAccionStock accionStock,
            Entities.MercadoLibreClaimAccionEconomica accionEconomica,
            string? resolucionManual,
            string? observaciones)
        {
            try
            {
                await _orderService.ResolverClaimAsync(
                    claimId,
                    accionStock,
                    accionEconomica,
                    resolucionManual,
                    observaciones,
                    User.Identity?.Name ?? "Sistema");

                TempData["Success"] = accionStock == Entities.MercadoLibreClaimAccionStock.ReingresarStock
                    ? "Claim resuelto: stock reingresado manualmente."
                    : $"Claim resuelto con accion de stock {accionStock}.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Orden), new { id });
        }

        // POST: /MercadoLibre/OrdenIgnorar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenIgnorar(int id)
        {
            try
            {
                await _orderService.MarcarIgnoradaAsync(id, User.Identity?.Name ?? "Sistema");
                TempData["Success"] = "Orden marcada como ignorada.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Orden), new { id });
        }

        // POST: /MercadoLibre/OrdenActualizarEnvio
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenActualizarEnvio(int id)
        {
            try
            {
                await _orderService.ActualizarEnvioAsync(id);
                TempData["Success"] = "Estado del envío actualizado.";
            }
            catch (MercadoLibreApiException ex)
            {
                TempData["Error"] = $"No se pudo consultar el envío: {ex.Message}";
            }

            return RedirectToAction(nameof(Orden), new { id });
        }

        // POST: /MercadoLibre/OrdenSimularEnvio
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenSimularEnvio(int id, string escenario)
        {
            var config = await _configuracionService.GetAsync();
            var permitirPorDevelopment = _hostEnvironment.IsDevelopment();

            if (!permitirPorDevelopment && !config.ModoSimulacion)
            {
                TempData["Error"] = "La simulacion de envio solo esta habilitada en Development o con ModoSimulacion=true.";
                return RedirectToAction(nameof(Orden), new { id });
            }

            try
            {
                var resultado = await _orderService.SimularEnvioAsync(
                    id,
                    escenario,
                    User.Identity?.Name ?? "Sistema",
                    permitirPorDevelopment);

                TempData[resultado.Ok ? "Success" : "Error"] = resultado.Mensaje;
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Orden), new { id });
        }

        // POST: /MercadoLibre/OrdenAsignarUnidades — asignación manual de unidades físicas
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> OrdenAsignarUnidades(int id, int orderItemId, int[] unidadIds)
        {
            try
            {
                await _orderService.AsignarUnidadesAsync(
                    id, orderItemId, unidadIds ?? Array.Empty<int>(), User.Identity?.Name ?? "Sistema");

                TempData["Success"] = "Unidades físicas asignadas. Ya podés crear la venta interna.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Orden), new { id });
        }

        // ------------------------------------------------------------------
        // Crear producto desde publicación (Checkpoint 2) + origen de stock
        // ------------------------------------------------------------------

        // GET: /MercadoLibre/ListingCrearProducto/5
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "link")]
        public async Task<IActionResult> ListingCrearProducto(int id)
        {
            try
            {
                var viewModel = await _listingService.GetCrearProductoViewModelAsync(id);

                if (viewModel is null)
                    return NotFound();

                await CargarCategoriasYMarcasAsync();
                return View(viewModel);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Listings));
            }
        }

        // POST: /MercadoLibre/ListingCrearProducto
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "link")]
        public async Task<IActionResult> ListingCrearProducto(MercadoLibreCrearProductoViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                await CargarCategoriasYMarcasAsync();
                return View(viewModel);
            }

            try
            {
                var productoId = await _listingService.CrearProductoDesdeListingAsync(
                    viewModel, User.Identity?.Name ?? "Sistema");

                TempData["Success"] = $"Producto {viewModel.Codigo} creado (id {productoId}) y vinculado a la publicación.";
                return RedirectToAction(nameof(Listing), new { id = viewModel.ListingId });
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                await CargarCategoriasYMarcasAsync();
                return View(viewModel);
            }
        }

        // POST: /MercadoLibre/ListingOrigenStock — override del origen de stock
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> ListingOrigenStock(
            int id, Entities.MercadoLibreOrigenStock? origen, int? productoUnidadId)
        {
            try
            {
                await _listingService.ConfigurarOrigenStockAsync(
                    id, origen, productoUnidadId, User.Identity?.Name ?? "Sistema");

                TempData["Success"] = origen is null
                    ? "La publicación vuelve a usar el origen de stock global."
                    : $"Origen de stock configurado: {origen}.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Listing), new { id });
        }

        // POST: /MercadoLibre/ListingVariacionVinculo — vínculo/origen local de una variación
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> ListingVariacionVinculo(
            int id,
            long variationId,
            int? productoId,
            Entities.MercadoLibreOrigenStock? origen,
            int? productoUnidadId)
        {
            try
            {
                await _listingService.ConfigurarVariacionAsync(
                    id, variationId, productoId, origen, productoUnidadId, User.Identity?.Name ?? "Sistema");

                TempData["Success"] = $"Variación {variationId} configurada.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Listing), new { id });
        }

        // ------------------------------------------------------------------
        // Borradores de publicación (Fase F / Checkpoint 3)
        // ------------------------------------------------------------------

        // GET: /MercadoLibre/Borradores
        public async Task<IActionResult> Borradores()
        {
            var borradores = await _publicacionService.GetBorradoresAsync();

            var (_, _, productos) = await _catalogLookupService.GetCategoriasMarcasYProductosAsync();

            ViewBag.ProductosPickerJson = System.Text.Json.JsonSerializer.Serialize(
                productos.OrderBy(p => p.Nombre).Select(p => new
                {
                    id = p.Id,
                    codigo = p.Codigo,
                    nombre = p.Nombre
                }));

            var config = await _configuracionService.GetAsync();
            ViewBag.PermitirPublicacion = config.PermitirPublicacionDesdeErp;
            ViewBag.CuentaConectada = config.AccountId.HasValue;

            return View(borradores);
        }

        // POST: /MercadoLibre/BorradorCrear — borrador desde producto ERP
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> BorradorCrear(int productoId)
        {
            try
            {
                var resultado = await _publicacionService.CrearBorradorAsync(productoId, User.Identity?.Name ?? "Sistema");
                TempData[resultado.Existia ? "Info" : "Success"] = resultado.Mensaje;
                if (resultado.ListingId.HasValue)
                    return RedirectToAction(nameof(Listing), new { id = resultado.ListingId.Value });

                return RedirectToAction(nameof(Borrador), new { id = resultado.BorradorId!.Value });
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Borradores));
            }
        }

        // GET: /MercadoLibre/Borrador/5
        public async Task<IActionResult> Borrador(int id)
        {
            var borrador = await _publicacionService.GetBorradorAsync(id);

            if (borrador is null)
                return NotFound();

            return View(borrador);
        }

        // POST: /MercadoLibre/BorradorActualizar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> BorradorActualizar(MercadoLibreBorradorEditViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                var recargado = await _publicacionService.GetBorradorAsync(viewModel.Id);
                if (recargado is null)
                    return NotFound();

                // Mantener lo editado pero re-hidratar los campos solo-lectura.
                viewModel.Estado = recargado.Estado;
                viewModel.ErroresValidacion = recargado.ErroresValidacion;
                viewModel.ProductoCodigo = recargado.ProductoCodigo;
                viewModel.ProductoNombre = recargado.ProductoNombre;
                viewModel.ProductoPrecioVenta = recargado.ProductoPrecioVenta;
                viewModel.ProductoStockActual = recargado.ProductoStockActual;
                viewModel.PermitirPublicacionDesdeErp = recargado.PermitirPublicacionDesdeErp;
                viewModel.CuentaConectada = recargado.CuentaConectada;

                return View(nameof(Borrador), viewModel);
            }

            try
            {
                await _publicacionService.ActualizarBorradorAsync(viewModel, User.Identity?.Name ?? "Sistema");
                TempData["Success"] = "Borrador guardado. Validalo de nuevo antes de publicar.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Borrador), new { id = viewModel.Id });
        }

        // POST: /MercadoLibre/BorradorSubirImagen — sube una imagen local del equipo
        // y devuelve su URL para agregarla a la lista de imágenes del borrador.
        // No persiste el borrador: solo guarda el archivo y devuelve la URL (el JS la
        // agrega al campo de imágenes y se persiste al guardar el borrador).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> BorradorSubirImagen(int id, IFormFile? archivo)
        {
            var borrador = await _publicacionService.GetBorradorAsync(id);
            if (borrador is null)
                return Json(new { ok = false, error = "El borrador no existe." });
            if (!borrador.PuedeEditar)
                return Json(new { ok = false, error = "El borrador no se puede editar en su estado actual." });

            var (ok, url, error) = await GuardarImagenMlAsync(archivo, $"borrador {id}");
            return Json(ok ? new { ok = true, url } : new { ok = false, error });
        }

        // POST: /MercadoLibre/ListingSubirImagen — sube una imagen local y devuelve su URL
        // pública para agregarla a la lista de imágenes de una publicación ya existente.
        // ML descarga la imagen desde esa URL: debe ser pública (p. ej. detrás de un túnel),
        // no localhost. No aplica nada en ML: solo guarda el archivo y devuelve la URL.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> ListingSubirImagen(IFormFile? archivo)
        {
            var (ok, url, error) = await GuardarImagenMlAsync(archivo, "listing");
            return Json(ok ? new { ok = true, url } : new { ok = false, error });
        }

        // Guarda una imagen JPG/PNG validada en wwwroot/uploads/mercadolibre y devuelve
        // su URL absoluta (construida con el host de la request, para que sea pública si
        // se accede por un host público). Compartido por borradores y listings.
        private async Task<(bool Ok, string? Url, string? Error)> GuardarImagenMlAsync(
            IFormFile? archivo, string contexto)
        {
            var (valido, errorArchivo) = DocumentoValidationHelper.ValidateFile(archivo!);
            if (!valido)
                return (false, null, errorArchivo);

            var ext = Path.GetExtension(archivo!.FileName).ToLowerInvariant();
            if (!ExtensionesImagenMl.Contains(ext))
                return (false, null, "Formato no permitido. Usá imágenes JPG o PNG.");

            try
            {
                var rutaRelativa = await _fileStorage.SaveAsync(archivo, "uploads/mercadolibre");
                var url = $"{Request.Scheme}://{Request.Host}/{rutaRelativa}";
                return (true, url, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al subir imagen ({Contexto})", contexto);
                return (false, null, "No se pudo guardar la imagen.");
            }
        }

        // POST: /MercadoLibre/ListingEditarImagenes — reemplaza TODAS las imágenes de una
        // publicación existente (PUT /items {pictures}). Respeta ModoSimulacion + confirmarReal.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> ListingEditarImagenes(int id, string? imagenesUrls, bool confirmarReal = false)
        {
            var urls = (imagenesUrls ?? string.Empty)
                .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var (ok, mensaje) = await _listingAdminService.EditarImagenesAsync(
                id, urls, confirmarReal, User.Identity?.Name ?? "Sistema");

            TempData[ok ? "Success" : "Error"] = mensaje;
            return RedirectToAction(nameof(Listing), new { id });
        }

        // POST: /MercadoLibre/BorradorValidar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> BorradorValidar(int id)
        {
            try
            {
                var (ok, errores, advertencias) = await _publicacionService.ValidarAsync(id, User.Identity?.Name ?? "Sistema");

                if (ok)
                    TempData["Success"] = $"Borrador validado ({advertencias.Count} advertencia(s)). Ya se puede publicar.";
                else
                    TempData["Error"] = $"La validación encontró {errores.Count} error(es): {string.Join(" | ", errores)}";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Borrador), new { id });
        }

        // POST: /MercadoLibre/BorradorPublicar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> BorradorPublicar(int id, bool confirmarReal = false)
        {
            var (ok, mensaje) = await _publicacionService.PublicarAsync(id, confirmarReal, User.Identity?.Name ?? "Sistema");
            TempData[ok ? "Success" : "Error"] = mensaje;
            return RedirectToAction(nameof(Borrador), new { id });
        }

        // POST: /MercadoLibre/BorradorDescartar
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> BorradorDescartar(int id)
        {
            try
            {
                await _publicacionService.DescartarAsync(id, User.Identity?.Name ?? "Sistema");
                TempData["Success"] = "Borrador descartado.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Borradores));
        }

        // ------------------------------------------------------------------
        // Picker de categorías ML (AJAX). Consumo online del árbol: predictor,
        // navegado y resolución de la categoría elegida (hoja + listing_allowed).
        // ------------------------------------------------------------------

        // GET: /MercadoLibre/CategoriaSugerencias?titulo=...
        [HttpGet]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> CategoriaSugerencias(string? titulo)
        {
            try
            {
                var sugerencias = await _categoriaService.SugerirAsync(titulo, HttpContext.RequestAborted);
                return Json(new { ok = true, sugerencias });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo el predictor de categorías ML para '{Titulo}'", titulo);
                return Json(new { ok = false, error = "No se pudieron obtener sugerencias de Mercado Libre." });
            }
        }

        // GET: /MercadoLibre/CategoriaHijos?categoryId=...
        [HttpGet]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> CategoriaHijos(string? categoryId)
        {
            try
            {
                var nivel = await _categoriaService.ListarHijosAsync(categoryId, HttpContext.RequestAborted);
                return Json(new { ok = true, nivel });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo el navegado de categorías ML (categoryId={CategoryId})", categoryId);
                return Json(new { ok = false, error = "No se pudo navegar el árbol de categorías (¿hay una cuenta ML conectada?)." });
            }
        }

        // GET: /MercadoLibre/CategoriaResolver?categoryId=...
        [HttpGet]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> CategoriaResolver(string categoryId)
        {
            try
            {
                var categoria = await _categoriaService.ResolverAsync(categoryId, HttpContext.RequestAborted);
                return Json(new { ok = true, categoria });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { ok = false, error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo la resolución de categoría ML {CategoryId}", categoryId);
                return Json(new { ok = false, error = "No se pudo resolver la categoría en Mercado Libre." });
            }
        }

        // GET: /MercadoLibre/CategoriaAtributos?categoryId=...&condition=new
        // Atributos obligatorios/recomendados de la categoría desde el catálogo LOCAL.
        [HttpGet]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "sync")]
        public async Task<IActionResult> CategoriaAtributos(string categoryId, string condition = "new", string? listingTypeId = null)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
                return Json(new { ok = true, importado = true, atributos = Array.Empty<object>() });

            try
            {
                var importado = await _catalogService.HayCatalogoAsync(ct: HttpContext.RequestAborted);
                if (!importado)
                    return Json(new { ok = true, importado = false, atributos = Array.Empty<object>() });

                var atributos = await _catalogService.GetRequiredAttributesAsync(
                    categoryId, condition, listingTypeId, ct: HttpContext.RequestAborted);

                return Json(new { ok = true, importado = true, atributos });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo la carga de atributos de categoría ML {CategoryId}", categoryId);
                return Json(new { ok = false, error = "No se pudieron cargar los atributos de la categoría." });
            }
        }

        private async Task CargarCategoriasYMarcasAsync()
        {
            var (categorias, marcas, _) = await _catalogLookupService.GetCategoriasMarcasYProductosAsync();
            ViewBag.Categorias = categorias;
            ViewBag.Marcas = marcas;
        }

        private async Task CargarAumentoNuevoAsync()
        {
            var (categorias, marcas, _) = await _catalogLookupService.GetCategoriasMarcasYProductosAsync();
            ViewBag.Categorias = categorias;
            ViewBag.Marcas = marcas;

            var vinculadas = await _listingService.GetListingsAsync("vinculadas");
            ViewBag.NoHayPublicacionesVinculadas = vinculadas.Count == 0;
        }

        // POST: /MercadoLibre/Vincular
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "link")]
        public async Task<IActionResult> Vincular(int listingId, int productoId)
        {
            try
            {
                await _listingService.VincularProductoAsync(listingId, productoId);
                TempData["Success"] = "Publicación vinculada al producto.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Listings));
        }

        // POST: /MercadoLibre/Desvincular
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "link")]
        public async Task<IActionResult> Desvincular(int listingId)
        {
            try
            {
                await _listingService.DesvincularProductoAsync(listingId);
                TempData["Success"] = "Publicación desvinculada.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Listings));
        }

        // ==================================================================
        // Preguntas preventa (Fase 16)
        // ==================================================================

        // GET: /MercadoLibre/Preguntas
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> Preguntas(string? filtro = null)
        {
            var config = await _configuracionService.GetAsync();
            ViewBag.Filtro = filtro;
            ViewBag.ModoSimulacion = config.ModoSimulacion;
            ViewBag.PuedeSimular = _hostEnvironment.IsDevelopment() || config.ModoSimulacion;

            var preguntas = await _questionService.GetPreguntasAsync(filtro, HttpContext.RequestAborted);
            return View(preguntas);
        }

        // GET: /MercadoLibre/Pregunta/5
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> Pregunta(int id)
        {
            var pregunta = await _questionService.GetPreguntaAsync(id, HttpContext.RequestAborted);
            if (pregunta is null)
                return NotFound();

            var config = await _configuracionService.GetAsync();
            ViewBag.ModoSimulacion = config.ModoSimulacion;
            return View(pregunta);
        }

        // POST: /MercadoLibre/PreguntaSimular — crea una pregunta QA sobre una publicación.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> PreguntaSimular(int listingId, string texto, string? returnUrl = null)
        {
            var resultado = await _questionService.SimularPreguntaAsync(
                listingId, texto ?? string.Empty, User.Identity?.Name ?? "Sistema", _hostEnvironment.IsDevelopment());

            TempData[resultado.Ok ? "Success" : "Error"] = resultado.Mensaje;

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Listing), new { id = listingId });
        }

        // POST: /MercadoLibre/PreguntaResponder — respuesta manual (simulada o real confirmada).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> PreguntaResponder(int id, string respuesta, bool confirmarReal = false)
        {
            var resultado = await _questionService.ResponderPreguntaAsync(
                id, respuesta ?? string.Empty, confirmarReal, User.Identity?.Name ?? "Sistema");

            TempData[resultado.Ok ? "Success" : "Error"] = resultado.Mensaje;
            return RedirectToAction(nameof(Pregunta), new { id });
        }

        // ==================================================================
        // Mensajes postventa (Fase 16)
        // ==================================================================

        // GET: /MercadoLibre/Mensajes
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> Mensajes(string? filtro = null)
        {
            var config = await _configuracionService.GetAsync();
            ViewBag.Filtro = filtro;
            ViewBag.ModoSimulacion = config.ModoSimulacion;

            var mensajes = await _messageService.GetMensajesAsync(filtro, HttpContext.RequestAborted);
            return View(mensajes);
        }

        // POST: /MercadoLibre/MensajeSimular — crea un mensaje entrante QA sobre una orden.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> MensajeSimular(int orderId, string texto)
        {
            var resultado = await _messageService.SimularMensajeAsync(
                orderId, texto ?? string.Empty, User.Identity?.Name ?? "Sistema", _hostEnvironment.IsDevelopment());

            TempData[resultado.Ok ? "Success" : "Error"] = resultado.Mensaje;
            return RedirectToAction(nameof(Orden), new { id = orderId });
        }

        // POST: /MercadoLibre/MensajeResponder — respuesta manual (simulada o real confirmada).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "mercadolibre", Accion = "orders")]
        public async Task<IActionResult> MensajeResponder(int orderId, string texto, bool confirmarReal = false)
        {
            var resultado = await _messageService.ResponderMensajeAsync(
                orderId, texto ?? string.Empty, confirmarReal, User.Identity?.Name ?? "Sistema");

            TempData[resultado.Ok ? "Success" : "Error"] = resultado.Mensaje;
            return RedirectToAction(nameof(Orden), new { id = orderId });
        }
    }
}

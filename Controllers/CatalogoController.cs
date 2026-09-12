using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    // Vista unificada de catálogo (productos) con acciones puntuales de precios masivos.
    // Default a nivel clase: productos.view (mismo módulo/accion que ProductoController.Index).
    // Las acciones que mutan o consultan precios usan el módulo "precios" ya seedeado y
    // consumido por CambiosPreciosController, en vez de crear un módulo nuevo.
    [PermisoRequerido(Modulo = ModuloProductos, Accion = AccionVer)]
    public class CatalogoController : Controller
    {
        private const string ModuloProductos = "productos";
        private const string ModuloPrecios = "precios";
        private const string AccionVer = "view";
        private const string AccionActualizar = "update";
        private const string AccionSimular = "simulate";
        private const string AccionAplicar = "apply";
        private const string AccionRevertir = "revert";

        private readonly ICatalogoService _catalogoService;
        private readonly ICatalogLookupService _catalogLookupService;
        private readonly IAlertaStockService _alertaStockService;
        private readonly IMovimientoStockService _movimientoStockService;
        private readonly IProductoService _productoService;
        private readonly IMovimientoStockReferenciaResolver _movimientoReferenciaResolver;
        private readonly ILogger<CatalogoController> _logger;
        private readonly IMapper _mapper;

        public CatalogoController(
            ICatalogoService catalogoService,
            ICatalogLookupService catalogLookupService,
            IAlertaStockService alertaStockService,
            IMovimientoStockService movimientoStockService,
            IProductoService productoService,
            IMovimientoStockReferenciaResolver movimientoReferenciaResolver,
            ILogger<CatalogoController> logger,
            IMapper mapper)
        {
            _catalogoService = catalogoService;
            _catalogLookupService = catalogLookupService;
            _alertaStockService = alertaStockService;
            _movimientoStockService = movimientoStockService;
            _productoService = productoService;
            _movimientoReferenciaResolver = movimientoReferenciaResolver;
            _logger = logger;
            _mapper = mapper;
        }

        #region Vistas — Index / Resumen / Historial / Detalle

        /// <summary>
        /// Vista unificada del catálogo con productos, filtros y precios.
        /// Usa ICatalogoService.ObtenerCatalogoAsync como único punto de acceso a datos.
        /// </summary>
        public async Task<IActionResult> Index(
            string? searchTerm = null,
            int? categoriaId = null,
            int? marcaId = null,
            bool stockBajo = false,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc",
            int? listaPrecioId = null,
            string tab = "productos",
            [Bind(Prefix = "alerta")] AlertaStockFiltroViewModel? alertaFiltro = null,
            [Bind(Prefix = "mov")] MovimientoStockFilterViewModel? movFiltro = null)
        {
            try
            {
                // Construir filtros desde parámetros de query
                var filtros = new FiltrosCatalogo
                {
                    TextoBusqueda = searchTerm,
                    CategoriaId = categoriaId,
                    MarcaId = marcaId,
                    SoloStockBajo = stockBajo,
                    SoloActivos = soloActivos,
                    OrdenarPor = orderBy,
                    DireccionOrden = orderDirection,
                    ListaPrecioId = listaPrecioId
                };

                // Obtener datos del servicio (único punto de acceso)
                var resultado = await _catalogoService.ObtenerCatalogoAsync(filtros);

                // Convertir a ViewModel para la vista
                var viewModel = CatalogoUnificadoViewModel.Desde(resultado, filtros);

                // Cargar listados completos para las pestañas de Categorías y Marcas
                var (categorias, marcas) = await _catalogLookupService.GetCategoriasYMarcasAsync();
                viewModel.CategoriasListado = _mapper.Map<List<CategoriaViewModel>>(categorias);
                viewModel.MarcasListado = _mapper.Map<List<MarcaViewModel>>(marcas);

                // Alícuotas IVA para los modales de Producto
                ViewBag.AlicuotasIVADatos = await _catalogLookupService.ObtenerAlicuotasIVAParaFormAsync();

                // Pestañas Alertas / Movimientos (Fase 7): cada una respeta el permiso real de
                // su pantalla standalone (stock.viewalerts / movimientos.view), distinto del
                // cotizaciones.view que protege Catálogo. Cada bloque tiene su propio try/catch
                // para que una falla ahí no tire abajo Productos/Categorías/Marcas, que ya
                // cargaron bien.
                viewModel.MostrarTabAlertas = User.TienePermiso("stock", "viewalerts");
                viewModel.MostrarTabMovimientos = User.TienePermiso("movimientos", "view");

                var tabsValidas = new[] { "productos", "categorias", "marcas", "alertas", "movimientos" };
                var tabSolicitada = tabsValidas.Contains(tab) ? tab : "productos";
                if ((tabSolicitada == "alertas" && !viewModel.MostrarTabAlertas) ||
                    (tabSolicitada == "movimientos" && !viewModel.MostrarTabMovimientos))
                {
                    tabSolicitada = "productos";
                }
                viewModel.TabActiva = tabSolicitada;

                if (viewModel.MostrarTabAlertas)
                {
                    try
                    {
                        var filtroAlertas = alertaFiltro ?? new AlertaStockFiltroViewModel();
                        if (!filtroAlertas.Estado.HasValue)
                        {
                            filtroAlertas.Estado = EstadoAlerta.Pendiente;
                        }

                        var resultadoAlertas = await _alertaStockService.BuscarAsync(filtroAlertas);
                        var (totalPendientes, totalCriticas) = await _alertaStockService.ContarPorEstadoAsync(filtroAlertas);

                        viewModel.AlertasPartial = new AlertaStockListadoPartialViewModel
                        {
                            Resultado = resultadoAlertas,
                            Filtro = filtroAlertas,
                            TiposAlerta = Enum.GetValues<TipoAlertaStock>(),
                            Prioridades = Enum.GetValues<PrioridadAlerta>(),
                            Estados = Enum.GetValues<EstadoAlerta>(),
                            TotalPendientes = totalPendientes,
                            TotalCriticas = totalCriticas,
                            Embed = true
                        };
                    }
                    catch (Exception exAlertas)
                    {
                        _logger.LogError(exAlertas, "Error al cargar la pestaña Alertas dentro de Catálogo");
                        viewModel.AlertasPartial = null;
                    }
                }

                if (viewModel.MostrarTabMovimientos)
                {
                    try
                    {
                        var filtroMovimientos = movFiltro ?? new MovimientoStockFilterViewModel();
                        var (movimientos, total, totalEntradas, totalSalidas, totalAjustes) = await _movimientoStockService.SearchPaginadoAsync(
                            productoId: filtroMovimientos.ProductoId,
                            tipo: filtroMovimientos.Tipo,
                            fechaDesde: filtroMovimientos.FechaDesde,
                            fechaHasta: filtroMovimientos.FechaHasta,
                            orderBy: filtroMovimientos.OrderBy,
                            orderDirection: filtroMovimientos.OrderDirection,
                            pageNumber: filtroMovimientos.PageNumber,
                            pageSize: filtroMovimientos.PageSize);

                        var movimientosVm = _mapper.Map<List<MovimientoStockViewModel>>(movimientos);
                        await _movimientoReferenciaResolver.EnriquecerAsync(movimientosVm);

                        filtroMovimientos.Movimientos = movimientosVm;
                        filtroMovimientos.TotalResultados = total;
                        filtroMovimientos.TotalEntradas = totalEntradas;
                        filtroMovimientos.TotalSalidas = totalSalidas;
                        filtroMovimientos.TotalAjustes = totalAjustes;

                        var productosParaFiltro = await _productoService.GetAllAsync();

                        viewModel.MovimientosPartial = new MovimientoStockListadoPartialViewModel
                        {
                            Filtro = filtroMovimientos,
                            Productos = new SelectList(productosParaFiltro.OrderBy(p => p.Nombre), "Id", "Nombre", filtroMovimientos.ProductoId),
                            Tipos = new SelectList(Enum.GetValues(typeof(TipoMovimiento))),
                            Embed = true
                        };
                    }
                    catch (Exception exMovimientos)
                    {
                        _logger.LogError(exMovimientos, "Error al cargar la pestaña Movimientos dentro de Catálogo");
                        viewModel.MovimientosPartial = null;
                    }
                }

                return View("Index_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener catálogo unificado");
                TempData["Error"] = "Error al cargar el catálogo. Intentá nuevamente.";
                return View("Index_tw", new CatalogoUnificadoViewModel());
            }
        }

        /// <summary>
        /// Vista legacy de resumen (categorías y marcas)
        /// </summary>
        [Route("Catalogo/Resumen")]
        public IActionResult Resumen()
        {
            TempData["Info"] = "El resumen legacy del catálogo ya no está disponible como pantalla separada.";
            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Destacado

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloProductos, Accion = AccionActualizar)]
        public async Task<IActionResult> ToggleDestacado(int productoId)
        {
            try
            {
                var esDestacado = await _catalogoService.ToggleDestacadoAsync(productoId);
                return Json(new { success = true, esDestacado });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Producto {ProductoId} no encontrado al alternar destacado", productoId);
                return NotFound(new { success = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al alternar destacado del producto {ProductoId}", productoId);
                return StatusCode(500, new { success = false, mensaje = "Error al actualizar el producto" });
            }
        }

        #endregion

        #region API de precios — Simular / Aplicar / Historial / Revertir

        // ──────────────────────────────────────────────────────────────
        // Acciones masivas de precios
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Simula un cambio masivo de precios sin persistir.
        /// Devuelve preview con Actual/Nuevo/Diferencia para confirmación.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionSimular)]
        public async Task<IActionResult> SimularCambioPrecios([FromBody] SolicitudSimulacionPrecios solicitud)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Datos de solicitud inválidos", detalles = ModelState });
                }

                var resultado = await _catalogoService.SimularCambioPreciosAsync(solicitud);

                return Json(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al simular cambio de precios");
                return StatusCode(500, new { error = "Error al simular el cambio de precios", mensaje = ex.Message });
            }
        }

        /// <summary>
        /// Aplica el cambio masivo de precios previamente simulado.
        /// Persiste los cambios con auditoría e historial.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionAplicar)]
        public async Task<IActionResult> AplicarCambioPrecios([FromBody] SolicitudAplicarPrecios solicitud)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Datos de solicitud inválidos", detalles = ModelState });
                }

                var resultado = await _catalogoService.AplicarCambioPreciosAsync(solicitud);

                if (!resultado.Exitoso)
                {
                    return BadRequest(new { error = resultado.Mensaje });
                }

                return Json(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al aplicar cambio de precios");
                return StatusCode(500, new { error = "Error al aplicar el cambio de precios", mensaje = ex.Message });
            }
        }
        /// <summary>
        /// Aplica un cambio directo de precio a productos seleccionados o filtrados desde el catálogo.
        /// Actualiza Producto.PrecioVenta, crea historial y permite revertir.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]        [Consumes("application/json")]
        [ActionName("AplicarCambioPrecioDirecto")]
        [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionAplicar)]
        public async Task<IActionResult> AplicarCambioPrecioDirectoJson([FromBody] AplicarCambioPrecioDirectoViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Datos de solicitud inválidos", detalles = ModelState });
                }

                // Llama al servicio para aplicar el cambio directo
                var resultado = await _catalogoService.AplicarCambioPrecioDirectoAsync(model);

                if (!resultado.Exitoso)
                {
                    return BadRequest(new { error = resultado.Mensaje });
                }

                return Json(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al aplicar cambio directo de precio");
                return StatusCode(500, new { error = "Error al aplicar el cambio de precio", mensaje = ex.Message });
            }
        }

        /// <summary>
        /// Aplica un cambio directo de precio desde formulario (redirect).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]        [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
        [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionAplicar)]
        public async Task<IActionResult> AplicarCambioPrecioDirecto(AplicarCambioPrecioDirectoViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    TempData["Error"] = "Datos de solicitud inválidos.";
                    return RedirectToAction(nameof(Index));
                }

                var resultado = await _catalogoService.AplicarCambioPrecioDirectoAsync(model);
                if (!resultado.Exitoso)
                {
                    TempData["Error"] = resultado.Mensaje;
                    return RedirectToAction(nameof(Index));
                }

                TempData["Success"] = resultado.Mensaje;
                if (resultado.CambioPrecioEventoId.HasValue)
                {
                    TempData["SuccessLinkUrl"] = Url.Action(nameof(DetalleCambioPrecio), new { id = resultado.CambioPrecioEventoId.Value });
                    TempData["SuccessLinkText"] = "Ver detalle";
                }
                else
                {
                    TempData["SuccessLinkUrl"] = Url.Action(nameof(HistorialCambiosPrecio));
                    TempData["SuccessLinkText"] = "Ver historial";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al aplicar cambio directo de precio (form)");
                TempData["Error"] = "Error al aplicar el cambio de precio.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]        public IActionResult HistorialCambiosPrecio()
        {
            TempData["Info"] = "El historial legacy de cambios de precio ya no está disponible como pantalla separada.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]        public IActionResult DetalleCambioPrecio(int id)
        {
            TempData["Info"] = "El detalle legacy de cambios de precio ya no está disponible como pantalla separada.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Catalogo/RevertirCambioPrecio/{eventoId:int}")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionRevertir)]
        public async Task<IActionResult> RevertirCambioPrecio(int eventoId, string? returnUrl = null)
        {
            try
            {
                var resultado = await _catalogoService.RevertirCambioPrecioAsync(eventoId);
                if (!resultado.Exitoso)
                {
                    TempData["Error"] = resultado.Mensaje;
                    if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                    {
                        return LocalRedirect(returnUrl);
                    }

                    return RedirectToAction(nameof(HistorialCambiosPrecio));
                }

                TempData["Success"] = resultado.Mensaje;
                if (resultado.EventoReversionId.HasValue)
                {
                    TempData["SuccessLinkUrl"] = Url.Action(nameof(DetalleCambioPrecio), new { id = resultado.EventoReversionId.Value });
                    TempData["SuccessLinkText"] = "Ver reversion";
                }

                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                return RedirectToAction(nameof(HistorialCambiosPrecio));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al revertir cambio de precio {EventoId}", eventoId);
                TempData["Error"] = "Error al revertir el cambio.";
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                return RedirectToAction(nameof(HistorialCambiosPrecio));
            }
        }

        /// <summary>
        /// AJAX: Obtiene el historial de cambios de precio de un producto específico.
        /// </summary>
        [HttpGet]
        [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionVer)]
        public async Task<IActionResult> HistorialPrecioProductoApi(int productoId)
        {
            try
            {
                var fila = await _catalogoService.ObtenerFilaAsync(productoId);
                if (fila == null)
                    return NotFound(new { error = "Producto no encontrado" });

                var historial = await _catalogoService.GetHistorialCambiosPrecioProductoAsync(productoId);

                return Json(new
                {
                    producto = new { fila.ProductoId, fila.Codigo, fila.Nombre },
                    items = historial.Select(h => new
                    {
                        eventoId = h.EventoId,
                        fecha = h.Fecha.ToString("dd MMM, yyyy · HH:mm"),
                        usuario = h.Usuario,
                        motivo = h.Motivo ?? "Sin motivo",
                        precioAnterior = h.PrecioAnterior,
                        precioNuevo = h.PrecioNuevo,
                        puedeRevertir = h.PuedeRevertir
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener historial de precio del producto {ProductoId}", productoId);
                return StatusCode(500, new { error = "Error al cargar el historial" });
            }
        }

        /// <summary>
        /// AJAX: Revierte un cambio de precio específico y devuelve JSON.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionRevertir)]
        public async Task<IActionResult> RevertirCambioPrecioApi(int eventoId)
        {
            try
            {
                var resultado = await _catalogoService.RevertirCambioPrecioAsync(eventoId);
                return Json(new { success = resultado.Exitoso, mensaje = resultado.Mensaje });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al revertir cambio de precio {EventoId}", eventoId);
                return StatusCode(500, new { success = false, mensaje = "Error al revertir el cambio" });
            }
        }

        #endregion
    }
}




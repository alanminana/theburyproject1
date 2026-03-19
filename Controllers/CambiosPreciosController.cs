using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Filters;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using System.Text.Json;

namespace TheBuryProject.Controllers;

/// <summary>
/// Controlador para gestión de cambios masivos de precios
/// Workflow: Simulación → Autorización → Aplicación → Reversión
/// </summary>
[Authorize]
[AutoValidateAntiforgeryToken]
[PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionVer)]
public class CambiosPreciosController : Controller
{
    private const string ModuloPrecios = "precios";
    private const string AccionVer = "view";
    private const string AccionSimular = "simulate";
    private const string AccionAprobar = "approve";
    private const string AccionAplicar = "apply";
    private const string AccionRevertir = "revert";
    private const string AccionHistorial = "history";

    private readonly AppDbContext _context;
    private readonly IPrecioService _precioService;
    private readonly IProductoService _productoService;
    private readonly ICategoriaService _categoriaService;
    private readonly IMarcaService _marcaService;
    private readonly ILogger<CambiosPreciosController> _logger;

    public CambiosPreciosController(
        AppDbContext context,
        IPrecioService precioService,
        IProductoService productoService,
        ICategoriaService categoriaService,
        IMarcaService marcaService,
        ILogger<CambiosPreciosController> logger)
    {
        _context = context;
        _precioService = precioService;
        _productoService = productoService;
        _categoriaService = categoriaService;
        _marcaService = marcaService;
        _logger = logger;
    }

    // ============================================
    // INDEX - Listar batches de cambios
    // ============================================

    /// <summary>
    /// Lista todos los batches de cambios con filtros
    /// </summary>
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionVer)]
    public async Task<IActionResult> Index(EstadoBatch? estado = null, int page = 1, int pageSize = 20)
    {
        try
        {
            var skip = (page - 1) * pageSize;
            var batches = await _precioService.GetBatchesAsync(
                estado: estado,
                fechaDesde: null,
                fechaHasta: null,
                skip: skip,
                take: pageSize);

            var viewModel = new BatchListViewModel
            {
                Batches = batches.Select(b => new BatchViewModel
                {
                    Id = b.Id,
                    Nombre = b.Nombre,
                    TipoCambio = b.TipoCambio,
                    TipoCambioDisplay = b.TipoCambio.ToString(),
                    TipoAplicacion = b.TipoAplicacion,
                    TipoAplicacionDisplay = b.TipoAplicacion.ToString(),
                    ValorCambio = b.ValorCambio,
                    Estado = b.Estado,
                    EstadoDisplay = b.Estado.ToString(),
                    CantidadProductos = b.CantidadProductos,
                    PorcentajePromedioCambio = b.PorcentajePromedioCambio,
                    SolicitadoPor = b.SolicitadoPor,
                    FechaSolicitud = b.FechaSolicitud,
                    AprobadoPor = b.AprobadoPor,
                    FechaAprobacion = b.FechaAprobacion,
                    AplicadoPor = b.AplicadoPor,
                    FechaAplicacion = b.FechaAplicacion,
                    RequiereAutorizacion = b.RequiereAutorizacion
                }).ToList(),
                EstadoFiltro = estado,
                PaginaActual = page,
                TamanioPagina = pageSize
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al listar batches de cambios");
            TempData["Error"] = "Error al cargar los batches de cambios.";
            return View(new BatchListViewModel());
        }
    }

    // ============================================
    // SIMULAR - Crear nueva simulación
    // ============================================

    /// <summary>
    /// Muestra el formulario para simular un cambio masivo de precios.
    /// Acepta parámetros opcionales para precargar filtros desde navegación cruzada.
    /// </summary>
    /// <param name="categoriasIds">IDs de categorías a preseleccionar</param>
    /// <param name="marcasIds">IDs de marcas a preseleccionar</param>
    /// <param name="productoIdsText">IDs de productos separados por comas</param>
    [HttpGet]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionSimular)]
    public async Task<IActionResult> Simular(
        [FromQuery] List<int>? categoriasIds = null,
        [FromQuery] List<int>? marcasIds = null,
        [FromQuery] string? productoIdsText = null)
    {
        try
        {
            await CargarDatosParaSimulacion();

            var viewModel = new SimularCambioMasivoViewModel
            {
                TipoCambio = TipoCambio.PorcentajeSobrePrecioActual,
                TipoAplicacion = TipoAplicacion.Aumento,
                ValorCambio = 0,
                ListasIds = new List<int>(),
                // Precargar filtros desde query string
                CategoriasIds = categoriasIds,
                MarcasIds = marcasIds,
                ProductoIdsText = productoIdsText
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar formulario de simulación");
            TempData["Error"] = "Error al cargar el formulario.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Procesa la simulación de un cambio masivo
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionSimular)]
    public async Task<IActionResult> Simular(SimularCambioMasivoViewModel viewModel)
    {
        if (!ModelState.IsValid)
        {
            await CargarDatosParaSimulacion();
            return View(viewModel);
        }

        try
        {
            if (viewModel.ListasIds == null || !viewModel.ListasIds.Any())
            {
                ModelState.AddModelError("ListasIds", "Debe seleccionar al menos una lista de precios.");
                await CargarDatosParaSimulacion();
                return View(viewModel);
            }

            if ((viewModel.ProductosIds == null || !viewModel.ProductosIds.Any())
                && !string.IsNullOrWhiteSpace(viewModel.ProductoIdsText))
            {
                var parsedIds = new List<int>();
                var tokens = viewModel.ProductoIdsText
                    .Split(new[] { ',', ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var token in tokens)
                {
                    if (!int.TryParse(token, out var id) || id <= 0)
                    {
                        ModelState.AddModelError(nameof(viewModel.ProductoIdsText), "Hay IDs de producto inválidos. Use números enteros positivos separados por comas.");
                        await CargarDatosParaSimulacion();
                        return View(viewModel);
                    }

                    parsedIds.Add(id);
                }

                viewModel.ProductosIds = parsedIds.Distinct().ToList();
            }

            var batch = await _precioService.SimularCambioMasivoAsync(
                nombre: viewModel.Nombre,
                tipoCambio: viewModel.TipoCambio,
                tipoAplicacion: viewModel.TipoAplicacion,
                valorCambio: viewModel.ValorCambio,
                listasIds: viewModel.ListasIds,
                categoriaIds: viewModel.CategoriasIds,
                marcaIds: viewModel.MarcasIds,
                productoIds: viewModel.ProductosIds);

            TempData["Success"] = $"Simulación '{batch.Nombre}' creada exitosamente. {batch.CantidadProductos} productos afectados.";
            return RedirectToAction(nameof(Preview), new { id = batch.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear simulación");
            ModelState.AddModelError("", "Error al crear la simulación: " + ex.Message);
            await CargarDatosParaSimulacion();
            return View(viewModel);
        }
    }

    /// <summary>
    /// Procesa la simulación de cambio de precios desde el Catálogo.
    /// Soporta dos modos:
    /// 1. Seleccionados: productos específicos por ID
    /// 2. Filtrados: todos los productos que coinciden con los filtros actuales
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionSimular)]
    public async Task<IActionResult> SimularDesdeCatalogo(SimularDesdeCatalogoViewModel viewModel)
    {
        // === DIAGNÓSTICO: Log de datos recibidos ===
        _logger.LogInformation(
            "[SimularDesdeCatalogo] Request recibido - Alcance: {Alcance}, ProductoIdsText: '{ProductoIdsText}', " +
            "FiltrosJson: '{FiltrosJson}', ListasPrecioIds: {ListasPrecioIds}, ValorInput: {ValorInput}, TipoCambio: {TipoCambio}",
            viewModel.Alcance,
            viewModel.ProductoIdsText ?? "(null)",
            viewModel.FiltrosJson ?? "(null)",
            viewModel.ListasPrecioIds != null ? string.Join(",", viewModel.ListasPrecioIds) : "(null)",
            viewModel.ValorInput,
            viewModel.TipoCambio ?? "(null)");

        // Validación básica del modelo (sin Required en ProductoIdsText)
        if (!ModelState.IsValid)
        {
            // Ignorar errores de ProductoIdsText si estamos en modo filtrados
            if (viewModel.Alcance == "filtrados")
            {
                ModelState.Remove("ProductoIdsText");
            }
            
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                _logger.LogWarning("[SimularDesdeCatalogo] Validación fallida: {Errors}", string.Join("; ", errors));
                TempData["Error"] = "Error de validación: " + string.Join(", ", errors);
                return RedirectToAction("Index", "Catalogo");
            }
        }

        try
        {
            var productosIds = new List<int>();
            string modoDescripcion;

            if (viewModel.Alcance == "filtrados" && !string.IsNullOrWhiteSpace(viewModel.FiltrosJson))
            {
                // Modo Filtrados: obtener productos desde los filtros
                productosIds = await ObtenerProductosPorFiltros(viewModel.FiltrosJson);
                modoDescripcion = "filtros aplicados";
            }
            else
            {
                // Modo Seleccionados: parsear IDs
                if (!string.IsNullOrWhiteSpace(viewModel.ProductoIdsText))
                {
                    var tokens = viewModel.ProductoIdsText
                        .Split(new[] { ',', ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    foreach (var token in tokens)
                    {
                        if (int.TryParse(token, out var id) && id > 0)
                        {
                            productosIds.Add(id);
                        }
                    }

                    productosIds = productosIds.Distinct().ToList();
                }
                modoDescripcion = "selección manual";
            }

            if (!productosIds.Any())
            {
                TempData["Error"] = "No se encontraron productos válidos para simular.";
                return RedirectToAction("Index", "Catalogo");
            }

            // Validar listas de precios
            if (viewModel.ListasPrecioIds == null || !viewModel.ListasPrecioIds.Any())
            {
                TempData["Error"] = "Debe seleccionar al menos una lista de precios.";
                return RedirectToAction("Index", "Catalogo");
            }

            // Convertir tipo de cambio
            var tipoCambio = viewModel.TipoCambio?.ToLower() switch
            {
                "fijo" => TipoCambio.ValorAbsoluto,
                _ => TipoCambio.PorcentajeSobrePrecioActual
            };

            // Determinar tipo de aplicación y valor absoluto
            var valorCambio = viewModel.ValorInput;
            var tipoAplicacion = valorCambio >= 0 ? TipoAplicacion.Aumento : TipoAplicacion.Disminucion;
            valorCambio = Math.Abs(valorCambio);

            // Generar nombre automático
            var direccion = tipoAplicacion == TipoAplicacion.Aumento ? "Aumento" : "Descuento";
            var sufijo = tipoCambio == TipoCambio.ValorAbsoluto ? "$" : "%";
            var nombreBatch = $"{direccion} {valorCambio}{sufijo} - {productosIds.Count} productos ({modoDescripcion}) - {DateTime.Now:dd/MM/yyyy HH:mm}";

            // Crear simulación
            var batch = await _precioService.SimularCambioMasivoAsync(
                nombre: nombreBatch,
                tipoCambio: tipoCambio,
                tipoAplicacion: tipoAplicacion,
                valorCambio: valorCambio,
                listasIds: viewModel.ListasPrecioIds,
                categoriaIds: null,
                marcaIds: null,
                productoIds: productosIds);

            TempData["Success"] = $"Simulación creada exitosamente. {batch.CantidadProductos} productos afectados.";
            TempData["OrigenCatalogo"] = true; // Marcar origen para navegación de regreso
            
            return RedirectToAction(nameof(Preview), new { id = batch.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear simulación desde catálogo");
            TempData["Error"] = "Error al crear la simulación: " + ex.Message;
            return RedirectToAction("Index", "Catalogo");
        }
    }

    /// <summary>
    /// Endpoint AJAX para simular cambio de precios desde el catálogo.
    /// Devuelve JSON con el BatchId para abrir Preview en modal/offcanvas.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionSimular)]
    public async Task<IActionResult> SimularCambioRapido([FromBody] SimularCambioRapidoRequest request)
    {
        try
        {
            // Validar request
            if (request == null)
            {
                return BadRequest(new { success = false, error = "Request inválido" });
            }

            if (request.Porcentaje == 0)
            {
                return BadRequest(new { success = false, error = "El porcentaje no puede ser 0" });
            }

            // Obtener productos según el modo
            var productosIds = new List<int>();
            string modoDescripcion;

            if (request.Modo == "filtrados")
            {
                // Validar que al menos un filtro esté definido
                var tieneFiltroCategoría = request.CategoriaId.HasValue && request.CategoriaId > 0;
                var tieneFiltroMarca = request.MarcaId.HasValue && request.MarcaId > 0;
                var tieneFiltroBusqueda = !string.IsNullOrWhiteSpace(request.SearchTerm);
                var tieneFiltroActivos = request.SoloActivos.HasValue && request.SoloActivos.Value;
                var tieneFiltroStock = request.StockBajo.HasValue && request.StockBajo.Value;

                if (!tieneFiltroCategoría && !tieneFiltroMarca && !tieneFiltroBusqueda && !tieneFiltroActivos && !tieneFiltroStock)
                {
                    return BadRequest(new { success = false, error = "Debe especificar al menos un filtro en modo 'filtrados'" });
                }

                // Construir filtrosJson desde el request
                var filtrosJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    categoriaId = request.CategoriaId,
                    marcaId = request.MarcaId,
                    busqueda = request.SearchTerm,
                    soloActivos = request.SoloActivos,
                    stockBajo = request.StockBajo
                });

                productosIds = await ObtenerProductosPorFiltros(filtrosJson);
                modoDescripcion = "filtros aplicados";
            }
            else
            {
                // Modo seleccionados
                productosIds = request.ProductoIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
                modoDescripcion = "selección manual";
            }

            if (!productosIds.Any())
            {
                return BadRequest(new { success = false, error = "No se encontraron productos válidos" });
            }

            // Usar lista predeterminada si no se especifican listas
            var listasIds = request.ListasPrecioIds?.Where(id => id > 0).ToList();
            if (listasIds == null || !listasIds.Any())
            {
                var listaPredeterminada = await _precioService.GetListaPredeterminadaAsync();
                if (listaPredeterminada != null)
                {
                    listasIds = new List<int> { listaPredeterminada.Id };
                }
                else
                {
                    return BadRequest(new { success = false, error = "Debe seleccionar al menos una lista de precios" });
                }
            }

            // Determinar tipo de aplicación
            var valorCambio = request.Porcentaje;
            var tipoAplicacion = valorCambio >= 0 ? TipoAplicacion.Aumento : TipoAplicacion.Disminucion;
            valorCambio = Math.Abs(valorCambio);

            // Generar nombre automático
            var direccion = tipoAplicacion == TipoAplicacion.Aumento ? "Aumento" : "Descuento";
            var nombreBatch = $"{direccion} {valorCambio}% - {productosIds.Count} productos ({modoDescripcion}) - {DateTime.Now:dd/MM/yyyy HH:mm}";

            // Crear simulación usando el servicio existente
            var batch = await _precioService.SimularCambioMasivoAsync(
                nombre: nombreBatch,
                tipoCambio: TipoCambio.PorcentajeSobrePrecioActual,
                tipoAplicacion: tipoAplicacion,
                valorCambio: valorCambio,
                listasIds: listasIds,
                categoriaIds: null,
                marcaIds: null,
                productoIds: productosIds);

            _logger.LogInformation("Simulación rápida creada: BatchId={BatchId}, Productos={Count}", 
                batch.Id, batch.CantidadProductos);

            return Ok(new
            {
                success = true,
                batchId = batch.Id,
                cantidadProductos = batch.CantidadProductos,
                previewUrl = $"/CambiosPrecios/Preview/{batch.Id}",
                mensaje = $"Simulación creada con {batch.CantidadProductos} productos"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear simulación rápida");
            return StatusCode(500, new { success = false, error = "Error interno al crear la simulación" });
        }
    }

    /// <summary>
    /// Endpoint AJAX para aplicar cambio de precios directamente desde el Catálogo.
    /// Simula y aplica en un solo paso.
    /// </summary>
    [HttpPost]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionAplicar)]
    public async Task<IActionResult> AplicarRapido([FromBody] AplicarRapidoRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { success = false, error = "Request inválido" });
        }

        _logger.LogInformation(
            "[AplicarRapido] Request - Modo: {Modo}, Porcentaje: {Porcentaje}, ProductoIds: {ProductoIds}, ListasIds: {ListasIds}",
            request.Modo, request.Porcentaje, 
            request.ProductoIds != null ? string.Join(",", request.ProductoIds) : "(null)",
            request.ListasPrecioIds != null ? string.Join(",", request.ListasPrecioIds) : "(null)");

        // Validar porcentaje
        if (request.Porcentaje == 0)
        {
            return BadRequest(new { success = false, error = "El porcentaje no puede ser 0" });
        }

        try
        {
            var productosIds = new List<int>();
            string modoDescripcion;

            if (request.Modo == "filtrados" && request.Filtros != null)
            {
                // Modo Filtrados: obtener productos desde los filtros
                var filtrosJson = System.Text.Json.JsonSerializer.Serialize(request.Filtros);
                productosIds = await ObtenerProductosPorFiltros(filtrosJson);
                modoDescripcion = "filtros aplicados";
            }
            else
            {
                // Modo Seleccionados
                if (request.ProductoIds != null)
                {
                    productosIds = request.ProductoIds.Where(id => id > 0).Distinct().ToList();
                }
                modoDescripcion = "selección manual";
            }

            if (!productosIds.Any())
            {
                return BadRequest(new { success = false, error = "No se encontraron productos válidos" });
            }

            // Validar listas de precios
            var listasIds = request.ListasPrecioIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            if (!listasIds.Any())
            {
                // Usar lista predeterminada
                var listaPredeterminada = await _context.ListasPrecios
                    .Where(l => l.Activa && l.EsPredeterminada)
                    .Select(l => l.Id)
                    .FirstOrDefaultAsync();

                if (listaPredeterminada > 0)
                {
                    listasIds.Add(listaPredeterminada);
                }
                else
                {
                    return BadRequest(new { success = false, error = "No hay lista de precios disponible" });
                }
            }

            // Determinar tipo de aplicación
            var valorCambio = request.Porcentaje;
            var tipoAplicacion = valorCambio >= 0 ? TipoAplicacion.Aumento : TipoAplicacion.Disminucion;
            valorCambio = Math.Abs(valorCambio);

            // Generar nombre automático
            var direccion = tipoAplicacion == TipoAplicacion.Aumento ? "Aumento" : "Descuento";
            var nombreBatch = $"{direccion} {valorCambio}% - {productosIds.Count} productos ({modoDescripcion}) - {DateTime.Now:dd/MM/yyyy HH:mm}";

            // Paso 1: Crear simulación
            var batch = await _precioService.SimularCambioMasivoAsync(
                nombre: nombreBatch,
                tipoCambio: TipoCambio.PorcentajeSobrePrecioActual,
                tipoAplicacion: tipoAplicacion,
                valorCambio: valorCambio,
                listasIds: listasIds,
                categoriaIds: null,
                marcaIds: null,
                productoIds: productosIds);

            _logger.LogInformation("Simulación creada: BatchId={BatchId}, Productos={Count}", 
                batch.Id, batch.CantidadProductos);

            var usuarioActual = User.Identity?.Name ?? "Sistema";

            // Paso 2: Aprobar automáticamente
            var batchAprobado = await _precioService.AprobarBatchAsync(
                batch.Id, 
                usuarioActual, 
                batch.RowVersion,
                notas: "Auto-aprobado desde Catálogo");

            _logger.LogInformation("Batch auto-aprobado: BatchId={BatchId}", batchAprobado.Id);

            // Paso 3: Aplicar inmediatamente
            var batchAplicado = await _precioService.AplicarBatchAsync(
                batchAprobado.Id, 
                usuarioActual, 
                batchAprobado.RowVersion);

            _logger.LogInformation("Cambio aplicado exitosamente: BatchId={BatchId}, ProductosAfectados={Count}", 
                batchAplicado.Id, batchAplicado.CantidadProductos);

            return Ok(new
            {
                success = true,
                batchId = batchAplicado.Id,
                productosAfectados = batch.CantidadProductos,
                mensaje = $"Cambio aplicado: {batch.CantidadProductos} productos actualizados"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al aplicar cambio rápido");
            return StatusCode(500, new { success = false, error = "Error interno al aplicar el cambio" });
        }
    }

    /// <summary>
    /// Endpoint AJAX para obtener el historial de cambios de precios.
    /// Se usa desde el offcanvas del Catálogo.
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionVer)]
    public async Task<IActionResult> HistorialApi(int? productoId = null, int take = 20)
    {
        try
        {
            var batches = await _precioService.GetBatchesAsync(
                estado: null,
                fechaDesde: null,
                fechaHasta: null,
                skip: 0,
                take: take);

            // Si se especifica un producto, filtrar solo los batches que lo afectan
            if (productoId.HasValue)
            {
                var batchIdsConProducto = await _context.PriceChangeItems
                    .Where(i => i.ProductoId == productoId.Value)
                    .Select(i => i.BatchId)
                    .Distinct()
                    .ToListAsync();

                batches = batches.Where(b => batchIdsConProducto.Contains(b.Id)).ToList();
            }

            var historial = batches.Select(b => new
            {
                id = b.Id,
                nombre = b.Nombre,
                fecha = b.FechaSolicitud.ToString("dd/MM/yyyy HH:mm"),
                fechaIso = b.FechaSolicitud.ToString("o"),
                usuario = b.SolicitadoPor,
                tipoCambio = b.TipoCambio.ToString(),
                tipoAplicacion = b.TipoAplicacion.ToString(),
                valorCambio = b.ValorCambio,
                cambioDisplay = $"{(b.TipoAplicacion == TipoAplicacion.Aumento ? "+" : "-")}{b.ValorCambio}{(b.TipoCambio == TipoCambio.ValorAbsoluto ? "$" : "%")}",
                estado = b.Estado.ToString(),
                estadoBadgeClass = GetEstadoBadgeClass(b.Estado),
                cantidadProductos = b.CantidadProductos,
                puedeRevertir = b.Estado == EstadoBatch.Aplicado,
                puedeVer = true,
                previewUrl = $"/CambiosPrecios/Preview/{b.Id}",
                fechaAplicacion = b.FechaAplicacion?.ToString("dd/MM/yyyy HH:mm"),
                aplicadoPor = b.AplicadoPor
            }).ToList();

            return Ok(new { success = true, historial });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener historial de cambios");
            return StatusCode(500, new { success = false, error = "Error al cargar el historial" });
        }
    }

    /// <summary>
    /// Endpoint AJAX para obtener datos de un batch para el modal de reversión.
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionRevertir)]
    public async Task<IActionResult> GetBatchParaRevertirApi(int id)
    {
        try
        {
            var batch = await _precioService.GetSimulacionAsync(id);
            if (batch == null)
            {
                return NotFound(new { success = false, error = "Batch no encontrado" });
            }

            if (batch.Estado != EstadoBatch.Aplicado)
            {
                return BadRequest(new { success = false, error = "Solo se pueden revertir batches en estado Aplicado" });
            }

            return Ok(new
            {
                success = true,
                batch = new
                {
                    id = batch.Id,
                    nombre = batch.Nombre,
                    cantidadProductos = batch.CantidadProductos,
                    aplicadoPor = batch.AplicadoPor,
                    fechaAplicacion = batch.FechaAplicacion?.ToString("dd/MM/yyyy HH:mm"),
                    fechaVigencia = batch.FechaVigencia?.ToString("dd/MM/yyyy HH:mm"),
                    rowVersion = Convert.ToBase64String(batch.RowVersion),
                    cambioDisplay = $"{(batch.TipoAplicacion == TipoAplicacion.Aumento ? "+" : "-")}{batch.ValorCambio}{(batch.TipoCambio == TipoCambio.ValorAbsoluto ? "$" : "%")}"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener batch para revertir {BatchId}", id);
            return StatusCode(500, new { success = false, error = "Error al obtener datos del batch" });
        }
    }

    /// <summary>
    /// Endpoint AJAX para ejecutar la reversión de un batch.
    /// Usa rowVersion para control de concurrencia.
    /// </summary>
    [HttpPost]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionRevertir)]
    public async Task<IActionResult> RevertirApi([FromBody] RevertirApiRequest request)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(new { success = false, error = "Request inválido" });
            }

            if (string.IsNullOrWhiteSpace(request.Motivo))
            {
                return BadRequest(new { success = false, error = "Debe especificar un motivo para revertir" });
            }

            if (string.IsNullOrEmpty(request.RowVersion))
            {
                return BadRequest(new { success = false, error = "RowVersion requerido para control de concurrencia" });
            }

            byte[] rowVersionBytes;
            try
            {
                rowVersionBytes = Convert.FromBase64String(request.RowVersion);
            }
            catch
            {
                return BadRequest(new { success = false, error = "RowVersion inválido" });
            }

            var revertidoPor = User.Identity?.Name ?? "Sistema";
            var batch = await _precioService.RevertirBatchAsync(request.BatchId, revertidoPor, rowVersionBytes, request.Motivo);

            _logger.LogInformation("Batch {BatchId} revertido via API por {Usuario}", batch.Id, revertidoPor);

            return Ok(new
            {
                success = true,
                mensaje = $"Batch '{batch.Nombre}' revertido exitosamente",
                batchId = batch.Id
            });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return Conflict(new { success = false, error = "El batch fue modificado por otro usuario. Recargue el historial y reintente." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al revertir batch {BatchId} via API", request?.BatchId);
            return StatusCode(500, new { success = false, error = "Error interno al revertir el batch" });
        }
    }

    /// <summary>
    /// Obtiene la clase CSS del badge según el estado del batch
    /// </summary>
    private static string GetEstadoBadgeClass(EstadoBatch estado) => estado switch
    {
        EstadoBatch.Simulado => "bg-info",
        EstadoBatch.Aprobado => "bg-success",
        EstadoBatch.Aplicado => "bg-primary",
        EstadoBatch.Revertido => "bg-secondary",
        EstadoBatch.Rechazado => "bg-danger",
        EstadoBatch.Cancelado => "bg-dark",
        _ => "bg-secondary"
    };

    // ============================================
    // PREVIEW - Ver resultados de simulación
    // ============================================

    /// <summary>
    /// Muestra los resultados de una simulación
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Preview(int id, int page = 1, int pageSize = 50)
    {
        try
        {
            var batch = await _precioService.GetSimulacionAsync(id);
            if (batch == null)
            {
                TempData["Error"] = "Simulación no encontrada.";
                return RedirectToAction(nameof(Index));
            }

            var skip = (page - 1) * pageSize;
            var items = await _precioService.GetItemsSimulacionAsync(id, skip, pageSize);

            // Parsear estadísticas del JSON
            Dictionary<string, object>? estadisticas = null;
            if (!string.IsNullOrEmpty(batch.SimulacionJson))
            {
                estadisticas = JsonSerializer.Deserialize<Dictionary<string, object>>(batch.SimulacionJson);
            }

            var viewModel = new SimulacionViewModel
            {
                BatchId = batch.Id,
                RowVersion = batch.RowVersion,
                Nombre = batch.Nombre,
                TipoCambio = batch.TipoCambio,
                TipoAplicacion = batch.TipoAplicacion,
                ValorCambio = batch.ValorCambio,
                Estado = batch.Estado,
                CantidadProductos = batch.CantidadProductos,
                PorcentajePromedioCambio = batch.PorcentajePromedioCambio,
                SolicitadoPor = batch.SolicitadoPor,
                FechaSolicitud = batch.FechaSolicitud,
                RequiereAutorizacion = batch.RequiereAutorizacion,
                Items = items.Select(i => new SimulacionItemViewModel
                {
                    ProductoCodigo = i.ProductoCodigo,
                    ProductoNombre = i.ProductoNombre,
                    ListaId = i.ListaId,
                    PrecioAnterior = i.PrecioAnterior,
                    PrecioNuevo = i.PrecioNuevo,
                    DiferenciaValor = i.DiferenciaValor,
                    DiferenciaPorcentaje = i.DiferenciaPorcentaje,
                    Costo = i.Costo,
                    MargenAnterior = i.MargenAnterior,
                    MargenNuevo = i.MargenNuevo,
                    TieneAdvertencia = i.TieneAdvertencia,
                    MensajeAdvertencia = i.MensajeAdvertencia
                }).ToList(),
                PaginaActual = page,
                TamanioPagina = pageSize,
                Estadisticas = estadisticas
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener preview de simulación {BatchId}", id);
            TempData["Error"] = "Error al cargar la simulación.";
            return RedirectToAction(nameof(Index));
        }
    }

    // ============================================
    // AUTORIZAR - Aprobar o rechazar batch
    // ============================================

    /// <summary>
    /// Muestra el formulario para autorizar (aprobar/rechazar) un batch
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionAprobar)]
    public async Task<IActionResult> Autorizar(int id)
    {
        try
        {
            var batch = await _precioService.GetSimulacionAsync(id);
            if (batch == null)
            {
                TempData["Error"] = "Batch no encontrado.";
                return RedirectToAction(nameof(Index));
            }

            if (batch.Estado != EstadoBatch.Simulado)
            {
                TempData["Error"] = "Solo se pueden autorizar batches en estado Simulado.";
                return RedirectToAction(nameof(Preview), new { id });
            }

            var viewModel = new AutorizarBatchViewModel
            {
                BatchId = batch.Id,
                RowVersion = batch.RowVersion,
                Nombre = batch.Nombre,
                TipoCambio = batch.TipoCambio,
                ValorCambio = batch.ValorCambio,
                CantidadProductos = batch.CantidadProductos,
                PorcentajePromedioCambio = batch.PorcentajePromedioCambio,
                SolicitadoPor = batch.SolicitadoPor,
                FechaSolicitud = batch.FechaSolicitud
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar formulario de autorización {BatchId}", id);
            TempData["Error"] = "Error al cargar el formulario de autorización.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Aprueba un batch de cambios
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionAprobar)]
    public async Task<IActionResult> Aprobar(int id, byte[] rowVersion, string? notas)
    {
        try
        {
            var aprobadoPor = User.Identity?.Name ?? "Sistema";
            var batch = await _precioService.AprobarBatchAsync(id, aprobadoPor, rowVersion, notas);

            TempData["Success"] = $"Batch '{batch.Nombre}' aprobado exitosamente.";
            return RedirectToAction(nameof(Preview), new { id = batch.Id });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            TempData["Error"] = "El batch fue modificado por otro usuario. Recargue y reintente.";
            return RedirectToAction(nameof(Autorizar), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al aprobar batch {BatchId}", id);
            TempData["Error"] = "Error al aprobar el batch: " + ex.Message;
            return RedirectToAction(nameof(Autorizar), new { id });
        }
    }

    /// <summary>
    /// Rechaza un batch de cambios
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionAprobar)]
    public async Task<IActionResult> Rechazar(int id, byte[] rowVersion, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            TempData["Error"] = "Debe especificar un motivo para rechazar.";
            return RedirectToAction(nameof(Autorizar), new { id });
        }

        try
        {
            var rechazadoPor = User.Identity?.Name ?? "Sistema";
            var batch = await _precioService.RechazarBatchAsync(id, rechazadoPor, rowVersion, motivo);

            TempData["Success"] = $"Batch '{batch.Nombre}' rechazado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            TempData["Error"] = "El batch fue modificado por otro usuario. Recargue y reintente.";
            return RedirectToAction(nameof(Autorizar), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al rechazar batch {BatchId}", id);
            TempData["Error"] = "Error al rechazar el batch: " + ex.Message;
            return RedirectToAction(nameof(Autorizar), new { id });
        }
    }

    /// <summary>
    /// Cancela un batch antes de ser aplicado
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionAprobar)]
    public async Task<IActionResult> Cancelar(int id, byte[] rowVersion, string? motivo)
    {
        try
        {
            var canceladoPor = User.Identity?.Name ?? "Sistema";
            var batch = await _precioService.CancelarBatchAsync(id, canceladoPor, rowVersion, motivo);

            TempData["Success"] = $"Batch '{batch.Nombre}' cancelado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            TempData["Error"] = "El batch fue modificado por otro usuario. Recargue y reintente.";
            return RedirectToAction(nameof(Preview), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cancelar batch {BatchId}", id);
            TempData["Error"] = "Error al cancelar el batch: " + ex.Message;
            return RedirectToAction(nameof(Preview), new { id });
        }
    }

    // ============================================
    // APLICAR - Aplicar batch aprobado
    // ============================================

    /// <summary>
    /// Muestra el formulario para aplicar un batch aprobado
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionAplicar)]
    public async Task<IActionResult> Aplicar(int id)
    {
        try
        {
            var batch = await _precioService.GetSimulacionAsync(id);
            if (batch == null)
            {
                TempData["Error"] = "Batch no encontrado.";
                return RedirectToAction(nameof(Index));
            }

            if (batch.Estado != EstadoBatch.Aprobado)
            {
                TempData["Error"] = "Solo se pueden aplicar batches en estado Aprobado.";
                return RedirectToAction(nameof(Preview), new { id });
            }

            var viewModel = new AplicarBatchViewModel
            {
                BatchId = batch.Id,
                RowVersion = batch.RowVersion,
                Nombre = batch.Nombre,
                CantidadProductos = batch.CantidadProductos,
                AprobadoPor = batch.AprobadoPor,
                FechaAprobacion = batch.FechaAprobacion,
                FechaVigencia = DateTime.UtcNow
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar formulario de aplicación {BatchId}", id);
            TempData["Error"] = "Error al cargar el formulario.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Procesa la aplicación de un batch aprobado
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionAplicar)]
    public async Task<IActionResult> AplicarConfirmed(int id, byte[] rowVersion, DateTime? fechaVigencia)
    {
        try
        {
            var aplicadoPor = User.Identity?.Name ?? "Sistema";
            var batch = await _precioService.AplicarBatchAsync(id, aplicadoPor, rowVersion, fechaVigencia);

            TempData["Success"] = $"Batch '{batch.Nombre}' aplicado exitosamente. {batch.CantidadProductos} precios actualizados.";
            return RedirectToAction(nameof(Preview), new { id = batch.Id });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            TempData["Error"] = "El batch fue modificado por otro usuario. Recargue y reintente.";
            return RedirectToAction(nameof(Aplicar), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al aplicar batch {BatchId}", id);
            TempData["Error"] = "Error al aplicar el batch: " + ex.Message;
            return RedirectToAction(nameof(Aplicar), new { id });
        }
    }

    // ============================================
    // REVERTIR - Revertir batch aplicado
    // ============================================

    /// <summary>
    /// Muestra el formulario para revertir un batch aplicado
    /// </summary>
    [HttpGet]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionRevertir)]
    public async Task<IActionResult> Revertir(int id)
    {
        try
        {
            var batch = await _precioService.GetSimulacionAsync(id);
            if (batch == null)
            {
                TempData["Error"] = "Batch no encontrado.";
                return RedirectToAction(nameof(Index));
            }

            if (batch.Estado != EstadoBatch.Aplicado)
            {
                TempData["Error"] = "Solo se pueden revertir batches en estado Aplicado.";
                return RedirectToAction(nameof(Preview), new { id });
            }

            var viewModel = new RevertirBatchViewModel
            {
                BatchId = batch.Id,
                RowVersion = batch.RowVersion,
                Nombre = batch.Nombre,
                CantidadProductos = batch.CantidadProductos,
                AplicadoPor = batch.AplicadoPor,
                FechaAplicacion = batch.FechaAplicacion,
                FechaVigencia = batch.FechaVigencia
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar formulario de reversión {BatchId}", id);
            TempData["Error"] = "Error al cargar el formulario.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// Procesa la reversión de un batch aplicado
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionRevertir)]
    public async Task<IActionResult> RevertirConfirmed(int id, byte[] rowVersion, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            TempData["Error"] = "Debe especificar un motivo para revertir.";
            return RedirectToAction(nameof(Revertir), new { id });
        }

        try
        {
            var revertidoPor = User.Identity?.Name ?? "Sistema";
            var batchReversion = await _precioService.RevertirBatchAsync(id, revertidoPor, rowVersion, motivo);

            TempData["Success"] = $"Batch revertido exitosamente. Se creó el batch de reversión #{batchReversion.Id}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            TempData["Error"] = "El batch fue modificado por otro usuario. Recargue y reintente.";
            return RedirectToAction(nameof(Revertir), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al revertir batch {BatchId}", id);
            TempData["Error"] = "Error al revertir el batch: " + ex.Message;
            return RedirectToAction(nameof(Revertir), new { id });
        }
    }

    /// <summary>
    /// Endpoint POST para revertir un batch aplicado.
    /// Acepta batchId en la ruta y crea un batch de reversión auditable.
    /// </summary>
    [HttpPost("CambiosPrecios/Revertir/{id:int}")]
    [PermisoRequerido(Modulo = ModuloPrecios, Accion = AccionRevertir)]
    public async Task<IActionResult> RevertirPost(int id, [FromBody] RevertirPostRequest? request)
    {
        try
        {
            // Obtener batch para validación y rowVersion
            var batch = await _precioService.GetSimulacionAsync(id);
            if (batch == null)
            {
                return NotFound(new { success = false, error = $"Batch {id} no encontrado" });
            }

            if (batch.Estado != EstadoBatch.Aplicado)
            {
                return BadRequest(new { 
                    success = false, 
                    error = $"Solo se pueden revertir batches en estado Aplicado (actual: {batch.Estado})" 
                });
            }

            var motivo = request?.Motivo;
            if (string.IsNullOrWhiteSpace(motivo))
            {
                return BadRequest(new { success = false, error = "Debe especificar un motivo para revertir" });
            }

            // Usar rowVersion del request o del batch actual
            byte[] rowVersion;
            if (!string.IsNullOrEmpty(request?.RowVersion))
            {
                try
                {
                    rowVersion = Convert.FromBase64String(request.RowVersion);
                }
                catch
                {
                    return BadRequest(new { success = false, error = "RowVersion inválido" });
                }
            }
            else
            {
                rowVersion = batch.RowVersion;
            }

            var revertidoPor = User.Identity?.Name ?? "Sistema";
            var batchReversion = await _precioService.RevertirBatchAsync(id, revertidoPor, rowVersion, motivo);

            _logger.LogInformation(
                "Batch {BatchId} revertido via POST por {Usuario}. Batch de reversión: {BatchReversionId}", 
                id, revertidoPor, batchReversion.Id);

            return Ok(new
            {
                success = true,
                mensaje = $"Batch revertido exitosamente",
                batchOriginalId = id,
                batchReversionId = batchReversion.Id,
                batchReversionNombre = batchReversion.Nombre
            });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return Conflict(new { 
                success = false, 
                error = "El batch fue modificado por otro usuario. Recargue y reintente." 
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al revertir batch {BatchId} via POST", id);
            return StatusCode(500, new { success = false, error = "Error interno al revertir el batch" });
        }
    }

    // ============================================
    // HISTORIAL - Ver historial de precios
    // ============================================

    /// <summary>
    /// Muestra el historial de precios de un producto
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Historial(int productoId, int? listaId)
    {
        try
        {
            var producto = await _productoService.GetByIdAsync(productoId);
            if (producto == null)
            {
                TempData["Error"] = "Producto no encontrado.";
                return RedirectToAction("Index", "Productos");
            }

            List<Models.Entities.ProductoPrecioLista> historial;

            if (listaId.HasValue)
            {
                historial = await _precioService.GetHistorialPreciosAsync(productoId, listaId.Value);
            }
            else
            {
                historial = await _precioService.GetPreciosProductoAsync(productoId);
            }

            var viewModel = new HistorialPreciosViewModel
            {
                ProductoId = productoId,
                ProductoCodigo = producto.Codigo,
                ProductoNombre = producto.Nombre,
                ListaId = listaId,
                Precios = historial.Select(p => new PrecioHistorialItemViewModel
                {
                    ListaId = p.ListaId,
                    ListaNombre = p.Lista.Nombre,
                    VigenciaDesde = p.VigenciaDesde,
                    VigenciaHasta = p.VigenciaHasta,
                    Costo = p.Costo,
                    Precio = p.Precio,
                    MargenPorcentaje = p.MargenPorcentaje,
                    EsManual = p.EsManual,
                    EsVigente = p.EsVigente,
                    CreadoPor = p.CreadoPor,
                    Notas = p.Notas
                }).OrderByDescending(p => p.VigenciaDesde).ToList()
            };

            // Cargar listas para el filtro
            var listas = await _precioService.GetAllListasAsync();
            ViewBag.Listas = new SelectList(listas, "Id", "Nombre", listaId);

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener historial de precios para producto {ProductoId}", productoId);
            TempData["Error"] = "Error al cargar el historial de precios.";
            return RedirectToAction("Index", "Productos");
        }
    }

    // ============================================
    // MÉTODOS AUXILIARES
    // ============================================

    /// <summary>
    /// Carga datos necesarios para el formulario de simulación
    /// </summary>
    private async Task CargarDatosParaSimulacion()
    {
        var listas = await _precioService.GetAllListasAsync();
        ViewBag.Listas = new SelectList(listas, "Id", "Nombre");

        var categorias = await _categoriaService.GetAllAsync();
        ViewBag.Categorias = new SelectList(categorias, "Id", "Nombre");

        var marcas = await _marcaService.GetAllAsync();
        ViewBag.Marcas = new SelectList(marcas, "Id", "Nombre");
    }

    /// <summary>
    /// Obtiene IDs de productos que coinciden con los filtros del catálogo
    /// </summary>
    /// <param name="filtrosJson">JSON con los filtros del catálogo</param>
    /// <returns>Lista de IDs de productos</returns>
    private async Task<List<int>> ObtenerProductosPorFiltros(string filtrosJson)
    {
        try
        {
            var filtros = System.Text.Json.JsonSerializer.Deserialize<FiltrosCatalogoDto>(filtrosJson, 
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (filtros == null)
            {
                _logger.LogWarning("Filtros JSON inválidos: {Json}", filtrosJson);
                return new List<int>();
            }

            // Construir query con los mismos filtros que usa el catálogo
            var query = _context.Productos.AsQueryable();

            // Filtro por categoría
            if (filtros.CategoriaId.HasValue && filtros.CategoriaId > 0)
            {
                query = query.Where(p => p.CategoriaId == filtros.CategoriaId);
            }

            // Filtro por marca
            if (filtros.MarcaId.HasValue && filtros.MarcaId > 0)
            {
                query = query.Where(p => p.MarcaId == filtros.MarcaId);
            }

            // Filtro por texto de búsqueda
            if (!string.IsNullOrWhiteSpace(filtros.Busqueda))
            {
                var busqueda = filtros.Busqueda.ToLower();
                query = query.Where(p => 
                    p.Codigo.ToLower().Contains(busqueda) || 
                    p.Nombre.ToLower().Contains(busqueda) ||
                    (p.Descripcion != null && p.Descripcion.ToLower().Contains(busqueda)));
            }

            // Filtro por activos
            if (filtros.SoloActivos.HasValue && filtros.SoloActivos.Value)
            {
                query = query.Where(p => p.Activo);
            }

            // Filtro por stock bajo
            if (filtros.StockBajo.HasValue && filtros.StockBajo.Value)
            {
                query = query.Where(p => p.StockActual <= p.StockMinimo);
            }

            // Obtener solo IDs para mejor rendimiento
            var ids = await query.Select(p => p.Id).ToListAsync();

            _logger.LogInformation("Filtros aplicados: {Filtros}. Productos encontrados: {Count}", 
                filtrosJson, ids.Count);

            return ids;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Error al deserializar filtros JSON: {Json}", filtrosJson);
            return new List<int>();
        }
    }

    /// <summary>
    /// DTO para deserializar los filtros del catálogo
    /// </summary>
    private class FiltrosCatalogoDto
    {
        public int? CategoriaId { get; set; }
        public int? MarcaId { get; set; }
        public string? Busqueda { get; set; }
        public bool? SoloActivos { get; set; }
        public bool? StockBajo { get; set; }
        public int? ListaPrecioId { get; set; }
    }
}
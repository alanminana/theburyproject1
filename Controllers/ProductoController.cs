using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "productos", Accion = "view")]
    public class ProductoController : Controller
    {
        private readonly IProductoService _productoService;
        private readonly IProductoUnidadService _productoUnidadService;
        private readonly ICatalogLookupService _catalogLookupService;
        private readonly ICatalogoService _catalogoService;
        private readonly ILogger<ProductoController> _logger;
        private readonly IMapper _mapper;
        private const int MaxUnidadesCargaMasiva = 200;

        public ProductoController(
            IProductoService productoService,
            IProductoUnidadService productoUnidadService,
            ICatalogLookupService catalogLookupService,
            ICatalogoService catalogoService,
            ILogger<ProductoController> logger,
            IMapper mapper)
        {
            _productoService = productoService;
            _productoUnidadService = productoUnidadService;
            _catalogLookupService = catalogLookupService;
            _catalogoService = catalogoService;
            _logger = logger;
            _mapper = mapper;
        }

        #region CRUD

        // GET: Producto
        /// <summary>
        /// Vista legacy de productos. Redirige al Catálogo unificado.
        /// </summary>
        public IActionResult Index(
            string? searchTerm = null,
            int? categoriaId = null,
            int? marcaId = null,
            bool stockBajo = false,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc")
        {
            // Redirect permanente al nuevo Catálogo unificado preservando filtros
            return RedirectToActionPermanent("Index", "Catalogo", new
            {
                searchTerm,
                categoriaId,
                marcaId,
                stockBajo,
                soloActivos,
                orderBy,
                orderDirection
            });
        }

        // GET: Producto/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            try
            {
                var producto = await _productoService.GetByIdAsync(id.Value);
                if (producto == null)
                {
                    return NotFound();
                }

                var viewModel = _mapper.Map<ProductoViewModel>(producto);
                return View("Details_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener detalles del producto {Id}", id);
                TempData["Error"] = "Error al cargar los detalles del producto. Intentá nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }
        /// <summary>
        /// Carga los dropdowns de Categorías y Marcas para los formularios y filtros
        /// </summary>
        /// <param name="categoriaSeleccionada">ID de categoría preseleccionada</param>
        /// <param name="marcaSeleccionada">ID de marca preseleccionada</param>
        /// <param name="paraFiltros">Si es true, usa ViewBag.CategoriasFiltro/MarcasFiltro; si no, ViewBag.Categorias/Marcas</param>
        private async Task CargarDropdownsAsync(int? categoriaSeleccionada = null, int? marcaSeleccionada = null, bool paraFiltros = false)
        {
            var (categorias, marcas) = await _catalogLookupService.GetCategoriasYMarcasAsync();

            var categoriasSelectList = new SelectList(
                categorias.OrderBy(c => c.Nombre),
                "Id",
                "Nombre",
                categoriaSeleccionada
            );

            var marcasSelectList = new SelectList(
                marcas.OrderBy(m => m.Nombre),
                "Id",
                "Nombre",
                marcaSeleccionada
            );

            if (paraFiltros)
            {
                ViewBag.CategoriasFiltro = categoriasSelectList;
                ViewBag.MarcasFiltro = marcasSelectList;
            }
            else
            {
                ViewBag.Categorias = categoriasSelectList;
                ViewBag.Marcas = marcasSelectList;
            }

            await CargarAlicuotasIVAAsync();
        }

        private async Task CargarAlicuotasIVAAsync(int? alicuotaSeleccionada = null)
        {
            var alicuotas = await _catalogLookupService.ObtenerAlicuotasIVAParaFormAsync();
            ViewBag.AlicuotasIVA = new SelectList(alicuotas, "Id", "Texto", alicuotaSeleccionada);
            ViewBag.AlicuotasIVADatos = alicuotas;
        }
        // GET: Producto/Create
        public async Task<IActionResult> Create()
        {
            await CargarDropdownsAsync();
            return View("Create_tw");
        }

        // POST: Producto/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductoViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var producto = await MapearProductoParaPersistenciaAsync(viewModel);
                    await _productoService.CreateAsync(producto);

                    TempData["Success"] = "Producto creado exitosamente";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Error de validación al crear producto");
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al crear producto");
                    ModelState.AddModelError("", "Error al crear el producto. Intentá nuevamente.");
                }
            }

            await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
            await CargarAlicuotasIVAAsync(viewModel.AlicuotaIVAId);
            return View("Create_tw", viewModel);
        }

        /// <summary>
        /// Crea un producto vía AJAX (desde el modal del catálogo).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAjax(ProductoViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value!.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                    );
                return Json(new { success = false, errors });
            }

            try
            {
                var producto = await MapearProductoParaPersistenciaAsync(viewModel);
                await _productoService.CreateAsync(producto);

                var productoCreado = await _productoService.GetByIdAsync(producto.Id);

                return Json(new
                {
                    success = true,
                    message = "Producto creado exitosamente",
                    entity = new
                    {
                        id                 = producto.Id,
                        codigo             = productoCreado?.Codigo             ?? producto.Codigo,
                        nombre             = productoCreado?.Nombre             ?? producto.Nombre,
                        descripcion        = productoCreado?.Descripcion        ?? producto.Descripcion,
                        categoriaNombre    = productoCreado?.Categoria?.Nombre  ?? "—",
                        marcaNombre        = productoCreado?.Marca?.Nombre      ?? "—",
                        precioVenta        = productoCreado?.PrecioVenta        ?? viewModel.PrecioVenta,
                        comisionPorcentaje = 0m
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al crear producto vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear producto vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al crear el producto. Intentá nuevamente." } } } });
            }
        }

        // GET: Producto/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            try
            {
                var producto = await _productoService.GetByIdAsync(id.Value);
                if (producto == null)
                    return NotFound();

                var viewModel = _mapper.Map<ProductoViewModel>(producto);

                viewModel.PrecioVenta = _productoService.ObtenerPrecioVentaSinIva(
                    viewModel.PrecioVenta,
                    viewModel.PorcentajeIVA);

                await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
                await CargarAlicuotasIVAAsync(viewModel.AlicuotaIVAId);
                return View("Edit_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar producto para editar {Id}", id);
                TempData["Error"] = "Error al cargar el producto";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Producto/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductoViewModel viewModel)
        {
            if (id != viewModel.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var producto = await MapearProductoParaPersistenciaAsync(viewModel);
                    producto.RowVersion = viewModel.RowVersion!;
                    await _productoService.UpdateAsync(producto);

                    TempData["Success"] = "Producto actualizado exitosamente";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Error de validación al actualizar producto {Id}", id);
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al actualizar producto {Id}", id);
                    ModelState.AddModelError("", "Error al actualizar el producto. Intentá nuevamente.");
                }
            }

            await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
            await CargarAlicuotasIVAAsync(viewModel.AlicuotaIVAId);
            return View("Edit_tw", viewModel);
        }

        // GET: Producto/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            try
            {
                var producto = await _productoService.GetByIdAsync(id.Value);
                if (producto == null)
                    return NotFound();

                var viewModel = _mapper.Map<ProductoViewModel>(producto);
                return View("Delete_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar producto para eliminar {Id}", id);
                TempData["Error"] = "Error al cargar el producto";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Producto/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id, string? returnUrl = null)
        {
            try
            {
                var result = await _productoService.DeleteAsync(id);
                if (result)
                {
                    TempData["Success"] = "Producto eliminado exitosamente";
                }
                else
                {
                    TempData["Error"] = "No se encontró el producto a eliminar";
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al eliminar producto {Id}", id);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar producto {Id}", id);
                TempData["Error"] = "Error al eliminar el producto. Intentá nuevamente.";
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region API y helpers

        [HttpGet]
        public async Task<IActionResult> GetJson(int id)
        {
            try
            {
                var producto = await _productoService.GetByIdAsync(id);
                if (producto == null) return NotFound();

                var vm = _mapper.Map<ProductoViewModel>(producto);
                var precioSinIVA = _productoService.ObtenerPrecioVentaSinIva(
                    vm.PrecioVenta,
                    vm.PorcentajeIVA);
                var fila = await _catalogoService.ObtenerFilaAsync(id);

                return Json(new
                {
                    id = vm.Id,
                    rowVersion = vm.RowVersion != null ? Convert.ToBase64String(vm.RowVersion) : "",
                    codigo = vm.Codigo,
                    nombre = vm.Nombre,
                    descripcion = vm.Descripcion,
                    categoriaId = vm.CategoriaId,
                    categoriaNombre = vm.CategoriaNombre,
                    subcategoriaId = vm.SubcategoriaId,
                    subcategoriaNombre = vm.SubcategoriaNombre,
                    marcaId = vm.MarcaId,
                    marcaNombre = vm.MarcaNombre,
                    submarcaId = vm.SubmarcaId,
                    submarcaNombre = vm.SubmarcaNombre,
                    precioCompra = vm.PrecioCompra,
                    precioVenta = precioSinIVA,
                    porcentajeIVA = vm.PorcentajeIVA,
                    alicuotaIVAId = vm.AlicuotaIVAId,
                    comisionPorcentaje = vm.ComisionPorcentaje,
                    maxCuotasSinInteresPermitidas = vm.MaxCuotasSinInteresPermitidas,
                    stockActual = vm.StockActual,
                    stockMinimo = vm.StockMinimo,
                    activo = vm.Activo,
                    caracteristicas = vm.Caracteristicas.Select(c => new { id = c.Id, nombre = c.Nombre, valor = c.Valor }),
                    precioActual = fila?.PrecioActual ?? vm.PrecioVenta,
                    precioBase = fila?.PrecioBase ?? vm.PrecioVenta,
                    tienePrecioLista = fila?.TienePrecioLista ?? false,
                    listaPrecioActualNombre = fila?.ListaPrecioActualNombre
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener producto JSON {Id}", id);
                return StatusCode(500, new { error = "Error al cargar el producto" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAjax(int id, ProductoViewModel viewModel)
        {
            if (id != viewModel.Id)
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Id inválido." } } } });

            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
                return Json(new { success = false, errors });
            }

            try
            {
                var producto = await MapearProductoParaPersistenciaAsync(viewModel);
                producto.RowVersion = viewModel.RowVersion!;
                await _productoService.UpdateAsync(producto);

                var fila = await _catalogoService.ObtenerFilaAsync(id);

                return Json(new
                {
                    success = true,
                    message = "Producto actualizado exitosamente",
                    entity = new
                    {
                        id = fila!.ProductoId,
                        codigo = fila.Codigo,
                        nombre = fila.Nombre,
                        descripcion = fila.Descripcion,
                        categoriaNombre = fila.CategoriaNombre,
                        marcaNombre = fila.MarcaNombre,
                        precioActual = fila.PrecioActual,
                        precioBase = fila.PrecioBase,
                        tienePrecioLista = fila.TienePrecioLista,
                        listaPrecioActualNombre = fila.ListaPrecioActualNombre,
                        margenPorcentaje = fila.MargenPorcentaje,
                        comisionPorcentaje = fila.ComisionPorcentaje,
                        stockActual = fila.StockActual,
                        activo = fila.Activo
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar producto {Id} vía AJAX", id);
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al actualizar el producto. Intentá nuevamente." } } } });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "productos", Accion = "edit")]
        public async Task<IActionResult> ActualizarComisionVendedor(int productoId, string porcentajeComision)
        {
            if (!DecimalParsingHelper.TryParseFlexibleDecimal(porcentajeComision, out var porcentaje))
                return Json(new { success = false, message = "El porcentaje ingresado no es válido." });

            try
            {
                var producto = await _productoService.ActualizarComisionAsync(productoId, porcentaje);
                return Json(new { success = true, message = "Comisión actualizada exitosamente.", comisionPorcentaje = producto.ComisionPorcentaje });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar comisión del producto {Id}", productoId);
                return Json(new { success = false, message = "Error al actualizar la comisión. Intentá nuevamente." });
            }
        }

        /// <summary>
        /// Obtiene las subcategorías (hijas) de una categoría padre para dropdown AJAX
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSubcategorias(int categoriaId)
        {
            try
            {
                var subcategorias = await _catalogLookupService.GetSubcategoriasAsync(categoriaId);
                return Json(subcategorias.Select(s => new { id = s.Id, nombre = s.Nombre }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener subcategorías para categoría {CategoriaId}", categoriaId);
                return Json(new List<object>());
            }
        }

        /// <summary>
        /// Obtiene las submarcas (hijas) de una marca padre para dropdown AJAX
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSubmarcas(int marcaId)
        {
            try
            {
                var submarcas = await _catalogLookupService.GetSubmarcasAsync(marcaId);
                return Json(submarcas.Select(s => new { id = s.Id, nombre = s.Nombre }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener submarcas para marca {MarcaId}", marcaId);
                return Json(new List<object>());
            }
        }

        private async Task<Producto> MapearProductoParaPersistenciaAsync(ProductoViewModel viewModel)
        {
            var producto = _mapper.Map<Producto>(viewModel);
            await _productoService.PrepararPrecioVentaConIvaAsync(producto);

            viewModel.PorcentajeIVA = producto.PorcentajeIVA;
            viewModel.PrecioVenta = producto.PrecioVenta;

            return producto;
        }

        [HttpGet("Producto/Unidades/{productoId:int}")]
        public async Task<IActionResult> Unidades(
            int productoId,
            EstadoUnidad? estado = null,
            string? texto = null,
            bool soloDisponibles = false,
            bool soloVendidas = false,
            bool soloSinNumeroSerie = false)
        {
            try
            {
                var filtros = new ProductoUnidadFiltros
                {
                    Estado = estado,
                    Texto = texto,
                    SoloDisponibles = soloDisponibles,
                    SoloVendidas = soloVendidas,
                    SoloSinNumeroSerie = soloSinNumeroSerie
                };

                var viewModel = await ConstruirProductoUnidadesViewModelAsync(productoId, filtros);
                if (viewModel == null)
                    return NotFound();

                return View("Unidades", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar unidades del producto {ProductoId}", productoId);
                TempData["Error"] = "Error al cargar las unidades del producto. Intentá nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost("Producto/CrearUnidad")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "productos", Accion = "edit")]
        public async Task<IActionResult> CrearUnidad(ProductoUnidadCrearViewModel crearUnidad)
        {
            if (!ModelState.IsValid)
                return await VolverAUnidadesConFormularioAsync(crearUnidad);

            try
            {
                var producto = await _productoService.GetByIdAsync(crearUnidad.ProductoId);
                if (producto == null)
                    return NotFound();

                await _productoUnidadService.CrearUnidadAsync(
                    crearUnidad.ProductoId,
                    crearUnidad.NumeroSerie,
                    crearUnidad.UbicacionActual,
                    crearUnidad.Observaciones,
                    User?.Identity?.Name);

                TempData["Success"] = "Unidad fisica creada correctamente. El stock agregado no fue modificado.";
                return RedirectToAction(nameof(Unidades), new { productoId = crearUnidad.ProductoId });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validacion al crear unidad para producto {ProductoId}", crearUnidad.ProductoId);
                ModelState.AddModelError($"{nameof(ProductoUnidadesViewModel.CrearUnidad)}.{nameof(ProductoUnidadCrearViewModel.NumeroSerie)}", ex.Message);
                return await VolverAUnidadesConFormularioAsync(crearUnidad);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear unidad para producto {ProductoId}", crearUnidad.ProductoId);
                TempData["Error"] = "Error al crear la unidad fisica. Intenta nuevamente.";
                return RedirectToAction(nameof(Unidades), new { productoId = crearUnidad.ProductoId });
            }
        }

        [HttpPost("Producto/CrearUnidadesMasivas")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "productos", Accion = "edit")]
        public async Task<IActionResult> CrearUnidadesMasivas(ProductoUnidadCargaMasivaViewModel cargaMasiva)
        {
            if (!ModelState.IsValid)
                return await VolverAUnidadesConCargaMasivaAsync(cargaMasiva);

            var producto = await _productoService.GetByIdAsync(cargaMasiva.ProductoId);
            if (producto == null)
                return NotFound();

            await PrepararPreviewCargaMasivaAsync(cargaMasiva);

            if (!ModelState.IsValid || !cargaMasiva.Preview.Any())
                return await VolverAUnidadesConCargaMasivaAsync(cargaMasiva);

            if (!cargaMasiva.Confirmar)
            {
                cargaMasiva.PreviewListo = true;
                return await VolverAUnidadesConCargaMasivaAsync(cargaMasiva);
            }

            try
            {
                await _productoUnidadService.CrearUnidadesAsync(
                    cargaMasiva.ProductoId,
                    cargaMasiva.Preview.Select(p => p.NumeroSerie).ToList(),
                    cargaMasiva.UbicacionActual,
                    cargaMasiva.Observaciones,
                    User?.Identity?.Name);

                TempData["Success"] =
                    $"{cargaMasiva.Preview.Count} unidades fisicas creadas correctamente. El stock agregado no fue modificado.";

                return RedirectToAction(nameof(Unidades), new { productoId = cargaMasiva.ProductoId });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validacion al crear unidades masivas para producto {ProductoId}", cargaMasiva.ProductoId);
                ModelState.AddModelError($"{nameof(ProductoUnidadesViewModel.CargaMasiva)}.{nameof(ProductoUnidadCargaMasivaViewModel.NumerosSerieTexto)}", ex.Message);
                return await VolverAUnidadesConCargaMasivaAsync(cargaMasiva);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear unidades masivas para producto {ProductoId}", cargaMasiva.ProductoId);
                TempData["Error"] = "Error al crear las unidades fisicas. Intenta nuevamente.";
                return RedirectToAction(nameof(Unidades), new { productoId = cargaMasiva.ProductoId });
            }
        }

        [HttpGet("Producto/UnidadHistorial/{unidadId:int}")]
        public async Task<IActionResult> UnidadHistorial(int unidadId)
        {
            try
            {
                var unidad = await _productoUnidadService.ObtenerPorIdAsync(unidadId);
                if (unidad == null)
                    return NotFound();

                var movimientos = await _productoUnidadService.ObtenerHistorialAsync(unidadId);
                var viewModel = new ProductoUnidadHistorialViewModel
                {
                    UnidadId = unidad.Id,
                    ProductoId = unidad.ProductoId,
                    ProductoCodigo = unidad.Producto?.Codigo ?? string.Empty,
                    ProductoNombre = unidad.Producto?.Nombre ?? string.Empty,
                    CodigoInternoUnidad = unidad.CodigoInternoUnidad,
                    NumeroSerie = unidad.NumeroSerie,
                    EstadoActual = unidad.Estado,
                    Movimientos = movimientos.Select(m => new ProductoUnidadMovimientoItemViewModel
                    {
                        FechaCambio = m.FechaCambio,
                        EstadoAnterior = m.EstadoAnterior,
                        EstadoNuevo = m.EstadoNuevo,
                        Motivo = m.Motivo,
                        OrigenReferencia = m.OrigenReferencia,
                        UsuarioResponsable = m.UsuarioResponsable
                    }).ToList()
                };

                return View("UnidadHistorial", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar historial de unidad {UnidadId}", unidadId);
                TempData["Error"] = "Error al cargar el historial de la unidad. Intentá nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost("Producto/MarcarUnidadFaltante")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "productos", Accion = "edit")]
        public Task<IActionResult> MarcarUnidadFaltante(ProductoUnidadAjusteViewModel ajuste)
            => AplicarAjusteUnidadAsync(
                ajuste,
                (id, motivo, usuario) => _productoUnidadService.MarcarFaltanteAsync(id, motivo, usuario),
                "Unidad marcada como faltante. El stock agregado no fue modificado.");

        [HttpPost("Producto/DarUnidadBaja")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "productos", Accion = "edit")]
        public Task<IActionResult> DarUnidadBaja(ProductoUnidadAjusteViewModel ajuste)
            => AplicarAjusteUnidadAsync(
                ajuste,
                (id, motivo, usuario) => _productoUnidadService.MarcarBajaAsync(id, motivo, usuario),
                "Unidad dada de baja. El stock agregado no fue modificado.");

        [HttpPost("Producto/ReintegrarUnidadAStock")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "productos", Accion = "edit")]
        public Task<IActionResult> ReintegrarUnidadAStock(ProductoUnidadAjusteViewModel ajuste)
            => AplicarAjusteUnidadAsync(
                ajuste,
                (id, motivo, usuario) => _productoUnidadService.ReintegrarAStockAsync(id, motivo, usuario),
                "Unidad reintegrada a stock. El stock agregado no fue modificado.");

        private async Task<IActionResult> AplicarAjusteUnidadAsync(
            ProductoUnidadAjusteViewModel ajuste,
            Func<int, string, string?, Task<ProductoUnidad>> aplicarAjuste,
            string mensajeExito)
        {
            var unidad = await _productoUnidadService.ObtenerPorIdAsync(ajuste.ProductoUnidadId);
            if (unidad == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(ajuste.Motivo))
            {
                TempData["Error"] = "El motivo es obligatorio para ajustar la unidad.";
                return RedirectToAction(nameof(Unidades), new { productoId = unidad.ProductoId });
            }

            if (ajuste.Motivo.Length > 500)
            {
                TempData["Error"] = "El motivo no puede superar los 500 caracteres.";
                return RedirectToAction(nameof(Unidades), new { productoId = unidad.ProductoId });
            }

            try
            {
                await aplicarAjuste(
                    ajuste.ProductoUnidadId,
                    ajuste.Motivo.Trim(),
                    User?.Identity?.Name);

                TempData["Success"] = mensajeExito;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Motivo invalido al ajustar unidad {ProductoUnidadId}", ajuste.ProductoUnidadId);
                TempData["Error"] = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Transicion invalida al ajustar unidad {ProductoUnidadId}", ajuste.ProductoUnidadId);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ajustar unidad {ProductoUnidadId}", ajuste.ProductoUnidadId);
                TempData["Error"] = "Error al ajustar la unidad. Intenta nuevamente.";
            }

            return RedirectToAction(nameof(Unidades), new { productoId = unidad.ProductoId });
        }

        private async Task<IActionResult> VolverAUnidadesConFormularioAsync(ProductoUnidadCrearViewModel crearUnidad)
        {
            var viewModel = await ConstruirProductoUnidadesViewModelAsync(
                crearUnidad.ProductoId,
                new ProductoUnidadFiltros(),
                crearUnidad);

            if (viewModel == null)
                return NotFound();

            return View("Unidades", viewModel);
        }

        private async Task<IActionResult> VolverAUnidadesConCargaMasivaAsync(ProductoUnidadCargaMasivaViewModel cargaMasiva)
        {
            var viewModel = await ConstruirProductoUnidadesViewModelAsync(
                cargaMasiva.ProductoId,
                new ProductoUnidadFiltros(),
                cargaMasiva: cargaMasiva);

            if (viewModel == null)
                return NotFound();

            return View("Unidades", viewModel);
        }

        private async Task<ProductoUnidadesViewModel?> ConstruirProductoUnidadesViewModelAsync(
            int productoId,
            ProductoUnidadFiltros filtros,
            ProductoUnidadCrearViewModel? crearUnidad = null,
            ProductoUnidadCargaMasivaViewModel? cargaMasiva = null)
        {
            var producto = await _productoService.GetByIdAsync(productoId);
            if (producto == null)
                return null;

            var unidadesFiltradas = (await _productoUnidadService
                .ObtenerPorProductoFiltradoAsync(productoId, filtros))
                .ToList();

            var unidadesResumen = (await _productoUnidadService
                .ObtenerPorProductoAsync(productoId))
                .ToList();

            return new ProductoUnidadesViewModel
            {
                ProductoId = producto.Id,
                Codigo = producto.Codigo,
                Nombre = producto.Nombre,
                RequiereNumeroSerie = producto.RequiereNumeroSerie,
                StockActual = producto.StockActual,
                Filtros = new ProductoUnidadesFiltroViewModel
                {
                    Estado = filtros.Estado,
                    Texto = filtros.Texto,
                    SoloDisponibles = filtros.SoloDisponibles,
                    SoloVendidas = filtros.SoloVendidas,
                    SoloSinNumeroSerie = filtros.SoloSinNumeroSerie
                },
                CrearUnidad = crearUnidad ?? new ProductoUnidadCrearViewModel { ProductoId = producto.Id },
                CargaMasiva = cargaMasiva ?? new ProductoUnidadCargaMasivaViewModel { ProductoId = producto.Id },
                ResumenEstados = unidadesResumen
                    .GroupBy(u => u.Estado)
                    .OrderBy(g => g.Key)
                    .Select(g => new ProductoUnidadEstadoResumenViewModel
                    {
                        Estado = g.Key,
                        Cantidad = g.Count()
                    })
                    .ToList(),
                Unidades = unidadesFiltradas.Select(MapearUnidadItem).ToList()
            };
        }

        private static ProductoUnidadItemViewModel MapearUnidadItem(ProductoUnidad unidad)
            => new()
            {
                Id = unidad.Id,
                CodigoInternoUnidad = unidad.CodigoInternoUnidad,
                NumeroSerie = unidad.NumeroSerie,
                Estado = unidad.Estado,
                UbicacionActual = unidad.UbicacionActual,
                FechaIngreso = unidad.FechaIngreso,
                ClienteAsociado = unidad.Cliente?.ToDisplayName(),
                VentaDetalleId = unidad.VentaDetalleId,
                FechaVenta = unidad.FechaVenta,
                Observaciones = unidad.Observaciones,
                PuedeMarcarFaltante = unidad.Estado == EstadoUnidad.EnStock,
                PuedeDarBaja = unidad.Estado == EstadoUnidad.EnStock
                    || unidad.Estado == EstadoUnidad.Faltante
                    || unidad.Estado == EstadoUnidad.Devuelta,
                PuedeReintegrarAStock = unidad.Estado == EstadoUnidad.Faltante
                    || unidad.Estado == EstadoUnidad.Devuelta
            };

        private async Task PrepararPreviewCargaMasivaAsync(ProductoUnidadCargaMasivaViewModel cargaMasiva)
        {
            cargaMasiva.Preview.Clear();
            cargaMasiva.PreviewListo = false;

            var series = ParsearNumerosSerieCargaMasiva(cargaMasiva.NumerosSerieTexto);

            ValidarCantidadSinSerieCargaMasiva(cargaMasiva.CantidadSinSerie);
            ValidarDuplicadosEnCargaMasiva(series);
            await ValidarDuplicadosExistentesCargaMasivaAsync(cargaMasiva.ProductoId, series);

            var total = cargaMasiva.CantidadSinSerie + series.Count;
            if (total == 0)
            {
                ModelState.AddModelError(
                    $"{nameof(ProductoUnidadesViewModel.CargaMasiva)}.{nameof(ProductoUnidadCargaMasivaViewModel.CantidadSinSerie)}",
                    "Indica una cantidad sin serie o pega al menos un numero de serie.");
                return;
            }

            if (total > MaxUnidadesCargaMasiva)
            {
                ModelState.AddModelError(
                    $"{nameof(ProductoUnidadesViewModel.CargaMasiva)}.{nameof(ProductoUnidadCargaMasivaViewModel.CantidadSinSerie)}",
                    $"La carga masiva admite hasta {MaxUnidadesCargaMasiva} unidades por operacion.");
                return;
            }

            var orden = 1;
            for (var i = 0; i < cargaMasiva.CantidadSinSerie; i++)
            {
                cargaMasiva.Preview.Add(new ProductoUnidadCargaMasivaPreviewItemViewModel
                {
                    Orden = orden++
                });
            }

            foreach (var serie in series)
            {
                cargaMasiva.Preview.Add(new ProductoUnidadCargaMasivaPreviewItemViewModel
                {
                    Orden = orden++,
                    NumeroSerie = serie
                });
            }
        }

        private static List<string> ParsearNumerosSerieCargaMasiva(string? numerosSerieTexto)
            => string.IsNullOrWhiteSpace(numerosSerieTexto)
                ? new List<string>()
                : numerosSerieTexto
                    .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

        private void ValidarCantidadSinSerieCargaMasiva(int cantidadSinSerie)
        {
            if (cantidadSinSerie < 0)
            {
                ModelState.AddModelError(
                    $"{nameof(ProductoUnidadesViewModel.CargaMasiva)}.{nameof(ProductoUnidadCargaMasivaViewModel.CantidadSinSerie)}",
                    "La cantidad sin serie no puede ser negativa.");
            }

            if (cantidadSinSerie > MaxUnidadesCargaMasiva)
            {
                ModelState.AddModelError(
                    $"{nameof(ProductoUnidadesViewModel.CargaMasiva)}.{nameof(ProductoUnidadCargaMasivaViewModel.CantidadSinSerie)}",
                    $"La cantidad sin serie no puede superar {MaxUnidadesCargaMasiva} unidades.");
            }
        }

        private void ValidarDuplicadosEnCargaMasiva(IReadOnlyCollection<string> series)
        {
            var seriesDuplicadas = series
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (seriesDuplicadas.Any())
            {
                ModelState.AddModelError(
                    $"{nameof(ProductoUnidadesViewModel.CargaMasiva)}.{nameof(ProductoUnidadCargaMasivaViewModel.NumerosSerieTexto)}",
                    $"Hay numeros de serie repetidos en la carga: {string.Join(", ", seriesDuplicadas)}.");
            }

            var seriesLargas = series
                .Where(s => s.Length > 100)
                .ToList();

            if (seriesLargas.Any())
            {
                ModelState.AddModelError(
                    $"{nameof(ProductoUnidadesViewModel.CargaMasiva)}.{nameof(ProductoUnidadCargaMasivaViewModel.NumerosSerieTexto)}",
                    "Cada numero de serie debe tener 100 caracteres o menos.");
            }
        }

        private async Task ValidarDuplicadosExistentesCargaMasivaAsync(int productoId, IReadOnlyCollection<string> series)
        {
            if (!series.Any())
                return;

            var existentes = await _productoUnidadService.ObtenerPorProductoAsync(productoId);
            var seriesExistentes = existentes
                .Where(u => !string.IsNullOrWhiteSpace(u.NumeroSerie))
                .Select(u => u.NumeroSerie!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var duplicadas = series
                .Where(seriesExistentes.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (duplicadas.Any())
            {
                ModelState.AddModelError(
                    $"{nameof(ProductoUnidadesViewModel.CargaMasiva)}.{nameof(ProductoUnidadCargaMasivaViewModel.NumerosSerieTexto)}",
                    $"Ya existen unidades activas con estos numeros de serie: {string.Join(", ", duplicadas)}.");
            }
        }

        #endregion
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// D-01: ABM de listas de precios y fijación manual del precio de un producto en una lista.
    /// Ver = precios.view; alta = precios.create; edición = precios.update; baja = precios.delete;
    /// fijar un precio = precios.apply (mismo permiso que modifica importes en el resto del ERP).
    /// Las reglas viven en <see cref="IPrecioService"/>; el controller solo coordina y audita.
    /// No cambia el cálculo de precio de Venta/Cotización ni asigna listas a clientes.
    /// </summary>
    [Authorize]
    [AutoValidateAntiforgeryToken]
    [PermisoRequerido(Modulo = "precios", Accion = "view")]
    public class ListaPrecioController : Controller
    {
        private const int TamanoPagina = 25;

        private readonly IPrecioService _precioService;
        private readonly IProductoService _productoService;
        private readonly ISeguridadAuditoriaService _auditoria;

        public ListaPrecioController(
            IPrecioService precioService,
            IProductoService productoService,
            ISeguridadAuditoriaService auditoria)
        {
            _precioService = precioService;
            _productoService = productoService;
            _auditoria = auditoria;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var vm = new ListaPrecioIndexViewModel
            {
                Listas = await _precioService.GetResumenListasAsync(),
                PuedeCrear = User.TienePermiso("precios", "create"),
                PuedeEditar = User.TienePermiso("precios", "update"),
                PuedeEliminar = User.TienePermiso("precios", "delete"),
                PuedeAplicar = User.TienePermiso("precios", "apply")
            };
            return View("Index_tw", vm);
        }

        // ---------- alta ----------

        [HttpGet]
        [PermisoRequerido(Modulo = "precios", Accion = "create")]
        public IActionResult Crear() => View("Form_tw", new ListaPrecioFormViewModel());

        [HttpPost]
        [PermisoRequerido(Modulo = "precios", Accion = "create")]
        public async Task<IActionResult> Crear(ListaPrecioFormViewModel modelo)
        {
            if (!ModelState.IsValid)
                return View("Form_tw", modelo);

            try
            {
                var lista = ALista(modelo);
                await _precioService.CreateListaAsync(lista);
                await _auditoria.RegistrarEventoAsync("precios", "crear-lista", nameof(ListaPrecio),
                    $"{lista.Codigo} id={lista.Id}" + (lista.EsPredeterminada ? " predeterminada" : string.Empty));
                TempData["Success"] = $"Lista «{lista.Nombre}» creada.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ViewBag.Error = ex.Message;
                return View("Form_tw", modelo);
            }
        }

        // ---------- edición ----------

        [HttpGet]
        [PermisoRequerido(Modulo = "precios", Accion = "update")]
        public async Task<IActionResult> Editar(int id)
        {
            var lista = await _precioService.GetListaByIdAsync(id);
            if (lista is null)
                return NotFound();

            return View("Form_tw", AModelo(lista));
        }

        [HttpPost]
        [PermisoRequerido(Modulo = "precios", Accion = "update")]
        public async Task<IActionResult> Editar(int id, ListaPrecioFormViewModel modelo)
        {
            if (id <= 0 || modelo.Id != id)
                return BadRequest();

            if (!ModelState.IsValid)
                return View("Form_tw", modelo);

            try
            {
                var lista = ALista(modelo);
                await _precioService.UpdateListaAsync(lista, modelo.RowVersion ?? Array.Empty<byte>());
                await _auditoria.RegistrarEventoAsync("precios", "editar-lista", nameof(ListaPrecio), $"{lista.Codigo.Trim().ToUpperInvariant()} id={id}");
                TempData["Success"] = $"Lista «{lista.Nombre}» actualizada.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                ViewBag.Error = "La lista fue modificada por otro usuario. Recargá la página para ver los datos actuales.";
                return View("Form_tw", modelo);
            }
            catch (InvalidOperationException ex)
            {
                ViewBag.Error = ex.Message;
                return View("Form_tw", modelo);
            }
        }

        // ---------- baja ----------

        [HttpPost]
        [PermisoRequerido(Modulo = "precios", Accion = "delete")]
        public async Task<IActionResult> Baja(int id, byte[]? rowVersion)
        {
            if (id <= 0)
                return BadRequest();

            try
            {
                var ok = await _precioService.DeleteListaAsync(id, rowVersion ?? Array.Empty<byte>());
                if (!ok)
                {
                    TempData["Error"] = "La lista no existe.";
                    return RedirectToAction(nameof(Index));
                }

                await _auditoria.RegistrarEventoAsync("precios", "baja-lista", nameof(ListaPrecio), $"id={id}");
                TempData["Success"] = "Lista dada de baja. Sus precios e historial se conservan.";
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["Error"] = "La lista fue modificada por otro usuario. Recargá e intentá de nuevo.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // ---------- precios por producto ----------

        [HttpGet]
        public async Task<IActionResult> Precios(int id, string? busqueda, int pagina = 1)
        {
            var lista = await _precioService.GetListaByIdAsync(id);
            if (lista is null)
                return NotFound();

            ViewBag.Lista = lista;
            ViewBag.Busqueda = busqueda;
            ViewBag.PuedeFijar = User.TienePermiso("precios", "apply");
            var datos = await _precioService.GetPreciosDeListaAsync(id, busqueda, pagina, TamanoPagina);
            ViewBag.Sugeridos = datos.Items.ToDictionary(
                i => i.ProductoId,
                i => i.CostoProducto > 0 ? _precioService.CalcularPrecioSugerido(lista, i.CostoProducto) : (decimal?)null);
            return View("Precios_tw", datos);
        }

        [HttpGet]
        [PermisoRequerido(Modulo = "precios", Accion = "apply")]
        public async Task<IActionResult> FijarPrecio(int listaId, int productoId)
        {
            var vm = await ArmarFijarPrecioAsync(listaId, productoId);
            return vm is null ? NotFound() : View("FijarPrecio_tw", vm);
        }

        [HttpPost]
        [PermisoRequerido(Modulo = "precios", Accion = "apply")]
        public async Task<IActionResult> FijarPrecio(int listaId, int productoId, decimal? precio, string? notas)
        {
            var vm = await ArmarFijarPrecioAsync(listaId, productoId);
            if (vm is null)
                return NotFound();

            vm.Precio = precio;
            vm.Notas = notas;

            if (!precio.HasValue || precio.Value <= 0)
            {
                ViewBag.Error = "Ingresá un precio mayor a cero.";
                return View("FijarPrecio_tw", vm);
            }

            try
            {
                // El costo se toma del producto en el servidor: el formulario no puede falsearlo.
                await _precioService.SetPrecioManualAsync(productoId, listaId, precio.Value, vm.CostoProducto, null, notas);
                await _auditoria.RegistrarEventoAsync("precios", "fijar-precio", nameof(ProductoPrecioLista),
                    $"producto={productoId} lista={listaId} precio={precio.Value:0.00}");
                TempData["Success"] = $"Precio de {vm.ProductoCodigo} en «{vm.ListaNombre}» actualizado.";
                return RedirectToAction(nameof(Precios), new { id = listaId });
            }
            catch (InvalidOperationException ex)
            {
                ViewBag.Error = ex.Message;
                return View("FijarPrecio_tw", vm);
            }
        }

        // ---------- helpers ----------

        private async Task<FijarPrecioViewModel?> ArmarFijarPrecioAsync(int listaId, int productoId)
        {
            var lista = await _precioService.GetListaByIdAsync(listaId);
            var producto = await _productoService.GetByIdAsync(productoId);
            if (lista is null || producto is null)
                return null;

            var historial = await _precioService.GetHistorialPreciosAsync(productoId, listaId);
            return new FijarPrecioViewModel
            {
                ListaId = lista.Id,
                ListaNombre = lista.Nombre,
                ListaCodigo = lista.Codigo,
                MargenMinimoPorcentaje = lista.MargenMinimoPorcentaje,
                ProductoId = producto.Id,
                ProductoCodigo = producto.Codigo,
                ProductoNombre = producto.Nombre,
                CostoProducto = producto.PrecioCompra,
                PrecioBase = producto.PrecioVenta,
                PrecioSugerido = producto.PrecioCompra > 0 ? _precioService.CalcularPrecioSugerido(lista, producto.PrecioCompra) : null,
                Historial = historial.Select(p => new PrecioHistorialItemViewModel
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
                }).Take(20).ToList()
            };
        }

        private static ListaPrecio ALista(ListaPrecioFormViewModel m) => new()
        {
            Id = m.Id,
            Nombre = m.Nombre,
            Codigo = m.Codigo,
            Tipo = m.Tipo,
            Descripcion = m.Descripcion,
            MargenPorcentaje = m.MargenPorcentaje,
            MargenMinimoPorcentaje = m.MargenMinimoPorcentaje,
            RecargoPorcentaje = m.RecargoPorcentaje,
            CantidadCuotas = m.CantidadCuotas,
            ReglaRedondeo = m.ReglaRedondeo,
            Orden = m.Orden,
            Notas = m.Notas,
            Activa = m.Activa,
            EsPredeterminada = m.EsPredeterminada
        };

        private static ListaPrecioFormViewModel AModelo(ListaPrecio l) => new()
        {
            Id = l.Id,
            RowVersion = l.RowVersion,
            Nombre = l.Nombre,
            Codigo = l.Codigo,
            Tipo = l.Tipo,
            Descripcion = l.Descripcion,
            MargenPorcentaje = l.MargenPorcentaje,
            MargenMinimoPorcentaje = l.MargenMinimoPorcentaje,
            RecargoPorcentaje = l.RecargoPorcentaje,
            CantidadCuotas = l.CantidadCuotas,
            ReglaRedondeo = l.ReglaRedondeo,
            Orden = l.Orden,
            Notas = l.Notas,
            Activa = l.Activa,
            EsPredeterminada = l.EsPredeterminada,
            EraPredeterminada = l.EsPredeterminada
        };
    }
}

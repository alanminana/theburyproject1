using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "reportes", Accion = "view")]
    public class ReporteController : Controller
    {
        private readonly IReporteService _reporteService;
        private readonly IClienteService _clienteService;
        private readonly IProductoService _productoService;
        private readonly ICategoriaService _categoriaService;
        private readonly IMarcaService _marcaService;
        private readonly IUsuarioService _usuarioService;
        private readonly ILogger<ReporteController> _logger;

        public ReporteController(
            IReporteService reporteService,
            IClienteService clienteService,
            IProductoService productoService,
            ICategoriaService categoriaService,
            IMarcaService marcaService,
            IUsuarioService usuarioService,
            ILogger<ReporteController> logger)
        {
            _reporteService = reporteService;
            _clienteService = clienteService;
            _productoService = productoService;
            _categoriaService = categoriaService;
            _marcaService = marcaService;
            _usuarioService = usuarioService;
            _logger = logger;
        }

        #region Vistas — Index / Ventas / Márgenes / Morosidad / Agrupadas

        // GET: Reporte
        public IActionResult Index()
        {
            return View("Index_tw");
        }

        // GET: Reporte/Ventas
        public async Task<IActionResult> Ventas()
        {
            await CargarFiltrosViewBagAsync();

            // Por defecto mostrar último mes
            var filtro = new ReporteVentasFiltroViewModel
            {
                FechaDesde = DateTime.Today.AddMonths(-1),
                FechaHasta = DateTime.Today
            };

            ViewBag.Filtro = filtro;
            return View("Ventas_tw");
        }

        // POST: Reporte/Ventas
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ventas(ReporteVentasFiltroViewModel filtro)
        {
            try
            {
                await CargarFiltrosViewBagAsync();

                var resultado = await _reporteService.GenerarReporteVentasAsync(filtro);
                ViewBag.Filtro = filtro;

                return View("Ventas_tw", resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de ventas");
                TempData["Error"] = "Error al generar el reporte de ventas";
                return RedirectToAction(nameof(Ventas));
            }
        }

        // GET: Reporte/Margenes
        public async Task<IActionResult> Margenes(int? categoriaId, int? marcaId)
        {
            try
            {
                await CargarFiltrosViewBagAsync();

                var resultado = await _reporteService.GenerarReporteMargenesAsync(categoriaId, marcaId);

                ViewBag.CategoriaId = categoriaId;
                ViewBag.MarcaId = marcaId;

                return View("Margenes_tw", resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de márgenes");
                TempData["Error"] = "Error al generar el reporte de márgenes";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reporte/Morosidad
        public async Task<IActionResult> Morosidad()
        {
            try
            {
                var resultado = await _reporteService.GenerarReporteMorosidadAsync();
                return View("Morosidad_tw", resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de morosidad");
                TempData["Error"] = "Error al generar el reporte de morosidad";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Reporte/VentasAgrupadas
        [HttpGet]
        public async Task<IActionResult> VentasAgrupadas(DateTime? fechaDesde, DateTime? fechaHasta, string agruparPor = "dia")
        {
            try
            {
                var desde = fechaDesde ?? DateTime.Today.AddMonths(-1);
                var hasta = fechaHasta ?? DateTime.Today;

                var datos = await _reporteService.ObtenerVentasAgrupadasAsync(desde, hasta, agruparPor);

                return Json(datos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener ventas agrupadas");
                return BadRequest("Error al obtener datos");
            }
        }

        #endregion

        #region Exportar — Excel y PDF

        // GET: Reporte/ExportarVentasExcel
        public async Task<IActionResult> ExportarVentasExcel([FromQuery] ReporteVentasFiltroViewModel filtro)
            => await ExportarExcel(
                () => _reporteService.ExportarVentasExcelAsync(filtro),
                "ReporteVentas",
                nameof(Ventas));

        // GET: Reporte/ExportarMargenesExcel
        public async Task<IActionResult> ExportarMargenesExcel(int? categoriaId, int? marcaId)
            => await ExportarExcel(
                () => _reporteService.ExportarMargenesExcelAsync(categoriaId, marcaId),
                "ReporteMargenes",
                nameof(Margenes));

        // GET: Reporte/ExportarMorosidadExcel
        public async Task<IActionResult> ExportarMorosidadExcel()
            => await ExportarExcel(
                () => _reporteService.ExportarMorosidadExcelAsync(),
                "ReporteMorosidad",
                nameof(Morosidad));

        // GET: Reporte/ExportarVentasPdf
        public async Task<IActionResult> ExportarVentasPdf([FromQuery] ReporteVentasFiltroViewModel filtro)
            => await ExportarPdf(
                () => _reporteService.GenerarVentasPdfAsync(filtro),
                "ReporteVentas",
                nameof(Ventas));

        // GET: Reporte/ExportarMorosidadPdf
        public async Task<IActionResult> ExportarMorosidadPdf()
            => await ExportarPdf(
                () => _reporteService.GenerarMorosidadPdfAsync(),
                "ReporteMorosidad",
                nameof(Morosidad));

        #endregion

        #region Métodos auxiliares

        private async Task<IActionResult> ExportarExcel(Func<Task<byte[]>> generarDatos, string nombreReporte, string accionFallback)
        {
            try
            {
                var data = await generarDatos();
                var fileName = $"{nombreReporte}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
                return File(data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (NotImplementedException)
            {
                TempData["Warning"] = "La exportación a Excel aún no está implementada. Requiere instalar el paquete ClosedXML.";
                return RedirectToAction(accionFallback);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al exportar {Reporte} a Excel", nombreReporte);
                TempData["Error"] = "Error al exportar el reporte";
                return RedirectToAction(accionFallback);
            }
        }

        private async Task<IActionResult> ExportarPdf(Func<Task<byte[]>> generarDatos, string nombreReporte, string accionFallback)
        {
            try
            {
                var data = await generarDatos();
                var fileName = $"{nombreReporte}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
                return File(data, "application/pdf", fileName);
            }
            catch (NotImplementedException)
            {
                TempData["Warning"] = "La exportación a PDF aún no está implementada. Requiere instalar el paquete QuestPDF o iTextSharp.";
                return RedirectToAction(accionFallback);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al exportar {Reporte} a PDF", nombreReporte);
                TempData["Error"] = "Error al exportar el reporte";
                return RedirectToAction(accionFallback);
            }
        }

        private async Task CargarFiltrosViewBagAsync()
        {
            var clientes = await _clienteService.GetAllAsync();
            var productos = await _productoService.GetAllAsync();
            var categorias = await _categoriaService.GetAllAsync();
            var marcas = await _marcaService.GetAllAsync();

            var usuarios = await _usuarioService.GetUsuarioSelectListAsync();

            ViewBag.Clientes = new SelectList(clientes, "Id", "NombreCompleto");
            ViewBag.Productos = new SelectList(productos, "Id", "Nombre");
            ViewBag.Categorias = new SelectList(categorias, "Id", "Nombre");
            ViewBag.Marcas = new SelectList(marcas, "Id", "Nombre");
            ViewBag.Vendedores = new SelectList(usuarios, "Id", "UserName");
        }

        #endregion
    }
}
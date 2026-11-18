using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// R-03: pantalla de LECTURA de la valoración gerencial del stock (costo promedio ponderado). Permiso propio
    /// <c>valoracionstock.view</c>. No tiene acciones que escriban: el cálculo vive en
    /// <see cref="IValoracionStockGerencialService"/> y no modifica costos, precios ni márgenes.
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "valoracionstock", Accion = "view")]
    public class ValoracionStockController : Controller
    {
        private const int TamanoPagina = 50;

        private readonly IValoracionStockGerencialService _service;
        private readonly AppDbContext _context;
        private readonly ILogger<ValoracionStockController> _logger;

        public ValoracionStockController(
            IValoracionStockGerencialService service, AppDbContext context, ILogger<ValoracionStockController> logger)
        {
            _service = service;
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(FiltroValoracionStock filtro, int pagina = 1, CancellationToken cancellationToken = default)
        {
            filtro ??= new FiltroValoracionStock();
            var resultado = await _service.CalcularAsync(filtro, cancellationToken);

            var totalPaginas = Math.Max(1, (int)Math.Ceiling(resultado.Items.Count / (double)TamanoPagina));
            pagina = Math.Clamp(pagina, 1, totalPaginas);

            var vm = new ValoracionStockIndexViewModel
            {
                Filtro = filtro,
                Resumen = resultado.Resumen,
                FechaCalculo = resultado.FechaCalculo,
                Pagina = pagina,
                TamanoPagina = TamanoPagina,
                TotalItems = resultado.Items.Count,
                Items = resultado.Items.Skip((pagina - 1) * TamanoPagina).Take(TamanoPagina).ToList(),
                Categorias = (await _context.Categorias.AsNoTracking().Where(c => !c.IsDeleted).OrderBy(c => c.Nombre)
                    .Select(c => new { c.Id, c.Nombre }).ToListAsync(cancellationToken)).Select(c => (c.Id, c.Nombre)).ToList(),
                Marcas = (await _context.Marcas.AsNoTracking().Where(m => !m.IsDeleted).OrderBy(m => m.Nombre)
                    .Select(m => new { m.Id, m.Nombre }).ToListAsync(cancellationToken)).Select(m => (m.Id, m.Nombre)).ToList()
            };
            return View("Index_tw", vm);
        }

        [HttpGet]
        public async Task<IActionResult> Exportar(FiltroValoracionStock filtro, CancellationToken cancellationToken = default)
        {
            var contenido = await _service.ExportarAsync(filtro ?? new FiltroValoracionStock(), cancellationToken);
            _logger.LogInformation("Valoración gerencial de stock exportada por {User}", User.Identity?.Name);
            return File(contenido,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"valoracion-stock-gerencial-{DateTime.UtcNow:yyyyMMdd}.xlsx");
        }
    }
}

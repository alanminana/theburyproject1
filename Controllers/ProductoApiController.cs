using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/productos")]
    public class ProductoApiController : ControllerBase
    {
        private readonly IProductoUnidadService _productoUnidadService;
        private readonly ILogger<ProductoApiController> _logger;

        public ProductoApiController(
            IProductoUnidadService productoUnidadService,
            ILogger<ProductoApiController> logger)
        {
            _productoUnidadService = productoUnidadService;
            _logger = logger;
        }

        /// <summary>
        /// Retorna las unidades físicas disponibles (EnStock) de un producto trazable.
        /// Usado por el selector de unidad en el formulario de venta.
        /// </summary>
        [HttpGet("{productoId:int}/unidades-disponibles")]
        [PermisoRequerido(Modulo = "ventas", Accion = "view")]
        public async Task<IActionResult> GetUnidadesDisponibles(int productoId)
        {
            try
            {
                var unidades = await _productoUnidadService.ObtenerDisponiblesPorProductoAsync(productoId);

                var resultado = unidades.Select(u => new
                {
                    id = u.Id,
                    codigoInternoUnidad = u.CodigoInternoUnidad,
                    numeroSerie = u.NumeroSerie,
                    ubicacionActual = u.UbicacionActual,
                    estado = u.Estado.ToString()
                });

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener unidades disponibles para producto {ProductoId}", productoId);
                return StatusCode(500, new { error = "Error al obtener unidades disponibles" });
            }
        }
    }
}

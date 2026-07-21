using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public sealed class ProductoCreditoPersonalConfigService : IProductoCreditoPersonalConfigService
    {
        private static readonly int[] CuotasCandidatasDefault = { 1, 3, 6, 9, 12, 18, 24 };

        private readonly AppDbContext _context;
        private readonly IConfiguracionPagoService _configuracionPagoService;

        public ProductoCreditoPersonalConfigService(
            AppDbContext context,
            IConfiguracionPagoService configuracionPagoService)
        {
            _context = context;
            _configuracionPagoService = configuracionPagoService;
        }

        public async Task<ProductoCreditoPersonalConfigViewModel> ObtenerAsync(int productoId)
        {
            var restriccion = await _context.ProductoCreditoRestricciones
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.ProductoId == productoId && r.Activo && !r.IsDeleted);

            var propias = await _context.ProductoCreditoPersonalCuotas
                .AsNoTracking()
                .Where(c => c.ProductoId == productoId)
                .OrderBy(c => c.CantidadCuotas)
                .ToListAsync();

            var globales = await _configuracionPagoService.GetCuotasCreditoPersonalActivasAsync();

            // Filas de edición: planes propios + plantillas candidatas (globales y defaults)
            // que el operador puede activar. Las plantillas no se persisten si quedan inactivas.
            var cuotas = propias
                .Select(c => new CuotaCreditoPersonalViewModel
                {
                    Id = c.Id,
                    CantidadCuotas = c.CantidadCuotas,
                    TasaMensual = c.TasaMensual,
                    Activo = c.Activo,
                    Orden = c.Orden
                })
                .ToList();

            var cantidadesExistentes = cuotas.Select(c => c.CantidadCuotas).ToHashSet();

            foreach (var plantilla in globales.Where(g => !cantidadesExistentes.Contains(g.CantidadCuotas)))
            {
                cuotas.Add(new CuotaCreditoPersonalViewModel
                {
                    Id = 0,
                    CantidadCuotas = plantilla.CantidadCuotas,
                    TasaMensual = plantilla.TasaMensual,
                    Activo = false,
                    Orden = plantilla.CantidadCuotas
                });
                cantidadesExistentes.Add(plantilla.CantidadCuotas);
            }

            foreach (var cantidad in CuotasCandidatasDefault.Where(c => !cantidadesExistentes.Contains(c)))
            {
                cuotas.Add(new CuotaCreditoPersonalViewModel
                {
                    Id = 0,
                    CantidadCuotas = cantidad,
                    TasaMensual = 0m,
                    Activo = false,
                    Orden = cantidad
                });
            }

            return new ProductoCreditoPersonalConfigViewModel
            {
                AdmiteCreditoPersonal = restriccion?.Permitido ?? true,
                MaxCuotasCredito = restriccion?.MaxCuotasCredito,
                Cuotas = cuotas.OrderBy(c => c.CantidadCuotas).ToList()
            };
        }

        public async Task GuardarAsync(int productoId, ProductoCreditoPersonalConfigViewModel config, string usuario)
        {
            ArgumentNullException.ThrowIfNull(config);

            var restriccion = await _context.ProductoCreditoRestricciones
                .FirstOrDefaultAsync(r => r.ProductoId == productoId && r.Activo && !r.IsDeleted);

            var necesitaRestriccion = !config.AdmiteCreditoPersonal || config.MaxCuotasCredito.HasValue;

            if (restriccion == null && necesitaRestriccion)
            {
                restriccion = new ProductoCreditoRestriccion
                {
                    ProductoId = productoId,
                    Activo = true
                };
                _context.ProductoCreditoRestricciones.Add(restriccion);
            }

            if (restriccion != null)
            {
                restriccion.Permitido = config.AdmiteCreditoPersonal;
                restriccion.MaxCuotasCredito = config.MaxCuotasCredito;
            }

            var existentes = await _context.ProductoCreditoPersonalCuotas
                .Where(c => c.ProductoId == productoId)
                .ToListAsync();

            var entrantes = (config.Cuotas ?? new List<CuotaCreditoPersonalViewModel>())
                .Where(c => c.CantidadCuotas >= 1 && c.CantidadCuotas <= 120 && c.TasaMensual >= 0 && c.TasaMensual <= 100)
                .GroupBy(c => c.CantidadCuotas)
                .Select(g => g.First())
                .ToList();

            foreach (var entrante in entrantes)
            {
                var existente = existentes.FirstOrDefault(e => e.CantidadCuotas == entrante.CantidadCuotas);

                if (existente != null)
                {
                    existente.TasaMensual = entrante.TasaMensual;
                    existente.Activo = entrante.Activo;
                    existente.Orden = entrante.Orden;
                    existente.FechaActualizacion = DateTime.UtcNow;
                    existente.UsuarioActualizacion = usuario;
                }
                else if (entrante.Activo)
                {
                    // Plantilla activada por el operador: alta de plan propio del producto.
                    _context.ProductoCreditoPersonalCuotas.Add(new ProductoCreditoPersonalCuota
                    {
                        ProductoId = productoId,
                        CantidadCuotas = entrante.CantidadCuotas,
                        TasaMensual = entrante.TasaMensual,
                        Activo = true,
                        Orden = entrante.Orden,
                        FechaActualizacion = DateTime.UtcNow,
                        UsuarioActualizacion = usuario
                    });
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}

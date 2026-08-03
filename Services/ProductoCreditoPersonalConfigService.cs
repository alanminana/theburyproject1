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
                    TasaMensual = null, // heredar la tasa global por defecto al activar la plantilla
                    Activo = false,
                    Orden = cantidad
                });
            }

            var modo = restriccion?.Permitido == false
                ? ModoCreditoPersonalProducto.NoDisponible
                : propias.Any(c => c.Activo)
                    ? ModoCreditoPersonalProducto.ConfiguracionPropia
                    : ModoCreditoPersonalProducto.HeredaGlobal;

            return new ProductoCreditoPersonalConfigViewModel
            {
                Modo = modo,
                AdmiteCreditoPersonal = restriccion?.Permitido ?? true,
                MaxCuotasCredito = restriccion?.MaxCuotasCredito,
                Cuotas = cuotas.OrderBy(c => c.CantidadCuotas).ToList()
            };
        }

        public List<string> Validar(ProductoCreditoPersonalConfigViewModel config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var errores = new List<string>();
            var entrantes = config.Cuotas ?? new List<CuotaCreditoPersonalViewModel>();

            // El modo declarado (radio de la UI) es una señal explícita e independiente de
            // AdmiteCreditoPersonal/Cuotas: un payload manipulado que los contradiga se rechaza
            // acá, antes de tocar la base de datos.
            var bloqueadoPorFlag = !config.AdmiteCreditoPersonal;
            var bloqueadoPorModo = config.Modo == ModoCreditoPersonalProducto.NoDisponible;
            if (bloqueadoPorFlag != bloqueadoPorModo)
                errores.Add("El modo declarado de Crédito Personal no coincide con 'Admite crédito personal'.");

            var hayActivosEntrantes = entrantes.Any(c => c.Activo);
            if (bloqueadoPorFlag && hayActivosEntrantes)
                errores.Add("No se puede bloquear Crédito Personal y mantener planes propios activos a la vez.");
            if (config.Modo == ModoCreditoPersonalProducto.HeredaGlobal && hayActivosEntrantes)
                errores.Add("El modo 'Hereda configuración global' no admite planes propios activos.");

            var cantidades = entrantes.Select(c => c.CantidadCuotas).ToList();
            var repetidas = cantidades.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repetidas.Count > 0)
                errores.Add($"Cantidades de cuotas duplicadas: {string.Join(", ", repetidas)}.");

            var fueraDeRango = cantidades.Where(c => c < 1 || c > 120).ToList();
            if (fueraDeRango.Count > 0)
                errores.Add($"Cantidades de cuotas fuera de rango 1–120: {string.Join(", ", fueraDeRango)}.");

            if (entrantes.Any(c => c.TasaMensual < 0))
                errores.Add("El recargo no puede ser negativo.");

            if (config.MaxCuotasCredito is < 1 or > 120)
                errores.Add("El máximo de cuotas debe estar entre 1 y 120.");

            return errores;
        }

        public async Task<(bool Ok, List<string> Errores)> GuardarAsync(int productoId, ProductoCreditoPersonalConfigViewModel config, string usuario)
        {
            ArgumentNullException.ThrowIfNull(config);

            var errores = Validar(config);
            if (errores.Count > 0)
                return (false, errores);

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

            var entrantes = config.Cuotas ?? new List<CuotaCreditoPersonalViewModel>();

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

            return (true, errores);
        }
    }
}

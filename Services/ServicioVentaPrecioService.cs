using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class ServicioVentaPrecioService : IServicioVentaPrecioService
    {
        private readonly AppDbContext _context;

        public ServicioVentaPrecioService(AppDbContext context)
        {
            _context = context;
        }

        public Task<IReadOnlyList<ServicioVentaPrecioInfo>> ListarAsync() =>
            ServiciosVentaPrecios.ListarAsync(_context);

        public async Task GuardarAsync(IEnumerable<ServicioVentaPrecioComando> comandos)
        {
            var lista = comandos.ToList();

            foreach (var c in lista)
            {
                if (!Enum.IsDefined(c.Tipo))
                    throw new InvalidOperationException("Tipo de servicio inválido.");
                if (c.Precio < 0m)
                    throw new InvalidOperationException($"El precio de «{c.Tipo.NombreVisible()}» no puede ser negativo.");
                if (c.Precio > Validation.MontoArgentinoAttribute.Maximo)
                    throw new InvalidOperationException($"El precio de «{c.Tipo.NombreVisible()}» no puede superar 20.000.000.");
                if (c.Costo < 0m)
                    throw new InvalidOperationException($"El costo de «{c.Tipo.NombreVisible()}» no puede ser negativo.");
                if (c.Costo > Validation.MontoArgentinoAttribute.Maximo)
                    throw new InvalidOperationException($"El costo de «{c.Tipo.NombreVisible()}» no puede superar 20.000.000.");
            }

            var duplicado = lista.GroupBy(c => c.Tipo).FirstOrDefault(g => g.Count() > 1);
            if (duplicado != null)
                throw new InvalidOperationException($"«{duplicado.Key.NombreVisible()}» está repetido.");

            var existentes = await _context.ServiciosVentaPrecios
                .Where(s => !s.IsDeleted)
                .ToDictionaryAsync(s => s.Tipo);

            foreach (var c in lista)
            {
                var precio = Math.Round(c.Precio, 2, MidpointRounding.AwayFromZero);
                var costo = Math.Round(c.Costo, 2, MidpointRounding.AwayFromZero);
                if (existentes.TryGetValue(c.Tipo, out var fila))
                {
                    fila.Precio = precio;
                    fila.Costo = costo;
                    fila.Activo = c.Activo;
                }
                else
                {
                    _context.ServiciosVentaPrecios.Add(new ServicioVentaPrecio
                    {
                        Tipo = c.Tipo,
                        Precio = precio,
                        Costo = costo,
                        Activo = c.Activo
                    });
                }
            }

            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Lectura compartida de los precios globales: la usan la administración y VentaService
    /// (que resuelve el precio server-side al guardar y al calcular el preview).
    /// </summary>
    internal static class ServiciosVentaPrecios
    {
        public static async Task<IReadOnlyList<ServicioVentaPrecioInfo>> ListarAsync(AppDbContext context)
        {
            var filas = await context.ServiciosVentaPrecios
                .AsNoTracking()
                .Where(s => !s.IsDeleted)
                .ToDictionaryAsync(s => s.Tipo);

            return TipoServicioVentaExtensions.Todos
                .Select(t => filas.TryGetValue(t, out var f)
                    ? new ServicioVentaPrecioInfo(t, f.Precio, f.Activo, f.Costo)
                    : new ServicioVentaPrecioInfo(t, 0m, true))
                .ToList();
        }

        /// <summary>
        /// Precio a cobrar del servicio. Lanza si el tipo no es un servicio válido para el uso pedido
        /// (envío vs armado) o si está desactivado.
        /// </summary>
        public static async Task<decimal> ObtenerPrecioAsync(AppDbContext context, TipoServicioVenta tipo)
        {
            if (!Enum.IsDefined(tipo))
                throw new InvalidOperationException("Tipo de servicio inválido.");

            var fila = await context.ServiciosVentaPrecios
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Tipo == tipo && !s.IsDeleted);

            if (fila is { Activo: false })
                throw new InvalidOperationException($"«{tipo.NombreVisible()}» no está disponible.");

            return fila?.Precio ?? 0m;
        }

        /// <summary>
        /// Costo estándar (para el negocio) del servicio, para congelar el snapshot en la venta. 0 si no está
        /// configurado. No valida Activo: el costo de un servicio ya vendido no depende de que siga ofrecido.
        /// </summary>
        public static async Task<decimal> ObtenerCostoAsync(AppDbContext context, TipoServicioVenta tipo)
        {
            var costo = await context.ServiciosVentaPrecios
                .AsNoTracking()
                .Where(s => s.Tipo == tipo && !s.IsDeleted)
                .Select(s => (decimal?)s.Costo)
                .FirstOrDefaultAsync();

            return costo ?? 0m;
        }
    }
}

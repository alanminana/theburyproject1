using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    public class VentaNumberGenerator
    {
        private readonly AppDbContext _context;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public VentaNumberGenerator(AppDbContext context)
        {
            _context = context;
        }

        public async Task<string> GenerarNumeroAsync(EstadoVenta estado)
        {
            await _semaphore.WaitAsync();
            try
            {
                var prefijo = estado == EstadoVenta.Cotizacion
                    ? VentaConstants.PREFIJO_COTIZACION
                    : VentaConstants.PREFIJO_VENTA;

                var fecha = DateTime.UtcNow;
                var periodo = fecha.ToString(VentaConstants.FORMATO_PERIODO);
                var prefijoCompleto = $"{prefijo}-{periodo}";

                var ultimaVenta = await _context.Ventas
                    .Where(v => v.Numero.StartsWith(prefijoCompleto))
                    .OrderByDescending(v => v.Numero)
                    .FirstOrDefaultAsync();

                int siguiente = 1;

                if (ultimaVenta != null)
                {
                    var partes = ultimaVenta.Numero.Split('-');
                    if (partes.Length == 3 && int.TryParse(partes[2], out int ultimo))
                    {
                        siguiente = ultimo + 1;
                    }
                }

                return string.Format(VentaConstants.FORMATO_NUMERO_VENTA, prefijo, periodo, siguiente);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<string> GenerarNumeroFacturaAsync(TipoFactura tipo)
        {
            await _semaphore.WaitAsync();
            try
            {
                var prefijo = ObtenerPrefijoFactura(tipo);
                var fecha = DateTime.UtcNow;
                var periodo = fecha.ToString(VentaConstants.FORMATO_PERIODO);
                var prefijoCompleto = $"{prefijo}-{periodo}";

                var ultimaFactura = await _context.Facturas
                    .Where(f => f.Numero.StartsWith(prefijoCompleto))
                    .OrderByDescending(f => f.Numero)
                    .FirstOrDefaultAsync();

                int siguiente = 1;

                if (ultimaFactura != null)
                {
                    var partes = ultimaFactura.Numero.Split('-');
                    if (partes.Length >= 3 && int.TryParse(partes[2], out int ultimo))
                    {
                        siguiente = ultimo + 1;
                    }
                }

                return string.Format(VentaConstants.FORMATO_NUMERO_VENTA, prefijo, periodo, siguiente);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static string ObtenerPrefijoFactura(TipoFactura tipo)
        {
            return tipo switch
            {
                TipoFactura.A => VentaConstants.FacturaPrefijos.TIPO_A,
                TipoFactura.B => VentaConstants.FacturaPrefijos.TIPO_B,
                TipoFactura.C => VentaConstants.FacturaPrefijos.TIPO_C,
                TipoFactura.NotaCredito => VentaConstants.FacturaPrefijos.NOTA_CREDITO,
                TipoFactura.NotaDebito => VentaConstants.FacturaPrefijos.NOTA_DEBITO,
                _ => VentaConstants.FacturaPrefijos.GENERICO
            };
        }
    }
}
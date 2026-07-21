using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public sealed partial class MovimientoStockReferenciaResolver : IMovimientoStockReferenciaResolver
    {
        private readonly AppDbContext _context;

        public MovimientoStockReferenciaResolver(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnriquecerAsync(
            IReadOnlyCollection<MovimientoStockViewModel> movimientos,
            CancellationToken cancellationToken = default)
        {
            if (movimientos is null || movimientos.Count == 0)
                return;

            // Números de venta referenciados como "Venta {Numero}" en el texto libre de Referencia.
            var numerosVenta = movimientos
                .Select(m => ExtraerNumeroVenta(m.Referencia))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct()
                .ToList();

            var ventasPorNumero = numerosVenta.Count == 0
                ? new Dictionary<string, VentaRef>(StringComparer.OrdinalIgnoreCase)
                : await _context.Ventas
                    .AsNoTracking()
                    .Where(v => numerosVenta.Contains(v.Numero))
                    .Select(v => new VentaRef(
                        v.Id,
                        v.Numero,
                        v.ClienteId,
                        v.Cliente != null ? v.Cliente.Nombre : null,
                        v.Cliente != null ? v.Cliente.Apellido : null,
                        v.TipoPago,
                        v.Credito != null ? (int?)v.Credito.CantidadCuotas : null))
                    .ToDictionaryAsync(v => v.Numero, StringComparer.OrdinalIgnoreCase, cancellationToken);

            // Proveedor de cada orden de compra referenciada por las entradas.
            var ordenIds = movimientos
                .Where(m => m.OrdenCompraId.HasValue)
                .Select(m => m.OrdenCompraId!.Value)
                .Distinct()
                .ToList();

            var proveedoresPorOrden = ordenIds.Count == 0
                ? new Dictionary<int, ProveedorRef>()
                : await _context.OrdenesCompra
                    .AsNoTracking()
                    .Where(o => ordenIds.Contains(o.Id))
                    .Select(o => new { o.Id, ProveedorId = o.ProveedorId, Razon = o.Proveedor != null ? o.Proveedor.RazonSocial : null })
                    .ToDictionaryAsync(o => o.Id, o => new ProveedorRef(o.ProveedorId, o.Razon), cancellationToken);

            foreach (var m in movimientos)
            {
                if (m.OrdenCompraId.HasValue)
                {
                    m.ReferenciaTipo = "OrdenCompra";
                    proveedoresPorOrden.TryGetValue(m.OrdenCompraId.Value, out var prov);
                    m.ProveedorId = prov?.ProveedorId;
                    m.ProveedorNombre = prov?.RazonSocial;

                    var numeroOc = m.OrdenCompraNumero ?? $"#{m.OrdenCompraId.Value}";
                    m.ReferenciaTexto = string.IsNullOrWhiteSpace(m.ProveedorNombre)
                        ? $"Compra {numeroOc}"
                        : $"Compra {numeroOc} — Proveedor: {m.ProveedorNombre}";
                    continue;
                }

                var numeroVenta = ExtraerNumeroVenta(m.Referencia);
                if (numeroVenta != null && ventasPorNumero.TryGetValue(numeroVenta, out var venta))
                {
                    m.ReferenciaTipo = "Venta";
                    m.VentaId = venta.Id;
                    m.VentaNumero = venta.Numero;
                    m.ClienteId = venta.ClienteId;
                    m.ClienteNombre = ArmarNombreCliente(venta.Nombre, venta.Apellido);
                    m.MedioPagoTexto = ArmarMedioPagoTexto(venta.TipoPago, venta.CantidadCuotas);

                    var clienteTxt = m.ClienteNombre ?? "Consumidor final";
                    m.ReferenciaTexto = $"Venta {venta.Numero} — Cliente: {clienteTxt} — {m.MedioPagoTexto}";
                    continue;
                }

                // Sin OC ni venta resoluble: clasificar por el texto de la referencia.
                if (!string.IsNullOrWhiteSpace(m.Referencia) &&
                    m.Referencia.Contains("Devolucion", StringComparison.OrdinalIgnoreCase))
                {
                    m.ReferenciaTipo = "Devolucion";
                    m.ReferenciaTexto = m.Referencia;
                }
                else if (m.Tipo == TipoMovimiento.Ajuste)
                {
                    m.ReferenciaTipo = "Ajuste";
                    m.ReferenciaTexto = string.IsNullOrWhiteSpace(m.Referencia) ? m.Motivo : m.Referencia;
                }
                else
                {
                    m.ReferenciaTipo = "Otro";
                    m.ReferenciaTexto = m.Referencia;
                }
            }
        }

        private static string? ExtraerNumeroVenta(string? referencia)
        {
            if (string.IsNullOrWhiteSpace(referencia))
                return null;

            var match = VentaNumeroRegex().Match(referencia);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static string? ArmarNombreCliente(string? nombre, string? apellido)
        {
            var completo = $"{nombre} {apellido}".Trim();
            return string.IsNullOrWhiteSpace(completo) ? null : completo;
        }

        private static string ArmarMedioPagoTexto(TipoPago tipoPago, int? cantidadCuotas)
        {
            return tipoPago switch
            {
                TipoPago.Efectivo => "Efectivo",
                TipoPago.Transferencia => "Transferencia",
                TipoPago.TarjetaDebito => "Tarjeta débito",
                TipoPago.TarjetaCredito or TipoPago.Tarjeta => "Tarjeta crédito",
                TipoPago.Cheque => "Cheque",
                TipoPago.MercadoPago => "Mercado Pago",
                TipoPago.CreditoPersonal => cantidadCuotas.HasValue && cantidadCuotas.Value > 0
                    ? $"Crédito personal, {cantidadCuotas.Value} cuotas"
                    : "Crédito personal",
                TipoPago.CuentaCorriente => "Cuenta corriente",
                _ => tipoPago.ToString()
            };
        }

        [GeneratedRegex(@"Venta\s+([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase)]
        private static partial Regex VentaNumeroRegex();

        private sealed record VentaRef(
            int Id,
            string Numero,
            int ClienteId,
            string? Nombre,
            string? Apellido,
            TipoPago TipoPago,
            int? CantidadCuotas);

        private sealed record ProveedorRef(int ProveedorId, string? RazonSocial);
    }
}

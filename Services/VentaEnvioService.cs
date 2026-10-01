using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class VentaEnvioService : IVentaEnvioService
    {
        // Máquina de estados del envío: sólo estas transiciones están permitidas.
        // Cualquier par (actual, nuevo) que no figure acá se rechaza sin escribir nada
        // (fail-closed). Entregado y Cancelado son terminales.
        private static readonly Dictionary<EstadoEnvio, EstadoEnvio[]> TransicionesValidas = new()
        {
            [EstadoEnvio.Pendiente] = new[] { EstadoEnvio.Preparando, EstadoEnvio.Entregado, EstadoEnvio.Reprogramado, EstadoEnvio.Cancelado },
            [EstadoEnvio.Preparando] = new[] { EstadoEnvio.Despachado, EstadoEnvio.Entregado, EstadoEnvio.Reprogramado, EstadoEnvio.Cancelado },
            [EstadoEnvio.Despachado] = new[] { EstadoEnvio.EnCamino, EstadoEnvio.Entregado, EstadoEnvio.Fallido, EstadoEnvio.Reprogramado },
            [EstadoEnvio.EnCamino] = new[] { EstadoEnvio.Entregado, EstadoEnvio.Fallido, EstadoEnvio.Reprogramado },
            [EstadoEnvio.Fallido] = new[] { EstadoEnvio.Pendiente, EstadoEnvio.Preparando, EstadoEnvio.Reprogramado, EstadoEnvio.Cancelado },
            // Reprogramado sigue pendiente de entrega: se puede reprogramar de nuevo (otra fecha),
            // empezar a prepararlo, entregarlo o cancelarlo.
            [EstadoEnvio.Reprogramado] = new[] { EstadoEnvio.Preparando, EstadoEnvio.Despachado, EstadoEnvio.Entregado, EstadoEnvio.Cancelado },
            [EstadoEnvio.Entregado] = Array.Empty<EstadoEnvio>(),
            [EstadoEnvio.Cancelado] = Array.Empty<EstadoEnvio>()
        };

        // EF Core traduce a SQL una comparación contra un array de forma más confiable
        // que HashSet<T>.Contains sobre un enum; se usa este array (no el diccionario de
        // arriba) para el filtro de GetPendientesAsync.
        private static readonly EstadoEnvio[] EstadosTerminales =
        {
            EstadoEnvio.Entregado,
            EstadoEnvio.Cancelado
        };

        private readonly AppDbContext _context;
        private readonly ILogger<VentaEnvioService> _logger;

        public VentaEnvioService(AppDbContext context, ILogger<VentaEnvioService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public bool EsTransicionValida(EstadoEnvio estadoActual, EstadoEnvio nuevoEstado)
        {
            // Único "mismo estado" válido: volver a reprogramar con otra fecha.
            if (estadoActual == nuevoEstado)
                return estadoActual == EstadoEnvio.Reprogramado;

            return TransicionesValidas.TryGetValue(estadoActual, out var permitidos)
                && permitidos.Contains(nuevoEstado);
        }

        public async Task<VentaEnvio?> GetByVentaIdAsync(int ventaId)
        {
            return await _context.VentaEnvios
                .FirstOrDefaultAsync(e => e.VentaId == ventaId);
        }

        public async Task<List<VentaEnvio>> GetPendientesAsync()
        {
            return await _context.VentaEnvios
                .Include(e => e.Venta)
                    .ThenInclude(v => v.Cliente)
                .Where(e => !EstadosTerminales.Contains(e.Estado) && e.Venta.Estado != EstadoVenta.Cancelada)
                .OrderBy(e => e.FechaProgramada ?? e.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<VentaEnvio>> GetCerradosPorMesAsync(int anio, int mes)
        {
            var desde = new DateTime(anio, mes, 1);
            var hasta = desde.AddMonths(1);

            // Cerrado = envío terminal, o envío de una venta cancelada que quedó sin cerrar (legacy).
            // La fecha de referencia es cuándo se cerró: entrega real, cancelación de la venta, última
            // actualización del envío y, en último caso, la fecha de la venta.
            var cerrados = _context.VentaEnvios
                .Where(e => !e.IsDeleted
                    && (EstadosTerminales.Contains(e.Estado) || e.Venta.Estado == EstadoVenta.Cancelada));

            return await cerrados
                .Where(e => (e.FechaEntregaReal ?? e.Venta.FechaCancelacion ?? e.UpdatedAt ?? e.Venta.FechaVenta) >= desde
                    && (e.FechaEntregaReal ?? e.Venta.FechaCancelacion ?? e.UpdatedAt ?? e.Venta.FechaVenta) < hasta)
                .Include(e => e.Venta)
                    .ThenInclude(v => v.Cliente)
                .OrderByDescending(e => e.FechaEntregaReal ?? e.Venta.FechaCancelacion ?? e.UpdatedAt ?? e.Venta.FechaVenta)
                .ToListAsync();
        }

        public async Task<CambiarEstadoEnvioResultado> CambiarEstadoAsync(
            int ventaId,
            EstadoEnvio nuevoEstado,
            string? motivo,
            string? usuario,
            DateTime? nuevaFechaProgramada = null)
        {
            var envio = await _context.VentaEnvios
                .Include(e => e.Venta)
                .FirstOrDefaultAsync(e => e.VentaId == ventaId);

            if (envio == null)
            {
                return CambiarEstadoEnvioResultado.Fallido("La venta no tiene envío registrado.");
            }

            if (!EsTransicionValida(envio.Estado, nuevoEstado))
            {
                return CambiarEstadoEnvioResultado.Fallido(
                    $"No se puede pasar de {envio.Estado} a {nuevoEstado}.");
            }

            if (nuevoEstado == EstadoEnvio.Fallido && string.IsNullOrWhiteSpace(motivo))
            {
                return CambiarEstadoEnvioResultado.Fallido(
                    "Indicá el motivo por el que no se pudo entregar.");
            }

            if (nuevoEstado == EstadoEnvio.Reprogramado)
            {
                if (!nuevaFechaProgramada.HasValue)
                {
                    return CambiarEstadoEnvioResultado.Fallido(
                        "Indicá la nueva fecha de entrega para reprogramar el envío.");
                }

                // Mismo criterio que la fecha programada al crear/editar la venta: no admite días pasados.
                if (nuevaFechaProgramada.Value.Date < DateTime.Today)
                {
                    return CambiarEstadoEnvioResultado.Fallido(
                        "La nueva fecha de entrega no puede ser anterior a hoy.");
                }
            }

            var estadoAnterior = envio.Estado;
            envio.Estado = nuevoEstado;
            envio.UpdatedAt = DateTime.UtcNow;
            envio.UpdatedBy = usuario;

            switch (nuevoEstado)
            {
                case EstadoEnvio.Despachado:
                    envio.FechaDespacho ??= DateTime.UtcNow;
                    break;
                case EstadoEnvio.Entregado:
                    envio.FechaEntregaReal ??= DateTime.UtcNow;
                    // FechaEntrega ya existe en Venta y hoy no la llena nadie: se sella
                    // acá como efecto del envío, sin tocar EstadoVenta ni ningún otro
                    // campo financiero.
                    envio.Venta.FechaEntrega ??= DateTime.UtcNow;
                    break;
                case EstadoEnvio.Fallido:
                    envio.MotivoNoEntrega = motivo;
                    break;
                case EstadoEnvio.Reprogramado:
                    envio.FechaProgramada = nuevaFechaProgramada!.Value.Date;
                    // Un envío reprogramado vuelve a esperar despacho: la fecha de despacho anterior
                    // (si hubo un intento) deja de describir el estado actual.
                    envio.FechaDespacho = null;
                    var detalleReprogramacion = $"Reprogramado para el {envio.FechaProgramada:dd/MM/yyyy}"
                        + (string.IsNullOrWhiteSpace(motivo) ? string.Empty : $": {motivo.Trim()}");
                    envio.Observaciones = string.IsNullOrWhiteSpace(envio.Observaciones)
                        ? detalleReprogramacion
                        : $"{envio.Observaciones}\n{detalleReprogramacion}";
                    break;
                case EstadoEnvio.Cancelado:
                    if (!string.IsNullOrWhiteSpace(motivo))
                        envio.Observaciones = string.IsNullOrWhiteSpace(envio.Observaciones)
                            ? $"Cancelado: {motivo}"
                            : $"{envio.Observaciones}\nCancelado: {motivo}";
                    break;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Envío de venta {VentaId} cambió de {Anterior} a {Nuevo} por {Usuario}",
                ventaId, estadoAnterior, nuevoEstado, usuario);

            return CambiarEstadoEnvioResultado.Ok(envio);
        }
    }
}

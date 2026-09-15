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
            [EstadoEnvio.Pendiente] = new[] { EstadoEnvio.Preparando, EstadoEnvio.Cancelado },
            [EstadoEnvio.Preparando] = new[] { EstadoEnvio.Despachado, EstadoEnvio.Cancelado },
            [EstadoEnvio.Despachado] = new[] { EstadoEnvio.EnCamino, EstadoEnvio.Entregado, EstadoEnvio.Fallido },
            [EstadoEnvio.EnCamino] = new[] { EstadoEnvio.Entregado, EstadoEnvio.Fallido },
            [EstadoEnvio.Fallido] = new[] { EstadoEnvio.Preparando, EstadoEnvio.Cancelado },
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
            if (estadoActual == nuevoEstado)
                return false;

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
                .Where(e => !EstadosTerminales.Contains(e.Estado))
                .OrderBy(e => e.FechaProgramada ?? e.CreatedAt)
                .ToListAsync();
        }

        public async Task<CambiarEstadoEnvioResultado> CambiarEstadoAsync(
            int ventaId,
            EstadoEnvio nuevoEstado,
            string? motivo,
            string? usuario)
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

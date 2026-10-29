using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

/// <summary>
/// H4-E2: canje de una nota de crédito (de una devolución) como medio de pago de UNA venta, por el total a cobrar.
/// El backend es la autoridad: cliente, estado, vencimiento y saldo se validan acá, y el consumo del saldo es un único
/// UPDATE condicional (el motor serializa por fila: dos ventas simultáneas sobre la misma nota aplican una sola vez).
/// Todo corre dentro de la transacción del llamador (confirmar / cancelar la venta).
/// </summary>
internal static class NotaCreditoCanje
{
    /// <summary>Regla pura de elegibilidad de una nota para pagar <paramref name="monto"/> a un cliente. Devuelve el motivo de rechazo o null.</summary>
    internal static string? MotivoRechazo(NotaCredito nc, int? clienteVentaId, decimal monto, DateTime ahoraUtc)
    {
        if (nc.IsDeleted) return "La nota de crédito no existe.";
        if (!clienteVentaId.HasValue || nc.ClienteId != clienteVentaId.Value)
            return "La nota de crédito no pertenece al cliente de la venta.";
        if (nc.Estado != EstadoNotaCredito.Vigente && nc.Estado != EstadoNotaCredito.UtilizadaParcialmente)
            return $"La nota de crédito {nc.NumeroNotaCredito} no admite usos en estado {nc.Estado}.";
        if (nc.FechaVencimiento.HasValue && nc.FechaVencimiento.Value < ahoraUtc)
            return $"La nota de crédito {nc.NumeroNotaCredito} venció el {nc.FechaVencimiento.Value:dd/MM/yyyy}.";
        if (monto <= 0m) return "El importe a cobrar de la venta debe ser mayor a cero.";
        var disponible = nc.MontoTotal - nc.MontoUtilizado;
        if (disponible < monto)
            return $"La nota de crédito {nc.NumeroNotaCredito} no cubre el total de la venta: disponible {disponible:N2}, a cobrar {monto:N2}.";
        return null;
    }

    /// <summary>Estados de venta que nunca se concretaron: una nota emitida por una devolución sobre ellas no es dinero real del cliente.</summary>
    internal static readonly EstadoVenta[] EstadosOrigenNoConcretados =
    {
        EstadoVenta.Cotizacion, EstadoVenta.Presupuesto, EstadoVenta.PendienteRequisitos, EstadoVenta.PendienteFinanciacion
    };

    /// <summary>No se canjea una nota cuyo origen es una venta que nunca se concretó.</summary>
    private static async Task AsegurarOrigenConcretadoAsync(AppDbContext ctx, NotaCredito nc)
    {
        if (nc.DevolucionId <= 0) return;
        var estadoOrigen = await ctx.Devoluciones.AsNoTracking()
            .Where(d => d.Id == nc.DevolucionId)
            .Select(d => (EstadoVenta?)d.Venta.Estado)
            .FirstOrDefaultAsync();
        if (estadoOrigen.HasValue && EstadosOrigenNoConcretados.Contains(estadoOrigen.Value))
            throw new InvalidOperationException(
                "La nota de crédito proviene de una devolución sobre una venta que no se concretó y no puede utilizarse.");
    }

    /// <summary>
    /// Validación de la selección (Crear/Editar/Confirmar, antes de escribir): exige nota, mismo cliente, vigente y con saldo
    /// suficiente. Lectura sin tracking. Es un chequeo temprano; la garantía real es <see cref="ConsumirAsync"/>.
    /// </summary>
    internal static async Task ValidarSeleccionAsync(AppDbContext ctx, int? notaCreditoId, int? clienteId, decimal totalACobrar)
    {
        if (!notaCreditoId.HasValue)
            throw new InvalidOperationException("Elegí la nota de crédito con la que se paga la venta.");
        if (!clienteId.HasValue)
            throw new InvalidOperationException("Pagar con nota de crédito requiere un cliente registrado.");

        var nc = await ctx.NotasCredito.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == notaCreditoId.Value && !n.IsDeleted)
            ?? throw new InvalidOperationException("La nota de crédito indicada no existe.");

        var motivo = MotivoRechazo(nc, clienteId, totalACobrar, DateTime.UtcNow);
        if (motivo != null)
            throw new InvalidOperationException(motivo);
        await AsegurarOrigenConcretadoAsync(ctx, nc);
    }

    /// <summary>
    /// Consume el saldo de la nota por <c>venta.TotalACobrar</c> y registra el uso. Idempotente por venta: si el uso ya existe
    /// (reintento) no vuelve a descontar. Lanza <see cref="InvalidOperationException"/> si la nota ya no alcanza.
    /// </summary>
    internal static async Task ConsumirAsync(AppDbContext ctx, Venta venta, string usuario, ISeguridadAuditoriaService? auditoria = null)
    {
        var notaCreditoId = venta.NotaCreditoId
            ?? throw new InvalidOperationException("La venta se paga con nota de crédito pero no tiene ninguna asociada.");
        var clienteId = venta.ClienteId
            ?? throw new InvalidOperationException("Pagar con nota de crédito requiere un cliente registrado.");
        var monto = venta.TotalACobrar;

        if (await ctx.NotasCreditoUso.AnyAsync(u => u.VentaId == venta.Id && !u.IsDeleted))
            return;

        var ahora = DateTime.UtcNow;

        var notaActual = await ctx.NotasCredito.AsNoTracking().FirstOrDefaultAsync(n => n.Id == notaCreditoId && !n.IsDeleted);
        if (notaActual != null)
            await AsegurarOrigenConcretadoAsync(ctx, notaActual);

        // UPDATE condicional atómico: el saldo, el estado, el cliente y el vencimiento se verifican en el mismo comando que
        // descuenta. Dos canjes simultáneos de la misma nota se serializan por el lock de fila: el segundo ve el saldo
        // ya consumido, no cumple el WHERE y afecta 0 filas.
        var filas = await ctx.NotasCredito
            .Where(n => n.Id == notaCreditoId
                        && !n.IsDeleted
                        && n.ClienteId == clienteId
                        && (n.Estado == EstadoNotaCredito.Vigente || n.Estado == EstadoNotaCredito.UtilizadaParcialmente)
                        && (n.FechaVencimiento == null || n.FechaVencimiento >= ahora)
                        && (n.MontoTotal - n.MontoUtilizado) >= monto)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.MontoUtilizado, n => n.MontoUtilizado + monto)
                .SetProperty(n => n.Estado, n => (n.MontoTotal - n.MontoUtilizado - monto) == 0m
                    ? EstadoNotaCredito.UtilizadaTotalmente
                    : EstadoNotaCredito.UtilizadaParcialmente)
                .SetProperty(n => n.UpdatedAt, ahora)
                .SetProperty(n => n.UpdatedBy, usuario));

        if (filas == 0)
        {
            // Mensaje preciso (la nota pudo agotarse, vencer, cambiar de cliente) leyendo el estado actual.
            var actual = await ctx.NotasCredito.AsNoTracking().FirstOrDefaultAsync(n => n.Id == notaCreditoId && !n.IsDeleted);
            throw new InvalidOperationException(
                (actual == null ? "La nota de crédito indicada no existe." : MotivoRechazo(actual, clienteId, monto, ahora))
                ?? "La nota de crédito ya no está disponible para esta venta. Reintentá.");
        }

        ctx.NotasCreditoUso.Add(new NotaCreditoUso
        {
            NotaCreditoId = notaCreditoId,
            VentaId = venta.Id,
            Monto = monto,
            FechaUso = ahora,
            Usuario = usuario,
            CreatedBy = usuario
        });

        // Traza legible en la propia nota (mismo formato que el uso manual); StringLength(500): se conservan los más recientes.
        await AgregarObservacionAsync(ctx, notaCreditoId, $"[{ahora:dd/MM/yyyy}] Canje {monto:N2} en venta {venta.Numero}");

        // SEG-BAJA: auditoría de seguridad en el mismo SaveChanges que el registro del uso.
        auditoria?.AgregarEvento("NotasCredito", "UsarNotaCredito", $"NotaCredito:{notaCreditoId}",
            $"Venta={venta.Numero}; Monto={monto:0.00}; Usuario={usuario}");

        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Devuelve a la nota el saldo canjeado por la venta (cancelación). Idempotente: el canje se marca revertido con un
    /// UPDATE condicional (<c>FechaReversion IS NULL</c>); solo quien lo marca restituye el saldo. Devuelve el monto restituido
    /// (0 si la venta no tenía canje o ya estaba revertido).
    /// </summary>
    internal static async Task<decimal> RevertirAsync(AppDbContext ctx, Venta venta, string motivo, string usuario, ISeguridadAuditoriaService? auditoria = null)
    {
        var uso = await ctx.NotasCreditoUso.AsNoTracking()
            .FirstOrDefaultAsync(u => u.VentaId == venta.Id && !u.IsDeleted);
        if (uso == null || uso.FechaReversion != null)
            return 0m;

        var ahora = DateTime.UtcNow;
        var motivoCorto = motivo.Length <= 500 ? motivo : motivo[..500];
        var marcado = await ctx.NotasCreditoUso
            .Where(u => u.Id == uso.Id && u.FechaReversion == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FechaReversion, ahora)
                .SetProperty(u => u.MotivoReversion, motivoCorto)
                .SetProperty(u => u.UpdatedAt, ahora)
                .SetProperty(u => u.UpdatedBy, usuario));
        if (marcado == 0)
            return 0m;

        var monto = uso.Monto;
        // El saldo vuelve; el estado solo se reabre si la nota estaba consumida (una Cancelada/Vencida conserva su estado).
        await ctx.NotasCredito
            .Where(n => n.Id == uso.NotaCreditoId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.MontoUtilizado, n => n.MontoUtilizado - monto)
                .SetProperty(n => n.Estado, n =>
                    n.Estado == EstadoNotaCredito.UtilizadaTotalmente || n.Estado == EstadoNotaCredito.UtilizadaParcialmente
                        ? ((n.MontoUtilizado - monto) <= 0m ? EstadoNotaCredito.Vigente : EstadoNotaCredito.UtilizadaParcialmente)
                        : n.Estado)
                .SetProperty(n => n.UpdatedAt, ahora)
                .SetProperty(n => n.UpdatedBy, usuario));

        await AgregarObservacionAsync(ctx, uso.NotaCreditoId,
            $"[{ahora:dd/MM/yyyy}] Restituido {monto:N2} por cancelación de la venta {venta.Numero}");

        if (auditoria != null)
        {
            // SEG-BAJA: el saldo se restituye con ExecuteUpdate; el evento se guarda acá mismo (dentro de la transacción de
            // la cancelación, que todavía no se confirmó).
            auditoria.AgregarEvento("NotasCredito", "RestituirNotaCredito", $"NotaCredito:{uso.NotaCreditoId}",
                $"Venta={venta.Numero}; Monto={monto:0.00}; Usuario={usuario}");
            await ctx.SaveChangesAsync();
        }

        return monto;
    }

    private static async Task AgregarObservacionAsync(AppDbContext ctx, int notaCreditoId, string linea)
    {
        var obs = await ctx.NotasCredito.AsNoTracking().Where(n => n.Id == notaCreditoId).Select(n => n.Observaciones).FirstAsync();
        var nueva = string.IsNullOrWhiteSpace(obs) ? linea : $"{obs}\n{linea}";
        if (nueva.Length > 500) nueva = nueva[^500..];
        await ctx.NotasCredito.Where(n => n.Id == notaCreditoId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Observaciones, nueva));
    }
}

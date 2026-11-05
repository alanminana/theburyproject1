using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// O-1 - el reembolso en efectivo de una devolucion no puede superar el efectivo fisico de la caja, ni siquiera con dos
// devoluciones simultaneas que compiten por el mismo dinero. Operaciones REALES (DevolucionService.CompletarDevolucionAsync).
// ---------------------------------------------------------------------------------------------
public partial class FinResidualSqlServerTests
{
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO (CI) [R-15]: O-1 dos devoluciones simultaneas sobre el mismo efectivo: solo una se paga y el efectivo esperado nunca queda negativo")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task R15_DosDevolucionesSimultaneas_ConsumenElMismoEfectivo_SoloUnaSePaga()
    {
        var aperturaId = await NuevaAperturaAsync();
        // Dos ventas sin ingreso propio (la caja solo tiene el fondo): cada reembolso de 1 unidad = $50 y el efectivo alcanza para uno.
        var v1 = await NuevaVentaContadoAsync(aperturaId, conIngreso: false, stock: 10m);
        var v2 = await NuevaVentaContadoAsync(aperturaId, conIngreso: false, stock: 10m);
        await FinanciarEfectivoAsync(aperturaId, 50m);
        var (dev1, rv1) = await DevolucionAprobadaConReembolsoAsync(v1, cantidad: 1);
        var (dev2, rv2) = await DevolucionAprobadaConReembolsoAsync(v2, cantidad: 1);

        // La primera se detiene justo antes de asentar su egreso (ya con el lock de la caja); la segunda debe esperar ese lock.
        var plan = AntesDeInsertarMovimiento();
        Task<string>? primera = null, segunda = null;
        bool bloqueada;
        try
        {
            primera = CompletarDevolucionAsync(dev1, rv1, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, primera), "La primera devolución no llegó a asentar el reembolso.");
            segunda = CompletarDevolucionAsync(dev2, rv2);
            bloqueada = await QuedaBloqueadaAsync(segunda);
        }
        finally { plan.Release.TrySetResult(); }
        var r1 = await ResultadoAsync("primera devolución", primera!, plan);
        var r2 = await ResultadoAsync("segunda devolución", segunda!);

        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var egresos = await ctx.MovimientosCaja.AsNoTracking().CountAsync(m => m.AperturaCajaId == aperturaId
            && m.Concepto == ConceptoMovimientoCaja.DevolucionCliente && !m.IsDeleted);
        var estados = await ctx.Devoluciones.AsNoTracking().Where(d => d.Id == dev1 || d.Id == dev2)
            .OrderBy(d => d.Id).Select(d => d.Estado).ToListAsync();
        var stock2 = await StockAsync(v2.ProductoId);

        Informe("R-15", $"segunda bloqueada={bloqueada}", $"primera={r1}", $"segunda={r2}", $"egresos={egresos}");
        SinFalloDeInfra(r1, r2);
        Assert.True(bloqueada);
        Assert.StartsWith("OK", r1);
        Assert.StartsWith("InvalidOperationException", r2);
        Assert.Contains("Efectivo insuficiente", r2);
        Assert.Equal(1, egresos);
        Assert.Equal(EstadoDevolucion.Completada, estados[0]);
        Assert.Equal(EstadoDevolucion.Aprobada, estados[1]); // la rechazada hizo rollback: no quedó completada
        Assert.Equal(10m, stock2); // ...ni reintegró stock
    }
}

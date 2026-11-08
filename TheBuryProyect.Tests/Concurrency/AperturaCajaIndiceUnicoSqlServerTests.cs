using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Concurrency;

/// <summary>
/// D-04: la base rechaza una segunda apertura vigente (Cerrada=0, IsDeleted=0) de la misma caja, incluso si alguna vía de
/// escritura esquivara los locks de aplicación de AbrirCajaAsync. Las cerradas / eliminadas no cuentan (cierre y reapertura).
/// Mismo gate que el resto de los harness SQL Server: QA_SEC_SUPERADMIN_SQL con catálogo "qa_sec_superadmin*".
/// </summary>
public class AperturaCajaIndiceUnicoSqlServerTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (SuperAdminSqlEnv.SkipReason() != null) return;
        var csb = new SqlConnectionStringBuilder(SuperAdminSqlEnv.ConnectionString);
        var catalogo = csb.InitialCatalog;
        csb.InitialCatalog = "master";
        await using (var c = new SqlConnection(csb.ConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = new SqlCommand(
                $"IF DB_ID(N'{catalogo}') IS NOT NULL BEGIN ALTER DATABASE [{catalogo}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalogo}]; END; CREATE DATABASE [{catalogo}];", c);
            await cmd.ExecuteNonQueryAsync();
        }
        await using var ctx = Nuevo();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (SuperAdminSqlEnv.SkipReason() != null) return;
        await using var ctx = Nuevo();
        await ctx.Database.EnsureDeletedAsync();
    }

    private static AppDbContext Nuevo() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(SuperAdminSqlEnv.ConnectionString).Options);

    private static AperturaCaja Apertura(int cajaId, string usuario, bool cerrada = false, bool eliminada = false) => new()
    {
        CajaId = cajaId, FechaApertura = DateTime.UtcNow, MontoInicial = 0, UsuarioApertura = usuario,
        Cerrada = cerrada, IsDeleted = eliminada, CreatedAt = DateTime.UtcNow
    };

    private static async Task<int> NuevaCajaAsync()
    {
        await using var ctx = Nuevo();
        var codigo = "D04" + Guid.NewGuid().ToString("N")[..6];
        var caja = new Caja { Codigo = codigo, Nombre = codigo, Activa = true, Estado = EstadoCaja.Cerrada };
        ctx.Cajas.Add(caja);
        await ctx.SaveChangesAsync();
        return caja.Id;
    }

    [SuperAdminSqlServerFact(DisplayName = "D-04 [IDX-1] dos aperturas vigentes de la misma caja: la segunda es rechazada por la base")]
    public async Task SegundaAperturaVigente_EsRechazadaPorLaBase()
    {
        var cajaId = await NuevaCajaAsync();
        await using (var ctx = Nuevo()) { ctx.AperturasCaja.Add(Apertura(cajaId, "u1")); await ctx.SaveChangesAsync(); }

        await using var ctx2 = Nuevo();
        ctx2.AperturasCaja.Add(Apertura(cajaId, "u2"));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        Assert.Contains("UX_AperturasCaja_CajaId_Abierta", ex.InnerException?.Message ?? ex.Message);
    }

    [SuperAdminSqlServerFact(DisplayName = "D-04 [IDX-2] cierre y reapertura: con la anterior cerrada (o eliminada) se puede abrir otra; otra caja no se ve afectada")]
    public async Task CierreYReapertura_YOtrasCajas_NoSeBloquean()
    {
        var cajaId = await NuevaCajaAsync();
        var otra = await NuevaCajaAsync();
        await using (var ctx = Nuevo())
        {
            ctx.AperturasCaja.Add(Apertura(cajaId, "u1", cerrada: true));
            ctx.AperturasCaja.Add(Apertura(cajaId, "u1", cerrada: true));
            ctx.AperturasCaja.Add(Apertura(cajaId, "u1", eliminada: true));
            ctx.AperturasCaja.Add(Apertura(cajaId, "u3"));
            ctx.AperturasCaja.Add(Apertura(otra, "u4"));
            await ctx.SaveChangesAsync();
        }
        await using var verif = Nuevo();
        Assert.Equal(1, await verif.AperturasCaja.CountAsync(a => a.CajaId == cajaId && !a.Cerrada && !a.IsDeleted));

        // cerrar la vigente y abrir otra: debe pasar
        var vigente = await verif.AperturasCaja.SingleAsync(a => a.CajaId == cajaId && !a.Cerrada && !a.IsDeleted);
        vigente.Cerrada = true;
        verif.AperturasCaja.Add(Apertura(cajaId, "u5"));
        await verif.SaveChangesAsync();
    }

    [SuperAdminSqlServerFact(DisplayName = "D-04 [IDX-3] aperturas simultaneas de la misma caja desde distintas conexiones: a lo sumo una vigente")]
    public async Task InsercionesSimultaneas_DejanUnaSolaVigente()
    {
        var cajaId = await NuevaCajaAsync();
        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = Nuevo();
            ctx.AperturasCaja.Add(Apertura(cajaId, "u" + i));
            try { await ctx.SaveChangesAsync(); return true; } catch (DbUpdateException) { return false; }
        }));
        Assert.Equal(1, resultados.Count(r => r));
        await using var verif = Nuevo();
        Assert.Equal(1, await verif.AperturasCaja.CountAsync(a => a.CajaId == cajaId && !a.Cerrada && !a.IsDeleted));
    }
}

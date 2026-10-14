using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// QA-FIN-CONC — Modelo de LOCKS de "CancelarVentaAsync" vs "RegistrarPagoCuotaAsync" en SQL Server.
//
// Alcance y límites (leer antes de interpretar resultados):
//  * Reproduce la HUELLA de bloqueos/aislamiento de ambas transacciones, derivada del código real
//    (docs/qa-fin-conc-auditoria-concurrencia.md §3): cancelación = READ COMMITTED + UPDATE de fila de
//    venta + guardia de lectura + carga de cuotas + UPDATE con RowVersion; pago = SERIALIZABLE.
//  * NO ejecuta los servicios reales (eso es la fase 2, tras confirmar este modelo).
//  * SQLite NO sirve para esto (un único escritor serializa todo): por eso va contra SQL Server.
//  * NO corre por defecto. Requiere AMBAS variables:
//      QA_FIN_CONC_SQL      cadena de conexión a una BD DESCARTABLE cuyo Initial Catalog empiece con "qa_fin_conc"
//      QA_RELEASE_TURNO=1   autorización explícita del turno de QA-RELEASE
//    Solo toca el esquema [qaconc] (lo recrea en cada test). Nunca la BD de desarrollo.
// ---------------------------------------------------------------------------------------------

public sealed class SqlServerConcFactAttribute : FactAttribute
{
    public SqlServerConcFactAttribute()
    {
        var skip = ConcEnv.SkipReason();
        if (skip != null) Skip = skip;
    }
}

internal static class ConcEnv
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("QA_FIN_CONC_SQL");

    public static string? SkipReason()
    {
        if (Environment.GetEnvironmentVariable("QA_RELEASE_TURNO") != "1")
            return "BLOCKED: falta autorización de turno QA-RELEASE (QA_RELEASE_TURNO=1).";
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return "BLOCKED: falta QA_FIN_CONC_SQL (BD SQL Server descartable).";
        var catalog = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        if (!catalog.StartsWith("qa_fin_conc", StringComparison.OrdinalIgnoreCase))
            return $"BLOCKED: Initial Catalog '{catalog}' no empieza con 'qa_fin_conc' (protección de la BD de desarrollo).";
        return null;
    }
}

public sealed record Outcome(string Name, string Detail = "")
{
    public override string ToString() => string.IsNullOrEmpty(Detail) ? Name : $"{Name} ({Detail})";
}

public class CancelarVsPagoLockModelSqlServerTests : IAsyncLifetime
{
    // Estados del modelo (espejo reducido de los enums reales)
    private const int CuotaPendiente = 0, CuotaParcial = 1, CuotaPagada = 2, CuotaCancelada = 3;
    private const int CreditoActivo = 0, CreditoCancelado = 1;
    private const int VentaConfirmada = 0, VentaCancelada = 1;

    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(20);

    // ---------- esquema ----------
    public async Task InitializeAsync()
    {
        if (ConcEnv.SkipReason() != null) return;
        await using var c = await AbrirAsync();
        await Exec(c, @"
IF SCHEMA_ID('qaconc') IS NULL EXEC('CREATE SCHEMA qaconc');
DROP TABLE IF EXISTS qaconc.Pagos; DROP TABLE IF EXISTS qaconc.Cuotas;
DROP TABLE IF EXISTS qaconc.Creditos; DROP TABLE IF EXISTS qaconc.Ventas;
CREATE TABLE qaconc.Ventas   (Id int PRIMARY KEY, Estado int NOT NULL, UpdatedAt datetime2 NULL, RowVer rowversion);
CREATE TABLE qaconc.Creditos (Id int PRIMARY KEY, Estado int NOT NULL, SaldoPendiente decimal(18,2) NOT NULL, RowVer rowversion);
CREATE TABLE qaconc.Cuotas   (Id int PRIMARY KEY, CreditoId int NOT NULL, Estado int NOT NULL, MontoTotal decimal(18,2) NOT NULL,
                              MontoPagado decimal(18,2) NOT NULL, RowVer rowversion);
CREATE INDEX IX_Cuotas_Credito ON qaconc.Cuotas(CreditoId);
CREATE TABLE qaconc.Pagos    (Id int IDENTITY PRIMARY KEY, CuotaId int NOT NULL, Importe decimal(18,2) NOT NULL);
INSERT qaconc.Ventas(Id,Estado) VALUES (1,0);
INSERT qaconc.Creditos(Id,Estado,SaldoPendiente) VALUES (1,0,300);
INSERT qaconc.Cuotas(Id,CreditoId,Estado,MontoTotal,MontoPagado) VALUES (1,1,0,100,0),(2,1,0,100,0),(3,1,0,100,0);");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<SqlConnection> AbrirAsync()
    {
        var c = new SqlConnection(ConcEnv.ConnectionString);
        await c.OpenAsync();
        await Exec(c, "SET LOCK_TIMEOUT 15000;");
        return c;
    }

    private static async Task Exec(SqlConnection c, string sql, SqlTransaction? tx = null)
    {
        await using var cmd = new SqlCommand(sql, c, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> Scalar<T>(SqlConnection c, string sql, SqlTransaction? tx = null)
    {
        await using var cmd = new SqlCommand(sql, c, tx);
        // Convert.ChangeType: SQL Server devuelve 'bit' como bool y el test pide int (is_read_committed_snapshot_on).
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T));
    }

    // ---------- modelo: CancelarVentaAsync (READ COMMITTED) ----------
    private static async Task<Outcome> CancelarAsync(
        bool mitigado = false, Func<Task>? trasGuardia = null, Func<Task>? trasCarga = null)
    {
        await using var c = await AbrirAsync();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            // 1) "claim": UPDATE de la fila de venta (X lock sobre Ventas; no toca Cuotas/Creditos)
            await Exec(c, "UPDATE qaconc.Ventas SET UpdatedAt = SYSUTCDATETIME() WHERE Id = 1 AND Estado <> 1;", tx);

            // Mitigación propuesta: lock de crédito (mismo recurso que usa el pago) ANTES de la guardia
            if (mitigado)
                await Scalar<int>(c, "SELECT Id FROM qaconc.Creditos WITH (UPDLOCK, HOLDLOCK) WHERE Id = 1;", tx);

            // 2) guardia AsegurarSinPagosAplicadosParaCancelarAsync: lecturas READ COMMITTED sin lock retenido
            var conPago = await Scalar<int>(c, @"
SELECT (SELECT COUNT(*) FROM qaconc.Cuotas WHERE CreditoId = 1 AND (MontoPagado > 0 OR Estado IN (1,2)))
     + (SELECT COUNT(*) FROM qaconc.Pagos p JOIN qaconc.Cuotas q ON q.Id = p.CuotaId WHERE q.CreditoId = 1);", tx);
            if (conPago > 0)
            {
                await tx.RollbackAsync();
                return new Outcome("Rechazada", "guardia detectó pagos");
            }

            if (trasGuardia != null) await trasGuardia();

            // 3) RestaurarCreditoPersonall: carga (tracked) de cuotas del crédito + captura de RowVersion
            var cuotas = new List<(int Id, int Estado, byte[] Rv)>();
            await using (var cmd = new SqlCommand("SELECT Id, Estado, RowVer FROM qaconc.Cuotas WHERE CreditoId = 1;", c, tx))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    cuotas.Add((r.GetInt32(0), r.GetInt32(1), (byte[])r["RowVer"]));

            if (trasCarga != null) await trasCarga();

            // 4) SaveChanges: UPDATE con RowVersion original (concurrency token) por cada cuota no pagada
            foreach (var q in cuotas.Where(q => q.Estado != CuotaPagada && q.Estado != CuotaCancelada))
            {
                await using var up = new SqlCommand(
                    "UPDATE qaconc.Cuotas SET Estado = 3 WHERE Id = @id AND RowVer = @rv;", c, tx);
                up.Parameters.AddWithValue("@id", q.Id);
                up.Parameters.Add("@rv", SqlDbType.Binary, 8).Value = q.Rv;
                if (await up.ExecuteNonQueryAsync() == 0)
                {
                    await tx.RollbackAsync();
                    return new Outcome("ConflictoConcurrencia", $"cuota {q.Id}: RowVersion obsoleta (DbUpdateConcurrencyException)");
                }
            }

            await Exec(c, "UPDATE qaconc.Creditos SET Estado = 1, SaldoPendiente = 0 WHERE Id = 1;", tx);
            await Exec(c, "UPDATE qaconc.Ventas SET Estado = 1 WHERE Id = 1;", tx);
            await tx.CommitAsync();
            return new Outcome("Cancelada");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222)
        {
            try { await tx.RollbackAsync(); } catch { /* ya abortada */ }
            return new Outcome(ex.Number == 1205 ? "VictimaDeadlock" : "LockTimeout");
        }
    }

    // ---------- modelo: RegistrarPagoCuotaAsync (SERIALIZABLE) ----------
    private static async Task<Outcome> PagarAsync(
        bool mitigado = false, bool leeVenta = false,
        Func<Task>? trasLectura = null, Func<Task>? trasUpdate = null)
    {
        await using var c = await AbrirAsync();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (mitigado)
            {
                var estCr = await Scalar<int>(c, "SELECT Estado FROM qaconc.Creditos WITH (UPDLOCK, HOLDLOCK) WHERE Id = 1;", tx);
                if (estCr == CreditoCancelado)
                {
                    await tx.RollbackAsync();
                    return new Outcome("Rechazado", "crédito cancelado");
                }
            }

            // ObtenerCuotaDelCreditoAsync: cuota + Include(Credito) bajo SERIALIZABLE (range/S locks retenidos)
            byte[] rv; int estado;
            await using (var cmd = new SqlCommand(
                "SELECT c.Estado, c.RowVer, cr.Estado FROM qaconc.Cuotas c JOIN qaconc.Creditos cr ON cr.Id = c.CreditoId WHERE c.Id = 1;", c, tx))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                await r.ReadAsync();
                estado = r.GetInt32(0); rv = (byte[])r["RowVer"];
            }

            // ResolverCobroCuotaAsync: rechaza cuota Cancelada/Pagada
            if (estado is CuotaCancelada or CuotaPagada)
            {
                await tx.RollbackAsync();
                return new Outcome("Rechazado", $"cuota en estado {estado}");
            }

            if (trasLectura != null) await trasLectura();

            await using (var up = new SqlCommand(
                "UPDATE qaconc.Cuotas SET MontoPagado = MontoPagado + 50, Estado = 1 WHERE Id = 1 AND RowVer = @rv;", c, tx))
            {
                up.Parameters.Add("@rv", SqlDbType.Binary, 8).Value = rv;
                if (await up.ExecuteNonQueryAsync() == 0)
                {
                    await tx.RollbackAsync();
                    return new Outcome("ConflictoConcurrencia", "RowVersion de cuota obsoleta");
                }
            }

            if (trasUpdate != null) await trasUpdate();

            await Exec(c, "INSERT qaconc.Pagos(CuotaId, Importe) VALUES (1, 50);", tx); // PagoCuota + MovimientoCaja

            // EmitirDocumentosPagoAsync → DocumentoContextoBuilder lee Ventas (a confirmar en fase 2)
            if (leeVenta)
                await Scalar<int>(c, "SELECT Estado FROM qaconc.Ventas WHERE Id = 1;", tx);

            await tx.CommitAsync();
            return new Outcome("Aplicado");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222)
        {
            try { await tx.RollbackAsync(); } catch { /* ya abortada */ }
            return new Outcome(ex.Number == 1205 ? "VictimaDeadlock" : "LockTimeout");
        }
    }

    // ---------- invariante global ----------
    /// <summary>Pagos aplicados que cuelgan de un crédito cancelado (debe ser 0 siempre).</summary>
    private static async Task<int> ViolacionesAsync()
    {
        await using var c = await AbrirAsync();
        return await Scalar<int>(c, @"
SELECT COUNT(*) FROM qaconc.Pagos p
JOIN qaconc.Cuotas q ON q.Id = p.CuotaId
JOIN qaconc.Creditos cr ON cr.Id = q.CreditoId
WHERE cr.Estado = 1;");
    }

    // ---------- helpers de orquestación ----------
    private static async Task<bool> TerminaAsync(Task t, TimeSpan plazo)
        => await Task.WhenAny(t, Task.Delay(plazo)) == t;

    private static TaskCompletionSource Señal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Esperar(Task t)
    {
        if (!await TerminaAsync(t, Espera)) throw new TimeoutException("El orquestador esperó una señal que no llegó.");
    }

    // ================= ESCENARIOS =================

    /// I-1 (PASS esperado): el pago se confirma por completo antes de que arranque la cancelación.
    [SqlServerConcFact]
    public async Task I1_PagoCompletoAntesDeCancelar_CancelacionRechazada()
    {
        Assert.Equal("Aplicado", (await PagarAsync()).Name);
        var cancel = await CancelarAsync();
        Assert.Equal("Rechazada", cancel.Name);
        Assert.Equal(0, await ViolacionesAsync());
    }

    /// I-2 (PASS esperado): el pago está en vuelo (X lock en cuota). La guardia RC de la cancelación se
    /// bloquea hasta el commit y luego rechaza. Válido solo con READ_COMMITTED_SNAPSHOT OFF.
    [SqlServerConcFact]
    public async Task I2_PagoEnVueloConLockDeEscritura_GuardiaEsperaYRechaza()
    {
        await using (var c = await AbrirAsync())
            Assert.Equal(0, await Scalar<int>(c, "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();"));

        var tieneX = Señal(); var soltarPago = Señal();
        var pago = PagarAsync(trasUpdate: async () => { tieneX.SetResult(); await soltarPago.Task; });
        await Esperar(tieneX.Task);

        var cancel = CancelarAsync();
        Assert.False(await TerminaAsync(cancel, TimeSpan.FromSeconds(2)), "La guardia RC debía bloquearse tras el X lock del pago.");
        soltarPago.SetResult();

        Assert.Equal("Aplicado", (await pago).Name);
        Assert.Equal("Rechazada", (await cancel).Name);
        Assert.Equal(0, await ViolacionesAsync());
    }

    /// I-3 (PASS esperado, salvaguarda por RowVersion): la cancelación ya cargó las cuotas cuando el pago confirma.
    [SqlServerConcFact]
    public async Task I3_PagoConfirmaTrasCargaDeCuotasDeLaCancelacion_RowVersionAbortaLaCancelacion()
    {
        var cargadas = Señal(); var soltar = Señal();
        var cancel = CancelarAsync(trasCarga: async () => { cargadas.SetResult(); await soltar.Task; });
        await Esperar(cargadas.Task);

        Assert.Equal("Aplicado", (await PagarAsync()).Name);   // la cancelación no retiene locks sobre Cuotas (RC)
        soltar.SetResult();

        var r = await cancel;
        Assert.Equal("ConflictoConcurrencia", r.Name);
        Assert.Equal(0, await ViolacionesAsync());
    }

    /// I-4 (FAIL esperado — TOCTOU): la guardia pasa, el pago completo confirma, y recién después la
    /// cancelación carga las cuotas (ya Parcial) y las cancela con RowVersion fresca.
    /// Resultado de pago + crédito cancelado => viola el invariante.
    [SqlServerConcFact]
    public async Task I4_GuardiaPasaYElPagoConfirmaAntesDeLaCargaDeCuotas_NoDebeQuedarPagoEnCreditoCancelado()
    {
        var guardiaOk = Señal(); var soltar = Señal();
        var cancel = CancelarAsync(trasGuardia: async () => { guardiaOk.SetResult(); await soltar.Task; });
        await Esperar(guardiaOk.Task);

        var pago = await PagarAsync();          // no depende de la fila de venta => no se bloquea
        soltar.SetResult();
        var cancelR = await cancel;

        Assert.True(pago.Name == "Aplicado", $"pago: {pago}");
        Assert.True(await ViolacionesAsync() == 0,
            $"VIOLACIÓN DE INVARIANTE: pago {pago} y cancelación {cancelR}; crédito cancelado con pago aplicado.");
    }

    /// I-5 (a observar): igual que I-4 pero el pago lee la fila de venta (emisión de documentos). Se espera
    /// bloqueo cruzado -> deadlock/lock timeout; el invariante debe conservarse. Registra quién pierde.
    [SqlServerConcFact]
    public async Task I5_PagoQueLeeLaVentaTrasLaGuardia_InvarianteSeConserva_ResultadoSeRegistra()
    {
        var guardiaOk = Señal(); var soltar = Señal();
        var cancel = CancelarAsync(trasGuardia: async () => { guardiaOk.SetResult(); await soltar.Task; });
        await Esperar(guardiaOk.Task);

        var pago = PagarAsync(leeVenta: true);
        await TerminaAsync(pago, TimeSpan.FromSeconds(2));    // queda esperando la fila de venta
        soltar.SetResult();

        var (p, c) = (await pago, await cancel);
        Assert.True(await ViolacionesAsync() == 0, $"VIOLACIÓN: pago {p}, cancelación {c}");
        // Se registran en la salida de xUnit vía mensaje de aserción positiva:
        Assert.True(true, $"I-5 -> pago: {p}; cancelación: {c}");
    }

    /// M-1 (PASS esperado con mitigación): ambos toman el lock de crédito primero. Cancelación gana el lock,
    /// el pago espera y luego es rechazado.
    [SqlServerConcFact]
    public async Task M1_Mitigado_CancelacionTomaLockDeCredito_PagoEsperaYEsRechazado()
    {
        var guardiaOk = Señal(); var soltar = Señal();
        var cancel = CancelarAsync(mitigado: true, trasGuardia: async () => { guardiaOk.SetResult(); await soltar.Task; });
        await Esperar(guardiaOk.Task);

        var pago = PagarAsync(mitigado: true);
        Assert.False(await TerminaAsync(pago, TimeSpan.FromSeconds(2)), "El pago debía esperar el lock de crédito.");
        soltar.SetResult();

        Assert.Equal("Cancelada", (await cancel).Name);
        Assert.Equal("Rechazado", (await pago).Name);
        Assert.Equal(0, await ViolacionesAsync());
    }

    /// M-2 (PASS esperado con mitigación): el pago gana el lock de crédito; la cancelación espera y la guardia ve el pago.
    [SqlServerConcFact]
    public async Task M2_Mitigado_PagoTomaLockDeCredito_CancelacionEsperaYEsRechazada()
    {
        var pagoLee = Señal(); var soltar = Señal();
        var pago = PagarAsync(mitigado: true, trasLectura: async () => { pagoLee.SetResult(); await soltar.Task; });
        await Esperar(pagoLee.Task);

        var cancel = CancelarAsync(mitigado: true);
        Assert.False(await TerminaAsync(cancel, TimeSpan.FromSeconds(2)), "La cancelación debía esperar el lock de crédito.");
        soltar.SetResult();

        Assert.Equal("Aplicado", (await pago).Name);
        Assert.Equal("Rechazada", (await cancel).Name);
        Assert.Equal(0, await ViolacionesAsync());
    }
}

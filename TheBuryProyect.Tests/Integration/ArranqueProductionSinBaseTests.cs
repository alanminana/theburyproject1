using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------------------------
// Arranque en Production con la inicialización activada y la BASE INEXISTENTE (servidor SQL accesible).
//
// Defecto (reproducido): el servidor responde pero la base no existe. MigracionesGuard la trata como "nada que proteger" y
// DbInitializer falla al abrir la conexión (error 4060). Program.cs registraba el error y, fuera de Development, SEGUÍA: la app quedaba
// viva (/health/live 200, /health/ready 503) con los servicios en segundo plano contra una base ausente y la inicialización nunca se
// reintentaba, así que un supervisor con `restart: unless-stopped` no lo detectaba. Ahora el proceso termina con exit 1.
// (Con el servidor inalcanzable ya fallaba cerrado: MigracionesGuard lanza al verificar el esquema; por eso esta prueba necesita un
// servidor real y no puede simularse con un puerto cerrado.)
//
// NO corre por defecto. Requiere QA_ARRANQUE_SQL: cadena con credenciales de administración (p. ej. sa) a un SQL Server DESCARTABLE
// en loopback. La prueba crea un login sin permisos y elige un nombre de base inexistente; al final borra el login y comprueba que
// la app NO creó la base. Nunca toca TheBuryProjectDb. Contrato que NO cambia: con Database:InitializeOnStartup=false (Docker/servidor
// real) no se inicializa nada y el proceso sigue vivo.
// ---------------------------------------------------------------------------------------------

public sealed class ArranqueSqlFactAttribute : FactAttribute
{
    public ArranqueSqlFactAttribute()
    {
        var skip = ArranqueSqlEnv.SkipReason();
        if (skip != null) Skip = skip;
    }
}

internal static class ArranqueSqlEnv
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("QA_ARRANQUE_SQL");

    public static string? SkipReason()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return "BLOCKED: falta QA_ARRANQUE_SQL (SQL Server descartable en loopback).";
        var host = new SqlConnectionStringBuilder(ConnectionString).DataSource;
        var nombre = host.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase).Split(',', '\\')[0].Trim();
        return nombre is "127.0.0.1" or "localhost" or "::1" or "(local)" or "."
            ? null
            : $"BLOCKED: el servidor '{host}' no es loopback (protección contra bases reales).";
    }
}

[Collection("HttpIntegration.4")]
public class ArranqueProductionSinBaseTests : IAsyncLifetime
{
    private static readonly TimeSpan LimiteSalida = TimeSpan.FromSeconds(90);
    private const string Clave = "Qa#Arranque-Strong9";

    private readonly string _sufijo = Guid.NewGuid().ToString("N")[..8];
    private string Login => "erp_arranque_" + _sufijo;
    private string BaseInexistente => "qa_arranque_sinbase_" + _sufijo;
    private bool _habilitado;
    private int _puertoApp;

    public async Task InitializeAsync()
    {
        _habilitado = ArranqueSqlEnv.SkipReason() == null;
        if (!_habilitado) return;
        // Login sin permisos de servidor (sin CREATE ANY DATABASE) ni acceso a ninguna base: igual que el login de la app.
        await EjecutarAdminAsync($"CREATE LOGIN [{Login}] WITH PASSWORD = N'{Clave}', CHECK_POLICY = OFF;");
        Assert.Equal(0, await ContarBasesAsync(BaseInexistente));
    }

    public async Task DisposeAsync()
    {
        if (!_habilitado) return;
        await EjecutarAdminAsync($"IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{Login}') DROP LOGIN [{Login}];");
    }

    [ArranqueSqlFact]
    public async Task Production_InicializacionActiva_BaseInexistente_TerminaConExit1_YNoCreaLaBase()
    {
        var (proceso, salida) = Iniciar(inicializarAlArrancar: true);
        using (proceso)
        {
            using var cts = new CancellationTokenSource(LimiteSalida);
            try
            {
                await proceso.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                proceso.Kill(entireProcessTree: true);
                Assert.Fail("La app siguió viva tras fallar la inicialización de la base en Production. Salida:\n" + salida());
            }

            Assert.True(proceso.ExitCode == 1, $"Exit code esperado 1, fue {proceso.ExitCode}. Salida:\n{salida()}");
            Assert.Contains("el arranque se detiene", salida());
        }

        // Seguridad: el arranque fallido no debe haber creado ni modificado nada.
        Assert.Equal(0, await ContarBasesAsync(BaseInexistente));
    }

    [ArranqueSqlFact]
    public async Task Production_InicializacionDesactivada_BaseInexistente_NoCambiaElContrato_SigueViva()
    {
        var (proceso, salida) = Iniciar(inicializarAlArrancar: false);
        using (proceso)
        {
            try
            {
                // Debe estar ATENDIENDO (no basta con "no terminó"): /health/live = 200. /health/ready sería 503 (la base no existe).
                var vivo = false;
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var limite = DateTime.UtcNow.AddSeconds(45);
                while (DateTime.UtcNow < limite && !proceso.HasExited && !vivo)
                {
                    try { vivo = (await http.GetAsync($"http://127.0.0.1:{_puertoApp}/health/live")).StatusCode == HttpStatusCode.OK; }
                    catch (HttpRequestException) { await Task.Delay(500); }
                    catch (TaskCanceledException) { await Task.Delay(500); }
                }
                Assert.True(vivo, "Con InitializeOnStartup=false la app debe quedar atendiendo (/health/live 200) aunque la base no exista. " +
                    $"Terminó={proceso.HasExited}. Salida:\n{salida()}");
            }
            finally
            {
                if (!proceso.HasExited) proceso.Kill(entireProcessTree: true);
            }
        }

        Assert.Equal(0, await ContarBasesAsync(BaseInexistente));
    }

    private (Process Proceso, Func<string> Salida) Iniciar(bool inicializarAlArrancar)
    {
        var dll = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "TheBuryProyect.dll");
        Assert.True(File.Exists(dll), $"No se encontró {dll}");

        var admin = new SqlConnectionStringBuilder(ArranqueSqlEnv.ConnectionString)
        {
            InitialCatalog = BaseInexistente,
            UserID = Login,
            Password = Clave,
            ConnectTimeout = 5,
        };
        // Production exige credenciales SQL separadas: la cadena base sin usuario ni clave.
        var basis = new SqlConnectionStringBuilder(admin.ConnectionString) { UserID = "", Password = "", IntegratedSecurity = false };

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(dll)!,
        };
        psi.ArgumentList.Add(dll);
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        _puertoApp = PuertoLibre();
        psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{_puertoApp}";
        psi.Environment["ConnectionStrings__DefaultConnection"] = basis.ConnectionString;
        psi.Environment["ErpDb__User"] = Login;
        psi.Environment["ErpDb__Password"] = Clave;
        psi.Environment["Admin__Password"] = "Prueba#Admin-Inexistente9";
        psi.Environment["Database__InitializeOnStartup"] = inicializarAlArrancar ? "true" : "false";

        var buffer = new StringBuilder();
        var gate = new object();
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (gate) buffer.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (gate) buffer.AppendLine(e.Data); };
        Assert.True(p.Start());
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return (p, () => { lock (gate) return buffer.ToString(); });
    }

    private static int PuertoLibre()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var puerto = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return puerto;
    }

    private static async Task EjecutarAdminAsync(string sql)
    {
        var csb = new SqlConnectionStringBuilder(ArranqueSqlEnv.ConnectionString) { InitialCatalog = "master" };
        await using var c = new SqlConnection(csb.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> ContarBasesAsync(string nombre)
    {
        var csb = new SqlConnectionStringBuilder(ArranqueSqlEnv.ConnectionString) { InitialCatalog = "master" };
        await using var c = new SqlConnection(csb.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @n", c);
        cmd.Parameters.AddWithValue("@n", nombre);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TheBuryProject.Tests.Integration;

/// <summary>LOG-D-002: el acceso denegado (403) queda registrado como Warning, con el id del usuario y no con su nombre.</summary>
[Collection("HttpIntegration")]
public class ObservabilidadLogsHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ObservabilidadLogsHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AccesoDenegado_SeRegistraComoWarning_ConIdDeUsuario()
    {
        await _factory.SeedUserWithContratoSoloVentasAsync();
        var captura = new CapturaLogs();
        _factory.Services.GetRequiredService<ILoggerFactory>().AddProvider(captura);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ContratoSoloVentasUserId);

        var respuesta = await client.GetAsync("/Caja");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var entrada = Assert.Single(captura.Entradas, e => e.Mensaje.StartsWith("Acceso denegado a /Caja"));
        Assert.Equal(LogLevel.Warning, entrada.Nivel);
        Assert.Contains(CustomWebApplicationFactory.ContratoSoloVentasUserId, entrada.Mensaje);
    }

    private sealed class CapturaLogs : ILoggerProvider
    {
        public ConcurrentBag<(LogLevel Nivel, string Mensaje)> Entradas { get; } = new();
        public ILogger CreateLogger(string categoryName) => new L(this);
        public void Dispose() { }

        private sealed class L(CapturaLogs padre) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => padre.Entradas.Add((logLevel, formatter(state, exception)));
        }
    }
}

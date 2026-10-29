using System.Net;
using TheBuryProject.Tests.Infrastructure;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// H6-J1 (test intermitente ApiAntiforgeryHttpTests.SinToken_Devuelve400, CI 2026-10-28, HttpClient.Timeout de 300 s).
///
/// Causa medida: el servidor respondía en ~150 ms, pero la continuación de <c>HttpClient.SendAsync</c> del cliente de
/// test se publicaba en el SynchronizationContext de xunit (MaxConcurrencySyncContext, un hilo por núcleo). Con la suite
/// completa, ese contexto está ocupado por clases de tests síncronos y la continuación esperaba minutos en cola
/// (67/128/145 s medidos con 2 núcleos; en CI superó los 300 s del timeout del cliente, que cuenta ese tiempo de cola).
/// El cliente de test no debe depender del contexto de quien llama.
/// </summary>
[Collection("HttpIntegration")]
public class ClienteDeTestSinContextoDeSincronizacionTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ClienteDeTestSinContextoDeSincronizacionTests(CustomWebApplicationFactory factory) => _factory = factory;

    /// <summary>Contexto que acepta trabajo y nunca lo ejecuta: simula el contexto de xunit saturado.</summary>
    private sealed class ContextoSaturado : SynchronizationContext
    {
        public int Publicados;
        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref Publicados);
        public override void Send(SendOrPostCallback d, object? state) => Interlocked.Increment(ref Publicados);
    }

    [Theory]
    [InlineData(false)] // pedido sin token antiforgery: pasa por AntiforgeryApiHandler + ClienteComoHandler
    [InlineData(true)]  // pedido con token: además obtiene el token con un GET por el mismo pipeline
    public async Task ElPedido_Completa_AunqueElContextoDeQuienLlamaNoEjecuteContinuaciones(bool conToken)
    {
        await _factory.SeedTestUserAsync();
        using var client = _factory.CreateAuthenticatedClient();
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/tickets/checklist/1");
        if (!conToken)
            request.Headers.Add(AntiforgeryApiHandler.SinTokenHeader, "1");

        var original = SynchronizationContext.Current;
        var saturado = new ContextoSaturado();
        Task<HttpResponseMessage> envio;
        SynchronizationContext.SetSynchronizationContext(saturado);
        try
        {
            envio = client.SendAsync(request);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }

        using var respuesta = await envio.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.NotEqual(HttpStatusCode.RequestTimeout, respuesta.StatusCode);
        if (!conToken)
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(0, saturado.Publicados);
    }
}

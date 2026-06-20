using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Helpers;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests del cliente REST: forma de las requests (sin SDK, Bearer, form-encoded)
/// y parseo de respuestas reales de la API.
/// </summary>
public class MercadoLibreApiClientTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return Responder(request);
        }
    }

    private static (MercadoLibreApiClient Cliente, FakeHttpMessageHandler Handler) BuildCliente()
    {
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadolibre.com") };

        var opciones = Microsoft.Extensions.Options.Options.Create(new MercadoLibreOptions
        {
            ClientId = "cid",
            ClientSecret = "csecret",
            RedirectUri = "https://test/MercadoLibre/OAuthCallback"
        });

        return (new MercadoLibreApiClient(http, opciones, NullLogger<MercadoLibreApiClient>.Instance), handler);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ExchangeAuthorizationCode_PosteaFormCorrectoYParseaRespuesta()
    {
        var (cliente, handler) = BuildCliente();

        handler.Responder = _ => Json("""
            {
              "access_token": "APP_USR-token",
              "token_type": "Bearer",
              "expires_in": 21600,
              "scope": "offline_access read write",
              "user_id": 123,
              "refresh_token": "TG-refresh"
            }
            """);

        var token = await cliente.ExchangeAuthorizationCodeAsync("el-code");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/oauth/token", request.RequestUri!.AbsolutePath);

        var body = handler.RequestBodies[0];
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("client_id=cid", body);
        Assert.Contains("code=el-code", body);
        Assert.Contains("redirect_uri=", body);

        Assert.Equal("APP_USR-token", token.AccessToken);
        Assert.Equal("TG-refresh", token.RefreshToken);
        Assert.Equal(21600, token.ExpiresIn);
        Assert.Equal(123, token.UserId);
    }

    [Fact]
    public async Task RefreshAccessToken_UsaGrantTypeRefreshToken()
    {
        var (cliente, handler) = BuildCliente();

        handler.Responder = _ => Json("""
            {"access_token":"nuevo","token_type":"Bearer","expires_in":21600,"user_id":1,"refresh_token":"rotado"}
            """);

        var token = await cliente.RefreshAccessTokenAsync("viejo");

        Assert.Contains("grant_type=refresh_token", handler.RequestBodies[0]);
        Assert.Contains("refresh_token=viejo", handler.RequestBodies[0]);
        Assert.Equal("rotado", token.RefreshToken);
    }

    [Fact]
    public async Task TokenEndpoint_Error_LanzaSinFiltrarElBody()
    {
        var (cliente, handler) = BuildCliente();

        handler.Responder = _ => Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<MercadoLibreApiException>(
            () => cliente.ExchangeAuthorizationCodeAsync("code-vencido"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        // El mensaje del token endpoint no incluye el body (podría contener tokens).
        Assert.DoesNotContain("invalid_grant", ex.Message);
    }

    [Fact]
    public async Task GetCurrentUser_MandaBearerYParsea()
    {
        var (cliente, handler) = BuildCliente();

        handler.Responder = _ => Json("""{"id":456,"nickname":"NICK","site_id":"MLA"}""");

        var usuario = await cliente.GetCurrentUserAsync("mi-access-token");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("mi-access-token", request.Headers.Authorization.Parameter);
        Assert.Equal("/users/me", request.RequestUri!.AbsolutePath);

        Assert.Equal(456, usuario.Id);
        Assert.Equal("NICK", usuario.Nickname);
    }

    [Fact]
    public async Task GetItems_TroceaEnLotesDe20YDescartaErrores()
    {
        var (cliente, handler) = BuildCliente();

        var ids = Enumerable.Range(1, 25).Select(i => $"MLA{i}").ToList();

        handler.Responder = request =>
        {
            var idsEnQuery = request.RequestUri!.Query.Split("ids=")[1].Split('&')[0];
            var entradas = Uri.UnescapeDataString(idsEnQuery)
                .Split(',')
                .Select(id => id == "MLA3"
                    ? "{\"code\":404,\"body\":{\"id\":\"" + id + "\"}}"
                    : "{\"code\":200,\"body\":{\"id\":\"" + id + "\",\"title\":\"Item " + id + "\",\"price\":100.5,\"available_quantity\":3}}");

            return Json("[" + string.Join(',', entradas) + "]");
        };

        var items = await cliente.GetItemsAsync("token", ids);

        Assert.Equal(2, handler.Requests.Count); // 25 ids → lotes de 20 + 5
        Assert.Equal(24, items.Count);           // MLA3 vino con code 404 → descartado
        Assert.DoesNotContain(items, i => i.Id == "MLA3");
        Assert.Equal(100.5m, items[0].Price);
    }

    [Fact]
    public async Task SearchItemIds_ScanConScrollId()
    {
        var (cliente, handler) = BuildCliente();

        handler.Responder = _ => Json("""
            {"results":["MLA1","MLA2"],"scroll_id":"abc==","paging":{"total":2,"limit":100,"offset":0}}
            """);

        var pagina = await cliente.SearchItemIdsAsync("token", 123, scrollId: null);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/users/123/items/search", request.RequestUri!.ToString());
        Assert.Contains("search_type=scan", request.RequestUri.Query);

        Assert.Equal(new[] { "MLA1", "MLA2" }, pagina.Results);
        Assert.Equal("abc==", pagina.ScrollId);
    }

    [Fact]
    public async Task UpdateItemVariation_UsaEndpointDeVariacionYPayloadMinimo()
    {
        var (cliente, handler) = BuildCliente();

        handler.Responder = _ => Json("""
            {"id":196332252298,"price":500000,"available_quantity":1}
            """);

        var payload = new Dictionary<string, object>
        {
            ["available_quantity"] = 2,
            ["price"] = 510000m
        };

        var variacion = await cliente.UpdateItemVariationAsync(
            "token-seguro", "MLA1833540601", 196332252298, payload);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/items/MLA1833540601/variations/196332252298", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("token-seguro", request.Headers.Authorization.Parameter);

        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("\"available_quantity\":2", body);
        Assert.Contains("\"price\":510000", body);
        Assert.DoesNotContain("variations", body);

        Assert.Equal(196332252298, variacion.Id);
        Assert.Equal(500000m, variacion.Price);
        Assert.Equal(1, variacion.AvailableQuantity);
    }
}

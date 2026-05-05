using System.Net;

namespace TheBuryProject.Tests.Integration;

[Collection("HttpIntegration")]
public class LoginPageTest : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LoginPageTest(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Get_LoginPage_Returns200()
    {
        var response = await _client.GetAsync("/Identity/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

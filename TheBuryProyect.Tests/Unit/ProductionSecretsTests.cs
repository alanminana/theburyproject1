using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

public class ProductionSecretsTests
{
    private static IConfiguration Config(string? password) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=db;Database=erp",
            ["ErpDb:User"] = "runtime",
            ["ErpDb:Password"] = password
        }).Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("CHANGE_ME")]
    [InlineData("prefix_change_me_suffix")]
    public void ProductionRejectsMissingOrProvisionalPassword(string? password)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ProductionSecrets.ConnectionString(Config(password), true));
        Assert.Contains("ErpDb:Password", error.Message);
        if (!string.IsNullOrWhiteSpace(password)) Assert.DoesNotContain(password, error.Message);
    }

    [Fact]
    public void SpecialCharactersRoundTripWithoutChangingConnectionOptions()
    {
        var password = "Aa9" + Guid.NewGuid() + "$'\";#\\$(not_a_variable)";
        var result = new SqlConnectionStringBuilder(ProductionSecrets.ConnectionString(Config(password), true));
        Assert.Equal(password, result.Password);
        Assert.Equal("db", result.DataSource);
        Assert.Equal("runtime", result.UserID);
        Assert.False(result.PersistSecurityInfo);
    }

    [Fact]
    public void DevelopmentAllowsIntegratedAuthentication()
    {
        var basis = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = basis }).Build();
        Assert.Equal(basis, ProductionSecrets.ConnectionString(config, false));
    }

    [Fact]
    public void InvalidConnectionStringDoesNotExposeInputInException()
    {
        var config = Config("temporary-test-value");
        config["ConnectionStrings:DefaultConnection"] = "private-fragment=hidden";
        var error = Assert.Throws<InvalidOperationException>(() => ProductionSecrets.ConnectionString(config, true));
        Assert.DoesNotContain("private-fragment", error.ToString());
        Assert.Null(error.InnerException);
    }
}

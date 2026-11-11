using System.ComponentModel.DataAnnotations;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>C-F3: el nombre de usuario exige al menos una letra o un número en el alta y en la edición.</summary>
public class NombreUsuarioValidacionTests
{
    private static bool FallaUserName(object vm)
    {
        var resultados = new List<ValidationResult>();
        Validator.TryValidateObject(vm, new ValidationContext(vm), resultados, validateAllProperties: true);
        return resultados.Any(r => r.MemberNames.Contains("UserName"));
    }

    private static CrearUsuarioViewModel Alta(string userName) => new() { UserName = userName, Email = "a@example.invalid", Password = "Probe123!x", ConfirmPassword = "Probe123!x" };
    private static SeguridadEditarUsuarioViewModel Edicion(string userName) => new() { Id = "1", UserName = userName, Email = "a@example.invalid" };

    [Theory]
    [InlineData("jperez", false)]
    [InlineData("j.perez", false)]
    [InlineData("a@b", false)]
    [InlineData("..a", false)]
    [InlineData("123", false)]
    [InlineData("a_-+", false)]
    [InlineData("...", true)]
    [InlineData("---", true)]
    [InlineData("@@@", true)]
    [InlineData("+_.-", true)]
    [InlineData("ab", true)]
    [InlineData("jo se", true)]
    [InlineData("josé", true)]
    [InlineData("jperez\n", true)]
    public void AltaYEdicion_aplican_la_misma_regla(string userName, bool debeFallar)
    {
        Assert.Equal(debeFallar, FallaUserName(Alta(userName)));
        Assert.Equal(debeFallar, FallaUserName(Edicion(userName)));
    }

    [Fact]
    public void Mas_de_50_caracteres_falla()
    {
        Assert.True(FallaUserName(Alta(new string('a', 51))));
        Assert.False(FallaUserName(Alta(new string('a', 50))));
    }
}

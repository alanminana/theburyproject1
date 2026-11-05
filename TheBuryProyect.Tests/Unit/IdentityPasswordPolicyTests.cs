using TheBuryProject.Helpers;
using Xunit;

namespace TheBuryProject.Tests.Unit
{
    public class IdentityPasswordPolicyTests
    {
        [Theory]
        [InlineData(true, null, 10)]
        [InlineData(true, "", 10)]
        [InlineData(true, "6", 10)]   // en produccion la configuracion no relaja
        [InlineData(true, "8", 10)]
        [InlineData(true, "14", 14)]  // pero si endurece
        [InlineData(false, null, 6)]
        [InlineData(false, "8", 8)]
        [InlineData(false, "3", 6)]   // nunca por debajo de 6
        [InlineData(false, "abc", 6)]
        public void RequiredLength_segun_entorno(bool production, string? configurado, int esperado)
            => Assert.Equal(esperado, IdentityPasswordPolicy.RequiredLength(production, configurado));
    }
}

using Microsoft.AspNetCore.DataProtection;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;

namespace TheBuryProject.Modules.MercadoLibre.Services
{
    public class MercadoLibreTokenProtector : IMercadoLibreTokenProtector
    {
        // Versionar el purpose permite rotar el esquema sin romper tokens viejos.
        private const string Purpose = "TheBuryProject.MercadoLibre.Tokens.v1";

        private readonly IDataProtector _protector;

        public MercadoLibreTokenProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector(Purpose);
        }

        public string Protect(string plaintext) => _protector.Protect(plaintext);

        public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
    }
}

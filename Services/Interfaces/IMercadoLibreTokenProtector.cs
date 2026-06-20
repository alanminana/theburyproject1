namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Cifra/descifra tokens OAuth de Mercado Libre con ASP.NET Core Data Protection.
    /// Los tokens nunca se persisten ni loguean en texto plano.
    /// </summary>
    public interface IMercadoLibreTokenProtector
    {
        string Protect(string plaintext);

        string Unprotect(string protectedValue);
    }
}

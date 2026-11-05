namespace TheBuryProject.Helpers;

/// <summary>
/// Interruptor del webhook de Mercado Libre (<c>MercadoLibre:Webhook:Enabled</c>). Por defecto esta habilitado para no
/// cambiar el comportamiento existente; con <c>false</c> el endpoint anonimo responde 404 y el consumidor en segundo plano
/// no corre. Recomendado en <c>false</c> mientras la integracion con Mercado Libre este fuera de alcance operativo.
/// </summary>
internal static class MercadoLibreWebhookSwitch
{
    internal const string Clave = "MercadoLibre:Webhook:Enabled";

    internal static bool Habilitado(IConfiguration? configuration)
    {
        var valor = configuration?[Clave];
        // Solo un "false" valido lo apaga; ausente o ilegible conserva el comportamiento actual.
        return !(bool.TryParse(valor, out var habilitado) && !habilitado);
    }
}

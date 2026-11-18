using System.Text.Json;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// D-02: lee <c>Venta.RazonesAutorizacionJson</c> para saber si la autorización registrada cubrió la razón
    /// «referencias verificadas insuficientes». Una autorización por otro motivo (mora, cupo...) nunca la cubre.
    /// </summary>
    internal static class VentaAutorizacionReferencias
    {
        public static bool ContieneReferencias(string? razonesAutorizacionJson)
        {
            if (string.IsNullOrWhiteSpace(razonesAutorizacionJson))
            {
                return false;
            }

            try
            {
                using var documento = JsonDocument.Parse(razonesAutorizacionJson);
                if (documento.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var razon in documento.RootElement.EnumerateArray())
                {
                    if (razon.ValueKind == JsonValueKind.Object
                        && razon.TryGetProperty("Tipo", out var tipo)
                        && tipo.ValueKind == JsonValueKind.Number
                        && tipo.TryGetInt32(out var valor)
                        && valor == (int)TipoRazonAutorizacion.ReferenciasInsuficientes)
                    {
                        return true;
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Devuelve el JSON sin la razón «referencias insuficientes», para que una autorización dada a un monto anterior
        /// no pueda volver a cubrirla si las referencias faltan de nuevo. Conserva las demás razones. Si el JSON no se
        /// puede leer, devuelve el original (no se pierde información de auditoría).
        /// </summary>
        public static string? QuitarReferencias(string? razonesAutorizacionJson)
        {
            if (string.IsNullOrWhiteSpace(razonesAutorizacionJson))
            {
                return razonesAutorizacionJson;
            }

            try
            {
                using var documento = JsonDocument.Parse(razonesAutorizacionJson);
                if (documento.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return razonesAutorizacionJson;
                }

                var restantes = documento.RootElement.EnumerateArray()
                    .Where(razon => !(razon.ValueKind == JsonValueKind.Object
                        && razon.TryGetProperty("Tipo", out var tipo)
                        && tipo.ValueKind == JsonValueKind.Number
                        && tipo.TryGetInt32(out var valor)
                        && valor == (int)TipoRazonAutorizacion.ReferenciasInsuficientes))
                    .Select(razon => razon.Clone())
                    .ToList();

                return JsonSerializer.Serialize(restantes);
            }
            catch (JsonException)
            {
                return razonesAutorizacionJson;
            }
        }
    }
}

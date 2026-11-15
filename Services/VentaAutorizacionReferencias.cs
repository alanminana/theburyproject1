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
    }
}

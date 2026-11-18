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
        /// D-02: la razón de referencias está presente y fue autorizada de forma EXPRESA (flag por razón). Que la venta esté
        /// Autorizada por otras razones no alcanza: la excepción de referencias nunca se da por implícita.
        /// </summary>
        public static bool ReferenciasAutorizadas(string? razonesAutorizacionJson) =>
            VentaRazonesAutorizacionJson.Leer(razonesAutorizacionJson)
                .Any(r => r.Tipo == TipoRazonAutorizacion.ReferenciasInsuficientes && r.Autorizada);

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

namespace TheBuryProject.Services
{
    /// <summary>
    /// Lectura y escritura de <c>Venta.RazonesAutorizacionJson</c> con el estado de autorización por razón.
    /// </summary>
    internal static class VentaRazonesAutorizacionJson
    {
        public static List<RazonAutorizacion> Leer(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<RazonAutorizacion>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<RazonAutorizacion>>(json) ?? new List<RazonAutorizacion>();
            }
            catch (JsonException)
            {
                return new List<RazonAutorizacion>();
            }
        }

        public static string Serializar(IEnumerable<RazonAutorizacion> razones) =>
            JsonSerializer.Serialize(razones.Select(r => new
            {
                r.Tipo,
                r.Descripcion,
                r.DetalleAdicional,
                r.Unidad,
                r.MontoAsociado,
                r.DiasAsociado,
                r.Autorizada,
                r.AutorizadaPor,
                r.FechaAutorizacion,
                r.MotivoAutorizacion
            }));

        /// <summary>Descripción legible de las razones que todavía no tienen su autorización.</summary>
        public static string DescribirPendientes(string? json) =>
            string.Join("; ", Leer(json).Where(r => !r.Autorizada).Select(r => r.TipoDisplay));
    }
}

using System.Globalization;
using TheBuryProject.Data;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Interés por mora que se imprime en contratos y pagarés. Autoridad única: la
    /// <c>ConfiguracionPunitorio</c> vigente en la fecha comercial de emisión (la misma que usa
    /// <c>PunitorioCalculator</c> para devengar la deuda real). No usa <c>ConfiguracionMora.TasaMoraBase</c>
    /// ni valores semilla de empresa/plantilla, y no tiene fallback a una tasa positiva.
    ///
    /// Es un interés distinto del financiero de los planes de cuotas (<c>Credito.TasaInteres</c>).
    /// Un punitorio del 0 % es válido: el texto dice que no se devenga interés punitorio.
    /// Si no hay configuración vigente o está inactiva el interés NO está disponible y la emisión debe
    /// bloquearse: nunca se inventa una tasa.
    /// </summary>
    public sealed record InteresMoraContractual(
        EstadoConfiguracionPunitorio Estado,
        decimal Porcentaje,
        int PeriodoDias,
        int DiasGracia,
        DateOnly? VigenteDesde)
    {
        private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-AR");

        public const string ErrorNoDisponible =
            "No hay una configuración de punitorios vigente y activa: el contrato no puede indicar el interés por mora. " +
            "Definila en Configuración → Punitorios (puede ser 0 %).";

        /// <summary>Variables de plantilla que expresan el interés por mora (nuevas y antiguas, con sus alias legados).</summary>
        public static readonly string[] VariablesDeInteres =
        {
            "contrato.interesMoraTexto", "contrato.interesMoraPorcentaje", "contrato.interesMoraPeriodoDias",
            "contrato.interesMoraGraciaDias", "contrato.interesMoraVigenteDesde",
            "contrato.interesMoraDiario", "empresa.interesMoraDiario", "INTERES_MORA", "INTERES_MORA_TEXTO"
        };

        /// <summary>Variables de interés que usa un texto, leídas con el mismo criterio que el renderizador.</summary>
        public static IEnumerable<string> VariablesDeInteresUsadasEn(string contenido)
        {
            var usadas = PlantillaRenderer.TokensUsados(contenido);
            return VariablesDeInteres.Where(v => usadas.Contains(v, StringComparer.OrdinalIgnoreCase));
        }

        private static readonly string[] ClavesDeContexto =
        {
            "contrato.interesMoraTexto", "contrato.interesMoraPorcentaje", "contrato.interesMoraPeriodoDias",
            "contrato.interesMoraGraciaDias", "contrato.interesMoraVigenteDesde", "empresa.interesMoraDiario", "contrato.interesMoraDiario"
        };

        /// <summary>
        /// Al regenerar un documento se conservan las condiciones de mora con que se emitió (las de su snapshot), nunca las vigentes hoy.
        /// Lo que el original no tenga queda vacío.
        /// </summary>
        public static void RestaurarDesdeSnapshot(DocumentoContexto ctx, string? snapshotJson)
        {
            System.Text.Json.JsonElement valores = default;
            var hay = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(snapshotJson))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(snapshotJson);
                    if (doc.RootElement.TryGetProperty("valores", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        valores = v.Clone();
                        hay = true;
                    }
                }
            }
            catch (System.Text.Json.JsonException) { /* snapshot ilegible: se tratan como ausentes */ }

            foreach (var clave in ClavesDeContexto)
            {
                string? valor = null;
                if (hay && valores.TryGetProperty(clave, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String)
                    valor = e.GetString();
                ctx.Set(clave, valor);
            }
        }

        public static readonly InteresMoraContractual NoDisponible =
            new(EstadoConfiguracionPunitorio.Ausente, 0m, 0, 0, null);

        public bool Disponible => Estado == EstadoConfiguracionPunitorio.Activa && PeriodoDias > 0;

        public bool EsCero => Disponible && Porcentaje == 0m;

        public static InteresMoraContractual Desde(ConfiguracionPunitorioVigente vigente)
        {
            var c = vigente.Configuracion;
            return c is null
                ? new InteresMoraContractual(vigente.Estado, 0m, 0, 0, null)
                : new InteresMoraContractual(vigente.Estado, c.Porcentaje, c.PeriodoDias, c.DiasGracia, c.VigenteDesde);
        }

        /// <summary>Resuelve la configuración vigente hoy (día comercial). Solo lectura.</summary>
        public static async Task<InteresMoraContractual> ResolverAsync(
            AppDbContext context, IRelojComercial reloj, CancellationToken cancellationToken = default)
        {
            var servicio = new ConfiguracionPunitorioService(context, reloj);
            return Desde(await servicio.ObtenerVigenteAsync(reloj.HoyComercial, cancellationToken));
        }

        public string? PorcentajeTexto => Disponible ? Porcentaje.ToString("0.00##", Cultura) + "%" : null;

        /// <summary>
        /// Cláusula de mora para el contrato/pagaré, coherente con el cálculo real (porcentaje por período,
        /// prorrateo diario, gracia real) y con la vigencia de la configuración al emitir.
        /// </summary>
        public string? Texto
        {
            get
            {
                if (!Disponible) return null;
                var vigencia = VigenteDesde is DateOnly d ? $" (configuración de punitorios vigente desde el {d.ToString("dd/MM/yyyy", Cultura)})" : string.Empty;
                if (EsCero)
                    return $"a la fecha de emisión de este documento no se devenga interés punitorio por la falta de pago en término (0 %){vigencia}";
                var gracia = DiasGracia > 0
                    ? $", a partir del día siguiente a los {DiasGracia} día(s) de gracia posteriores al vencimiento"
                    : ", a partir del día siguiente al vencimiento";
                return $"la falta de pago en término devengará un interés punitorio del {Porcentaje.ToString("0.00##", Cultura)} % cada {PeriodoDias} día(s), " +
                       $"calculado día a día sobre el saldo impago de cada cuota vencida{gracia}{vigencia}";
            }
        }

        /// <summary>
        /// Valor del equivalente diario para plantillas antiguas (<c>{{contrato.interesMoraDiario}}</c>,
        /// <c>{{INTERES_MORA}}</c>). Solo existe de forma exacta cuando el interés es 0; con una tasa positiva
        /// el equivalente diario sería una aproximación, así que no se informa y la plantilla debe usar el texto nuevo.
        /// </summary>
        public decimal? EquivalenteDiarioExacto => EsCero ? 0m : null;

        /// <summary>Carga en el contexto documental las variables de interés por mora. Sin disponibilidad quedan vacías.</summary>
        public void AplicarA(DocumentoContexto ctx)
        {
            ctx.Set("contrato.interesMoraTexto", Texto);
            ctx.Set("contrato.interesMoraPorcentaje", PorcentajeTexto);
            ctx.Set("contrato.interesMoraPeriodoDias", Disponible ? PeriodoDias.ToString(Cultura) : null);
            ctx.Set("contrato.interesMoraGraciaDias", Disponible ? DiasGracia.ToString(Cultura) : null);
            ctx.Set("contrato.interesMoraVigenteDesde", Disponible && VigenteDesde is DateOnly d ? d.ToString("dd/MM/yyyy", Cultura) : null);

            // Variables antiguas: solo con interés 0 (exacto). Con tasa positiva quedan sin valor para forzar el texto nuevo.
            ctx.Set("empresa.interesMoraDiario", EquivalenteDiarioExacto?.ToString("F4", Cultura));
            ctx.Set("contrato.interesMoraDiario", EquivalenteDiarioExacto is null ? null : "0,00%");
        }
    }
}

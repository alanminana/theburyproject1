using System.Globalization;

namespace TheBuryProject.Services.Documentos
{
    public enum AlineacionDocumento { Izquierda, Derecha, Centro }

    public abstract record BloqueDocumento;
    public sealed record BloqueTexto(string Texto, AlineacionDocumento Alineacion, bool Negrita) : BloqueDocumento;
    public sealed record BloqueEspacio : BloqueDocumento;
    public sealed record BloqueLinea : BloqueDocumento;
    public sealed record BloqueSalto : BloqueDocumento;
    public sealed record BloqueFirmas : BloqueDocumento;
    public sealed record BloqueTabla(float[] Pesos, AlineacionDocumento[] Alineaciones, IReadOnlyList<string[]> Filas, bool ConEncabezado = true) : BloqueDocumento;
    public sealed record BloqueColumnas(float[] Pesos, IReadOnlyList<IReadOnlyList<BloqueDocumento>> Columnas) : BloqueDocumento;

    /// <summary>
    /// Formato de impresión administrativo de los documentos. La plantilla es texto y puede traer, una por línea,
    /// directivas de disposición que NO ejecutan nada (solo ordenan el texto ya renderizado):
    /// <list type="bullet">
    /// <item><c>@@admin</c> (primera línea): A4 blanco y negro, tipografía monoespaciada, sin cabecera ni pie decorativos.</item>
    /// <item><c>@@linea</c>: línea horizontal. <c>@@salto</c>: salto de página. <c>@@firmas</c>: dónde van los espacios de firma.</item>
    /// <item><c>@@cols 60/40</c> … <c>@@col</c> … <c>@@fincols</c>: dos o más columnas con ese ancho relativo.</item>
    /// <item><c>@@tabla 10,30,12;I,I,D</c> … <c>@@fintabla</c>: tabla de columnas (I izquierda, D derecha, C centro); la primera
    /// fila es el encabezado y las celdas se separan con <c>¦</c>. Con <c>;I,I,D;plana</c> no hay fila de encabezado.</item>
    /// <item>Prefijos de línea: <c>&gt;&gt; </c> alinea a la derecha, <c>^^ </c> centra, <c>** </c> negrita.</item>
    /// </list>
    /// El texto sin directivas (documentos del sistema anterior) se imprime como siempre.
    /// </summary>
    public static class DocumentoLayout
    {
        public const string MarcaAdministrativo = "@@admin";
        public const char SeparadorCelda = '¦';

        public static bool EsAdministrativo(string? contenido)
        {
            if (string.IsNullOrWhiteSpace(contenido))
                return false;

            foreach (var linea in contenido.Split('\n'))
            {
                var t = linea.Trim();
                if (t.Length == 0)
                    continue;
                return string.Equals(t, MarcaAdministrativo, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static IReadOnlyList<BloqueDocumento> Parsear(string contenido)
        {
            var lineas = contenido.Replace("\r\n", "\n").Split('\n');
            var i = 0;
            return ParsearBloques(lineas, ref i, null);
        }

        private static List<BloqueDocumento> ParsearBloques(string[] lineas, ref int i, string? cierre)
        {
            var bloques = new List<BloqueDocumento>();

            while (i < lineas.Length)
            {
                var linea = lineas[i].TrimEnd();
                var t = linea.TrimStart();

                if (t.StartsWith("@@", StringComparison.Ordinal))
                {
                    var (directiva, argumento) = Dividir(t);

                    if (cierre != null && (directiva == cierre || (cierre == "@@fincols" && directiva == "@@col")))
                        return bloques;

                    i++;
                    switch (directiva)
                    {
                        case "@@admin":
                            break;
                        case "@@linea":
                            bloques.Add(new BloqueLinea());
                            break;
                        case "@@salto":
                            bloques.Add(new BloqueSalto());
                            break;
                        case "@@firmas":
                            bloques.Add(new BloqueFirmas());
                            break;
                        case "@@tabla":
                            bloques.Add(ParsearTabla(lineas, ref i, argumento));
                            break;
                        case "@@cols":
                            bloques.Add(ParsearColumnas(lineas, ref i, argumento));
                            break;
                        default:
                            // Directiva desconocida o cierre huérfano: se ignora (nunca se imprime como texto).
                            break;
                    }

                    continue;
                }

                i++;
                if (t.Length == 0)
                {
                    bloques.Add(new BloqueEspacio());
                    continue;
                }

                var alineacion = AlineacionDocumento.Izquierda;
                var negrita = false;
                var texto = linea;
                if (t.StartsWith(">> ", StringComparison.Ordinal)) { alineacion = AlineacionDocumento.Derecha; texto = t[3..]; }
                else if (t.StartsWith("^^ ", StringComparison.Ordinal)) { alineacion = AlineacionDocumento.Centro; texto = t[3..]; }
                else if (t.StartsWith("** ", StringComparison.Ordinal)) { negrita = true; texto = t[3..]; }

                bloques.Add(new BloqueTexto(texto, alineacion, negrita));
            }

            return bloques;
        }

        private static BloqueTabla ParsearTabla(string[] lineas, ref int i, string argumento)
        {
            var partes = argumento.Split(';');
            var pesos = ParsearPesos(partes[0], ',');
            var alineaciones = pesos.Select(_ => AlineacionDocumento.Izquierda).ToArray();
            if (partes.Length > 1)
            {
                var letras = partes[1].Split(',', StringSplitOptions.TrimEntries);
                for (var k = 0; k < alineaciones.Length && k < letras.Length; k++)
                    alineaciones[k] = letras[k].ToUpperInvariant() switch { "D" => AlineacionDocumento.Derecha, "C" => AlineacionDocumento.Centro, _ => AlineacionDocumento.Izquierda };
            }

            var filas = new List<string[]>();
            while (i < lineas.Length)
            {
                var t = lineas[i].Trim();
                if (t.StartsWith("@@fintabla", StringComparison.Ordinal))
                {
                    i++;
                    break;
                }

                i++;
                if (t.Length == 0)
                    continue;

                var celdas = t.Split(SeparadorCelda).Select(c => c.Trim()).ToArray();
                // Normaliza al ancho de la tabla: sobrantes se unen en la última celda, faltantes quedan vacías.
                if (celdas.Length > pesos.Length)
                    celdas = celdas.Take(pesos.Length - 1).Append(string.Join(" ", celdas.Skip(pesos.Length - 1))).ToArray();
                else if (celdas.Length < pesos.Length)
                    celdas = celdas.Concat(Enumerable.Repeat(string.Empty, pesos.Length - celdas.Length)).ToArray();
                filas.Add(celdas);
            }

            var plana = partes.Length > 2 && string.Equals(partes[2].Trim(), "plana", StringComparison.OrdinalIgnoreCase);
            return new BloqueTabla(pesos, alineaciones, filas, !plana);
        }

        private static BloqueColumnas ParsearColumnas(string[] lineas, ref int i, string argumento)
        {
            var pesos = ParsearPesos(argumento, '/');
            var columnas = new List<IReadOnlyList<BloqueDocumento>>();

            while (columnas.Count < pesos.Length)
            {
                columnas.Add(ParsearBloques(lineas, ref i, "@@fincols"));
                if (i >= lineas.Length)
                    break;

                var (directiva, _) = Dividir(lineas[i].Trim());
                i++;
                if (directiva == "@@fincols")
                    break;
            }

            while (columnas.Count < pesos.Length)
                columnas.Add(Array.Empty<BloqueDocumento>());

            return new BloqueColumnas(pesos, columnas);
        }

        private static (string Directiva, string Argumento) Dividir(string t)
        {
            var corte = t.IndexOf(' ');
            return corte < 0 ? (t.ToLowerInvariant(), string.Empty) : (t[..corte].ToLowerInvariant(), t[(corte + 1)..].Trim());
        }

        private static float[] ParsearPesos(string texto, char separador)
        {
            var pesos = texto.Split(separador, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(p => float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 1f)
                .ToArray();
            return pesos.Length == 0 ? new[] { 1f } : pesos;
        }
    }
}

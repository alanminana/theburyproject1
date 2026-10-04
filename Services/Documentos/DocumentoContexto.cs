using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Datos de la operación aplanados y desacoplados de las entidades EF: es lo único que ven las
    /// plantillas y las condiciones. Se serializa como snapshot del documento emitido.
    /// </summary>
    public sealed class DocumentoContexto
    {
        public AnclaDocumento Ancla { get; set; }

        /// <summary>Identifica la operación para idempotencia (ej. "venta:12", "pago:45").</summary>
        public string ClaveAncla { get; set; } = string.Empty;

        public int? ClienteId { get; set; }
        public int? VentaId { get; set; }
        public int? CreditoId { get; set; }
        public int? CuotaId { get; set; }
        public int? PagoCuotaId { get; set; }
        public int? CotizacionId { get; set; }

        public Dictionary<string, object?> Valores { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<Dictionary<string, object?>>> Colecciones { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string ruta, object? valor) => Valores[ruta] = valor;

        /// <summary>Resuelve una ruta o alias legado. false = la variable no existe en este contexto.</summary>
        public bool TryResolver(string token, out object? valor)
        {
            if (Valores.TryGetValue(token, out valor))
                return true;

            if (CatalogoDocumento.AliasesLegados.TryGetValue(token, out var ruta) && Valores.TryGetValue(ruta, out valor))
                return true;

            valor = null;
            return false;
        }

        public DocumentoContexto Clonar()
        {
            var c = new DocumentoContexto
            {
                Ancla = Ancla, ClaveAncla = ClaveAncla, ClienteId = ClienteId, VentaId = VentaId,
                CreditoId = CreditoId, CuotaId = CuotaId, PagoCuotaId = PagoCuotaId, CotizacionId = CotizacionId
            };
            foreach (var kv in Valores) c.Valores[kv.Key] = kv.Value;
            foreach (var kv in Colecciones) c.Colecciones[kv.Key] = kv.Value;
            return c;
        }

        public string ToSnapshotJson()
            => JsonSerializer.Serialize(new { valores = Valores, colecciones = Colecciones });
    }

    public sealed class RenderResultado
    {
        public string Texto { get; init; } = string.Empty;

        /// <summary>Variables referenciadas por la plantilla que existen pero no tienen valor.</summary>
        public IReadOnlyList<string> Vacias { get; init; } = Array.Empty<string>();

        /// <summary>Variables referenciadas que no existen en el contexto de este evento.</summary>
        public IReadOnlyList<string> NoResueltas { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Motor de plantillas propio, sin evaluación de código. Sintaxis:
    /// <c>{{ruta.variable}}</c>, secciones <c>{{#cuotas}}...{{/cuotas}}</c> (colección o booleano) e
    /// invertidas <c>{{^cuotas}}...{{/cuotas}}</c>. Los tokens legados (<c>{{COMPRADOR_NOMBRE}}</c>)
    /// siguen funcionando vía alias. El texto no se interpreta como HTML ni como expresión.
    /// </summary>
    public static class PlantillaRenderer
    {
        private static readonly Regex TokenRegex = new(@"\{\{\s*([#^/]?)\s*([A-Za-z0-9_.]+)\s*\}\}", RegexOptions.Compiled);
        private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-AR");

        public static RenderResultado Renderizar(string plantilla, DocumentoContexto contexto)
        {
            var vacias = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var noResueltas = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            var pos = 0;
            RenderBloque(plantilla, ref pos, null, contexto, new List<Dictionary<string, object?>>(), sb, true, vacias, noResueltas);
            return new RenderResultado { Texto = sb.ToString(), Vacias = vacias.ToList(), NoResueltas = noResueltas.ToList() };
        }

        /// <summary>Variables {{x}} y secciones usadas por una plantilla (para validar al guardar).</summary>
        public static IReadOnlyList<string> TokensUsados(string plantilla)
            => TokenRegex.Matches(plantilla).Select(m => m.Groups[2].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Valida balance de secciones y que todos los tokens existan en el catálogo.</summary>
        public static IReadOnlyList<string> Validar(string plantilla)
        {
            var errores = new List<string>();
            var abiertas = new Stack<string>();
            var enColeccion = new Stack<ColeccionDocumento?>();

            foreach (Match m in TokenRegex.Matches(plantilla))
            {
                var tipo = m.Groups[1].Value;
                var nombre = m.Groups[2].Value;

                if (tipo is "#" or "^")
                {
                    abiertas.Push(nombre);
                    var col = CatalogoDocumento.BuscarColeccion(nombre);
                    var campoBool = CatalogoDocumento.Buscar(nombre);
                    if (col == null && (campoBool == null || campoBool.Tipo != TipoCampoDocumento.Booleano))
                        errores.Add($"La sección '{nombre}' no es una colección ni un campo verdadero/falso conocido.");
                    enColeccion.Push(col);
                }
                else if (tipo == "/")
                {
                    if (abiertas.Count == 0 || !string.Equals(abiertas.Pop(), nombre, StringComparison.OrdinalIgnoreCase))
                        errores.Add($"Cierre de sección '{{{{/{nombre}}}}}' sin apertura correspondiente.");
                    if (enColeccion.Count > 0) enColeccion.Pop();
                }
                else
                {
                    var col = enColeccion.FirstOrDefault(c => c != null);
                    if (col != null && col.Campos.Contains(nombre, StringComparer.OrdinalIgnoreCase))
                        continue;
                    if (!CatalogoDocumento.EsTokenConocido(nombre))
                        errores.Add($"Variable desconocida: '{{{{{nombre}}}}}'.");
                }
            }

            if (abiertas.Count > 0)
                errores.Add($"Sección '{{{{#{abiertas.Peek()}}}}}' sin cerrar.");

            return errores.Distinct().ToList();
        }

        private static void RenderBloque(
            string t, ref int pos, string? cierre, DocumentoContexto ctx,
            List<Dictionary<string, object?>> scopes, StringBuilder sb, bool emitir,
            SortedSet<string> vacias, SortedSet<string> noResueltas)
        {
            while (pos < t.Length)
            {
                var m = TokenRegex.Match(t, pos);
                if (!m.Success)
                {
                    if (emitir) sb.Append(t, pos, t.Length - pos);
                    pos = t.Length;
                    return;
                }

                if (emitir) sb.Append(t, pos, m.Index - pos);
                pos = m.Index + m.Length;

                var tipo = m.Groups[1].Value;
                var nombre = m.Groups[2].Value;

                // Etiqueta de sección sola en su línea: no deja una línea en blanco en la salida.
                if (tipo.Length > 0 && (m.Index == 0 || t[m.Index - 1] == '\n'))
                {
                    if (pos < t.Length && t[pos] == '\n') pos++;
                    else if (pos + 1 < t.Length && t[pos] == '\r' && t[pos + 1] == '\n') pos += 2;
                }

                if (tipo == "/")
                {
                    if (cierre != null && string.Equals(cierre, nombre, StringComparison.OrdinalIgnoreCase))
                        return;
                    continue; // cierre huérfano: se ignora (Validar lo reporta al guardar)
                }

                if (tipo is "#" or "^")
                {
                    var inicioCuerpo = pos;

                    if (ctx.Colecciones.TryGetValue(nombre, out var coleccion))
                    {
                        if (tipo == "#" && emitir && coleccion.Count > 0)
                        {
                            foreach (var item in coleccion)
                            {
                                pos = inicioCuerpo;
                                scopes.Insert(0, item);
                                RenderBloque(t, ref pos, nombre, ctx, scopes, sb, true, vacias, noResueltas);
                                scopes.RemoveAt(0);
                            }
                        }
                        else
                        {
                            // Sin ítems, sección invertida o bloque descartado: se consume el cuerpo.
                            var emiteInvertida = tipo == "^" && emitir && coleccion.Count == 0;
                            pos = inicioCuerpo;
                            RenderBloque(t, ref pos, nombre, ctx, scopes, sb, emiteInvertida, vacias, noResueltas);
                        }
                    }
                    else
                    {
                        var verdadero = ResolverBooleano(nombre, ctx, scopes);
                        pos = inicioCuerpo;
                        RenderBloque(t, ref pos, nombre, ctx, scopes, sb,
                            emitir && (tipo == "#" ? verdadero : !verdadero), vacias, noResueltas);
                    }

                    continue;
                }

                // Variable simple: primero el scope del ítem, luego el contexto global.
                object? valor = null;
                var encontrada = false;
                foreach (var scope in scopes)
                {
                    if (scope.TryGetValue(nombre, out valor)) { encontrada = true; break; }
                }

                if (!encontrada)
                    encontrada = ctx.TryResolver(nombre, out valor);

                if (!encontrada)
                {
                    if (emitir) noResueltas.Add(nombre);
                    continue;
                }

                var texto = Formatear(valor);
                if (texto.Length == 0 && emitir)
                    vacias.Add(nombre);
                if (emitir) sb.Append(texto);
            }
        }

        private static bool ResolverBooleano(string nombre, DocumentoContexto ctx, List<Dictionary<string, object?>> scopes)
        {
            object? valor = null;
            var ok = false;
            foreach (var scope in scopes)
                if (scope.TryGetValue(nombre, out valor)) { ok = true; break; }
            if (!ok) ok = ctx.TryResolver(nombre, out valor);

            return ok && valor switch
            {
                bool b => b,
                null => false,
                string s => !string.IsNullOrWhiteSpace(s),
                _ => true
            };
        }

        public static string Formatear(object? valor) => valor switch
        {
            null => string.Empty,
            string s => s,
            decimal d => d.ToString("C2", Cultura),
            DateTime dt => dt.ToString("dd/MM/yyyy", Cultura),
            DateOnly dO => dO.ToString("dd/MM/yyyy", Cultura),
            bool b => b ? "Sí" : "No",
            IFormattable f => f.ToString(null, Cultura),
            _ => valor.ToString() ?? string.Empty
        };
    }

    /// <summary>Importe en letras en castellano (pesos con centavos), para recibos.</summary>
    public static class NumeroALetras
    {
        private static readonly string[] Unidades =
        {
            "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce",
            "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve", "veinte", "veintiuno",
            "veintidós", "veintitrés", "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve"
        };

        private static readonly string[] Decenas = { "", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa" };

        private static readonly string[] Centenas =
        {
            "", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos"
        };

        public static string Importe(decimal importe)
        {
            var negativo = importe < 0;
            importe = Math.Abs(Math.Round(importe, 2, MidpointRounding.AwayFromZero));
            var enteros = (long)Math.Truncate(importe);
            var centavos = (int)Math.Round((importe - enteros) * 100m);

            var palabras = enteros == 1 ? "un peso" : ConvertirApocopado(enteros) + " pesos";
            if (enteros > 0 && enteros % 1_000_000 == 0)
                palabras = ConvertirApocopado(enteros) + " de pesos";

            var texto = $"{(negativo ? "menos " : string.Empty)}{palabras} con {centavos:00}/100";
            return char.ToUpper(texto[0], CultureInfo.GetCultureInfo("es-AR")) + texto[1..];
        }

        private static string Convertir(long n)
        {
            if (n < 30) return Unidades[n];
            if (n < 100)
            {
                var d = (int)(n / 10);
                var u = (int)(n % 10);
                return u == 0 ? Decenas[d] : $"{Decenas[d]} y {Unidades[u]}";
            }
            if (n == 100) return "cien";
            if (n < 1000)
            {
                var c = (int)(n / 100);
                var r = n % 100;
                return r == 0 ? Centenas[c] : $"{Centenas[c]} {Convertir(r)}";
            }
            if (n < 1_000_000)
            {
                var miles = n / 1000;
                var r = n % 1000;
                var prefijo = miles == 1 ? "mil" : $"{ConvertirApocopado(miles)} mil";
                return r == 0 ? prefijo : $"{prefijo} {Convertir(r)}";
            }
            if (n < 1_000_000_000_000L)
            {
                var millones = n / 1_000_000;
                var r = n % 1_000_000;
                var prefijo = millones == 1 ? "un millón" : $"{ConvertirApocopado(millones)} millones";
                return r == 0 ? prefijo : $"{prefijo} {Convertir(r)}";
            }
            return n.ToString(CultureInfo.InvariantCulture);
        }

        // "veintiún mil", "treinta y un mil", "un millón"...: uno → un delante de sustantivo.
        private static string ConvertirApocopado(long n)
        {
            var s = Convertir(n);
            if (s.EndsWith("veintiuno", StringComparison.Ordinal)) return s[..^3] + "ún";
            if (s.EndsWith("uno", StringComparison.Ordinal)) return s[..^1];
            return s;
        }
    }
}

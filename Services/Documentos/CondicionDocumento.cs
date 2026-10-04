using System.Globalization;
using System.Text.Json;

namespace TheBuryProject.Services.Documentos
{
    public static class OperadoresDocumento
    {
        public const string Igual = "igual";
        public const string Distinto = "distinto";
        public const string Mayor = "mayor";
        public const string MayorIgual = "mayorIgual";
        public const string Menor = "menor";
        public const string MenorIgual = "menorIgual";
        public const string Contiene = "contiene";
        public const string En = "en";
        public const string NoEn = "noEn";
        public const string Existe = "existe";
        public const string Verdadero = "verdadero";
        public const string Falso = "falso";

        public const string GrupoTodas = "todas";
        public const string GrupoCualquiera = "cualquiera";

        public static IReadOnlyList<(string Codigo, string Etiqueta)> Todos { get; } = new[]
        {
            (Igual, "es igual a"), (Distinto, "es distinto de"), (Mayor, "es mayor que"),
            (MayorIgual, "es mayor o igual que"), (Menor, "es menor que"), (MenorIgual, "es menor o igual que"),
            (Contiene, "contiene"), (En, "está en la lista"), (NoEn, "no está en la lista"),
            (Existe, "tiene valor"), (Verdadero, "es verdadero"), (Falso, "es falso")
        };

        public static IReadOnlyList<string> Permitidos(TipoCampoDocumento tipo) => tipo switch
        {
            TipoCampoDocumento.Texto => new[] { Igual, Distinto, Contiene, En, NoEn, Existe },
            TipoCampoDocumento.Numero => new[] { Igual, Distinto, Mayor, MayorIgual, Menor, MenorIgual, En, NoEn, Existe },
            TipoCampoDocumento.Fecha => new[] { Igual, Distinto, Mayor, MayorIgual, Menor, MenorIgual, Existe },
            TipoCampoDocumento.Booleano => new[] { Verdadero, Falso, Existe },
            _ => Array.Empty<string>()
        };

        public static bool SinValor(string operador)
            => operador is Existe or Verdadero or Falso;
    }

    /// <summary>
    /// Árbol de condiciones declarativo. Forma JSON (nunca se ejecuta código):
    /// <code>{"op":"todas","condiciones":[{"campo":"venta.tipoPago","operador":"igual","valor":"CreditoPersonal"},
    ///   {"op":"cualquiera","condiciones":[...]}]}</code>
    /// Una hoja es {campo, operador, valor}; un grupo es {op: todas|cualquiera, condiciones: [...]}.
    /// </summary>
    public sealed class CondicionDocumento
    {
        public string? Grupo { get; init; }
        public IReadOnlyList<CondicionDocumento> Hijos { get; init; } = Array.Empty<CondicionDocumento>();
        public string? Campo { get; init; }
        public string? Operador { get; init; }
        public IReadOnlyList<string> Valores { get; init; } = Array.Empty<string>();
        public TipoCampoDocumento Tipo { get; init; }

        public bool EsGrupo => Grupo != null;
    }

    public sealed class CondicionParseResult
    {
        public CondicionDocumento? Condicion { get; init; }
        public IReadOnlyList<string> Errores { get; init; } = Array.Empty<string>();
        public bool EsValida => Errores.Count == 0;
    }

    public static class CondicionDocumentoParser
    {
        public const int ProfundidadMaxima = 5;
        public const int NodosMaximos = 50;

        /// <summary>
        /// Valida y convierte el JSON. Vacío/nulo = sin condición (siempre aplica). Los campos deben
        /// existir en el catálogo y el operador/valor ser coherentes con su tipo.
        /// </summary>
        public static CondicionParseResult Parsear(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new CondicionParseResult();

            var errores = new List<string>();
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            }
            catch (JsonException ex)
            {
                return new CondicionParseResult { Errores = new[] { "El JSON de condiciones no es válido: " + ex.Message } };
            }

            using (doc)
            {
                var nodos = 0;
                var raiz = ParsearNodo(doc.RootElement, 1, errores, ref nodos);
                return new CondicionParseResult { Condicion = errores.Count == 0 ? raiz : null, Errores = errores };
            }
        }

        private static CondicionDocumento? ParsearNodo(JsonElement el, int profundidad, List<string> errores, ref int nodos)
        {
            if (++nodos > NodosMaximos)
            {
                if (nodos == NodosMaximos + 1) errores.Add($"Demasiadas condiciones (máximo {NodosMaximos}).");
                return null;
            }

            if (profundidad > ProfundidadMaxima)
            {
                errores.Add($"Los grupos de condiciones no pueden anidarse más de {ProfundidadMaxima} niveles.");
                return null;
            }

            if (el.ValueKind != JsonValueKind.Object)
            {
                errores.Add("Cada condición debe ser un objeto JSON.");
                return null;
            }

            if (el.TryGetProperty("op", out var opEl))
            {
                var op = opEl.GetString();
                if (op is not (OperadoresDocumento.GrupoTodas or OperadoresDocumento.GrupoCualquiera))
                {
                    errores.Add($"Operador de grupo inválido '{op}' (use '{OperadoresDocumento.GrupoTodas}' o '{OperadoresDocumento.GrupoCualquiera}').");
                    return null;
                }

                if (!el.TryGetProperty("condiciones", out var hijosEl) || hijosEl.ValueKind != JsonValueKind.Array)
                {
                    errores.Add("Un grupo debe tener una lista 'condiciones'.");
                    return null;
                }

                var hijos = new List<CondicionDocumento>();
                foreach (var h in hijosEl.EnumerateArray())
                {
                    var hijo = ParsearNodo(h, profundidad + 1, errores, ref nodos);
                    if (hijo != null) hijos.Add(hijo);
                }

                return new CondicionDocumento { Grupo = op, Hijos = hijos };
            }

            var campo = el.TryGetProperty("campo", out var c) ? c.GetString() : null;
            var operador = el.TryGetProperty("operador", out var o) ? o.GetString() : null;

            var info = CatalogoDocumento.Buscar(campo);
            if (info == null)
            {
                errores.Add($"Campo no permitido en condiciones: '{campo}'.");
                return null;
            }

            if (operador == null || !OperadoresDocumento.Permitidos(info.Tipo).Contains(operador))
            {
                errores.Add($"El operador '{operador}' no es válido para el campo '{info.Etiqueta}' ({info.Tipo}).");
                return null;
            }

            var valores = new List<string>();
            if (!OperadoresDocumento.SinValor(operador))
            {
                if (!el.TryGetProperty("valor", out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    errores.Add($"Falta el valor de la condición sobre '{info.Etiqueta}'.");
                    return null;
                }

                var crudos = new List<string>();
                if (v.ValueKind == JsonValueKind.Array)
                    crudos.AddRange(v.EnumerateArray().Select(x => ValorComoTexto(x)));
                else if (operador is OperadoresDocumento.En or OperadoresDocumento.NoEn)
                    crudos.AddRange(ValorComoTexto(v).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                else
                    crudos.Add(ValorComoTexto(v));

                if (crudos.Count == 0 || (operador is not (OperadoresDocumento.En or OperadoresDocumento.NoEn) && crudos.Count != 1))
                {
                    errores.Add($"Valor inválido en la condición sobre '{info.Etiqueta}'.");
                    return null;
                }

                foreach (var crudo in crudos)
                {
                    if (!ValorValido(info, crudo, out var normalizado, out var error))
                    {
                        errores.Add($"'{info.Etiqueta}': {error}");
                        return null;
                    }

                    valores.Add(normalizado);
                }
            }

            return new CondicionDocumento { Campo = info.Ruta, Operador = operador, Valores = valores, Tipo = info.Tipo };
        }

        private static string ValorComoTexto(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? string.Empty,
            JsonValueKind.Number => v.GetDecimal().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => v.ToString()
        };

        private static bool ValorValido(CampoDocumento campo, string crudo, out string normalizado, out string error)
        {
            normalizado = crudo.Trim();
            error = string.Empty;

            switch (campo.Tipo)
            {
                case TipoCampoDocumento.Numero:
                    if (!decimal.TryParse(normalizado.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
                    {
                        error = $"'{crudo}' no es un número.";
                        return false;
                    }
                    normalizado = n.ToString(CultureInfo.InvariantCulture);
                    return true;
                case TipoCampoDocumento.Fecha:
                    if (!DateTime.TryParseExact(normalizado, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
                    {
                        error = $"'{crudo}' no es una fecha (use AAAA-MM-DD).";
                        return false;
                    }
                    normalizado = f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return true;
                case TipoCampoDocumento.Booleano:
                    if (!bool.TryParse(normalizado, out var b))
                    {
                        error = $"'{crudo}' no es verdadero/falso.";
                        return false;
                    }
                    normalizado = b.ToString().ToLowerInvariant();
                    return true;
                default:
                    if (campo.Valores != null && !campo.Valores.Contains(normalizado, StringComparer.OrdinalIgnoreCase))
                    {
                        error = $"'{crudo}' no es un valor permitido ({string.Join(", ", campo.Valores)}).";
                        return false;
                    }
                    return true;
            }
        }
    }

    public static class CondicionDocumentoEvaluador
    {
        public static bool Evaluar(CondicionDocumento? condicion, DocumentoContexto contexto)
        {
            if (condicion == null)
                return true;

            if (condicion.EsGrupo)
            {
                if (condicion.Hijos.Count == 0)
                    return true;

                return condicion.Grupo == OperadoresDocumento.GrupoTodas
                    ? condicion.Hijos.All(h => Evaluar(h, contexto))
                    : condicion.Hijos.Any(h => Evaluar(h, contexto));
            }

            contexto.Valores.TryGetValue(condicion.Campo!, out var valor);
            return EvaluarHoja(condicion, valor);
        }

        private static bool EvaluarHoja(CondicionDocumento c, object? valor)
        {
            var vacio = valor == null || (valor is string s && string.IsNullOrWhiteSpace(s));
            switch (c.Operador)
            {
                case OperadoresDocumento.Existe: return !vacio;
                case OperadoresDocumento.Verdadero: return valor is bool b1 && b1;
                case OperadoresDocumento.Falso: return valor is bool b2 && !b2;
            }

            if (vacio)
                return c.Operador is OperadoresDocumento.Distinto or OperadoresDocumento.NoEn;

            switch (c.Tipo)
            {
                case TipoCampoDocumento.Numero:
                    var n = ComoDecimal(valor!);
                    var objetivos = c.Valores.Select(v => decimal.Parse(v, CultureInfo.InvariantCulture)).ToList();
                    return CompararOrden(c.Operador!, n.CompareTo(objetivos[0]), objetivos.Contains(n));
                case TipoCampoDocumento.Fecha:
                    var f = ComoFecha(valor!);
                    var obj = DateTime.ParseExact(c.Valores[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return CompararOrden(c.Operador!, f.CompareTo(obj), f == obj);
                default:
                    var t = Convert.ToString(valor, CultureInfo.InvariantCulture) ?? string.Empty;
                    var enLista = c.Valores.Any(v => string.Equals(v, t, StringComparison.OrdinalIgnoreCase));
                    return c.Operador switch
                    {
                        OperadoresDocumento.Igual => enLista,
                        OperadoresDocumento.Distinto => !enLista,
                        OperadoresDocumento.En => enLista,
                        OperadoresDocumento.NoEn => !enLista,
                        OperadoresDocumento.Contiene => t.Contains(c.Valores[0], StringComparison.OrdinalIgnoreCase),
                        _ => false
                    };
            }
        }

        private static bool CompararOrden(string operador, int cmp, bool enLista) => operador switch
        {
            OperadoresDocumento.Igual => cmp == 0,
            OperadoresDocumento.Distinto => cmp != 0,
            OperadoresDocumento.Mayor => cmp > 0,
            OperadoresDocumento.MayorIgual => cmp >= 0,
            OperadoresDocumento.Menor => cmp < 0,
            OperadoresDocumento.MenorIgual => cmp <= 0,
            OperadoresDocumento.En => enLista,
            OperadoresDocumento.NoEn => !enLista,
            _ => false
        };

        private static decimal ComoDecimal(object v) => v switch
        {
            decimal d => d,
            int i => i,
            long l => l,
            double db => (decimal)db,
            _ => decimal.Parse(Convert.ToString(v, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture)
        };

        private static DateTime ComoFecha(object v) => v switch
        {
            DateTime d => d.Date,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            _ => DateTime.Parse(Convert.ToString(v, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture).Date
        };
    }
}

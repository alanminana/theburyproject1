using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Importador por streaming del catálogo ML. El archivo es un objeto JSON gigante
    /// { "MLA1403": {...}, "MLA416632": {...}, ... }: se recorre con un
    /// <see cref="Utf8JsonReader"/> sobre un buffer que se rellena del stream, y cada
    /// categoría individual se materializa con <see cref="JsonDocument.ParseValue"/>.
    /// Así nunca se carga el archivo completo en memoria. Persiste por lotes y limpia
    /// el ChangeTracker para mantener acotada la memoria.
    /// </summary>
    public class MercadoLibreCategoryCatalogImportService : IMercadoLibreCategoryCatalogImportService
    {
        private const int BatchSize = 500;
        private const int InitialBufferSize = 256 * 1024;

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ILogger<MercadoLibreCategoryCatalogImportService> _logger;

        public MercadoLibreCategoryCatalogImportService(
            IDbContextFactory<AppDbContext> contextFactory,
            ILogger<MercadoLibreCategoryCatalogImportService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<MercadoLibreCategoryImportResult> ImportFromFileAsync(
            string filePath, string siteId = "MLA", CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var nowUtc = DateTime.UtcNow;
            siteId = string.IsNullOrWhiteSpace(siteId) ? "MLA" : siteId.Trim().ToUpperInvariant();

            var esGzip = filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
            var result = new MercadoLibreCategoryImportResult
            {
                SiteId = siteId,
                SourceFilePath = filePath,
                SourceKind = esGzip ? "gzip" : "json"
            };

            if (!File.Exists(filePath))
            {
                result.Error = $"No existe el archivo: {filePath}";
                await RegistrarEstadoAsync(siteId, result, nowUtc, sw.ElapsedMilliseconds, ct);
                return result;
            }

            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

            try
            {
                var md5 = await CalcularMd5Async(filePath, ct);

                // Reemplazo wholesale: el caché es un snapshot completo del archivo.
                // Se borran primero los atributos (hijos) y luego las categorías.
                await ctx.MercadoLibreCategoryAttributes.Where(a => a.SiteId == siteId).ExecuteDeleteAsync(ct);
                await ctx.MercadoLibreCategories.Where(c => c.SiteId == siteId).ExecuteDeleteAsync(ct);

                await using var fileStream = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1 << 20, useAsync: true);

                Stream stream = fileStream;
                if (esGzip)
                    stream = new GZipStream(fileStream, CompressionMode.Decompress);

                try
                {
                    var (cats, attrs, leaves, listing, maxCreated) =
                        await StreamAndPersistAsync(stream, siteId, ctx, nowUtc, ct);

                    result.Ok = true;
                    result.ImportedCategories = cats;
                    result.ImportedAttributes = attrs;
                    result.LeafCategories = leaves;
                    result.ListingAllowedCategories = listing;
                    result.DurationMs = sw.ElapsedMilliseconds;

                    await RegistrarEstadoAsync(siteId, result, nowUtc, result.DurationMs, ct, md5, maxCreated);

                    _logger.LogInformation(
                        "Catálogo ML importado ({Site}): {Cats} categorías, {Leaves} hojas, " +
                        "{Listing} publicables, {Attrs} atributos en {Ms} ms desde {File}",
                        siteId, cats, leaves, listing, attrs, result.DurationMs, filePath);
                }
                finally
                {
                    if (esGzip)
                        await stream.DisposeAsync();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
                result.DurationMs = sw.ElapsedMilliseconds;
                _logger.LogError(ex, "Error importando el catálogo ML desde {File}", filePath);
                await RegistrarEstadoAsync(siteId, result, nowUtc, result.DurationMs, ct);
            }

            return result;
        }

        // ── Streaming + persistencia por lotes ─────────────────────────────────
        private async Task<(int Cats, int Attrs, int Leaves, int Listing, DateTime? MaxCreated)>
            StreamAndPersistAsync(Stream stream, string siteId, AppDbContext ctx, DateTime nowUtc, CancellationToken ct)
        {
            int catCount = 0, attrCount = 0, leafCount = 0, listingCount = 0;
            DateTime? maxCreated = null;
            var pending = new List<MercadoLibreCategory>(BatchSize + 64);

            void OnCategory(string catId, JsonElement el)
            {
                var cat = MapCategory(siteId, catId, el, nowUtc, out var atributos, out var created);
                cat.Attributes = atributos;
                pending.Add(cat);

                catCount++;
                attrCount += atributos.Count;
                if (cat.IsLeaf) leafCount++;
                if (cat.ListingAllowed) listingCount++;
                if (created.HasValue && (!maxCreated.HasValue || created.Value > maxCreated.Value))
                    maxCreated = created.Value;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
            try
            {
                int dataLength = 0;
                bool isFinalBlock = false;
                bool bomVerificado = false;
                JsonReaderState state = default;
                bool rootEnded = false;

                while (!rootEnded)
                {
                    if (!isFinalBlock)
                    {
                        // Un único valor más grande que el buffer: crecer y reintentar.
                        if (dataLength == buffer.Length)
                        {
                            var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                            Array.Copy(buffer, bigger, dataLength);
                            ArrayPool<byte>.Shared.Return(buffer);
                            buffer = bigger;
                        }

                        int read = await stream.ReadAsync(buffer.AsMemory(dataLength), ct);
                        dataLength += read;
                        if (read == 0) isFinalBlock = true;

                        // Saltar el BOM UTF-8 (EF BB BF) si el archivo lo trae: Utf8JsonReader
                        // no lo tolera. Se verifica una sola vez sobre el primer chunk.
                        if (!bomVerificado && dataLength >= 3)
                        {
                            bomVerificado = true;
                            if (buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                            {
                                Array.Copy(buffer, 3, buffer, 0, dataLength - 3);
                                dataLength -= 3;
                            }
                        }
                    }

                    long consumed = ProcessBuffer(
                        buffer.AsSpan(0, dataLength), isFinalBlock, ref state, OnCategory, out rootEnded);

                    int leftover = dataLength - (int)consumed;
                    if (consumed > 0 && leftover > 0)
                        Array.Copy(buffer, (int)consumed, buffer, 0, leftover);
                    dataLength = leftover;

                    if (pending.Count >= BatchSize)
                    {
                        await FlushAsync(ctx, pending, ct);
                        pending.Clear();
                        ctx.ChangeTracker.Clear();
                    }

                    // Sin progreso sobre el bloque final ⇒ fin (o JSON truncado).
                    if (isFinalBlock && consumed == 0 && !rootEnded)
                        break;
                }

                if (pending.Count > 0)
                {
                    await FlushAsync(ctx, pending, ct);
                    pending.Clear();
                    ctx.ChangeTracker.Clear();
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return (catCount, attrCount, leafCount, listingCount, maxCreated);
        }

        private static async Task FlushAsync(AppDbContext ctx, List<MercadoLibreCategory> batch, CancellationToken ct)
        {
            await ctx.MercadoLibreCategories.AddRangeAsync(batch, ct);
            await ctx.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Recorre el buffer parseando todas las categorías completas que contenga.
        /// Devuelve los bytes consumidos hasta el último punto de resync válido y
        /// actualiza <paramref name="state"/>. Cada Utf8JsonReader vive solo dentro de
        /// este método síncrono (es ref struct: no cruza await/yield).
        /// </summary>
        private static long ProcessBuffer(
            ReadOnlySpan<byte> span, bool isFinalBlock, ref JsonReaderState state,
            Action<string, JsonElement> onCategory, out bool rootEnded)
        {
            rootEnded = false;
            var reader = new Utf8JsonReader(span, isFinalBlock, state);
            long lastConsumed = 0;
            var lastState = state;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        var catId = reader.GetString() ?? string.Empty;

                        // ¿Está el valor completo en el buffer? Probar con una copia.
                        var probe = reader;
                        if (!probe.Read() || !probe.TrySkip())
                        {
                            // Valor incompleto: reanudar ANTES de este property name.
                            state = lastState;
                            return lastConsumed;
                        }

                        reader.Read(); // avanzar al inicio del valor (objeto categoría)
                        using (var doc = JsonDocument.ParseValue(ref reader))
                            onCategory(catId, doc.RootElement);

                        lastConsumed = reader.BytesConsumed;
                        lastState = reader.CurrentState;
                        break;

                    case JsonTokenType.EndObject:
                        lastConsumed = reader.BytesConsumed;
                        lastState = reader.CurrentState;
                        rootEnded = true;
                        state = lastState;
                        return lastConsumed;

                    default:
                        // StartObject raíz u otros: avanzar el checkpoint.
                        lastConsumed = reader.BytesConsumed;
                        lastState = reader.CurrentState;
                        break;
                }
            }

            // Read() devolvió false: token parcial al final del buffer.
            state = lastState;
            return lastConsumed;
        }

        // ── Mapeo categoría/atributos ──────────────────────────────────────────
        internal static MercadoLibreCategory MapCategory(
            string siteId, string catId, JsonElement el, DateTime nowUtc,
            out List<MercadoLibreCategoryAttribute> atributos, out DateTime? dateCreated)
        {
            string id = GetString(el, "id") ?? catId;

            var settings = el.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object
                ? s : default;

            var children = el.TryGetProperty("children_categories", out var ch) && ch.ValueKind == JsonValueKind.Array
                ? ch : default;
            bool isLeaf = children.ValueKind != JsonValueKind.Array || children.GetArrayLength() == 0;

            string? parentId = null;
            if (el.TryGetProperty("path_from_root", out var pfr) && pfr.ValueKind == JsonValueKind.Array)
            {
                int len = pfr.GetArrayLength();
                if (len >= 2)
                    parentId = GetString(pfr[len - 2], "id");
            }

            dateCreated = GetDateTime(el, "date_created");

            var cat = new MercadoLibreCategory
            {
                SiteId = siteId,
                CategoryId = id,
                Name = GetString(el, "name") ?? string.Empty,
                ParentCategoryId = parentId,
                PathFromRootJson = RawOrNull(el, "path_from_root"),
                ChildrenJson = RawOrNull(el, "children_categories"),
                IsLeaf = isLeaf,
                ListingAllowed = GetBool(settings, "listing_allowed"),
                BuyingAllowed = GetBool(settings, "buying_allowed"),
                Status = GetString(settings, "status"),
                AttributeTypes = GetString(el, "attribute_types"),
                CatalogDomain = GetString(settings, "catalog_domain"),
                Vertical = GetString(settings, "vertical"),
                SubVertical = GetString(settings, "sub_vertical"),
                MaxTitleLength = GetInt(settings, "max_title_length"),
                MaxPicturesPerItem = GetInt(settings, "max_pictures_per_item"),
                MaxVariationsAllowed = GetInt(settings, "max_variations_allowed"),
                ItemConditionsJson = RawOrNull(settings, "item_conditions"),
                BuyingModesJson = RawOrNull(settings, "buying_modes"),
                ShippingOptionsJson = RawOrNull(settings, "shipping_options"),
                TotalItemsInThisCategory = GetInt(el, "total_items_in_this_category"),
                Permalink = GetString(el, "permalink"),
                Picture = GetString(el, "picture"),
                // RawJson del nodo sin attributes/channels_settings (esos no aportan al caché).
                RawJson = CloneObjectExcept(el, "attributes", "channels_settings"),
                LastSeenAtUtc = nowUtc,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };

            atributos = new List<MercadoLibreCategoryAttribute>();
            if (el.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in attrs.EnumerateArray())
                    atributos.Add(MapAttribute(siteId, id, a, nowUtc));
            }

            return cat;
        }

        private static MercadoLibreCategoryAttribute MapAttribute(
            string siteId, string categoryId, JsonElement a, DateTime nowUtc)
        {
            var tags = a.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;

            return new MercadoLibreCategoryAttribute
            {
                SiteId = siteId,
                CategoryId = categoryId,
                AttributeId = GetString(a, "id") ?? string.Empty,
                Name = GetString(a, "name") ?? string.Empty,
                ValueType = GetString(a, "value_type"),
                Hierarchy = GetString(a, "hierarchy"),
                Relevance = GetInt(a, "relevance"),
                Required = GetBool(tags, "required"),
                CatalogRequired = GetBool(tags, "catalog_required") || GetBool(tags, "catalog_listing_required"),
                ConditionalRequired = GetBool(tags, "conditional_required"),
                NewRequired = GetBool(tags, "new_required"),
                ReadOnly = GetBool(tags, "read_only"),
                Hidden = GetBool(tags, "hidden"),
                AllowVariations = GetBool(tags, "allow_variations"),
                VariationAttribute = GetBool(tags, "variation_attribute"),
                Multivalued = GetBool(tags, "multivalued"),
                ValueMaxLength = GetInt(a, "value_max_length"),
                ValuesJson = RawOrNull(a, "values"),
                AllowedUnitsJson = RawOrNull(a, "allowed_units"),
                DefaultUnit = GetString(a, "default_unit"),
                AttributeGroupId = GetString(a, "attribute_group_id"),
                AttributeGroupName = GetString(a, "attribute_group_name"),
                Hint = Truncar(GetString(a, "hint"), 500),
                Tooltip = GetString(a, "tooltip"),
                LastSeenAtUtc = nowUtc
            };
        }

        // ── Helpers de lectura JSON (defensivos) ───────────────────────────────
        private static string? GetString(JsonElement parent, string prop)
        {
            if (parent.ValueKind != JsonValueKind.Object) return null;
            if (!parent.TryGetProperty(prop, out var v)) return null;
            return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private static bool GetBool(JsonElement parent, string prop)
        {
            if (parent.ValueKind != JsonValueKind.Object) return false;
            if (!parent.TryGetProperty(prop, out var v)) return false;
            return v.ValueKind == JsonValueKind.True;
        }

        private static int? GetInt(JsonElement parent, string prop)
        {
            if (parent.ValueKind != JsonValueKind.Object) return null;
            if (!parent.TryGetProperty(prop, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l))
                return l is >= int.MinValue and <= int.MaxValue ? (int)l : null;
            return null;
        }

        private static DateTime? GetDateTime(JsonElement parent, string prop)
        {
            if (parent.ValueKind != JsonValueKind.Object) return null;
            if (!parent.TryGetProperty(prop, out var v)) return null;
            if (v.ValueKind == JsonValueKind.String && v.TryGetDateTime(out var dt))
                return dt.ToUniversalTime();
            return null;
        }

        private static string? RawOrNull(JsonElement parent, string prop)
        {
            if (parent.ValueKind != JsonValueKind.Object) return null;
            if (!parent.TryGetProperty(prop, out var v)) return null;
            if (v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            return v.GetRawText();
        }

        /// <summary>Serializa el objeto excluyendo ciertas propiedades (para un RawJson liviano).</summary>
        private static string? CloneObjectExcept(JsonElement obj, params string[] exclude)
        {
            if (obj.ValueKind != JsonValueKind.Object) return null;

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                foreach (var p in obj.EnumerateObject())
                {
                    if (Array.IndexOf(exclude, p.Name) >= 0) continue;
                    p.WriteTo(w);
                }
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static string? Truncar(string? valor, int max)
            => valor is null ? null : (valor.Length <= max ? valor : valor[..max]);

        private static async Task<string?> CalcularMd5Async(string filePath, CancellationToken ct)
        {
            try
            {
                await using var fs = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1 << 20, useAsync: true);
                using var md5 = MD5.Create();
                var hash = await md5.ComputeHashAsync(fs, ct);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return null;
            }
        }

        private async Task RegistrarEstadoAsync(
            string siteId, MercadoLibreCategoryImportResult result, DateTime nowUtc, long durationMs,
            CancellationToken ct, string? md5 = null, DateTime? maxCreated = null)
        {
            try
            {
                await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

                var estado = await ctx.MercadoLibreCategorySyncStates
                    .FirstOrDefaultAsync(e => e.SiteId == siteId, ct);

                if (estado is null)
                {
                    estado = new MercadoLibreCategorySyncState { SiteId = siteId };
                    ctx.MercadoLibreCategorySyncStates.Add(estado);
                }

                estado.SourceFilePath = result.SourceFilePath;
                estado.SourceKind = result.SourceKind;
                estado.LastImportedAtUtc = nowUtc;
                estado.DurationMs = durationMs;
                estado.LastError = result.Ok ? null : result.Error;

                if (result.Ok)
                {
                    estado.LastSuccessAtUtc = nowUtc;
                    estado.ImportedCategories = result.ImportedCategories;
                    estado.ImportedAttributes = result.ImportedAttributes;
                    estado.LeafCategories = result.LeafCategories;
                    estado.ListingAllowedCategories = result.ListingAllowedCategories;
                    estado.LastContentMd5 = md5;
                    estado.LastContentCreated = maxCreated;
                }

                await ctx.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo registrar el estado de importación del catálogo ML.");
            }
        }
    }
}

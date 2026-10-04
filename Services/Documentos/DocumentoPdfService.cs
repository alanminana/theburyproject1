using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Arma el PDF a partir del contenido YA renderizado y guardado del documento. No re-evalúa
    /// plantillas ni datos: reimprimir siempre produce el mismo texto histórico. Varios documentos
    /// (ej. contrato + pagaré) se imprimen juntos en un único PDF, una sección por documento.
    /// </summary>
    public class DocumentoPdfService : IDocumentoPdfService
    {
        private static readonly Dictionary<string, string> EtiquetasFirmante = new(StringComparer.OrdinalIgnoreCase)
        {
            ["vendedor"] = "Vendedor",
            ["comprador"] = "Comprador",
            ["fiador"] = "Fiador / Garante",
            ["firmante"] = "Firma y aclaración"
        };

        public DocumentoPdfArchivo GenerarPdf(IReadOnlyList<DocumentoGenerado> documentos)
        {
            if (documentos.Count == 0)
                throw new DocumentoException(DocumentoErrores.Operacion, "No hay documentos para imprimir.");

            var pdf = CrearDocumento(documentos);

            var nombre = documentos.Count == 1
                ? $"{Sanitizar(documentos[0].Numero)}.pdf"
                : $"documentos-{Sanitizar(documentos[0].Numero)}.pdf";

            return new DocumentoPdfArchivo { NombreArchivo = nombre, Contenido = pdf.GeneratePdf() };
        }

        /// <summary>Arma el documento de impresión (también lo usan los tests para obtener imágenes de cada página).</summary>
        internal static Document CrearDocumento(IReadOnlyList<DocumentoGenerado> documentos)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            // Documentos en formato administrativo (pagaré + contrato, por ejemplo) se imprimen como una sola
            // composición continua: comparten página mientras entren y el salto de página es automático.
            var todosAdministrativos = documentos.All(d => DocumentoLayout.EsAdministrativo(d.ContenidoRenderizado));

            var pdf = Document.Create(container =>
            {
                if (todosAdministrativos)
                {
                    var copias = documentos.Max(d => Math.Max(1, d.PlantillaDocumento?.Copias ?? 1));
                    for (var copia = 1; copia <= copias; copia++)
                    {
                        var etiquetaCopia = copias > 1 ? $"Copia {copia} de {copias}" : null;
                        container.Page(page => ComponerPaginaAdministrativa(page, documentos, etiquetaCopia));
                    }

                    return;
                }

                foreach (var doc in documentos)
                {
                    var copias = Math.Max(1, doc.PlantillaDocumento?.Copias ?? 1);
                    for (var copia = 1; copia <= copias; copia++)
                    {
                        var etiquetaCopia = copias > 1 ? $"Copia {copia} de {copias}" : null;
                        if (DocumentoLayout.EsAdministrativo(doc.ContenidoRenderizado))
                            container.Page(page => ComponerPaginaAdministrativa(page, new[] { doc }, etiquetaCopia));
                        else
                            container.Page(page => ComponerPagina(page, doc, etiquetaCopia));
                    }
                }
            });

            return pdf;
        }

        private static void ComponerPagina(PageDescriptor page, DocumentoGenerado doc, string? etiquetaCopia)
        {
            page.Size(PageSizes.A4);
            page.Margin(1.6f, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

            var titulo = doc.TipoDocumento?.Nombre ?? "Documento";
            page.Header().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(8).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(titulo).SemiBold().FontSize(18).FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"Número: {doc.Numero}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    if (etiquetaCopia != null)
                        col.Item().Text(etiquetaCopia).FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(120).AlignRight().Text(doc.FechaGeneracionUtc.ToString("dd/MM/yyyy HH:mm"))
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(8);

                if (doc.Estado is EstadoDocumentoGenerado.Cancelado or EstadoDocumentoGenerado.Reemplazado)
                {
                    var leyenda = doc.Estado == EstadoDocumentoGenerado.Cancelado ? "DOCUMENTO ANULADO" : "DOCUMENTO REEMPLAZADO";
                    col.Item().Background(Colors.Red.Lighten4).Padding(6).AlignCenter()
                        .Text(leyenda).SemiBold().FontColor(Colors.Red.Darken3);
                }

                col.Item().Text(doc.ContenidoRenderizado).LineHeight(1.25f);

                var firmantes = ObtenerFirmantes(doc);
                if (firmantes.Count > 0)
                    col.Item().PaddingTop(18).Element(c => Firmas(c, doc, firmantes));
            });

            page.Footer().BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(6).AlignCenter().Text(text =>
            {
                text.Span("Generado por TheBuryProject - Página ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        }

        // ------------------------------------------------------------------ Formato administrativo

        private static readonly string[] FuentesAdministrativas = { "Courier New", "Liberation Mono", "Consolas" };

        private static readonly Dictionary<string, string> EtiquetasFirmaAdministrativa = new(StringComparer.OrdinalIgnoreCase)
        {
            ["vendedor"] = "Firma Vendedor",
            ["comprador"] = "Firma Comprador",
            ["fiador"] = "Firma Fiador/es",
            ["firmante"] = "Firma"
        };

        private static void ComponerPaginaAdministrativa(PageDescriptor page, IReadOnlyList<DocumentoGenerado> documentos, string? etiquetaCopia)
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(1.3f, Unit.Centimetre);
            page.MarginVertical(1.1f, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily(FuentesAdministrativas).FontColor(Colors.Black).LineHeight(1.2f));

            page.Content().Column(col =>
            {
                if (etiquetaCopia != null)
                    col.Item().AlignRight().Text(etiquetaCopia).FontSize(7);

                for (var i = 0; i < documentos.Count; i++)
                {
                    var doc = documentos[i];
                    if (i > 0)
                        col.Item().PaddingVertical(10).LineHorizontal(0.5f).LineColor(Colors.Black);

                    col.Item().Element(c => ContenidoAdministrativo(c, doc));
                }
            });
        }

        private static void ContenidoAdministrativo(IContainer container, DocumentoGenerado doc)
        {
            var bloques = DocumentoLayout.Parsear(doc.ContenidoRenderizado);
            var firmantes = ObtenerFirmantes(doc);
            var firmas = LeerFirmas(doc.FirmasJson);

            container.Column(col =>
            {
                if (doc.Estado is EstadoDocumentoGenerado.Cancelado or EstadoDocumentoGenerado.Reemplazado)
                {
                    var leyenda = doc.Estado == EstadoDocumentoGenerado.Cancelado ? "DOCUMENTO ANULADO" : "DOCUMENTO REEMPLAZADO";
                    col.Item().Border(1).Padding(3).AlignCenter().Text(leyenda).Bold();
                }

                BloquesAdministrativos(col, bloques, firmantes, firmas);

                // Sin marca explícita, las firmas van al final del documento.
                if (firmantes.Count > 0 && !TieneFirmas(bloques))
                    col.Item().ShowEntire().PaddingTop(16).Element(c => FirmasAdministrativas(c, firmantes, firmas));
            });
        }

        private static bool TieneFirmas(IReadOnlyList<BloqueDocumento> bloques)
            => bloques.Any(b => b is BloqueFirmas || (b is BloqueColumnas cols && cols.Columnas.Any(TieneFirmas)));

        private static void BloquesAdministrativos(ColumnDescriptor col, IReadOnlyList<BloqueDocumento> bloques, List<string> firmantes, List<FirmaRegistrada> firmas)
        {
            foreach (var bloque in bloques)
            {
                switch (bloque)
                {
                    case BloqueTexto t:
                        col.Item().Text(text =>
                        {
                            if (t.Alineacion == AlineacionDocumento.Derecha) text.AlignRight();
                            else if (t.Alineacion == AlineacionDocumento.Centro) text.AlignCenter();
                            var span = text.Span(t.Texto);
                            if (t.Negrita) span.Bold();
                        });
                        break;
                    case BloqueEspacio:
                        col.Item().Height(5);
                        break;
                    case BloqueLinea:
                        col.Item().PaddingVertical(2).LineHorizontal(0.6f).LineColor(Colors.Black);
                        break;
                    case BloqueSalto:
                        col.Item().PageBreak();
                        break;
                    case BloqueFirmas:
                        if (firmantes.Count > 0)
                            col.Item().ShowEntire().PaddingTop(16).Element(c => FirmasAdministrativas(c, firmantes, firmas));
                        break;
                    case BloqueTabla tabla:
                        col.Item().Element(c => TablaAdministrativa(c, tabla));
                        break;
                    case BloqueColumnas columnas:
                        col.Item().Row(row =>
                        {
                            for (var k = 0; k < columnas.Columnas.Count; k++)
                            {
                                var contenido = columnas.Columnas[k];
                                row.RelativeItem(columnas.Pesos[k]).PaddingRight(k < columnas.Columnas.Count - 1 ? 8 : 0)
                                    .Column(inner => BloquesAdministrativos(inner, contenido, firmantes, firmas));
                            }
                        });
                        break;
                }
            }
        }

        private static void TablaAdministrativa(IContainer container, BloqueTabla tabla)
        {
            if (tabla.Filas.Count == 0)
                return;

            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    foreach (var peso in tabla.Pesos)
                        c.RelativeColumn(peso);
                });

                for (var f = 0; f < tabla.Filas.Count; f++)
                {
                    var encabezado = tabla.ConEncabezado && f == 0;
                    var fila = tabla.Filas[f];
                    for (var c = 0; c < tabla.Pesos.Length; c++)
                    {
                        var alineacion = tabla.Alineaciones[c];
                        var texto = fila[c];
                        var celda = table.Cell();
                        var caja = encabezado ? celda.BorderBottom(0.6f).BorderColor(Colors.Black) : celda;
                        caja.PaddingVertical(1).PaddingHorizontal(2).Text(text =>
                        {
                            if (alineacion == AlineacionDocumento.Derecha) text.AlignRight();
                            else if (alineacion == AlineacionDocumento.Centro) text.AlignCenter();
                            var span = text.Span(texto);
                            if (encabezado) span.Bold();
                        });
                    }
                }
            });
        }

        private static void FirmasAdministrativas(IContainer container, List<string> firmantes, List<FirmaRegistrada> firmas)
        {
            container.Row(row =>
            {
                foreach (var rol in firmantes)
                {
                    var etiqueta = EtiquetasFirmaAdministrativa.TryGetValue(rol, out var e) ? e : $"Firma {rol}";
                    var firma = firmas.FirstOrDefault(f => string.Equals(f.Rol, rol, StringComparison.OrdinalIgnoreCase));
                    var imagen = string.IsNullOrEmpty(firma?.ImagenPng) ? null : Convert.FromBase64String(firma.ImagenPng);

                    row.RelativeItem().PaddingHorizontal(6).Column(col =>
                    {
                        // Espacio para la firma manuscrita (o la imagen capturada en pantalla).
                        if (imagen != null)
                            col.Item().Height(36).AlignCenter().Image(imagen).FitArea();
                        else
                            col.Item().Height(36);

                        col.Item().BorderTop(0.6f).BorderColor(Colors.Black).PaddingTop(2).AlignCenter().Text(etiqueta);

                        // El pagaré lleva además la aclaración debajo de la firma.
                        if (string.Equals(rol, "firmante", StringComparison.OrdinalIgnoreCase))
                        {
                            col.Item().Height(26);
                            col.Item().BorderTop(0.6f).BorderColor(Colors.Black).PaddingTop(2).AlignCenter().Text("Aclaración");
                        }
                    });
                }
            });
        }

        private static List<string> ObtenerFirmantes(DocumentoGenerado doc)
            => (doc.FirmantesRequeridos ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();

        private static void Firmas(IContainer container, DocumentoGenerado doc, List<string> firmantes)
        {
            var firmas = LeerFirmas(doc.FirmasJson);

            container.Row(row =>
            {
                foreach (var rol in firmantes)
                {
                    var etiqueta = EtiquetasFirmante.TryGetValue(rol, out var e) ? e : rol;
                    var firma = firmas.FirstOrDefault(f => string.Equals(f.Rol, rol, StringComparison.OrdinalIgnoreCase));

                    var imagen = string.IsNullOrEmpty(firma?.ImagenPng) ? null : Convert.FromBase64String(firma.ImagenPng);
                    row.RelativeItem().PaddingTop(imagen == null ? 35 : 0).Column(col =>
                    {
                        if (imagen != null)
                            col.Item().Height(35).AlignCenter().Image(imagen).FitArea();
                        col.Item().BorderTop(1).BorderColor(Colors.Grey.Darken2).PaddingTop(4).AlignCenter()
                            .Text(etiqueta).FontSize(9);
                        if (firma != null)
                            col.Item().AlignCenter().Text($"Firmado: {firma.Firmante} ({firma.FechaUtc:dd/MM/yyyy})")
                                .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                }
            });
        }

        public static List<FirmaRegistrada> LeerFirmas(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<FirmaRegistrada>();

            try
            {
                return JsonSerializer.Deserialize<List<FirmaRegistrada>>(json) ?? new List<FirmaRegistrada>();
            }
            catch (JsonException)
            {
                return new List<FirmaRegistrada>();
            }
        }

        private static string Sanitizar(string valor)
        {
            var invalid = Path.GetInvalidFileNameChars().ToHashSet();
            return new string(valor.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }

    public sealed class FirmaRegistrada
    {
        public string Rol { get; set; } = string.Empty;
        public string Firmante { get; set; } = string.Empty;
        public DateTime FechaUtc { get; set; }
        public string Usuario { get; set; } = string.Empty;

        /// <summary>Firma manuscrita capturada en pantalla (PNG en base64). Opcional.</summary>
        public string? ImagenPng { get; set; }

        /// <summary>SHA-256 de la imagen, para detectar alteraciones.</summary>
        public string? HashImagen { get; set; }
    }
}

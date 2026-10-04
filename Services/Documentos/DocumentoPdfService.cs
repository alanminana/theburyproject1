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

            QuestPDF.Settings.License = LicenseType.Community;

            var pdf = Document.Create(container =>
            {
                foreach (var doc in documentos)
                {
                    var copias = Math.Max(1, doc.PlantillaDocumento?.Copias ?? 1);
                    for (var copia = 1; copia <= copias; copia++)
                    {
                        var etiquetaCopia = copias > 1 ? $"Copia {copia} de {copias}" : null;
                        container.Page(page => ComponerPagina(page, doc, etiquetaCopia));
                    }
                }
            });

            var nombre = documentos.Count == 1
                ? $"{Sanitizar(documentos[0].Numero)}.pdf"
                : $"documentos-{Sanitizar(documentos[0].Numero)}.pdf";

            return new DocumentoPdfArchivo { NombreArchivo = nombre, Contenido = pdf.GeneratePdf() };
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

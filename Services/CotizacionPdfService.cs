using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

public sealed class CotizacionPdfService : ICotizacionPdfService
{
    private static readonly string ColorNegro = "#111827";
    private static readonly string ColorGrisOscuro = "#4B5563";
    private static readonly string ColorGrisBorde = "#D1D5DB";
    private static readonly string ColorGrisFondo = "#F3F4F6";
    private static readonly string ColorVerde = "#059669";
    private static readonly string ColorAmbar = "#D97706";
    private static readonly string ColorRojo = "#DC2626";
    private static readonly string ColorGrisMedio = "#6B7280";

    public byte[] Generar(CotizacionResultado cotizacion)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.8f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                page.Header().Element(c => RenderHeader(c, cotizacion));
                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Spacing(12);
                    col.Item().Element(c => RenderSectionGrid(c, cotizacion));
                    col.Item().Element(c => RenderTablaProductos(c, cotizacion));
                    RenderAjustePago(col, cotizacion);
                    col.Item().Element(c => RenderTotales(c, cotizacion));
                    if (!string.IsNullOrWhiteSpace(cotizacion.Observaciones))
                        col.Item().Element(c => RenderObservaciones(c, cotizacion.Observaciones!));
                    col.Item().Element(RenderDisclaimer);
                });
                page.Footer().Element(RenderFooter);
            });
        }).GeneratePdf();
    }

    private static void RenderHeader(IContainer container, CotizacionResultado cotizacion)
    {
        container.BorderBottom(2).BorderColor(ColorNegro).PaddingBottom(12).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("The Bury Project").FontSize(20).Bold().FontColor(ColorNegro);
                col.Item().Text("Cotización de venta").FontSize(9).FontColor(ColorGrisOscuro);
                col.Item().PaddingTop(6).Text(EstadoTexto(cotizacion.Estado))
                    .FontSize(9).Bold().FontColor(EstadoColor(cotizacion.Estado));
            });

            row.ConstantItem(180).Border(2).BorderColor(ColorNegro).Column(col =>
            {
                col.Item()
                    .BorderBottom(2).BorderColor(ColorNegro)
                    .Padding(8)
                    .AlignCenter()
                    .Text("COTIZACIÓN").FontSize(14).Bold().FontColor(ColorNegro);

                col.Item().Padding(10).Column(inner =>
                {
                    inner.Spacing(3);
                    inner.Item().Row(r =>
                    {
                        r.ConstantItem(55).Text("Nro.").Bold().FontSize(9).FontColor(ColorGrisOscuro);
                        r.RelativeItem().Text(cotizacion.Numero).FontSize(9).Bold();
                    });
                    inner.Item().Row(r =>
                    {
                        r.ConstantItem(55).Text("Fecha").Bold().FontSize(9).FontColor(ColorGrisOscuro);
                        r.RelativeItem().Text(cotizacion.Fecha.ToLocalTime().ToString("dd/MM/yyyy")).FontSize(9);
                    });
                    if (cotizacion.FechaVencimiento.HasValue)
                    {
                        inner.Item().Row(r =>
                        {
                            r.ConstantItem(55).Text("Vence").Bold().FontSize(9).FontColor(ColorGrisOscuro);
                            r.RelativeItem().Text(cotizacion.FechaVencimiento.Value.ToLocalTime().ToString("dd/MM/yyyy")).FontSize(9);
                        });
                    }
                });
            });
        });
    }

    private static void RenderSectionGrid(IContainer container, CotizacionResultado cotizacion)
    {
        var cliente = cotizacion.ClienteNombre ?? cotizacion.NombreClienteLibre ?? "Cliente mostrador";

        container.Row(row =>
        {
            row.RelativeItem().Border(1).BorderColor(ColorGrisBorde).Padding(12).Column(col =>
            {
                col.Item().Text("CLIENTE").FontSize(9).Bold().FontColor(ColorNegro);
                col.Item().PaddingTop(6).Row(r =>
                {
                    r.ConstantItem(70).Text("Nombre").FontSize(9).FontColor(ColorGrisOscuro).Bold();
                    r.RelativeItem().Text(cliente).FontSize(9);
                });
                if (!string.IsNullOrWhiteSpace(cotizacion.TelefonoClienteLibre))
                {
                    col.Item().Row(r =>
                    {
                        r.ConstantItem(70).Text("Teléfono").FontSize(9).FontColor(ColorGrisOscuro).Bold();
                        r.RelativeItem().Text(cotizacion.TelefonoClienteLibre).FontSize(9);
                    });
                }
            });

            row.ConstantItem(10);

            row.RelativeItem().Border(1).BorderColor(ColorGrisBorde).Padding(12).Column(col =>
            {
                col.Item().Text("PAGO SELECCIONADO").FontSize(9).Bold().FontColor(ColorNegro);
                if (cotizacion.MedioPagoSeleccionado.HasValue)
                {
                    col.Item().PaddingTop(6).Row(r =>
                    {
                        r.ConstantItem(70).Text("Medio").FontSize(9).FontColor(ColorGrisOscuro).Bold();
                        r.RelativeItem().Text(MedioPagoTexto(cotizacion.MedioPagoSeleccionado.Value)).FontSize(9);
                    });
                    if (!string.IsNullOrWhiteSpace(cotizacion.PlanSeleccionado))
                    {
                        col.Item().Row(r =>
                        {
                            r.ConstantItem(70).Text("Plan").FontSize(9).FontColor(ColorGrisOscuro).Bold();
                            r.RelativeItem().Text(cotizacion.PlanSeleccionado).FontSize(9);
                        });
                    }
                    if (cotizacion.CantidadCuotasSeleccionada.HasValue)
                    {
                        col.Item().Row(r =>
                        {
                            r.ConstantItem(70).Text("Cuotas").FontSize(9).FontColor(ColorGrisOscuro).Bold();
                            r.RelativeItem().Text(cotizacion.CantidadCuotasSeleccionada.Value.ToString()).FontSize(9);
                        });
                    }
                    if (cotizacion.ValorCuotaSeleccionada.HasValue)
                    {
                        col.Item().Row(r =>
                        {
                            r.ConstantItem(70).Text("Valor cuota").FontSize(9).FontColor(ColorGrisOscuro).Bold();
                            r.RelativeItem().Text(cotizacion.ValorCuotaSeleccionada.Value.ToString("C2")).FontSize(9);
                        });
                    }
                }
                else
                {
                    col.Item().PaddingTop(6).Text("Sin opción de pago seleccionada.").FontSize(9).FontColor(ColorGrisOscuro).Italic();
                }
            });
        });
    }

    private static void RenderTablaProductos(IContainer container, CotizacionResultado cotizacion)
    {
        var tieneDescuento = cotizacion.Detalles.Any(d =>
            d.DescuentoPorcentajeSnapshot.HasValue || d.DescuentoImporteSnapshot.HasValue);

        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(3);
                cols.RelativeColumn(1.5f);
                cols.ConstantColumn(55);
                cols.ConstantColumn(70);
                if (tieneDescuento) cols.ConstantColumn(65);
                cols.ConstantColumn(70);
            });

            table.Header(header =>
            {
                void Th(string texto, bool right = false)
                {
                    header.Cell()
                        .Background(ColorGrisFondo)
                        .Border(1).BorderColor(ColorGrisBorde)
                        .Padding(6)
                        .AlignLeft()
                        .Text(texto).FontSize(8).Bold().FontColor(ColorNegro);
                }

                void ThRight(string texto)
                {
                    header.Cell()
                        .Background(ColorGrisFondo)
                        .Border(1).BorderColor(ColorGrisBorde)
                        .Padding(6)
                        .AlignRight()
                        .Text(texto).FontSize(8).Bold().FontColor(ColorNegro);
                }

                Th("PRODUCTO");
                Th("CÓDIGO");
                ThRight("CANTIDAD");
                ThRight("PRECIO UNIT.");
                if (tieneDescuento) ThRight("DESCUENTO");
                ThRight("SUBTOTAL");
            });

            foreach (var item in cotizacion.Detalles)
            {
                void Td(IContainer c, string texto) =>
                    c.Border(1).BorderColor(ColorGrisBorde).Padding(6).Text(texto).FontSize(9);

                void TdRight(IContainer c, string texto) =>
                    c.Border(1).BorderColor(ColorGrisBorde).Padding(6).AlignRight().Text(texto).FontSize(9);

                table.Cell().Element(c => Td(c, item.NombreProductoSnapshot));
                table.Cell().Element(c => Td(c, item.CodigoProductoSnapshot));
                table.Cell().Element(c => TdRight(c, item.Cantidad.ToString("G29")));
                table.Cell().Element(c => TdRight(c, item.PrecioUnitarioSnapshot.ToString("C2")));

                if (tieneDescuento)
                {
                    table.Cell().Element(c =>
                    {
                        var texto = item.DescuentoPorcentajeSnapshot.HasValue
                            ? $"{item.DescuentoPorcentajeSnapshot.Value:0.##}%"
                            : item.DescuentoImporteSnapshot.HasValue
                                ? item.DescuentoImporteSnapshot.Value.ToString("C2")
                                : "-";
                        TdRight(c, texto);
                    });
                }

                table.Cell().Element(c => TdRight(c, item.Subtotal.ToString("C2")));
            }
        });
    }

    private static void RenderAjustePago(ColumnDescriptor col, CotizacionResultado cotizacion)
    {
        var seleccionada = cotizacion.OpcionesPago.FirstOrDefault(o => o.Seleccionado);
        if (seleccionada is null) return;
        if (seleccionada.RecargoPorcentaje == 0 && seleccionada.DescuentoPorcentaje == 0 && seleccionada.InteresPorcentaje == 0)
            return;

        col.Item().Border(1).BorderColor(ColorGrisBorde).Padding(12).Column(inner =>
        {
            inner.Item().Text("AJUSTE POR PLAN DE PAGO").FontSize(9).Bold().FontColor(ColorNegro);
            inner.Item().PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3);
                    if (seleccionada.RecargoPorcentaje != 0) cols.ConstantColumn(70);
                    if (seleccionada.DescuentoPorcentaje != 0) cols.ConstantColumn(70);
                    if (seleccionada.InteresPorcentaje != 0) cols.ConstantColumn(70);
                    cols.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    header.Cell().Background(ColorGrisFondo).Border(1).BorderColor(ColorGrisBorde).Padding(5)
                        .Text("MEDIO / PLAN").FontSize(8).Bold();
                    if (seleccionada.RecargoPorcentaje != 0)
                        header.Cell().Background(ColorGrisFondo).Border(1).BorderColor(ColorGrisBorde).Padding(5).AlignRight()
                            .Text("RECARGO").FontSize(8).Bold();
                    if (seleccionada.DescuentoPorcentaje != 0)
                        header.Cell().Background(ColorGrisFondo).Border(1).BorderColor(ColorGrisBorde).Padding(5).AlignRight()
                            .Text("DESCUENTO").FontSize(8).Bold();
                    if (seleccionada.InteresPorcentaje != 0)
                        header.Cell().Background(ColorGrisFondo).Border(1).BorderColor(ColorGrisBorde).Padding(5).AlignRight()
                            .Text("INTERÉS").FontSize(8).Bold();
                    header.Cell().Background(ColorGrisFondo).Border(1).BorderColor(ColorGrisBorde).Padding(5).AlignRight()
                        .Text("TOTAL").FontSize(8).Bold();
                });

                var descripcion = string.IsNullOrWhiteSpace(seleccionada.Plan)
                    ? MedioPagoTexto(seleccionada.MedioPago)
                    : $"{MedioPagoTexto(seleccionada.MedioPago)} / {seleccionada.Plan}";

                table.Cell().Border(1).BorderColor(ColorGrisBorde).Padding(5).Text(descripcion).FontSize(9);
                if (seleccionada.RecargoPorcentaje != 0)
                    table.Cell().Border(1).BorderColor(ColorGrisBorde).Padding(5).AlignRight()
                        .Text($"{seleccionada.RecargoPorcentaje:0.##}%").FontSize(9);
                if (seleccionada.DescuentoPorcentaje != 0)
                    table.Cell().Border(1).BorderColor(ColorGrisBorde).Padding(5).AlignRight()
                        .Text($"{seleccionada.DescuentoPorcentaje:0.##}%").FontSize(9);
                if (seleccionada.InteresPorcentaje != 0)
                    table.Cell().Border(1).BorderColor(ColorGrisBorde).Padding(5).AlignRight()
                        .Text($"{seleccionada.InteresPorcentaje:0.##}%").FontSize(9);
                table.Cell().Border(1).BorderColor(ColorGrisBorde).Padding(5).AlignRight()
                    .Text(seleccionada.Total.ToString("C2")).FontSize(9);
            });
        });
    }

    private static void RenderTotales(IContainer container, CotizacionResultado cotizacion)
    {
        var totalMostrar = cotizacion.TotalSeleccionado ?? cotizacion.TotalBase;

        container.AlignRight().Border(1).BorderColor(ColorNegro).Width(220).Column(col =>
        {
            FilaTotales(col, "Subtotal", cotizacion.Subtotal.ToString("C2"), false);

            if (cotizacion.DescuentoTotal != 0)
                FilaTotales(col, "Descuento total", cotizacion.DescuentoTotal.ToString("C2"), false);

            FilaTotales(col, "Total base", cotizacion.TotalBase.ToString("C2"), false);

            if (cotizacion.TotalSeleccionado.HasValue && cotizacion.TotalSeleccionado != cotizacion.TotalBase)
                FilaTotales(col, "Total c/ plan", cotizacion.TotalSeleccionado.Value.ToString("C2"), false);

            FilaTotales(col, "TOTAL", totalMostrar.ToString("C2"), true);
        });
    }

    private static void FilaTotales(ColumnDescriptor col, string etiqueta, string valor, bool esTotal)
    {
        col.Item()
            .Background(esTotal ? ColorNegro : Colors.White)
            .BorderBottom(esTotal ? 0 : 1).BorderColor(ColorGrisBorde)
            .Padding(8)
            .Row(row =>
            {
                if (esTotal)
                {
                    row.RelativeItem().Text(etiqueta).FontSize(12).FontColor(Colors.White).Bold();
                    row.AutoItem().Text(valor).FontSize(12).FontColor(Colors.White).Bold();
                }
                else
                {
                    row.RelativeItem().Text(etiqueta).FontSize(10).FontColor(ColorNegro);
                    row.AutoItem().Text(valor).FontSize(10).FontColor(ColorNegro);
                }
            });
    }

    private static void RenderObservaciones(IContainer container, string observaciones)
    {
        container.BorderTop(1).BorderColor(ColorGrisBorde).PaddingTop(10).Column(col =>
        {
            col.Item().Text(t =>
            {
                t.Span("Observaciones: ").Bold().FontSize(9).FontColor(ColorNegro);
                t.Span(observaciones).FontSize(9).FontColor(ColorGrisOscuro);
            });
        });
    }

    private static void RenderDisclaimer(IContainer container)
    {
        container.Border(1).BorderColor(ColorGrisBorde).Padding(10).AlignCenter()
            .Text("Cotización sujeta a disponibilidad y vigencia de precios. Documento generado automáticamente.")
            .FontSize(8).FontColor(ColorGrisMedio).Italic();
    }

    private static void RenderFooter(IContainer container)
    {
        container.AlignRight().Text(t =>
        {
            t.Span("Página ").FontSize(8).FontColor(ColorGrisOscuro);
            t.CurrentPageNumber().FontSize(8).FontColor(ColorGrisOscuro);
            t.Span(" de ").FontSize(8).FontColor(ColorGrisOscuro);
            t.TotalPages().FontSize(8).FontColor(ColorGrisOscuro);
        });
    }

    private static string EstadoTexto(EstadoCotizacion estado) => estado switch
    {
        EstadoCotizacion.Emitida => "Emitida",
        EstadoCotizacion.Vencida => "Vencida",
        EstadoCotizacion.ConvertidaAVenta => "Convertida a venta",
        EstadoCotizacion.Cancelada => "Cancelada",
        _ => estado.ToString()
    };

    private static string EstadoColor(EstadoCotizacion estado) => estado switch
    {
        EstadoCotizacion.Emitida => ColorAmbar,
        EstadoCotizacion.Vencida => ColorRojo,
        EstadoCotizacion.ConvertidaAVenta => ColorVerde,
        EstadoCotizacion.Cancelada => ColorGrisMedio,
        _ => ColorNegro
    };

    private static string MedioPagoTexto(CotizacionMedioPagoTipo tipo) => tipo switch
    {
        CotizacionMedioPagoTipo.Efectivo => "Efectivo",
        CotizacionMedioPagoTipo.Transferencia => "Transferencia",
        CotizacionMedioPagoTipo.TarjetaCredito => "Tarjeta de crédito",
        CotizacionMedioPagoTipo.TarjetaDebito => "Tarjeta de débito",
        CotizacionMedioPagoTipo.MercadoPago => "Mercado Pago",
        CotizacionMedioPagoTipo.CreditoPersonal => "Crédito personal",
        _ => tipo.ToString()
    };
}

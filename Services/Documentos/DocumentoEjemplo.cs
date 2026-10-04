namespace TheBuryProject.Services.Documentos
{
    /// <summary>Contexto con datos de ejemplo para previsualizar plantillas sin una operación real.</summary>
    public static class DocumentoEjemplo
    {
        public static DocumentoContexto Construir(AnclaDocumento ancla)
        {
            var ctx = new DocumentoContexto { Ancla = ancla, ClaveAncla = "ejemplo:0" };

            foreach (var campo in CatalogoDocumento.ParaAncla(ancla))
            {
                ctx.Set(campo.Ruta, campo.Tipo switch
                {
                    TipoCampoDocumento.Numero => campo.Ruta.Contains("cantidad", StringComparison.OrdinalIgnoreCase) || campo.Ruta.EndsWith("numero", StringComparison.OrdinalIgnoreCase)
                        ? 6
                        : (object)12345.67m,
                    TipoCampoDocumento.Fecha => DateTime.Today,
                    TipoCampoDocumento.Booleano => true,
                    _ => campo.Valores?.FirstOrDefault() ?? $"[{campo.Etiqueta}]"
                });
            }

            ctx.Set("pago.importeEnLetras", NumeroALetras.Importe(12345.67m));
            ctx.Set("documento.numero", "XXX-000000-000001");

            var productos = Enumerable.Range(1, 3).Select(i => DocumentoContextoBuilder.ItemProducto(
                $"P{i:000}", "Marca", $"Producto de ejemplo {i}", i, 1000m * i, 1000m * i * i)).ToList();
            DocumentoContextoBuilder.AplicarProductos(ctx, productos);

            var cuotas = Enumerable.Range(1, 6).Select(i => DocumentoContextoBuilder.ItemCuota(
                i, 5000m, 4000m, 1000m, DateTime.Today.AddMonths(i), "Pendiente")).ToList();
            DocumentoContextoBuilder.AplicarCuotas(ctx, cuotas);

            return ctx;
        }
    }
}

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Filtros para consulta del catálogo unificado
    /// </summary>
    public class FiltrosCatalogo
    {
        /// <summary>
        /// Búsqueda por texto en código, nombre o descripción
        /// </summary>
        public string? TextoBusqueda { get; set; }

        /// <summary>
        /// Filtrar por categoría específica
        /// </summary>
        public int? CategoriaId { get; set; }

        /// <summary>
        /// Filtrar por marca específica
        /// </summary>
        public int? MarcaId { get; set; }

        /// <summary>
        /// Mostrar solo productos con stock bajo
        /// </summary>
        public bool SoloStockBajo { get; set; }

        /// <summary>
        /// Mostrar solo productos activos
        /// </summary>
        public bool SoloActivos { get; set; }

        /// <summary>
        /// Campo por el cual ordenar (nombre, codigo, precio, stock)
        /// </summary>
        public string? OrdenarPor { get; set; }

        /// <summary>
        /// Dirección del ordenamiento (asc/desc)
        /// </summary>
        public string? DireccionOrden { get; set; } = "asc";

        /// <summary>
        /// ID de la lista de precios a usar (null = predeterminada)
        /// </summary>
        public int? ListaPrecioId { get; set; }
    }

    /// <summary>
    /// Fila individual del catálogo con producto y precio según lista
    /// </summary>
    public class FilaCatalogo
    {
        // ============================================
        // PRODUCTO
        // ============================================

        /// <summary>
        /// ID del producto
        /// </summary>
        public int ProductoId { get; set; }

        /// <summary>
        /// Código único del producto
        /// </summary>
        public string Codigo { get; set; } = string.Empty;

        /// <summary>
        /// Nombre del producto
        /// </summary>
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Descripción del producto
        /// </summary>
        public string? Descripcion { get; set; }

        // ============================================
        // CATEGORÍA Y MARCA
        // ============================================

        /// <summary>
        /// ID de la categoría
        /// </summary>
        public int? CategoriaId { get; set; }

        /// <summary>
        /// Nombre de la categoría
        /// </summary>
        public string? CategoriaNombre { get; set; }

        /// <summary>
        /// ID de la marca
        /// </summary>
        public int? MarcaId { get; set; }

        /// <summary>
        /// Nombre de la marca
        /// </summary>
        public string? MarcaNombre { get; set; }

        // ============================================
        // PRECIOS
        // ============================================

        /// <summary>
        /// Costo/precio de costo del producto
        /// </summary>
        public decimal Costo { get; set; }

        /// <summary>
        /// Precio de venta actual (según lista seleccionada o precio base)
        /// </summary>
        public decimal PrecioActual { get; set; }

        /// <summary>
        /// Precio base del producto (sin aplicar lista)
        /// </summary>
        public decimal PrecioBase { get; set; }

        /// <summary>
        /// Indica si el precio viene de una lista específica
        /// </summary>
        public bool TienePrecioLista { get; set; }

        /// <summary>
        /// Margen de ganancia en porcentaje
        /// </summary>
        public decimal MargenPorcentaje { get; set; }

        // ============================================
        // STOCK
        // ============================================

        /// <summary>
        /// Cantidad actual en stock
        /// </summary>
        public decimal StockActual { get; set; }

        /// <summary>
        /// Stock mínimo configurado
        /// </summary>
        public decimal StockMinimo { get; set; }

        /// <summary>
        /// Estado del stock (Normal, Stock Bajo, Sin Stock)
        /// </summary>
        public string EstadoStock { get; set; } = "Normal";

        // ============================================
        // FLAGS / INDICADORES
        // ============================================

        /// <summary>
        /// Producto activo para venta
        /// </summary>
        public bool Activo { get; set; }

        /// <summary>
        /// Producto sin precio asignado en la lista
        /// </summary>
        public bool SinPrecio => PrecioActual <= 0;

        /// <summary>
        /// Producto con stock crítico (bajo o sin stock)
        /// </summary>
        public bool StockCritico => StockActual <= StockMinimo;

        /// <summary>
        /// Producto inactivo
        /// </summary>
        public bool Inactivo => !Activo;

        // ============================================
        // ÚLTIMO CAMBIO DIRECTO
        // ============================================

        /// <summary>
        /// ID del último evento de cambio directo que afectó al producto
        /// </summary>
        public int? UltimoCambioEventoId { get; set; }

        /// <summary>
        /// Fecha del último cambio directo
        /// </summary>
        public DateTime? UltimoCambioFecha { get; set; }

        /// <summary>
        /// Usuario que realizó el último cambio directo
        /// </summary>
        public string? UltimoCambioUsuario { get; set; }

        /// <summary>
        /// Valor porcentual del último cambio directo
        /// </summary>
        public decimal? UltimoCambioPorcentaje { get; set; }

        /// <summary>
        /// Indica si el último evento fue revertido
        /// </summary>
        public bool UltimoCambioRevertido { get; set; }

        /// <summary>
        /// Indica si el último evento es una reversión
        /// </summary>
        public bool UltimoCambioEsReversion { get; set; }

        /// <summary>
        /// Determina si se puede mostrar botón de revertir en la fila
        /// </summary>
        public bool UltimoCambioPuedeRevertir =>
            UltimoCambioEventoId.HasValue &&
            !UltimoCambioRevertido &&
            !UltimoCambioEsReversion;
    }

    /// <summary>
    /// Resumen del último cambio directo por producto
    /// </summary>
    public class UltimoCambioProductoResumen
    {
        public int EventoId { get; set; }
        public int ProductoId { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public decimal ValorPorcentaje { get; set; }
        public bool Revertido { get; set; }
        public bool EsReversion { get; set; }
    }

    /// <summary>
    /// Resultado completo de la consulta al catálogo
    /// </summary>
    public class ResultadoCatalogo
    {
        // ============================================
        // FILAS DE PRODUCTOS
        // ============================================

        /// <summary>
        /// Lista de productos con precios
        /// </summary>
        public IEnumerable<FilaCatalogo> Filas { get; set; } = new List<FilaCatalogo>();

        /// <summary>
        /// Total de resultados
        /// </summary>
        public int TotalResultados { get; set; }

        // ============================================
        // DATOS DE LA LISTA DE PRECIOS
        // ============================================

        /// <summary>
        /// ID de la lista de precios usada
        /// </summary>
        public int? ListaPrecioId { get; set; }

        /// <summary>
        /// Nombre de la lista de precios usada
        /// </summary>
        public string ListaPrecioNombre { get; set; } = "Predeterminada";

        // ============================================
        // OPCIONES PARA DROPDOWNS
        // ============================================

        /// <summary>
        /// Lista de categorías disponibles (Id, Nombre)
        /// </summary>
        public IEnumerable<OpcionDropdown> Categorias { get; set; } = new List<OpcionDropdown>();

        /// <summary>
        /// Lista de marcas disponibles (Id, Nombre)
        /// </summary>
        public IEnumerable<OpcionDropdown> Marcas { get; set; } = new List<OpcionDropdown>();

        /// <summary>
        /// Listas de precios disponibles (Id, Nombre)
        /// </summary>
        public IEnumerable<OpcionDropdown> ListasPrecios { get; set; } = new List<OpcionDropdown>();

        // ============================================
        // MÉTRICAS
        // ============================================

        /// <summary>
        /// Total de categorías en el sistema
        /// </summary>
        public int TotalCategorias { get; set; }

        /// <summary>
        /// Total de marcas en el sistema
        /// </summary>
        public int TotalMarcas { get; set; }

        /// <summary>
        /// Productos activos en el resultado
        /// </summary>
        public int ProductosActivos => Filas.Count(f => f.Activo);

        /// <summary>
        /// Productos con stock crítico en el resultado
        /// </summary>
        public int ProductosStockCritico => Filas.Count(f => f.StockCritico);

        /// <summary>
        /// Productos sin precio asignado
        /// </summary>
        public int ProductosSinPrecio => Filas.Count(f => f.SinPrecio);
    }

    /// <summary>
    /// Opción genérica para dropdowns
    /// </summary>
    public class OpcionDropdown
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    // ============================================
    // DTOs PARA CAMBIOS MASIVOS DE PRECIOS
    // ============================================

    /// <summary>
    /// Solicitud de simulación de cambio masivo de precios
    /// </summary>
    public class SolicitudSimulacionPrecios
    {
        /// <summary>
        /// Nombre descriptivo del cambio
        /// </summary>
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Tipo de cambio: Porcentaje, Absoluto, NuevoPrecio
        /// </summary>
        public string TipoCambio { get; set; } = "Porcentaje";

        /// <summary>
        /// Valor del cambio (% o monto absoluto)
        /// </summary>
        public decimal Valor { get; set; }

        /// <summary>
        /// IDs de listas de precios afectadas
        /// </summary>
        public List<int> ListasIds { get; set; } = new();

        /// <summary>
        /// IDs de categorías afectadas (vacío = todas)
        /// </summary>
        public List<int> CategoriasIds { get; set; } = new();

        /// <summary>
        /// IDs de marcas afectadas (vacío = todas)
        /// </summary>
        public List<int> MarcasIds { get; set; } = new();

        /// <summary>
        /// IDs específicos de productos (vacío = usar categorías/marcas)
        /// </summary>
        public List<int> ProductosIds { get; set; } = new();
    }

    /// <summary>
    /// Fila de previsualización de cambio de precio
    /// </summary>
    public class FilaSimulacionPrecio
    {
        /// <summary>
        /// ID del producto
        /// </summary>
        public int ProductoId { get; set; }

        /// <summary>
        /// Código del producto
        /// </summary>
        public string Codigo { get; set; } = string.Empty;

        /// <summary>
        /// Nombre del producto
        /// </summary>
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Nombre de la categoría
        /// </summary>
        public string? Categoria { get; set; }

        /// <summary>
        /// Nombre de la marca
        /// </summary>
        public string? Marca { get; set; }

        /// <summary>
        /// ID de la lista de precios
        /// </summary>
        public int ListaId { get; set; }

        /// <summary>
        /// Nombre de la lista de precios
        /// </summary>
        public string ListaNombre { get; set; } = string.Empty;

        /// <summary>
        /// Precio actual antes del cambio
        /// </summary>
        public decimal PrecioActual { get; set; }

        /// <summary>
        /// Precio nuevo después del cambio
        /// </summary>
        public decimal PrecioNuevo { get; set; }

        /// <summary>
        /// Diferencia en valor absoluto
        /// </summary>
        public decimal Diferencia => PrecioNuevo - PrecioActual;

        /// <summary>
        /// Diferencia en porcentaje
        /// </summary>
        public decimal DiferenciaPorcentaje => PrecioActual > 0
            ? Math.Round((PrecioNuevo - PrecioActual) / PrecioActual * 100, 2)
            : 0;

        /// <summary>
        /// Indica si es aumento (true) o descuento (false)
        /// </summary>
        public bool EsAumento => Diferencia > 0;
    }

    /// <summary>
    /// Resultado de la simulación de cambio masivo
    /// </summary>
    public class ResultadoSimulacionPrecios
    {
        /// <summary>
        /// ID del batch generado (para aplicar después)
        /// </summary>
        public int BatchId { get; set; }

        /// <summary>
        /// Nombre del cambio
        /// </summary>
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Tipo de cambio aplicado
        /// </summary>
        public string TipoCambio { get; set; } = string.Empty;

        /// <summary>
        /// Valor del cambio
        /// </summary>
        public decimal Valor { get; set; }

        /// <summary>
        /// Filas con el preview de cada cambio
        /// </summary>
        public List<FilaSimulacionPrecio> Filas { get; set; } = new();

        /// <summary>
        /// Total de productos afectados
        /// </summary>
        public int TotalProductos => Filas.Count;

        /// <summary>
        /// Productos con aumento
        /// </summary>
        public int ProductosConAumento => Filas.Count(f => f.EsAumento);

        /// <summary>
        /// Productos con descuento
        /// </summary>
        public int ProductosConDescuento => Filas.Count(f => !f.EsAumento && f.Diferencia != 0);

        /// <summary>
        /// Porcentaje promedio de cambio
        /// </summary>
        public decimal PorcentajePromedio => Filas.Any()
            ? Math.Round(Filas.Average(f => f.DiferenciaPorcentaje), 2)
            : 0;

        /// <summary>
        /// Indica si requiere autorización
        /// </summary>
        public bool RequiereAutorizacion { get; set; }

        /// <summary>
        /// RowVersion para aplicar (base64)
        /// </summary>
        public string RowVersion { get; set; } = string.Empty;
    }

    /// <summary>
    /// Solicitud para aplicar un cambio previamente simulado
    /// </summary>
    public class SolicitudAplicarPrecios
    {
        /// <summary>
        /// ID del batch a aplicar
        /// </summary>
        public int BatchId { get; set; }

        /// <summary>
        /// RowVersion para concurrencia (base64)
        /// </summary>
        public string RowVersion { get; set; } = string.Empty;

        /// <summary>
        /// Fecha de vigencia (null = ahora)
        /// </summary>
        public DateTime? FechaVigencia { get; set; }

        /// <summary>
        /// Notas adicionales
        /// </summary>
        public string? Notas { get; set; }
    }

    /// <summary>
    /// Resultado de la aplicación de cambio de precios
    /// </summary>
    public class ResultadoAplicacionPrecios
    {
        /// <summary>
        /// Indica si se aplicó correctamente
        /// </summary>
        public bool Exitoso { get; set; }

        /// <summary>
        /// Mensaje descriptivo
        /// </summary>
        public string Mensaje { get; set; } = string.Empty;

        /// <summary>
        /// ID del batch aplicado
        /// </summary>
        public int BatchId { get; set; }

        /// <summary>
        /// Total de productos actualizados
        /// </summary>
        public int ProductosActualizados { get; set; }

        /// <summary>
        /// Fecha en que se aplicó
        /// </summary>
        public DateTime FechaAplicacion { get; set; }

        /// <summary>
        /// ID del evento de cambio directo (si aplica)
        /// </summary>
        public int? CambioPrecioEventoId { get; set; }
    }
}

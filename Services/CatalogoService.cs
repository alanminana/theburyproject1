using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio unificado para consulta del catálogo de productos con precios.
    /// Consolida la lógica previamente dispersa en controladores.
    /// </summary>
    public class CatalogoService : ICatalogoService
    {
        #region Constructor y dependencias

        private readonly ICatalogLookupService _catalogLookupService;
        private readonly IProductoService _productoService;
        private readonly IPrecioService _precioService;
        private readonly ILogger<CatalogoService> _logger;
        private readonly ICurrentUserService _currentUserService;

        public CatalogoService(
            ICatalogLookupService catalogLookupService,
            IProductoService productoService,
            IPrecioService precioService,
            ILogger<CatalogoService> logger,
            ICurrentUserService currentUserService)
        {
            _catalogLookupService = catalogLookupService;
            _productoService = productoService;
            _precioService = precioService;
            _logger = logger;
            _currentUserService = currentUserService;
        }

        #endregion

        #region Consultas de catálogo

        /// <inheritdoc />
        public async Task<ResultadoCatalogo> ObtenerCatalogoAsync(FiltrosCatalogo filtros)
        {
            // 1. Obtener catálogos base (categorías y marcas)
            var (categorias, marcas) = await _catalogLookupService.GetCategoriasYMarcasAsync();

            // 2. Obtener listas de precios
            var listasPrecio = await _precioService.GetAllListasAsync(soloActivas: true);
            var listaPredeterminada = await _precioService.GetListaPredeterminadaAsync();

            // 3. Determinar lista de precios a usar
            var listaActualId = filtros.ListaPrecioId ?? listaPredeterminada?.Id;
            var listaActual = listasPrecio.FirstOrDefault(l => l.Id == listaActualId) ?? listaPredeterminada;

            // 4. Buscar productos con filtros
            var productos = await _productoService.SearchAsync(
                filtros.TextoBusqueda,
                filtros.CategoriaId,
                filtros.MarcaId,
                filtros.SoloStockBajo,
                filtros.SoloActivos,
                filtros.OrdenarPor,
                filtros.DireccionOrden
            );

            // 5. Obtener precios de todos los productos en batch si es posible
            // (optimización futura: usar GetPreciosProductosAsync batch)
            var filas = new List<FilaCatalogo>();

            foreach (var producto in productos)
            {
                var fila = await CrearFilaCatalogoAsync(producto, listaActual?.Id);
                filas.Add(fila);
            }

            var ultimosCambios = await _precioService.GetUltimoCambioPorProductosAsync(filas.Select(f => f.ProductoId));
            foreach (var fila in filas)
            {
                if (!ultimosCambios.TryGetValue(fila.ProductoId, out var cambio))
                {
                    continue;
                }

                fila.UltimoCambioEventoId = cambio.EventoId;
                fila.UltimoCambioFecha = cambio.Fecha;
                fila.UltimoCambioUsuario = cambio.Usuario;
                fila.UltimoCambioPorcentaje = cambio.ValorPorcentaje;
                fila.UltimoCambioRevertido = cambio.Revertido;
                fila.UltimoCambioEsReversion = cambio.EsReversion;
            }

            // 6. Construir resultado
            var resultado = new ResultadoCatalogo
            {
                // Filas de productos
                Filas = filas,
                TotalResultados = filas.Count,

                // Datos de lista de precios
                ListaPrecioId = listaActualId,
                ListaPrecioNombre = listaActual?.Nombre ?? "Predeterminada",

                // Opciones para dropdowns
                Categorias = categorias
                    .OrderBy(c => c.Nombre)
                    .Select(c => new OpcionDropdown { Id = c.Id, Nombre = c.Nombre }),
                Marcas = marcas
                    .OrderBy(m => m.Nombre)
                    .Select(m => new OpcionDropdown { Id = m.Id, Nombre = m.Nombre }),
                ListasPrecios = listasPrecio
                    .Select(l => new OpcionDropdown { Id = l.Id, Nombre = l.Nombre }),

                // Métricas
                TotalCategorias = categorias.Count(),
                TotalMarcas = marcas.Count()
            };

            return resultado;
        }

        /// <inheritdoc />
        public async Task<FilaCatalogo?> ObtenerFilaAsync(int productoId, int? listaPrecioId = null)
        {
            var producto = await _productoService.GetByIdAsync(productoId);
            if (producto == null)
                return null;

            // Si no se especifica lista, usar la predeterminada
            if (listaPrecioId == null)
            {
                var listaPredeterminada = await _precioService.GetListaPredeterminadaAsync();
                listaPrecioId = listaPredeterminada?.Id;
            }

            return await CrearFilaCatalogoAsync(producto, listaPrecioId);
        }

        /// <summary>
        /// Crea una fila del catálogo a partir de un producto y su precio en la lista
        /// </summary>
        private async Task<FilaCatalogo> CrearFilaCatalogoAsync(
            Producto producto,
            int? listaPrecioId)
        {
            // Obtener precio de la lista si existe
            ProductoPrecioLista? precioLista = null;
            if (listaPrecioId.HasValue)
            {
                precioLista = await _precioService.GetPrecioVigenteAsync(producto.Id, listaPrecioId.Value);
            }

            // Calcular precio actual (lista > base)
            var precioActual = precioLista?.Precio ?? producto.PrecioVenta;

            // Calcular margen
            var margen = producto.PrecioCompra > 0
                ? Math.Round((precioActual - producto.PrecioCompra) / producto.PrecioCompra * 100, 2)
                : 0;

            // Determinar estado de stock
            var estadoStock = producto.StockActual <= 0 ? "Sin Stock"
                : producto.StockActual <= producto.StockMinimo ? "Stock Bajo"
                : "Normal";

            return new FilaCatalogo
            {
                // Producto
                ProductoId = producto.Id,
                Codigo = producto.Codigo,
                Nombre = producto.Nombre,
                Descripcion = producto.Descripcion,

                // Categoría y Marca
                CategoriaId = producto.CategoriaId,
                CategoriaNombre = producto.Categoria?.Nombre,
                MarcaId = producto.MarcaId,
                MarcaNombre = producto.Marca?.Nombre,

                // Precios
                Costo = producto.PrecioCompra,
                PrecioActual = precioActual,
                PrecioBase = producto.PrecioVenta,
                TienePrecioLista = precioLista != null,
                MargenPorcentaje = margen,

                // Stock
                StockActual = producto.StockActual,
                StockMinimo = producto.StockMinimo,
                EstadoStock = estadoStock,

                // Flags
                Activo = producto.Activo
            };
        }

        // ──────────────────────────────────────────────────────────────
        #endregion

        #region Cambios de precios

        /// <inheritdoc />
        public async Task<ResultadoSimulacionPrecios> SimularCambioPreciosAsync(SolicitudSimulacionPrecios solicitud)
        {
            _logger.LogInformation(
                "Simulando cambio de precios: {Nombre}, Tipo: {TipoCambio}, Valor: {Valor}",
                solicitud.Nombre, solicitud.TipoCambio, solicitud.Valor);

            // Determinar tipo de aplicación según el valor (positivo = aumento, negativo = disminución)
            var tipoAplicacion = solicitud.Valor >= 0
                ? TipoAplicacion.Aumento
                : TipoAplicacion.Disminucion;

            var valorAbs = Math.Abs(solicitud.Valor);

            // Parsear TipoCambio del DTO al enum
            var tipoCambio = ParseTipoCambio(solicitud.TipoCambio);

            // Usar el servicio de precios para simular el cambio masivo
            var batch = await _precioService.SimularCambioMasivoAsync(
                nombre: solicitud.Nombre,
                tipoCambio: tipoCambio,
                tipoAplicacion: tipoAplicacion,
                valorCambio: valorAbs,
                listasIds: solicitud.ListasIds?.ToList() ?? new List<int>(),
                categoriaIds: solicitud.CategoriasIds?.ToList(),
                marcaIds: solicitud.MarcasIds?.ToList(),
                productoIds: solicitud.ProductosIds?.ToList()
            );

            // Obtener los items simulados
            var items = await _precioService.GetItemsSimulacionAsync(batch.Id, skip: 0, take: 500);

            // Verificar si requiere autorización
            var requiereAutorizacion = await _precioService.RequiereAutorizacionAsync(batch.Id);

            // Construir filas de simulación (propiedades calculadas se deducen automáticamente)
            var filas = items.Select(item => new FilaSimulacionPrecio
            {
                ProductoId = item.ProductoId,
                Codigo = item.Producto?.Codigo ?? "",
                Nombre = item.Producto?.Nombre ?? $"Producto {item.ProductoId}",
                Categoria = item.Producto?.Categoria?.Nombre ?? "",
                Marca = item.Producto?.Marca?.Nombre ?? "",
                ListaId = item.ListaId,
                ListaNombre = item.Lista?.Nombre ?? $"Lista {item.ListaId}",
                PrecioActual = item.PrecioAnterior,
                PrecioNuevo = item.PrecioNuevo
                // Diferencia, DiferenciaPorcentaje, EsAumento son propiedades calculadas
            }).ToList();

            return new ResultadoSimulacionPrecios
            {
                BatchId = batch.Id,
                Nombre = batch.Nombre,
                TipoCambio = solicitud.TipoCambio,
                Valor = solicitud.Valor,
                Filas = filas,
                // TotalProductos, ProductosConAumento, ProductosConDescuento, PorcentajePromedio son calculadas
                RequiereAutorizacion = requiereAutorizacion,
                RowVersion = Convert.ToBase64String(batch.RowVersion)
            };
        }

        /// <inheritdoc />
        public async Task<ResultadoAplicacionPrecios> AplicarCambioPreciosAsync(SolicitudAplicarPrecios solicitud)
        {
            try
            {
                _logger.LogInformation(
                    "Aplicando cambio de precios: BatchId={BatchId}",
                    solicitud.BatchId);

                var usuario = _currentUserService.GetUsername();

                // Aprobar el batch primero si no lo está
                var batch = await _precioService.GetSimulacionAsync(solicitud.BatchId);
                if (batch == null)
                {
                    return new ResultadoAplicacionPrecios
                    {
                        Exitoso = false,
                        Mensaje = "No se encontró la simulación especificada"
                    };
                }

                // Validar concurrencia con RowVersion
                var rowVersionBytes = Convert.FromBase64String(solicitud.RowVersion);
                if (!batch.RowVersion.SequenceEqual(rowVersionBytes))
                {
                    return new ResultadoAplicacionPrecios
                    {
                        Exitoso = false,
                        Mensaje = "Los datos han sido modificados por otro usuario. Por favor, vuelva a simular."
                    };
                }

                // Si está en estado Simulado, aprobar primero
                if (batch.Estado == EstadoBatch.Simulado)
                {
                    batch = await _precioService.AprobarBatchAsync(
                        batch.Id,
                        usuario,
                        batch.RowVersion,
                        solicitud.Notas
                    );
                }

                // Aplicar el batch
                var batchAplicado = await _precioService.AplicarBatchAsync(
                    batch.Id,
                    usuario,
                    batch.RowVersion,
                    solicitud.FechaVigencia
                );

                _logger.LogInformation(
                    "Cambio de precios aplicado exitosamente: BatchId={BatchId}, Productos={CantidadProductos}",
                    batchAplicado.Id, batchAplicado.CantidadProductos);

                return new ResultadoAplicacionPrecios
                {
                    Exitoso = true,
                    Mensaje = $"Se actualizaron {batchAplicado.CantidadProductos} precios correctamente",
                    BatchId = batchAplicado.Id,
                    ProductosActualizados = batchAplicado.CantidadProductos,
                    FechaAplicacion = batchAplicado.FechaAplicacion ?? DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al aplicar cambio de precios: BatchId={BatchId}", solicitud.BatchId);
                return new ResultadoAplicacionPrecios
                {
                    Exitoso = false,
                    Mensaje = $"Error al aplicar los cambios: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Parsea el tipo de cambio desde string a enum
        /// </summary>
        private static TipoCambio ParseTipoCambio(string tipoCambio)
        {
            return tipoCambio?.ToLowerInvariant() switch
            {
                "porcentaje" or "porcentajesobreprecioactual" => TipoCambio.PorcentajeSobrePrecioActual,
                "porcentajecosto" or "porcentajesobrecosto" => TipoCambio.PorcentajeSobreCosto,
                "absoluto" or "valorabsoluto" => TipoCambio.ValorAbsoluto,
                "directo" or "asignaciondirecta" => TipoCambio.AsignacionDirecta,
                _ => TipoCambio.PorcentajeSobrePrecioActual // Default
            };
        }

        /// <inheritdoc />
        public async Task<ResultadoAplicacionPrecios> AplicarCambioPrecioDirectoAsync(AplicarCambioPrecioDirectoViewModel model)
        {
            // Lógica principal delegada al PrecioService (a implementar)
            return await _precioService.AplicarCambioPrecioDirectoAsync(model);
        }

        #endregion

        #region Historial de precios

        public async Task<CambioPrecioHistorialViewModel> GetHistorialCambiosPrecioAsync()
        {
            var eventos = await _precioService.GetCambioPrecioEventosAsync();

            return new CambioPrecioHistorialViewModel
            {
                Eventos = eventos.Select(e => new CambioPrecioEventoItemViewModel
                {
                    Id = e.Id,
                    Fecha = e.Fecha,
                    Usuario = e.Usuario,
                    ValorPorcentaje = e.ValorPorcentaje,
                    Alcance = e.Alcance?.ToLowerInvariant() switch
                    {
                        "individual" => "Individual",
                        "seleccionados" => "Seleccionados",
                        "filtrados" => "Filtrados",
                        "reversion" => "Reversión",
                        _ => e.Alcance ?? "Sin alcance"
                    },
                    CantidadProductos = e.CantidadProductos,
                    Motivo = e.Motivo,
                    Revertido = e.RevertidoEn.HasValue,
                    RevertidoEn = e.RevertidoEn,
                    RevertidoPor = e.RevertidoPor
                }).ToList()
            };
        }

        public async Task<CambioPrecioDetalleViewModel?> GetCambioPrecioDetalleAsync(int eventoId)
        {
            var evento = await _precioService.GetCambioPrecioEventoAsync(eventoId);
            if (evento == null)
                return null;

            return new CambioPrecioDetalleViewModel
            {
                EventoId = evento.Id,
                Fecha = evento.Fecha,
                Usuario = evento.Usuario,
                ValorPorcentaje = evento.ValorPorcentaje,
                Alcance = evento.Alcance,
                CantidadProductos = evento.CantidadProductos,
                Motivo = evento.Motivo,
                Revertido = evento.RevertidoEn.HasValue,
                RevertidoEn = evento.RevertidoEn,
                RevertidoPor = evento.RevertidoPor,
                Detalles = evento.Detalles.Select(d => new CambioPrecioDetalleItemViewModel
                {
                    ProductoId = d.ProductoId,
                    Codigo = d.Producto?.Codigo ?? string.Empty,
                    Nombre = d.Producto?.Nombre ?? $"Producto {d.ProductoId}",
                    PrecioAnterior = d.PrecioAnterior,
                    PrecioNuevo = d.PrecioNuevo
                }).ToList()
            };
        }

        public async Task<(bool Exitoso, string Mensaje, int? EventoReversionId)> RevertirCambioPrecioAsync(int eventoId)
        {
            return await _precioService.RevertirCambioPrecioEventoAsync(eventoId);
        }

        public async Task<List<HistorialPrecioProductoItemViewModel>> GetHistorialCambiosPrecioProductoAsync(int productoId)
        {
            var detalles = await _precioService.GetCambiosPrecioProductoAsync(productoId);

            return detalles.Select(d => new HistorialPrecioProductoItemViewModel
            {
                EventoId = d.EventoId,
                Fecha = d.Evento?.Fecha ?? DateTime.MinValue,
                Usuario = d.Evento?.Usuario ?? "",
                Motivo = d.Evento?.Motivo,
                PrecioAnterior = d.PrecioAnterior,
                PrecioNuevo = d.PrecioNuevo,
                PuedeRevertir = d.Evento != null && !d.Evento.RevertidoEn.HasValue
                                && !string.Equals(d.Evento.Alcance, "reversion", StringComparison.OrdinalIgnoreCase)
            }).ToList();
        }

        #endregion
    }
}

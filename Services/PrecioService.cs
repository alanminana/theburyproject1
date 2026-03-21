    /// <summary>
    /// Aplica un cambio directo de precio a productos seleccionados o filtrados desde el catálogo.
    /// Actualiza Producto.PrecioVenta, crea historial y permite revertir.
    /// </summary>

using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

/// <summary>
/// Implementación del servicio de gestión de precios con historial
/// </summary>
public class PrecioService : IPrecioService
{
    private readonly AppDbContext _context;
    private readonly ILogger<PrecioService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    public PrecioService(
        AppDbContext context,
        ILogger<PrecioService> logger,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    private string GetCurrentUser() =>
        _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

    #region Gestión de Listas de Precios

    public async Task<List<ListaPrecio>> GetAllListasAsync(bool soloActivas = true)
    {
        var query = _context.ListasPrecios
            .Where(l => !l.IsDeleted)
            .AsQueryable();

        if (soloActivas)
            query = query.Where(l => l.Activa);

        return await query
            .OrderBy(l => l.Orden)
            .ThenBy(l => l.Nombre)
            .ToListAsync();
    }

    public async Task<ListaPrecio?> GetListaByIdAsync(int id)
    {
        return await _context.ListasPrecios
            .Include(l => l.Precios)
            .FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted);
    }

    public async Task<ListaPrecio?> GetListaPredeterminadaAsync()
    {
        return await _context.ListasPrecios
            .FirstOrDefaultAsync(l => l.EsPredeterminada && l.Activa && !l.IsDeleted);
    }

    public async Task<ListaPrecio> CreateListaAsync(ListaPrecio lista)
    {
        // Si es predeterminada, quitar flag de otras
        if (lista.EsPredeterminada)
        {
            var now = DateTime.UtcNow;
            var user = GetCurrentUser();

            await _context.ListasPrecios
                .Where(l => l.EsPredeterminada && !l.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.EsPredeterminada, false)
                    .SetProperty(l => l.UpdatedAt, now)
                    .SetProperty(l => l.UpdatedBy, user));
        }

        _context.ListasPrecios.Add(lista);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Lista de precios creada: {Nombre} por {User}",
            lista.Nombre, GetCurrentUser());

        return lista;
    }

    public async Task<ListaPrecio> UpdateListaAsync(ListaPrecio lista, byte[] rowVersion)
    {
        var existing = await _context.ListasPrecios.FirstOrDefaultAsync(l => l.Id == lista.Id && !l.IsDeleted);
        if (existing == null)
            throw new InvalidOperationException($"Lista de precios {lista.Id} no encontrada");

        if (rowVersion == null || rowVersion.Length == 0)
            throw new InvalidOperationException("RowVersion es requerido para actualizar la lista de precios");

        _context.Entry(existing).Property(e => e.RowVersion).OriginalValue = rowVersion;

        // Si se marca como predeterminada, quitar flag de otras
        if (lista.EsPredeterminada && !existing.EsPredeterminada)
        {
            var now = DateTime.UtcNow;
            var user = GetCurrentUser();

            await _context.ListasPrecios
                .Where(l => l.EsPredeterminada && l.Id != lista.Id && !l.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.EsPredeterminada, false)
                    .SetProperty(l => l.UpdatedAt, now)
                    .SetProperty(l => l.UpdatedBy, user));
        }

        existing.Nombre = lista.Nombre;
        existing.Codigo = lista.Codigo;
        existing.Tipo = lista.Tipo;
        existing.Descripcion = lista.Descripcion;
        existing.MargenPorcentaje = lista.MargenPorcentaje;
        existing.RecargoPorcentaje = lista.RecargoPorcentaje;
        existing.CantidadCuotas = lista.CantidadCuotas;
        existing.Activa = lista.Activa;
        existing.EsPredeterminada = lista.EsPredeterminada;
        existing.Orden = lista.Orden;
        existing.ReglasJson = lista.ReglasJson;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Lista de precios actualizada: {Id} por {User}",
            lista.Id, GetCurrentUser());

        return existing;
    }

    public async Task<bool> DeleteListaAsync(int id, byte[] rowVersion)
    {
        var lista = await _context.ListasPrecios.FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted);
        if (lista == null)
            return false;

        if (rowVersion == null || rowVersion.Length == 0)
            throw new InvalidOperationException("RowVersion es requerido para eliminar la lista de precios");

        _context.Entry(lista).Property(e => e.RowVersion).OriginalValue = rowVersion;

        // Verificar si tiene precios asociados
        var tienePrecios = await _context.ProductosPrecios
            .AnyAsync(p => p.ListaId == id);

        // Soft delete (unificado)
        lista.IsDeleted = true;
        lista.Activa = false;
        lista.EsPredeterminada = false;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Lista de precios eliminada: {Id} por {User}",
            id, GetCurrentUser());

        return true;
    }

    #endregion

    #region Cambio Directo de Precio (Catalogo)

    public async Task<ResultadoAplicacionPrecios> AplicarCambioPrecioDirectoAsync(
        AplicarCambioPrecioDirectoViewModel model)
    {
        if (model == null)
        {
            return new ResultadoAplicacionPrecios
            {
                Exitoso = false,
                Mensaje = "Solicitud invalida."
            };
        }

        if (model.ValorPorcentaje == 0)
        {
            return new ResultadoAplicacionPrecios
            {
                Exitoso = false,
                Mensaje = "El porcentaje no puede ser 0."
            };
        }

        var alcance = model.Alcance?.Trim().ToLowerInvariant();
        if (alcance != "seleccionados" && alcance != "filtrados")
        {
            return new ResultadoAplicacionPrecios
            {
                Exitoso = false,
                Mensaje = "Alcance no valido."
            };
        }

        var query = _context.Productos
            .Where(p => !p.IsDeleted)
            .AsQueryable();

        if (alcance == "seleccionados")
        {
            var productoIds = ParseProductoIds(model.ProductoIdsText);
            if (!productoIds.Any())
            {
                return new ResultadoAplicacionPrecios
                {
                    Exitoso = false,
                    Mensaje = "No se encontraron productos validos para aplicar el cambio."
                };
            }

            query = query.Where(p => productoIds.Contains(p.Id));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(model.FiltrosJson))
            {
                return new ResultadoAplicacionPrecios
                {
                    Exitoso = false,
                    Mensaje = "Filtros invalidos para aplicar el cambio."
                };
            }

            FiltrosCatalogoDto? filtros;
            try
            {
                filtros = JsonSerializer.Deserialize<FiltrosCatalogoDto>(
                    model.FiltrosJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Error al deserializar filtros de catalogo: {FiltrosJson}", model.FiltrosJson);
                return new ResultadoAplicacionPrecios
                {
                    Exitoso = false,
                    Mensaje = "Filtros invalidos para aplicar el cambio."
                };
            }

            if (filtros == null)
            {
                return new ResultadoAplicacionPrecios
                {
                    Exitoso = false,
                    Mensaje = "Filtros invalidos para aplicar el cambio."
                };
            }

            if (filtros.CategoriaId.HasValue && filtros.CategoriaId > 0)
                query = query.Where(p => p.CategoriaId == filtros.CategoriaId);

            if (filtros.MarcaId.HasValue && filtros.MarcaId > 0)
                query = query.Where(p => p.MarcaId == filtros.MarcaId);

            if (!string.IsNullOrWhiteSpace(filtros.Busqueda))
            {
                var busqueda = filtros.Busqueda.ToLowerInvariant();
                query = query.Where(p =>
                    p.Codigo.ToLower().Contains(busqueda) ||
                    p.Nombre.ToLower().Contains(busqueda) ||
                    (p.Descripcion != null && p.Descripcion.ToLower().Contains(busqueda)));
            }

            if (filtros.SoloActivos.HasValue && filtros.SoloActivos.Value)
                query = query.Where(p => p.Activo);

            if (filtros.StockBajo.HasValue && filtros.StockBajo.Value)
                query = query.Where(p => p.StockActual <= p.StockMinimo);
        }

        var productos = await query.ToListAsync();
        if (!productos.Any())
        {
            return new ResultadoAplicacionPrecios
            {
                Exitoso = false,
                Mensaje = "No se encontraron productos para aplicar el cambio."
            };
        }

        int? listaPrecioIdObjetivo = null;
        if (model.ListaPrecioId.HasValue && model.ListaPrecioId.Value > 0)
        {
            var lista = await _context.ListasPrecios
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == model.ListaPrecioId.Value && !l.IsDeleted && l.Activa);

            if (lista == null)
            {
                return new ResultadoAplicacionPrecios
                {
                    Exitoso = false,
                    Mensaje = "La lista de precios seleccionada no existe o no está activa."
                };
            }

            listaPrecioIdObjetivo = lista.Id;
        }

        var productosIds = productos.Select(p => p.Id).ToList();
        var preciosListaVigentes = new Dictionary<int, ProductoPrecioLista>();
        if (listaPrecioIdObjetivo.HasValue)
        {
            var ahoraLista = DateTime.UtcNow;
            var vigentes = await _context.ProductosPrecios
                .Where(p => !p.IsDeleted
                            && p.EsVigente
                            && p.ListaId == listaPrecioIdObjetivo.Value
                            && productosIds.Contains(p.ProductoId)
                            && p.VigenciaDesde <= ahoraLista
                            && (p.VigenciaHasta == null || p.VigenciaHasta >= ahoraLista))
                .OrderByDescending(p => p.VigenciaDesde)
                .ToListAsync();

            preciosListaVigentes = vigentes
                .GroupBy(p => p.ProductoId)
                .Select(g => g.First())
                .ToDictionary(p => p.ProductoId, p => p);
        }

        var usuario = GetCurrentUser();
        var now = DateTime.UtcNow;
        var historicos = new List<PrecioHistorico>();
        var porcentaje = model.ValorPorcentaje;
        var nuevosPreciosLista = new List<ProductoPrecioLista>();
        var cambios = new List<(Producto producto, decimal anterior, decimal nuevo, ProductoPrecioLista? precioListaAnterior)>();

        foreach (var producto in productos)
        {
            var precioListaAnterior = listaPrecioIdObjetivo.HasValue && preciosListaVigentes.TryGetValue(producto.Id, out var pl)
                ? pl
                : null;

            var precioVentaAnterior = precioListaAnterior?.Precio ?? producto.PrecioVenta;
            var precioVentaNuevo = Math.Round(
                precioVentaAnterior * (1 + (porcentaje / 100m)),
                2,
                MidpointRounding.AwayFromZero);

            if (precioVentaNuevo < 0)
            {
                return new ResultadoAplicacionPrecios
                {
                    Exitoso = false,
                    Mensaje = $"El porcentaje genera precios negativos (Producto {producto.Codigo})."
                };
            }

            if (precioVentaNuevo == precioVentaAnterior)
                continue;

            cambios.Add((producto, precioVentaAnterior, precioVentaNuevo, precioListaAnterior));
        }

        if (!cambios.Any())
        {
            return new ResultadoAplicacionPrecios
            {
                Exitoso = false,
                Mensaje = "No hubo cambios para aplicar."
            };
        }

        var descripcionAlcance = cambios.Count == 1 ? "1 producto" : $"{cambios.Count} productos";
        var motivoFinal = string.IsNullOrWhiteSpace(model.Motivo)
            ? $"Actualización de precio ({descripcionAlcance})"
            : model.Motivo.Trim();

        var evento = new CambioPrecioEvento
        {
            Fecha = now,
            Usuario = usuario,
            Alcance = cambios.Count == 1 ? "individual" : alcance,
            ValorPorcentaje = porcentaje,
            Motivo = motivoFinal,
            FiltrosJson = alcance == "filtrados" ? model.FiltrosJson : null,
            CantidadProductos = cambios.Count,
            CreatedAt = now,
            CreatedBy = usuario
        };

        var detalles = cambios.Select(cambio => new CambioPrecioDetalle
        {
            ProductoId = cambio.producto.Id,
            PrecioAnterior = cambio.anterior,
            PrecioNuevo = cambio.nuevo,
            CreatedAt = now,
            CreatedBy = usuario
        }).ToList();

        evento.Detalles = detalles;

        foreach (var cambio in cambios)
        {
            cambio.producto.PrecioVenta = cambio.nuevo;
            cambio.producto.UpdatedAt = now;
            cambio.producto.UpdatedBy = usuario;

            if (listaPrecioIdObjetivo.HasValue)
            {
                if (cambio.precioListaAnterior != null)
                {
                    cambio.precioListaAnterior.EsVigente = false;
                    cambio.precioListaAnterior.VigenciaHasta = now;
                    cambio.precioListaAnterior.UpdatedAt = now;
                    cambio.precioListaAnterior.UpdatedBy = usuario;
                }

                var nuevoPrecioLista = new ProductoPrecioLista
                {
                    ProductoId = cambio.producto.Id,
                    ListaId = listaPrecioIdObjetivo.Value,
                    VigenciaDesde = now,
                    Costo = cambio.producto.PrecioCompra,
                    Precio = cambio.nuevo,
                    MargenPorcentaje = cambio.producto.PrecioCompra > 0
                        ? Math.Round(((cambio.nuevo - cambio.producto.PrecioCompra) / cambio.producto.PrecioCompra) * 100m, 2)
                        : 0,
                    MargenValor = Math.Round(cambio.nuevo - cambio.producto.PrecioCompra, 2),
                    EsManual = true,
                    EsVigente = true,
                    CreadoPor = usuario,
                    Notas = motivoFinal,
                    CreatedAt = now,
                    CreatedBy = usuario
                };

                nuevosPreciosLista.Add(nuevoPrecioLista);
            }

            historicos.Add(new PrecioHistorico
            {
                ProductoId = cambio.producto.Id,
                PrecioCompraAnterior = cambio.producto.PrecioCompra,
                PrecioCompraNuevo = cambio.producto.PrecioCompra,
                PrecioVentaAnterior = cambio.anterior,
                PrecioVentaNuevo = cambio.nuevo,
                MotivoCambio = motivoFinal,
                FechaCambio = now,
                UsuarioModificacion = usuario,
                PuedeRevertirse = true,
                CreatedAt = now,
                CreatedBy = usuario
            });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.CambioPrecioEventos.Add(evento);
            _context.PreciosHistoricos.AddRange(historicos);
            if (nuevosPreciosLista.Count > 0)
            {
                _context.ProductosPrecios.AddRange(nuevosPreciosLista);
            }
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error al aplicar cambio directo de precio.");
            throw;
        }

        _logger.LogInformation(
            "Cambio directo aplicado: {Count} productos por {User}",
            historicos.Count, usuario);

        return new ResultadoAplicacionPrecios
        {
            Exitoso = true,
            Mensaje = $"Se actualizaron {historicos.Count} productos correctamente.",
            ProductosActualizados = historicos.Count,
            FechaAplicacion = now,
            CambioPrecioEventoId = evento.Id
        };
    }

    public async Task<List<CambioPrecioEvento>> GetCambioPrecioEventosAsync(int take = 200)
    {
        return await _context.CambioPrecioEventos
            .Where(e => !e.IsDeleted)
            .AsNoTracking()
            .OrderByDescending(e => e.Fecha)
            .Take(take)
            .ToListAsync();
    }

    public async Task<List<CambioPrecioDetalle>> GetCambiosPrecioProductoAsync(int productoId, int take = 50)
    {
        return await _context.CambioPrecioDetalles
            .AsNoTracking()
            .Where(d => !d.IsDeleted
                        && d.ProductoId == productoId
                        && d.Evento != null
                        && !d.Evento.IsDeleted)
            .Include(d => d.Evento)
            .OrderByDescending(d => d.Evento.Fecha)
            .Take(take)
            .ToListAsync();
    }

    public async Task<Dictionary<int, UltimoCambioProductoResumen>> GetUltimoCambioPorProductosAsync(IEnumerable<int> productoIds)
    {
        var ids = productoIds?.Distinct().ToList() ?? new List<int>();
        if (!ids.Any())
        {
            return new Dictionary<int, UltimoCambioProductoResumen>();
        }

        var detalles = await _context.CambioPrecioDetalles
            .AsNoTracking()
            .Where(d => !d.IsDeleted
                        && ids.Contains(d.ProductoId)
                        && d.Evento != null
                        && !d.Evento.IsDeleted)
            .Include(d => d.Evento)
            .OrderByDescending(d => d.Evento.Fecha)
            .ToListAsync();

        return detalles
            .GroupBy(d => d.ProductoId)
            .Select(g => g.First())
            .ToDictionary(
                d => d.ProductoId,
                d => new UltimoCambioProductoResumen
                {
                    EventoId = d.EventoId,
                    ProductoId = d.ProductoId,
                    Fecha = d.Evento.Fecha,
                    Usuario = d.Evento.Usuario,
                    ValorPorcentaje = d.Evento.ValorPorcentaje,
                    Revertido = d.Evento.RevertidoEn.HasValue,
                    EsReversion = string.Equals(d.Evento.Alcance, "reversion", StringComparison.OrdinalIgnoreCase)
                });
    }

    public async Task<CambioPrecioEvento?> GetCambioPrecioEventoAsync(int eventoId)
    {
        return await _context.CambioPrecioEventos
            .Where(e => !e.IsDeleted)
            .Include(e => e.Detalles)
            .ThenInclude(d => d.Producto)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventoId);
    }

    public async Task<(bool Exitoso, string Mensaje, int? EventoReversionId)> RevertirCambioPrecioEventoAsync(int eventoId)
    {
        var evento = await _context.CambioPrecioEventos
            .Where(e => !e.IsDeleted)
            .Include(e => e.Detalles)
            .FirstOrDefaultAsync(e => e.Id == eventoId);

        if (evento == null)
            return (false, "Evento no encontrado.", null);

        if (evento.RevertidoEn.HasValue)
            return (false, "El evento ya fue revertido.", null);

        if (string.Equals(evento.Alcance, "reversion", StringComparison.OrdinalIgnoreCase))
            return (false, "No se puede revertir un evento de reversion.", null);

        if (evento.Detalles == null || evento.Detalles.Count == 0)
            return (false, "El evento no tiene detalles para revertir.", null);

        var productoIds = evento.Detalles.Select(d => d.ProductoId).Distinct().ToList();
        var productos = await _context.Productos
            .Where(p => productoIds.Contains(p.Id) && !p.IsDeleted)
            .ToListAsync();

        var faltantes = productoIds.Except(productos.Select(p => p.Id)).ToList();
        if (faltantes.Count > 0)
            return (false, "No se encontraron todos los productos para revertir.", null);

        var usuario = GetCurrentUser();
        var now = DateTime.UtcNow;
        var historicos = new List<PrecioHistorico>();

        foreach (var detalle in evento.Detalles)
        {
            var producto = productos.First(p => p.Id == detalle.ProductoId);
            var precioActual = producto.PrecioVenta;
            var precioRevertido = detalle.PrecioAnterior;

            producto.PrecioVenta = precioRevertido;
            producto.UpdatedAt = now;
            producto.UpdatedBy = usuario;

            historicos.Add(new PrecioHistorico
            {
                ProductoId = producto.Id,
                PrecioCompraAnterior = producto.PrecioCompra,
                PrecioCompraNuevo = producto.PrecioCompra,
                PrecioVentaAnterior = precioActual,
                PrecioVentaNuevo = precioRevertido,
                MotivoCambio = $"Reversion evento #{evento.Id}",
                FechaCambio = now,
                UsuarioModificacion = usuario,
                PuedeRevertirse = true,
                CreatedAt = now,
                CreatedBy = usuario
            });
        }

        var eventoReversion = new CambioPrecioEvento
        {
            Fecha = now,
            Usuario = usuario,
            Alcance = "reversion",
            ValorPorcentaje = 0m,
            Motivo = $"Reversion evento #{evento.Id}",
            FiltrosJson = evento.FiltrosJson,
            CantidadProductos = evento.Detalles.Count,
            CreatedAt = now,
            CreatedBy = usuario,
            Detalles = evento.Detalles.Select(d => new CambioPrecioDetalle
            {
                ProductoId = d.ProductoId,
                PrecioAnterior = d.PrecioNuevo,
                PrecioNuevo = d.PrecioAnterior,
                CreatedAt = now,
                CreatedBy = usuario
            }).ToList()
        };

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            evento.RevertidoEn = now;
            evento.RevertidoPor = usuario;
            evento.UpdatedAt = now;
            evento.UpdatedBy = usuario;

            _context.CambioPrecioEventos.Add(eventoReversion);
            _context.PreciosHistoricos.AddRange(historicos);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error al revertir evento de cambio directo {EventoId}.", eventoId);
            return (false, "Error al revertir el evento.", null);
        }

        return (true, "Cambio revertido correctamente.", eventoReversion.Id);
    }

    private static List<int> ParseProductoIds(string? productoIdsText)
    {
        if (string.IsNullOrWhiteSpace(productoIdsText))
            return new List<int>();

        var tokens = productoIdsText.Split(
            new[] { ',', ';', '\n', '\r', '\t', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens
            .Select(token => int.TryParse(token, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    private sealed class FiltrosCatalogoDto
    {
        public int? CategoriaId { get; set; }
        public int? MarcaId { get; set; }
        public string? Busqueda { get; set; }
        public bool? SoloActivos { get; set; }
        public bool? StockBajo { get; set; }
        public int? ListaPrecioId { get; set; }
    }

    #endregion

    #region Consulta de Precios Vigentes

    public async Task<ProductoPrecioLista?> GetPrecioVigenteAsync(
        int productoId,
        int listaId,
        DateTime? fecha = null)
    {
        fecha ??= DateTime.UtcNow;

        return await _context.ProductosPrecios
            .Include(p => p.Producto)
            .Include(p => p.Lista)
            .Where(p => p.ProductoId == productoId
                     && p.ListaId == listaId
                     && p.VigenciaDesde <= fecha
                     && (p.VigenciaHasta == null || p.VigenciaHasta >= fecha)
                     && p.EsVigente
                     && !p.IsDeleted
                     && !p.Producto.IsDeleted
                     && !p.Lista.IsDeleted)
            .OrderByDescending(p => p.VigenciaDesde)
            .FirstOrDefaultAsync();
    }

    public async Task<List<ProductoPrecioLista>> GetPreciosProductoAsync(
        int productoId,
        DateTime? fecha = null)
    {
        fecha ??= DateTime.UtcNow;

        return await _context.ProductosPrecios
            .Include(p => p.Lista)
            .Where(p => p.ProductoId == productoId
                     && p.VigenciaDesde <= fecha
                     && (p.VigenciaHasta == null || p.VigenciaHasta >= fecha)
                     && p.EsVigente
                     && !p.IsDeleted
                     && p.Producto != null
                     && !p.Producto.IsDeleted
                     && !p.Lista.IsDeleted)
            .OrderBy(p => p.Lista.Orden)
            .ToListAsync();
    }

    public async Task<List<ProductoPrecioLista>> GetHistorialPreciosAsync(
        int productoId,
        int listaId)
    {
        return await _context.ProductosPrecios
            .Include(p => p.Lista)
            .Include(p => p.Batch)
            .Where(p => p.ProductoId == productoId &&
                        p.ListaId == listaId &&
                        !p.IsDeleted &&
                        p.Producto != null &&
                        !p.Producto.IsDeleted)
            .OrderByDescending(p => p.VigenciaDesde)
            .ToListAsync();
    }

    #endregion

    #region Gestión de Precios Individuales

    public async Task<ProductoPrecioLista> SetPrecioManualAsync(
        int productoId,
        int listaId,
        decimal precio,
        decimal costo,
        DateTime? vigenciaDesde = null,
        string? notas = null)
    {
        vigenciaDesde ??= DateTime.UtcNow;

        using var transaction = await _context.Database.BeginTransactionAsync();

        var now = DateTime.UtcNow;
        var currentUser = GetCurrentUser();

        var productoExiste = await _context.Productos.AnyAsync(p => p.Id == productoId && !p.IsDeleted);
        if (!productoExiste)
            throw new InvalidOperationException($"Producto {productoId} no encontrado");

        var listaExiste = await _context.ListasPrecios.AnyAsync(l => l.Id == listaId && !l.IsDeleted);
        if (!listaExiste)
            throw new InvalidOperationException($"Lista {listaId} no encontrada");

        // Persist updates first to avoid violating the unique vigente constraint
        await _context.ProductosPrecios
            .Where(p => p.ProductoId == productoId
                     && p.ListaId == listaId
                     && p.EsVigente
                     && !p.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.EsVigente, false)
                .SetProperty(p => p.VigenciaHasta, vigenciaDesde.Value.AddSeconds(-1))
                .SetProperty(p => p.UpdatedAt, now)
                .SetProperty(p => p.UpdatedBy, currentUser));

        // Crear nuevo precio
        var margenValor = precio - costo;
        var margenPorcentaje = costo > 0 ? (margenValor / costo) * 100 : 0;

        var nuevoPrecio = new ProductoPrecioLista
        {
            ProductoId = productoId,
            ListaId = listaId,
            VigenciaDesde = vigenciaDesde.Value,
            Costo = costo,
            Precio = precio,
            MargenValor = margenValor,
            MargenPorcentaje = margenPorcentaje,
            EsManual = true,
            EsVigente = true,
            CreadoPor = currentUser,
            Notas = notas
        };

        _context.ProductosPrecios.Add(nuevoPrecio);
        await _context.SaveChangesAsync();

        await transaction.CommitAsync();

        _logger.LogInformation(
            "Precio manual establecido: Producto {ProductoId}, Lista {ListaId}, Precio {Precio} por {User}",
            productoId, listaId, precio, GetCurrentUser());

        return nuevoPrecio;
    }

    public async Task<decimal> CalcularPrecioAutomaticoAsync(
        int productoId,
        int listaId,
        decimal costo)
    {
        var lista = await _context.ListasPrecios
            .FirstOrDefaultAsync(l => l.Id == listaId && !l.IsDeleted);
        if (lista == null)
            throw new InvalidOperationException($"Lista {listaId} no encontrada");

        decimal precio = costo;

        // Aplicar margen si está configurado
        if (lista.MargenPorcentaje.HasValue && lista.MargenPorcentaje.Value > 0)
        {
            precio = costo * (1 + lista.MargenPorcentaje.Value / 100);
        }

        // Aplicar recargo si está configurado
        if (lista.RecargoPorcentaje.HasValue && lista.RecargoPorcentaje.Value > 0)
        {
            precio = precio * (1 + lista.RecargoPorcentaje.Value / 100);
        }

        // Aplicar redondeo
        precio = AplicarRedondeo(precio, lista.ReglaRedondeo);

        return precio;
    }

    #endregion

    #region Cambios Masivos - Simulación

    public async Task<PriceChangeBatch> SimularCambioMasivoAsync(
        string nombre,
        TipoCambio tipoCambio,
        TipoAplicacion tipoAplicacion,
        decimal valorCambio,
        List<int> listasIds,
        List<int>? categoriaIds = null,
        List<int>? marcaIds = null,
        List<int>? productoIds = null)
    {
        var currentUser = GetCurrentUser();

        // Obtener productos afectados
        var query = _context.Productos
            .Where(p => !p.IsDeleted)
            .AsQueryable();

        if (productoIds != null && productoIds.Any())
        {
            query = query.Where(p => productoIds.Contains(p.Id));
        }
        else
        {
            if (categoriaIds != null && categoriaIds.Any())
                query = query.Where(p => categoriaIds.Contains(p.CategoriaId));

            if (marcaIds != null && marcaIds.Any())
                query = query.Where(p => marcaIds.Contains(p.MarcaId));
        }

        var productos = await query.ToListAsync();

        // Crear batch
        var batch = new PriceChangeBatch
        {
            Nombre = nombre,
            TipoCambio = tipoCambio,
            TipoAplicacion = tipoAplicacion,
            ValorCambio = valorCambio,
            AlcanceJson = JsonSerializer.Serialize(new
            {
                categorias = categoriaIds,
                marcas = marcaIds,
                productos = productoIds
            }),
            ListasAfectadasJson = JsonSerializer.Serialize(listasIds),
            Estado = EstadoBatch.Simulado,
            SolicitadoPor = currentUser,
            FechaSolicitud = DateTime.UtcNow
        };

        _context.PriceChangeBatches.Add(batch);
        await _context.SaveChangesAsync();

        // Cargar TODOS los precios vigentes de una vez (optimización N+1)
        var productoIds2 = productos.Select(p => p.Id).ToList();
        var fechaActual = DateTime.UtcNow;
        var preciosVigentes = await _context.ProductosPrecios
            .Include(p => p.Producto)
            .Include(p => p.Lista)
            .Where(p => productoIds2.Contains(p.ProductoId)
                     && listasIds.Contains(p.ListaId)
                     && p.VigenciaDesde <= fechaActual
                     && (p.VigenciaHasta == null || p.VigenciaHasta >= fechaActual)
                     && p.EsVigente
                     && !p.IsDeleted
                     && !p.Producto.IsDeleted
                     && !p.Lista.IsDeleted)
            .ToListAsync();

        // Crear diccionario para lookup rápido O(1)
        var preciosPorProductoYLista = preciosVigentes
            .GroupBy(p => (p.ProductoId, p.ListaId))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(p => p.VigenciaDesde).First()
            );

        // Crear items de simulación
        var items = new List<PriceChangeItem>();
        decimal sumatoriaCambios = 0;
        int countCambios = 0;

        foreach (var producto in productos)
        {
            foreach (var listaId in listasIds)
            {
                // Lookup en memoria O(1) en lugar de query
                var key = (ProductoId: producto.Id, ListaId: listaId);
                if (!preciosPorProductoYLista.TryGetValue(key, out var precioActual))
                {
                    _logger.LogWarning(
                        "Producto {ProductoId} no tiene precio en lista {ListaId}",
                        producto.Id, listaId);
                    continue;
                }

                // Calcular nuevo precio
                decimal precioNuevo = CalcularNuevoPrecio(
                    precioActual.Precio,
                    precioActual.Costo,
                    tipoCambio,
                    tipoAplicacion,
                    valorCambio);

                var diferencia = precioNuevo - precioActual.Precio;
                var diferenciaPorcentaje = precioActual.Precio > 0
                    ? (diferencia / precioActual.Precio) * 100
                    : 0;

                var margenNuevo = precioActual.Costo > 0
                    ? ((precioNuevo - precioActual.Costo) / precioActual.Costo) * 100
                    : 0;

                var item = new PriceChangeItem
                {
                    BatchId = batch.Id,
                    ProductoId = producto.Id,
                    ListaId = listaId,
                    ProductoCodigo = producto.Codigo,
                    ProductoNombre = producto.Nombre,
                    PrecioAnterior = precioActual.Precio,
                    PrecioNuevo = precioNuevo,
                    DiferenciaValor = diferencia,
                    DiferenciaPorcentaje = diferenciaPorcentaje,
                    Costo = precioActual.Costo,
                    MargenAnterior = precioActual.MargenPorcentaje,
                    MargenNuevo = margenNuevo
                };

                // Validar advertencias
                if (margenNuevo < 0)
                {
                    item.TieneAdvertencia = true;
                    item.MensajeAdvertencia = $"Margen negativo: {margenNuevo:F2}%";
                }
                else if (precioNuevo <= 0)
                {
                    item.TieneAdvertencia = true;
                    item.MensajeAdvertencia = "Precio resultante es cero o negativo";
                }

                items.Add(item);

                sumatoriaCambios += Math.Abs(diferenciaPorcentaje);
                countCambios++;
            }
        }

        _context.PriceChangeItems.AddRange(items);

        // Actualizar batch con estadísticas
        batch.PorcentajePromedioCambio = countCambios > 0
            ? sumatoriaCambios / countCambios
            : 0;

        batch.SimulacionJson = JsonSerializer.Serialize(new
        {
            totalProductos = productos.Count,
            totalItems = items.Count,
            promedioAumento = batch.PorcentajePromedioCambio,
            itemsConAdvertencia = items.Count(i => i.TieneAdvertencia)
        });

        // Verificar si requiere autorización
        batch.RequiereAutorizacion = await RequiereAutorizacionAsync(batch.Id);

        // Cantidad de productos realmente afectados (distinct por ProductoId)
        batch.CantidadProductos = items.Select(i => i.ProductoId).Distinct().Count();

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Simulación creada: {BatchId} - {Nombre} por {User}",
            batch.Id, batch.Nombre, currentUser);

        return batch;
    }

    private decimal CalcularNuevoPrecio(
        decimal precioActual,
        decimal costo,
        TipoCambio tipoCambio,
        TipoAplicacion tipoAplicacion,
        decimal valorCambio)
    {
        decimal precioNuevo = precioActual;
        decimal baseCalculo = precioActual;

        // Determinar la base de cálculo según el tipo de cambio
        switch (tipoCambio)
        {
            case TipoCambio.PorcentajeSobrePrecioActual:
                baseCalculo = precioActual;
                break;

            case TipoCambio.PorcentajeSobreCosto:
                baseCalculo = costo;
                break;

            case TipoCambio.ValorAbsoluto:
                baseCalculo = precioActual;
                break;

            case TipoCambio.AsignacionDirecta:
                return valorCambio; // Asignación directa, ignorar tipoAplicacion
        }

        // Aplicar el cambio según si es aumento o disminución
        if (tipoCambio == TipoCambio.PorcentajeSobrePrecioActual ||
            tipoCambio == TipoCambio.PorcentajeSobreCosto)
        {
            // Cambio porcentual
            if (tipoAplicacion == TipoAplicacion.Aumento)
                precioNuevo = baseCalculo * (1 + valorCambio / 100);
            else // Disminucion
                precioNuevo = baseCalculo * (1 - valorCambio / 100);
        }
        else if (tipoCambio == TipoCambio.ValorAbsoluto)
        {
            // Cambio por valor absoluto
            if (tipoAplicacion == TipoAplicacion.Aumento)
                precioNuevo = precioActual + valorCambio;
            else // Disminucion
                precioNuevo = precioActual - valorCambio;
        }

        return precioNuevo;
    }

    public async Task<PriceChangeBatch?> GetSimulacionAsync(int batchId)
    {
        return await _context.PriceChangeBatches
            .Include(b => b.Items.Where(i => !i.IsDeleted))
            .FirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);
    }

    public async Task<List<PriceChangeItem>> GetItemsSimulacionAsync(
        int batchId,
        int skip = 0,
        int take = 50)
    {
        return await _context.PriceChangeItems
            .Include(i => i.Producto)
            .Include(i => i.Lista)
            .Where(i => i.BatchId == batchId
                     && !i.IsDeleted
                     && !i.Producto.IsDeleted
                     && !i.Lista.IsDeleted)
            .OrderBy(i => i.ProductoCodigo)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<List<int>> GetBatchIdsByProductoAsync(int productoId)
    {
        return await _context.PriceChangeItems
            .Where(i => i.ProductoId == productoId)
            .Select(i => i.BatchId)
            .Distinct()
            .ToListAsync();
    }

    #endregion

    #region Cambios Masivos - Autorización

    public async Task<PriceChangeBatch> AprobarBatchAsync(
        int batchId,
        string aprobadoPor,
        byte[] rowVersion,
        string? notas = null)
    {
        var batch = await _context.PriceChangeBatches
            .FirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);
        if (batch == null)
            throw new InvalidOperationException($"Batch {batchId} no encontrado");

        if (rowVersion == null || rowVersion.Length == 0)
            throw new InvalidOperationException("RowVersion es requerido para aprobar el batch");

        _context.Entry(batch).Property(e => e.RowVersion).OriginalValue = rowVersion;

        if (batch.Estado != EstadoBatch.Simulado)
            throw new InvalidOperationException(
                $"Batch {batchId} no está en estado Simulado (estado actual: {batch.Estado})");

        batch.Estado = EstadoBatch.Aprobado;
        batch.AprobadoPor = aprobadoPor;
        batch.FechaAprobacion = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(notas))
            batch.Notas = notas;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Batch aprobado: {BatchId} por {User}",
            batchId, aprobadoPor);

        return batch;
    }

    public async Task<PriceChangeBatch> RechazarBatchAsync(
        int batchId,
        string rechazadoPor,
        byte[] rowVersion,
        string motivo)
    {
        var batch = await _context.PriceChangeBatches
            .FirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);
        if (batch == null)
            throw new InvalidOperationException($"Batch {batchId} no encontrado");

        if (rowVersion == null || rowVersion.Length == 0)
            throw new InvalidOperationException("RowVersion es requerido para rechazar el batch");

        _context.Entry(batch).Property(e => e.RowVersion).OriginalValue = rowVersion;

        if (batch.Estado != EstadoBatch.Simulado)
            throw new InvalidOperationException(
                $"Batch {batchId} no está en estado Simulado");

        batch.Estado = EstadoBatch.Rechazado;
        batch.MotivoRechazo = motivo;
        batch.AprobadoPor = rechazadoPor; // Quien rechaza
        batch.FechaAprobacion = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Batch rechazado: {BatchId} por {User} - Motivo: {Motivo}",
            batchId, rechazadoPor, motivo);

        return batch;
    }

    public async Task<PriceChangeBatch> CancelarBatchAsync(
        int batchId,
        string canceladoPor,
        byte[] rowVersion,
        string? motivo = null)
    {
        var batch = await _context.PriceChangeBatches
            .FirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);
        if (batch == null)
            throw new InvalidOperationException($"Batch {batchId} no encontrado");

        if (rowVersion == null || rowVersion.Length == 0)
            throw new InvalidOperationException("RowVersion es requerido para cancelar el batch");

        _context.Entry(batch).Property(e => e.RowVersion).OriginalValue = rowVersion;

        if (batch.Estado == EstadoBatch.Aplicado)
            throw new InvalidOperationException(
                "No se puede cancelar un batch ya aplicado. Use Revertir en su lugar.");

        batch.Estado = EstadoBatch.Cancelado;
        batch.MotivoRechazo = motivo;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Batch cancelado: {BatchId} por {User}",
            batchId, canceladoPor);

        return batch;
    }

    public async Task<bool> RequiereAutorizacionAsync(int batchId)
    {
        var batch = await _context.PriceChangeBatches
            .FirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);
        if (batch == null)
            return false;

        // Obtener umbral de configuración, por defecto 10%
        var umbralPorcentaje = _configuration.GetValue<decimal>("Precios:UmbralAutorizacionPorcentaje", 10.0m);

        return batch.PorcentajePromedioCambio.HasValue &&
               Math.Abs(batch.PorcentajePromedioCambio.Value) >= umbralPorcentaje;
    }

    #endregion

    #region Cambios Masivos - Aplicación

    public async Task<PriceChangeBatch> AplicarBatchAsync(
        int batchId,
        string aplicadoPor,
        byte[] rowVersion,
        DateTime? fechaVigencia = null)
    {
        var batch = await _context.PriceChangeBatches
            .Include(b => b.Items.Where(i => !i.IsDeleted))
            .FirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);

        if (batch == null)
            throw new InvalidOperationException($"Batch {batchId} no encontrado");

        if (batch.Estado != EstadoBatch.Aprobado)
            throw new InvalidOperationException(
                $"Batch {batchId} debe estar Aprobado para aplicarse (estado actual: {batch.Estado})");

        if (rowVersion == null || rowVersion.Length == 0)
            throw new InvalidOperationException("RowVersion es requerido para aplicar el batch");

        _context.Entry(batch).Property(e => e.RowVersion).OriginalValue = rowVersion;

        fechaVigencia ??= DateTime.UtcNow;

        // Aplicar en transacción
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var nuevosPrecios = new List<ProductoPrecioLista>();
            var now = DateTime.UtcNow;

            foreach (var item in batch.Items)
            {
                // Marcar precio actual como no vigente
                await _context.ProductosPrecios
                    .Where(p => p.ProductoId == item.ProductoId
                             && p.ListaId == item.ListaId
                             && p.EsVigente
                             && !p.IsDeleted)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(p => p.EsVigente, false)
                        .SetProperty(p => p.VigenciaHasta, fechaVigencia.Value.AddSeconds(-1))
                        .SetProperty(p => p.UpdatedAt, now)
                        .SetProperty(p => p.UpdatedBy, aplicadoPor));

                // Crear nuevo precio
                var nuevoPrecio = new ProductoPrecioLista
                {
                    ProductoId = item.ProductoId,
                    ListaId = item.ListaId,
                    VigenciaDesde = fechaVigencia.Value,
                    Costo = item.Costo ?? 0,
                    Precio = item.PrecioNuevo,
                    MargenValor = item.PrecioNuevo - (item.Costo ?? 0),
                    MargenPorcentaje = item.MargenNuevo ?? 0,
                    EsManual = false,
                    EsVigente = true,
                    BatchId = batchId,
                    CreadoPor = aplicadoPor,
                    Notas = $"Aplicado desde batch: {batch.Nombre}"
                };

                nuevosPrecios.Add(nuevoPrecio);

                // Marcar item como aplicado
                item.Aplicado = true;
            }

            _context.ProductosPrecios.AddRange(nuevosPrecios);

            // Actualizar batch
            batch.Estado = EstadoBatch.Aplicado;
            batch.AplicadoPor = aplicadoPor;
            batch.FechaAplicacion = DateTime.UtcNow;
            batch.FechaVigencia = fechaVigencia;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Batch aplicado exitosamente: {BatchId} - {CantidadItems} items por {User}",
                batchId, batch.Items.Count, aplicadoPor);

            return batch;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error al aplicar batch {BatchId}", batchId);
            throw;
        }
    }

    public async Task<PriceChangeBatch> RevertirBatchAsync(
        int batchId,
        string revertidoPor,
        byte[] rowVersion,
        string motivo)
    {
        var batch = await _context.PriceChangeBatches
            .Include(b => b.Items.Where(i => !i.IsDeleted))
            .FirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);

        if (batch == null)
            throw new InvalidOperationException($"Batch {batchId} no encontrado");

        if (batch.Estado != EstadoBatch.Aplicado)
            throw new InvalidOperationException(
                $"Solo se pueden revertir batches Aplicados (estado actual: {batch.Estado})");

        if (rowVersion == null || rowVersion.Length == 0)
            throw new InvalidOperationException("RowVersion es requerido para revertir el batch");

        _context.Entry(batch).Property(e => e.RowVersion).OriginalValue = rowVersion;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var fechaReversion = DateTime.UtcNow;
            var now = DateTime.UtcNow;

            // Crear batch de reversión para auditoría
            var batchReversion = new PriceChangeBatch
            {
                Nombre = $"[REVERSIÓN] {batch.Nombre}",
                TipoCambio = batch.TipoCambio,
                TipoAplicacion = batch.TipoAplicacion == TipoAplicacion.Aumento 
                    ? TipoAplicacion.Disminucion 
                    : TipoAplicacion.Aumento,
                ValorCambio = batch.ValorCambio,
                AlcanceJson = batch.AlcanceJson,
                ListasAfectadasJson = batch.ListasAfectadasJson,
                Estado = EstadoBatch.Aplicado,
                CantidadProductos = batch.CantidadProductos,
                SolicitadoPor = revertidoPor,
                FechaSolicitud = fechaReversion,
                AprobadoPor = revertidoPor,
                FechaAprobacion = fechaReversion,
                AplicadoPor = revertidoPor,
                FechaAplicacion = fechaReversion,
                FechaVigencia = fechaReversion,
                RequiereAutorizacion = false,
                BatchPadreId = batchId,
                Notas = $"Reversión del batch #{batchId}. Motivo: {motivo}",
                MotivoReversion = motivo,
                PorcentajePromedioCambio = -batch.PorcentajePromedioCambio
            };

            _context.PriceChangeBatches.Add(batchReversion);
            await _context.SaveChangesAsync(); // Obtener ID del nuevo batch

            var nuevosPrecios = new List<ProductoPrecioLista>();
            var itemsReversion = new List<PriceChangeItem>();

            foreach (var item in batch.Items.Where(i => i.Aplicado))
            {
                // Marcar precio actual (del batch) como no vigente
                await _context.ProductosPrecios
                    .Where(p => p.ProductoId == item.ProductoId
                             && p.ListaId == item.ListaId
                             && p.BatchId == batchId
                             && p.EsVigente
                             && !p.IsDeleted)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(p => p.EsVigente, false)
                        .SetProperty(p => p.VigenciaHasta, fechaReversion.AddSeconds(-1))
                        .SetProperty(p => p.UpdatedAt, now)
                        .SetProperty(p => p.UpdatedBy, revertidoPor));

                // Restaurar precio anterior
                var nuevoPrecio = new ProductoPrecioLista
                {
                    ProductoId = item.ProductoId,
                    ListaId = item.ListaId,
                    VigenciaDesde = fechaReversion,
                    Costo = item.Costo ?? 0,
                    Precio = item.PrecioAnterior,
                    MargenValor = item.PrecioAnterior - (item.Costo ?? 0),
                    MargenPorcentaje = item.MargenAnterior ?? 0,
                    EsManual = false,
                    EsVigente = true,
                    BatchId = batchReversion.Id,
                    CreadoPor = revertidoPor,
                    Notas = $"Revertido desde batch #{batchId}: {motivo}"
                };

                nuevosPrecios.Add(nuevoPrecio);

                // Crear item de reversión para el nuevo batch
                var itemReversion = new PriceChangeItem
                {
                    BatchId = batchReversion.Id,
                    ProductoId = item.ProductoId,
                    ListaId = item.ListaId,
                    ProductoCodigo = item.ProductoCodigo,
                    ProductoNombre = item.ProductoNombre,
                    PrecioAnterior = item.PrecioNuevo,  // El "anterior" es el precio que estamos revirtiendo
                    PrecioNuevo = item.PrecioAnterior,  // El "nuevo" es el precio original
                    DiferenciaValor = item.PrecioAnterior - item.PrecioNuevo,
                    DiferenciaPorcentaje = -item.DiferenciaPorcentaje,
                    Costo = item.Costo,
                    MargenAnterior = item.MargenNuevo,
                    MargenNuevo = item.MargenAnterior,
                    Aplicado = true
                };

                itemsReversion.Add(itemReversion);

                // Marcar item original como revertido
                item.Revertido = true;
            }

            _context.ProductosPrecios.AddRange(nuevosPrecios);
            _context.PriceChangeItems.AddRange(itemsReversion);

            // Actualizar batch original
            batch.Estado = EstadoBatch.Revertido;
            batch.RevertidoPor = revertidoPor;
            batch.FechaReversion = fechaReversion;
            batch.MotivoReversion = motivo;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Batch revertido exitosamente: {BatchId} por {User} - Motivo: {Motivo}. Batch de reversión: {BatchReversionId}",
                batchId, revertidoPor, motivo, batchReversion.Id);

            return batchReversion; // Retornar el batch de reversión para referencia
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error al revertir batch {BatchId}", batchId);
            throw;
        }
    }

    #endregion

    #region Reportes y Estadísticas

    public async Task<List<PriceChangeBatch>> GetBatchesAsync(
        EstadoBatch? estado = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null,
        int skip = 0,
        int take = 50)
    {
        var query = _context.PriceChangeBatches
            .Where(b => !b.IsDeleted)
            .AsQueryable();

        if (estado.HasValue)
            query = query.Where(b => b.Estado == estado.Value);

        if (fechaDesde.HasValue)
            query = query.Where(b => b.FechaSolicitud >= fechaDesde.Value);

        if (fechaHasta.HasValue)
            query = query.Where(b => b.FechaSolicitud <= fechaHasta.Value);

        return await query
            .OrderByDescending(b => b.FechaSolicitud)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<Dictionary<string, object>> GetEstadisticasBatchAsync(int batchId)
    {
        var batch = await _context.PriceChangeBatches
            .Include(b => b.Items.Where(i => !i.IsDeleted))
            .FirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);

        if (batch == null)
            return new Dictionary<string, object>();

        var stats = new Dictionary<string, object>
        {
            ["TotalItems"] = batch.Items.Count,
            ["ItemsAplicados"] = batch.Items.Count(i => i.Aplicado),
            ["ItemsRevertidos"] = batch.Items.Count(i => i.Revertido),
            ["ItemsConAdvertencia"] = batch.Items.Count(i => i.TieneAdvertencia),
            ["PorcentajePromedioCambio"] = batch.PorcentajePromedioCambio ?? 0,
            ["AumentoMaximo"] = batch.Items.Max(i => i.DiferenciaPorcentaje),
            ["AumentoMinimo"] = batch.Items.Min(i => i.DiferenciaPorcentaje),
            ["DiferenciaTotal"] = batch.Items.Sum(i => i.DiferenciaValor)
        };

        return stats;
    }

    public async Task<byte[]> ExportarHistorialPreciosAsync(
        List<int> productoIds,
        DateTime fechaDesde,
        DateTime fechaHasta)
    {
        if (productoIds == null || productoIds.Count == 0)
            return Array.Empty<byte>();

        if (fechaHasta < fechaDesde)
            throw new ArgumentException("fechaHasta debe ser mayor o igual a fechaDesde");

        var precios = await _context.ProductosPrecios
            .Include(p => p.Producto)
            .Include(p => p.Lista)
            .Include(p => p.Batch)
            .Where(p => productoIds.Contains(p.ProductoId)
                     && !p.IsDeleted
                     && !p.Producto.IsDeleted
                     && !p.Lista.IsDeleted
                     && p.VigenciaDesde <= fechaHasta
                     && (p.VigenciaHasta == null || p.VigenciaHasta >= fechaDesde))
            .OrderBy(p => p.Producto.Codigo)
            .ThenBy(p => p.Lista.Orden)
            .ThenByDescending(p => p.VigenciaDesde)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("HistorialPrecios");

        worksheet.Cell(1, 1).Value = "ProductoId";
        worksheet.Cell(1, 2).Value = "Codigo";
        worksheet.Cell(1, 3).Value = "Producto";
        worksheet.Cell(1, 4).Value = "Lista";
        worksheet.Cell(1, 5).Value = "VigenciaDesde";
        worksheet.Cell(1, 6).Value = "VigenciaHasta";
        worksheet.Cell(1, 7).Value = "Precio";
        worksheet.Cell(1, 8).Value = "Costo";
        worksheet.Cell(1, 9).Value = "Margen%";
        worksheet.Cell(1, 10).Value = "EsVigente";
        worksheet.Cell(1, 11).Value = "EsManual";
        worksheet.Cell(1, 12).Value = "BatchId";
        worksheet.Cell(1, 13).Value = "Batch";
        worksheet.Cell(1, 14).Value = "Notas";

        var headerRange = worksheet.Range("A1:N1");
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

        int row = 2;
        foreach (var p in precios)
        {
            worksheet.Cell(row, 1).Value = p.ProductoId;
            worksheet.Cell(row, 2).Value = p.Producto?.Codigo ?? p.ProductoId.ToString();
            worksheet.Cell(row, 3).Value = p.Producto?.Nombre ?? string.Empty;
            worksheet.Cell(row, 4).Value = p.Lista?.Nombre ?? p.ListaId.ToString();
            worksheet.Cell(row, 5).Value = p.VigenciaDesde;
            worksheet.Cell(row, 6).Value = p.VigenciaHasta;
            worksheet.Cell(row, 7).Value = p.Precio;
            worksheet.Cell(row, 8).Value = p.Costo;
            worksheet.Cell(row, 9).Value = p.MargenPorcentaje;
            worksheet.Cell(row, 10).Value = p.EsVigente;
            worksheet.Cell(row, 11).Value = p.EsManual;
            worksheet.Cell(row, 12).Value = p.BatchId;
            worksheet.Cell(row, 13).Value = p.Batch?.Nombre ?? string.Empty;
            worksheet.Cell(row, 14).Value = p.Notas ?? string.Empty;
            row++;
        }

        if (row > 2)
        {
            worksheet.Range($"E2:F{row - 1}").Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
            worksheet.Range($"G2:H{row - 1}").Style.NumberFormat.Format = "$#,##0.00";
            worksheet.Range($"I2:I{row - 1}").Style.NumberFormat.Format = "0.00";
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    #endregion

    #region Validaciones y Utilidades

    public async Task<(bool esValido, string? mensaje)> ValidarMargenMinimoAsync(
        decimal precio,
        decimal costo,
        int listaId)
    {
        // Obtener margen mínimo de la lista de precios
        var lista = await _context.ListasPrecios
            .FirstOrDefaultAsync(l => l.Id == listaId && !l.IsDeleted);
        var margenMinimo = lista?.MargenMinimoPorcentaje ?? 10.0m;

        var margen = CalcularMargen(precio, costo);

        if (margen < margenMinimo)
        {
            return (false, $"El margen {margen:F2}% es inferior al mínimo permitido ({margenMinimo}%)");
        }

        return (true, null);
    }

    public decimal CalcularMargen(decimal precio, decimal costo)
    {
        if (costo <= 0)
            return 0;

        return ((precio - costo) / costo) * 100;
    }

    public decimal AplicarRedondeo(decimal precio, string? reglaRedondeo = null)
    {
        if (string.IsNullOrWhiteSpace(reglaRedondeo))
        {
            // Redondeo por defecto a centena
            return Math.Round(precio / 100) * 100;
        }

        return reglaRedondeo.ToLower() switch
        {
            "ninguno" => precio,
            "unidad" => Math.Round(precio, 0),
            "decena" => Math.Round(precio / 10) * 10,
            "centena" => Math.Round(precio / 100) * 100,
            _ => Math.Round(precio / 100) * 100 // Default a centena
        };
    }

    #endregion
}

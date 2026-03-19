using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Globalization;
using System.Text;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers;

/// <summary>
/// Controlador para gestión de devoluciones, garantías y RMAs
/// </summary>
[Authorize]
[PermisoRequerido(Modulo = "devoluciones", Accion = "view")]
public class DevolucionController : Controller
{
    private readonly IDevolucionService _devolucionService;
    private readonly IClienteService _clienteService;
    private readonly IVentaService _ventaService;
    private readonly IProveedorService _proveedorService;
    private readonly UserManager<ApplicationUser> _userManager;

    public DevolucionController(
        IDevolucionService devolucionService,
        IClienteService clienteService,
        IVentaService ventaService,
        IProveedorService proveedorService,
        UserManager<ApplicationUser> userManager)
    {
        _devolucionService = devolucionService;
        _clienteService = clienteService;
        _ventaService = ventaService;
        _proveedorService = proveedorService;
        _userManager = userManager;
    }

    #region Devoluciones

    /// <summary>
    /// Lista de todas las devoluciones
    /// </summary>
    public async Task<IActionResult> Index(
        string? tab = null,
        string? search = null,
        string? estado = null,
        string? resolucion = null,
        string? garantiaEstado = null,
        string? garantiaVentana = null,
        int page = 1,
        bool exportCsv = false)
    {
        var todasDevoluciones = await _devolucionService.ObtenerTodasDevolucionesAsync();
        var todasGarantias = await _devolucionService.ObtenerTodasGarantiasAsync();
        var proximasVencer = await _devolucionService.ObtenerGarantiasProximasVencerAsync(30);
        var motivosFrecuentes = await _devolucionService.ObtenerEstadisticasMotivoDevolucionAsync(DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow);
        var activeTab = string.Equals(tab, "garantias", StringComparison.OrdinalIgnoreCase)
            ? "garantias"
            : "devoluciones";
        var searchTerm = search?.Trim() ?? string.Empty;

        var devolucionesFiltradas = FiltrarDevoluciones(todasDevoluciones, searchTerm, estado, resolucion);
        var garantiasFiltradas = FiltrarGarantias(todasGarantias, searchTerm, garantiaEstado, garantiaVentana);

        if (exportCsv)
        {
            return activeTab == "garantias"
                ? ExportarGarantiasCsv(garantiasFiltradas)
                : ExportarDevolucionesCsv(devolucionesFiltradas);
        }

        const int pageSize = 10;
        var totalFiltered = activeTab == "garantias" ? garantiasFiltradas.Count : devolucionesFiltradas.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalFiltered / (double)pageSize));
        var currentPage = Math.Min(Math.Max(page, 1), totalPages);
        var firstItemIndex = totalFiltered == 0 ? 0 : ((currentPage - 1) * pageSize) + 1;
        var lastItemIndex = totalFiltered == 0 ? 0 : Math.Min(currentPage * pageSize, totalFiltered);

        var viewModel = new DevolucionIndexViewModel
        {
            ActiveTab = activeTab,
            Search = searchTerm,
            EstadoFilter = estado?.Trim() ?? string.Empty,
            ResolucionFilter = resolucion?.Trim() ?? string.Empty,
            GarantiaEstadoFilter = garantiaEstado?.Trim() ?? string.Empty,
            GarantiaVentanaFilter = garantiaVentana?.Trim() ?? string.Empty,
            CurrentPage = currentPage,
            PageSize = pageSize,
            TotalFiltered = totalFiltered,
            TotalPages = totalPages,
            FirstItemIndex = firstItemIndex,
            LastItemIndex = lastItemIndex,
            TodasDevoluciones = todasDevoluciones,
            Pendientes = todasDevoluciones.Where(d => d.Estado == EstadoDevolucion.Pendiente).ToList(),
            EnRevision = todasDevoluciones.Where(d => d.Estado == EstadoDevolucion.EnRevision).ToList(),
            Aprobadas = todasDevoluciones.Where(d => d.Estado == EstadoDevolucion.Aprobada).ToList(),
            Completadas = todasDevoluciones.Where(d => d.Estado == EstadoDevolucion.Completada).ToList(),
            Rechazadas = todasDevoluciones.Where(d => d.Estado == EstadoDevolucion.Rechazada).ToList(),
            DevolucionesPagina = devolucionesFiltradas
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToList(),
            TotalPendientes = todasDevoluciones.Count(d => d.Estado == EstadoDevolucion.Pendiente),
            TotalAprobadas = todasDevoluciones.Count(d => d.Estado == EstadoDevolucion.Aprobada),
            TotalRechazadas = todasDevoluciones.Count(d => d.Estado == EstadoDevolucion.Rechazada),
            TotalCompletadas = todasDevoluciones.Count(d => d.Estado == EstadoDevolucion.Completada),
            MontoTotalMes = todasDevoluciones
                .Where(d => d.FechaDevolucion >= DateTime.Now.AddMonths(-1) && d.Estado == EstadoDevolucion.Completada)
                .Sum(d => d.TotalDevolucion),
            TodasGarantias = todasGarantias,
            Vigentes = todasGarantias.Where(g => g.Estado == EstadoGarantia.Vigente && g.FechaVencimiento >= DateTime.Now).ToList(),
            ProximasVencer = proximasVencer,
            Vencidas = todasGarantias.Where(g => g.FechaVencimiento < DateTime.Now || g.Estado == EstadoGarantia.Vencida).ToList(),
            EnUso = todasGarantias.Where(g => g.Estado == EstadoGarantia.EnUso).ToList(),
            GarantiasPagina = garantiasFiltradas
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToList(),
            TotalGarantiasVigentes = todasGarantias.Count(g => g.Estado == EstadoGarantia.Vigente && g.FechaVencimiento >= DateTime.Now),
            TotalGarantiasProximasVencer = proximasVencer.Count,
            TotalGarantiasVencidas = todasGarantias.Count(g => g.FechaVencimiento < DateTime.Now || g.Estado == EstadoGarantia.Vencida),
            TotalGarantiasEnUso = todasGarantias.Count(g => g.Estado == EstadoGarantia.EnUso),
            RmasPendientes = await _devolucionService.ObtenerCantidadRMAsPendientesAsync(),
            ReembolsosPendientesCaja = todasDevoluciones.Count(d =>
                d.Estado == EstadoDevolucion.Aprobada &&
                d.TipoResolucion == TipoResolucionDevolucion.ReembolsoDinero &&
                d.RegistrarEgresoCaja),
            MontoReembolsosPendientesCaja = todasDevoluciones
                .Where(d =>
                    d.Estado == EstadoDevolucion.Aprobada &&
                    d.TipoResolucion == TipoResolucionDevolucion.ReembolsoDinero &&
                    d.RegistrarEgresoCaja)
                .Sum(d => d.TotalDevolucion),
            MotivosFrecuentes = motivosFrecuentes
                .OrderByDescending(kvp => kvp.Value)
                .Take(4)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };

        return View(viewModel);
    }

    private static List<Devolucion> FiltrarDevoluciones(
        IEnumerable<Devolucion> devoluciones,
        string search,
        string? estado,
        string? resolucion)
    {
        var query = devoluciones.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(d =>
                Contiene(d.NumeroDevolucion, search) ||
                Contiene(d.Cliente?.NombreCompleto, search) ||
                Contiene(d.Cliente?.NumeroDocumento, search) ||
                Contiene(d.Venta?.Numero, search) ||
                Contiene(d.Descripcion, search));
        }

        if (Enum.TryParse<EstadoDevolucion>(estado, true, out var estadoParsed))
        {
            query = query.Where(d => d.Estado == estadoParsed);
        }

        if (Enum.TryParse<TipoResolucionDevolucion>(resolucion, true, out var resolucionParsed))
        {
            query = query.Where(d => d.TipoResolucion == resolucionParsed);
        }

        return query
            .OrderByDescending(d => d.FechaDevolucion)
            .ThenByDescending(d => d.Id)
            .ToList();
    }

    private static List<Garantia> FiltrarGarantias(
        IEnumerable<Garantia> garantias,
        string search,
        string? garantiaEstado,
        string? garantiaVentana)
    {
        var hoy = DateTime.UtcNow.Date;
        var query = garantias.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(g =>
                Contiene(g.NumeroGarantia, search) ||
                Contiene(g.Cliente?.NombreCompleto, search) ||
                Contiene(g.Cliente?.NumeroDocumento, search) ||
                Contiene(g.Producto?.Nombre, search) ||
                Contiene(g.Producto?.Codigo, search) ||
                Contiene(g.ObservacionesActivacion, search));
        }

        if (Enum.TryParse<EstadoGarantia>(garantiaEstado, true, out var estadoParsed))
        {
            query = query.Where(g => g.Estado == estadoParsed);
        }

        query = garantiaVentana?.Trim().ToLowerInvariant() switch
        {
            "proximas" => query.Where(g => g.FechaVencimiento.Date >= hoy && g.FechaVencimiento.Date <= hoy.AddDays(30)),
            "vencidas" => query.Where(g => g.FechaVencimiento.Date < hoy || g.Estado == EstadoGarantia.Vencida),
            "enuso" => query.Where(g => g.Estado == EstadoGarantia.EnUso),
            "extendidas" => query.Where(g => g.GarantiaExtendida),
            _ => query
        };

        return query
            .OrderBy(g => g.FechaVencimiento)
            .ThenByDescending(g => g.Id)
            .ToList();
    }

    private static bool Contiene(string? source, string search)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               source.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private FileContentResult ExportarDevolucionesCsv(IEnumerable<Devolucion> devoluciones)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id;Cliente;Documento;Venta;Motivo;Resolucion;Impacto;Estado;Fecha;Monto");

        foreach (var devolucion in devoluciones)
        {
            var impacto = devolucion.TipoResolucion == TipoResolucionDevolucion.ReembolsoDinero
                ? (devolucion.RegistrarEgresoCaja ? "Reembolso por caja" : "Reembolso sin caja")
                : devolucion.TipoResolucion == TipoResolucionDevolucion.NotaCredito
                    ? "Nota de credito"
                    : "Cambio / reposicion";

            sb.AppendLine(string.Join(";",
                EscapeCsv(devolucion.NumeroDevolucion),
                EscapeCsv(devolucion.Cliente?.NombreCompleto),
                EscapeCsv(devolucion.Cliente?.NumeroDocumento),
                EscapeCsv(devolucion.Venta?.Numero),
                EscapeCsv(devolucion.Motivo.GetDisplayName()),
                EscapeCsv(devolucion.TipoResolucion.GetDisplayName()),
                EscapeCsv(impacto),
                EscapeCsv(devolucion.Estado.GetDisplayName()),
                EscapeCsv(devolucion.FechaDevolucion.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)),
                EscapeCsv(devolucion.TotalDevolucion.ToString("F2", CultureInfo.InvariantCulture))));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"devoluciones-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    private FileContentResult ExportarGarantiasCsv(IEnumerable<Garantia> garantias)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Garantia;Cliente;Documento;Producto;Codigo;Estado;Inicio;Vencimiento;CoberturaMeses;Extendida;Observacion");

        foreach (var garantia in garantias)
        {
            sb.AppendLine(string.Join(";",
                EscapeCsv(garantia.NumeroGarantia),
                EscapeCsv(garantia.Cliente?.NombreCompleto),
                EscapeCsv(garantia.Cliente?.NumeroDocumento),
                EscapeCsv(garantia.Producto?.Nombre),
                EscapeCsv(garantia.Producto?.Codigo),
                EscapeCsv(garantia.Estado.GetDisplayName()),
                EscapeCsv(garantia.FechaInicio.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                EscapeCsv(garantia.FechaVencimiento.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                EscapeCsv(garantia.MesesGarantia.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(garantia.GarantiaExtendida ? "Si" : "No"),
                EscapeCsv(garantia.ObservacionesActivacion)));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"garantias-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    private static string EscapeCsv(string? value)
    {
        var sanitized = (value ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{sanitized}\"";
    }

    /// <summary>
    /// Formulario para crear nueva devolución
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Create(int? ventaId)
    {
        var viewModel = new CrearDevolucionViewModel();

        if (ventaId.HasValue)
        {
            var venta = await _ventaService.GetByIdAsync(ventaId.Value);
            if (venta != null)
            {
                viewModel.VentaId = venta.Id;
                viewModel.ClienteId = venta.ClienteId;
                viewModel.NumeroVenta = venta.Numero;
                viewModel.ClienteNombre = venta.ClienteNombre;
                viewModel.FechaVenta = venta.FechaVenta;
                viewModel.TotalVenta = venta.Total;
                viewModel.DiasDesdeVenta = await _devolucionService.ObtenerDiasDesdeVentaAsync(venta.Id);
                viewModel.PuedeDevolver = await _devolucionService.PuedeDevolverVentaAsync(venta.Id);

                // Cargar productos de la venta
                foreach (var detalle in venta.Detalles)
                {
                    viewModel.Productos.Add(new ProductoDevolucionViewModel
                    {
                        ProductoId = detalle.ProductoId,
                        ProductoNombre = detalle.ProductoNombre ?? "Producto",
                        CantidadComprada = detalle.Cantidad,
                        PrecioUnitario = detalle.PrecioUnitario,
                        CantidadDevolver = 0
                    });
                }
            }
        }
        else
        {
            // Si no hay ventaId, cargar lista de ventas disponibles para devolución
            var ventasDisponibles = await _ventaService.GetAllAsync(new VentaFilterViewModel
            {
                Estado = Models.Enums.EstadoVenta.Confirmada
            });

            // También incluir facturadas y entregadas
            var ventasFacturadas = await _ventaService.GetAllAsync(new VentaFilterViewModel
            {
                Estado = Models.Enums.EstadoVenta.Facturada
            });

            var ventasEntregadas = await _ventaService.GetAllAsync(new VentaFilterViewModel
            {
                Estado = Models.Enums.EstadoVenta.Entregada
            });

            var todasVentas = ventasDisponibles
                .Concat(ventasFacturadas)
                .Concat(ventasEntregadas)
                .OrderByDescending(v => v.FechaVenta)
                .ToList();

            ViewBag.Ventas = todasVentas;
        }

        await CargarListasAsync();
        return View(viewModel);
    }

    /// <summary>
    /// Procesar creación de devolución
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CrearDevolucionViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await CargarListasAsync();
            return View(model);
        }

        try
        {
            // Validar que la venta existe y obtener el cliente correcto
            var venta = await _ventaService.GetByIdAsync(model.VentaId);
            if (venta == null)
            {
                ModelState.AddModelError("", "La venta especificada no existe");
                await CargarListasAsync();
                return View(model);
            }

            // Validar que el cliente de la devolución coincide con el cliente de la venta
            if (model.ClienteId != venta.ClienteId)
            {
                ModelState.AddModelError("", "El cliente de la devolución no coincide con el cliente de la venta");
                await CargarListasAsync();
                return View(model);
            }

            // Validar que puede devolver
            if (!await _devolucionService.PuedeDevolverVentaAsync(model.VentaId))
            {
                ModelState.AddModelError("", "Ha excedido el plazo para devolver esta venta (30 días)");
                await CargarListasAsync();
                return View(model);
            }

            // Crear devolución (el ClienteId ya está validado que coincide con la venta)
            var devolucion = new Devolucion
            {
                VentaId = model.VentaId,
                ClienteId = model.ClienteId,
                Motivo = model.Motivo,
                Descripcion = model.Descripcion,
                FechaDevolucion = DateTime.Now
            };

            // Crear detalles
            var detalles = model.Productos
                .Where(p => p.CantidadDevolver > 0)
                .Select(p => new DevolucionDetalle
                {
                    ProductoId = p.ProductoId,
                    Cantidad = p.CantidadDevolver,
                    PrecioUnitario = p.PrecioUnitario,
                    EstadoProducto = p.EstadoProducto,
                    AccesoriosCompletos = p.AccesoriosCompletos,
                    AccesoriosFaltantes = p.AccesoriosFaltantes,
                    TieneGarantia = p.TieneGarantia,
                    ObservacionesTecnicas = p.ObservacionesTecnicas,
                    AccionRecomendada = DeterminarAccionRecomendada(p.EstadoProducto)
                })
                .ToList();

            if (!detalles.Any())
            {
                ModelState.AddModelError("", "Debe seleccionar al menos un producto para devolver");
                await CargarListasAsync();
                return View(model);
            }

            await _devolucionService.CrearDevolucionAsync(devolucion, detalles);

            TempData["Success"] = $"Devolución {devolucion.NumeroDevolucion} creada exitosamente. Aguarde aprobación.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Error al crear devolución: {ex.Message}");
            await CargarListasAsync();
            return View(model);
        }
    }

    [HttpGet]
    [PermisoRequerido(Modulo = "devoluciones", Accion = "create")]
    public async Task<IActionResult> GetQuickCreateContext(int ventaId)
    {
        if (ventaId <= 0)
        {
            return BadRequest(new { success = false, message = "La venta indicada no es válida." });
        }

        var venta = await _ventaService.GetByIdAsync(ventaId);
        if (venta == null)
        {
            return NotFound(new { success = false, message = "No se encontró la venta seleccionada." });
        }

        var puedeDevolver = await _devolucionService.PuedeDevolverVentaAsync(ventaId);
        var diasDesdeVenta = await _devolucionService.ObtenerDiasDesdeVentaAsync(ventaId);
        var permiteImpactoCaja = PermiteImpactoCaja(venta.TipoPago);

        return Json(new
        {
            success = true,
            venta = new
            {
                id = venta.Id,
                numero = venta.Numero,
                clienteId = venta.ClienteId,
                clienteNombre = venta.ClienteNombre,
                fechaVenta = venta.FechaVenta.ToString("dd/MM/yyyy"),
                total = venta.Total,
                totalDisplay = venta.Total.ToString("C2"),
                tipoPago = venta.TipoPago.ToString(),
                tipoPagoDisplay = ObtenerTipoPagoDisplay(venta.TipoPago),
                diasDesdeVenta,
                puedeDevolver,
                permiteImpactoCaja,
                mensajeCaja = permiteImpactoCaja
                    ? "Si elegís reembolso, podés registrar el egreso en caja al completar la devolución."
                    : "Esta venta no genera impacto automático en caja. Si hay reintegro, deberá resolverse por fuera de caja.",
                items = venta.Detalles.Select(detalle => new
                {
                    productoId = detalle.ProductoId,
                    productoNombre = detalle.ProductoNombre ?? "Producto",
                    productoCodigo = detalle.ProductoCodigo,
                    cantidadDisponible = detalle.Cantidad,
                    precioUnitario = detalle.PrecioUnitario,
                    precioUnitarioDisplay = detalle.PrecioUnitario.ToString("C2"),
                    subtotalDisplay = detalle.Subtotal.ToString("C2")
                })
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermisoRequerido(Modulo = "devoluciones", Accion = "create")]
    public async Task<IActionResult> QuickCreate(CrearDevolucionRapidaViewModel model)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ObtenerPrimerErrorModelState()
                });
            }

            var venta = await _ventaService.GetByIdAsync(model.VentaId);
            if (venta == null)
            {
                return NotFound(new { success = false, message = "La venta ya no existe." });
            }

            if (model.ClienteId != venta.ClienteId)
            {
                return BadRequest(new { success = false, message = "El cliente de la devolución no coincide con el de la venta." });
            }

            if (!await _devolucionService.PuedeDevolverVentaAsync(model.VentaId))
            {
                return BadRequest(new { success = false, message = "La venta ya no está dentro de la ventana habilitada para devoluciones." });
            }

            var itemsSeleccionados = model.Items
                .Where(item => item.Seleccionado && item.CantidadDevolver > 0)
                .ToList();

            if (!itemsSeleccionados.Any())
            {
                return BadRequest(new { success = false, message = "Seleccioná al menos un producto y una cantidad a devolver." });
            }

            foreach (var item in itemsSeleccionados)
            {
                if (item.CantidadDevolver > item.CantidadDisponible)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"La cantidad a devolver de {item.ProductoNombre} supera lo vendido."
                    });
                }
            }

            var registrarEgresoCaja = model.TipoResolucion == TipoResolucionDevolucion.ReembolsoDinero
                && model.RegistrarEgresoCaja
                && PermiteImpactoCaja(venta.TipoPago);

            var devolucion = new Devolucion
            {
                VentaId = model.VentaId,
                ClienteId = model.ClienteId,
                Motivo = model.Motivo,
                Descripcion = model.Descripcion.Trim(),
                TipoResolucion = model.TipoResolucion,
                RegistrarEgresoCaja = registrarEgresoCaja,
                ObservacionesInternas = ConstruirObservacionesInternas(model.TipoResolucion, registrarEgresoCaja, venta.TipoPago)
            };

            var detalles = itemsSeleccionados
                .Select(item => new DevolucionDetalle
                {
                    ProductoId = item.ProductoId,
                    Cantidad = item.CantidadDevolver,
                    EstadoProducto = item.EstadoProducto,
                    AccionRecomendada = DeterminarAccionRecomendada(item.EstadoProducto)
                })
                .ToList();

            var creada = await _devolucionService.CrearDevolucionAsync(devolucion, detalles);

            return Json(new
            {
                success = true,
                numeroDevolucion = creada.NumeroDevolucion,
                message = $"Devolución {creada.NumeroDevolucion} creada. Quedó pendiente de aprobación."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Ver detalles de una devolución
    /// </summary>
    public async Task<IActionResult> Detalles(int id)
    {
        var devolucion = await _devolucionService.ObtenerDevolucionAsync(id);
        if (devolucion == null)
        {
            TempData["Error"] = "Devolución no encontrada";
            return RedirectToAction(nameof(Index));
        }

        var detalles = await _devolucionService.ObtenerDetallesDevolucionAsync(id);

        var viewModel = new DevolucionDetallesViewModel
        {
            Devolucion = devolucion,
            Detalles = detalles,
            NotaCredito = devolucion.NotaCredito,
            RMA = devolucion.RMA
        };

        await CargarListasAsync();
        return View(viewModel);
    }

    /// <summary>
    /// Aprobar devolución
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Aprobar(int id, byte[]? rowVersion)
    {
        try
        {
            if (rowVersion is null || rowVersion.Length == 0)
            {
                TempData["Error"] = "Falta información de concurrencia (RowVersion). Recargá la página e intentá nuevamente.";
                return RedirectToAction(nameof(Detalles), new { id });
            }

            var devolucion = await _devolucionService.ObtenerDevolucionAsync(id);
            if (devolucion == null)
            {
                TempData["Error"] = "Devolución no encontrada.";
                return RedirectToAction(nameof(Index));
            }

            var usuario = await _userManager.GetUserAsync(User);
            await _devolucionService.AprobarDevolucionAsync(id, usuario?.UserName ?? Roles.Administrador, rowVersion);

            TempData["Success"] = devolucion.TipoResolucion switch
            {
                TipoResolucionDevolucion.NotaCredito => "Devolución aprobada. Se generó la nota de crédito correspondiente.",
                TipoResolucionDevolucion.ReembolsoDinero => "Devolución aprobada. Quedó lista para completar el reembolso.",
                TipoResolucionDevolucion.CambioMismoProducto => "Devolución aprobada. Quedó lista para gestionar la reposición del mismo producto.",
                TipoResolucionDevolucion.CambioOtroProducto => "Devolución aprobada. Quedó lista para gestionar el cambio por otro producto.",
                _ => "Devolución aprobada exitosamente."
            };
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al aprobar devolución: {ex.Message}";
        }

        return RedirectToAction(nameof(Detalles), new { id });
    }

    /// <summary>
    /// Rechazar devolución
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rechazar(int id, string motivo, byte[]? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            TempData["Error"] = "Debe proporcionar un motivo para rechazar la devolución";
            return RedirectToAction(nameof(Detalles), new { id });
        }

        if (rowVersion is null || rowVersion.Length == 0)
        {
            TempData["Error"] = "Falta información de concurrencia (RowVersion). Recargá la página e intentá nuevamente.";
            return RedirectToAction(nameof(Detalles), new { id });
        }

        try
        {
            await _devolucionService.RechazarDevolucionAsync(id, motivo, rowVersion);
            TempData["Success"] = "Devolución rechazada";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al rechazar devolución: {ex.Message}";
        }

        return RedirectToAction(nameof(Detalles), new { id });
    }

    /// <summary>
    /// Completar devolución (procesar stock)
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Completar(int id, byte[]? rowVersion)
    {
        try
        {
            if (rowVersion is null || rowVersion.Length == 0)
            {
                TempData["Error"] = "Falta información de concurrencia (RowVersion). Recargá la página e intentá nuevamente.";
                return RedirectToAction(nameof(Detalles), new { id });
            }

            await _devolucionService.CompletarDevolucionAsync(id, rowVersion);
            TempData["Success"] = "Devolución completada. Stock actualizado según las acciones definidas.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al completar devolución: {ex.Message}";
        }

        return RedirectToAction(nameof(Detalles), new { id });
    }

    #endregion

    #region Garantías

    /// <summary>
    /// Lista de garantías
    /// </summary>
    public async Task<IActionResult> Garantias()
    {
        return RedirectToAction(nameof(Index), new { tab = "garantias" });
    }

    #endregion

    #region RMAs

    /// <summary>
    /// Estadísticas de RMAs y devoluciones
    /// </summary>
    public async Task<IActionResult> RMAs(DateTime? desde, DateTime? hasta)
    {
        var fechaDesde = desde ?? DateTime.Now.AddMonths(-1);
        var fechaHasta = hasta ?? DateTime.Now;

        var viewModel = new EstadisticasDevolucionViewModel
        {
            FechaDesde = fechaDesde,
            FechaHasta = fechaHasta,
            DevolucionesPorMotivo = await _devolucionService.ObtenerEstadisticasMotivoDevolucionAsync(fechaDesde, fechaHasta),
            ProductosMasDevueltos = await _devolucionService.ObtenerProductosMasDevueltosAsync(10),
            MontoTotalDevuelto = await _devolucionService.ObtenerTotalDevolucionesPeriodoAsync(fechaDesde, fechaHasta),
            RMAsPendientes = await _devolucionService.ObtenerCantidadRMAsPendientesAsync()
        };

        viewModel.TotalDevoluciones = viewModel.DevolucionesPorMotivo.Values.Sum();

        return View(viewModel);
    }

    /// <summary>
    /// Crear RMA para una devolución
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearRMA(int devolucionId, int proveedorId, string motivoSolicitud, byte[]? devolucionRowVersion)
    {
        try
        {
            if (devolucionRowVersion is null || devolucionRowVersion.Length == 0)
            {
                TempData["Error"] = "Falta información de concurrencia (RowVersion). Recargá la devolución e intentá nuevamente.";
                return RedirectToAction(nameof(Detalles), new { id = devolucionId });
            }

            var rma = new RMA
            {
                DevolucionId = devolucionId,
                ProveedorId = proveedorId,
                MotivoSolicitud = motivoSolicitud
            };

            await _devolucionService.CrearRMAAsync(rma, devolucionRowVersion);
            TempData["Success"] = $"RMA {rma.NumeroRMA} creado exitosamente";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al crear RMA: {ex.Message}";
        }

        return RedirectToAction(nameof(Detalles), new { id = devolucionId });
    }

    #endregion

    #region Notas de Crédito

    /// <summary>
    /// Ver notas de crédito de un cliente
    /// </summary>
    public async Task<IActionResult> NotasCredito(int clienteId)
    {
        var cliente = await _clienteService.GetByIdAsync(clienteId);
        if (cliente == null)
        {
            TempData["Error"] = "Cliente no encontrado";
            return RedirectToAction("Index", "Cliente");
        }

        var todasNotas = await _devolucionService.ObtenerNotasCreditoPorClienteAsync(clienteId);
        var creditoDisponible = await _devolucionService.ObtenerCreditoDisponibleClienteAsync(clienteId);

        var viewModel = new NotasCreditoClienteViewModel
        {
            ClienteId = clienteId,
            ClienteNombre = cliente.NombreCompleto ?? "Cliente",
            NotasVigentes = todasNotas.Where(nc => nc.MontoDisponible > 0 && nc.Estado == EstadoNotaCredito.Vigente).ToList(),
            NotasUtilizadas = todasNotas.Where(nc => nc.MontoDisponible == 0 || nc.Estado == EstadoNotaCredito.UtilizadaTotalmente).ToList(),
            CreditoTotalDisponible = creditoDisponible
        };

        return View(viewModel);
    }

    #endregion

    #region Estadísticas

    /// <summary>
    /// Estadísticas de devoluciones
    /// </summary>
    public async Task<IActionResult> Estadisticas(DateTime? desde, DateTime? hasta)
    {
        var fechaDesde = desde ?? DateTime.Now.AddMonths(-1);
        var fechaHasta = hasta ?? DateTime.Now;

        var viewModel = new EstadisticasDevolucionViewModel
        {
            FechaDesde = fechaDesde,
            FechaHasta = fechaHasta,
            DevolucionesPorMotivo = await _devolucionService.ObtenerEstadisticasMotivoDevolucionAsync(fechaDesde, fechaHasta),
            ProductosMasDevueltos = await _devolucionService.ObtenerProductosMasDevueltosAsync(10),
            MontoTotalDevuelto = await _devolucionService.ObtenerTotalDevolucionesPeriodoAsync(fechaDesde, fechaHasta),
            RMAsPendientes = await _devolucionService.ObtenerCantidadRMAsPendientesAsync()
        };

        viewModel.TotalDevoluciones = viewModel.DevolucionesPorMotivo.Values.Sum();

        return View(viewModel);
    }

    #endregion

    #region Métodos Privados

    private async Task CargarListasAsync()
    {
        ViewBag.Clientes = new SelectList(await _clienteService.GetAllAsync(), "Id", "NombreCompleto");
        ViewBag.Proveedores = new SelectList(await _proveedorService.GetAllAsync(), "Id", "RazonSocial");
    }

    private AccionProducto DeterminarAccionRecomendada(EstadoProductoDevuelto estado)
    {
        return estado switch
        {
            EstadoProductoDevuelto.Nuevo => AccionProducto.ReintegrarStock,
            EstadoProductoDevuelto.NuevoSellado => AccionProducto.ReintegrarStock,
            EstadoProductoDevuelto.UsadoBuenEstado => AccionProducto.ReintegrarStock,
            EstadoProductoDevuelto.AbiertoSinUso => AccionProducto.Cuarentena,
            EstadoProductoDevuelto.UsadoConDetalles => AccionProducto.Cuarentena,
            EstadoProductoDevuelto.Marcado => AccionProducto.Cuarentena,
            EstadoProductoDevuelto.Defectuoso => AccionProducto.DevolverProveedor,
            EstadoProductoDevuelto.Incompleto => AccionProducto.Cuarentena,
            EstadoProductoDevuelto.Danado => AccionProducto.Descarte,
            _ => AccionProducto.Cuarentena
        };
    }

    private string ObtenerPrimerErrorModelState()
    {
        return ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .FirstOrDefault(error => !string.IsNullOrWhiteSpace(error))
            ?? "No se pudo procesar la devolución.";
    }

    private static bool PermiteImpactoCaja(TipoPago tipoPago)
    {
        return tipoPago != TipoPago.CreditoPersonal && tipoPago != TipoPago.CuentaCorriente;
    }

    private static string ObtenerTipoPagoDisplay(TipoPago tipoPago)
    {
        return tipoPago switch
        {
            TipoPago.Efectivo => "Efectivo",
            TipoPago.Transferencia => "Transferencia",
            TipoPago.TarjetaDebito => "Tarjeta Débito",
            TipoPago.TarjetaCredito => "Tarjeta Crédito",
            TipoPago.Cheque => "Cheque",
            TipoPago.CreditoPersonal => "Crédito Personal",
            TipoPago.MercadoPago => "Mercado Pago",
            TipoPago.CuentaCorriente => "Cuenta Corriente",
            TipoPago.Tarjeta => "Tarjeta",
            _ => tipoPago.ToString()
        };
    }

    private static string ConstruirObservacionesInternas(
        TipoResolucionDevolucion tipoResolucion,
        bool registrarEgresoCaja,
        TipoPago tipoPagoOriginal)
    {
        var observaciones = new List<string>
        {
            $"Resolución solicitada: {tipoResolucion.GetDisplayName()}",
            $"Pago original: {ObtenerTipoPagoDisplay(tipoPagoOriginal)}"
        };

        if (tipoResolucion == TipoResolucionDevolucion.ReembolsoDinero)
        {
            observaciones.Add(registrarEgresoCaja
                ? "Registrar egreso en caja al completar."
                : "No registra egreso automático en caja.");
        }
        else
        {
            observaciones.Add("No genera movimiento automático en caja. La reposición se resuelve por circuito operativo.");
        }

        return string.Join(" | ", observaciones);
    }

    #endregion
}

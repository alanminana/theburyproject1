using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class MercadoLibreConfiguracionService : IMercadoLibreConfiguracionService
    {
        private static readonly string[] ReglasRedondeoValidas = { "ninguno", "decena", "centena", "mil" };

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ILogger<MercadoLibreConfiguracionService> _logger;

        public MercadoLibreConfiguracionService(
            IDbContextFactory<AppDbContext> contextFactory,
            ILogger<MercadoLibreConfiguracionService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<MercadoLibreConfiguracion> GetAsync(CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var config = await context.MercadoLibreConfiguraciones
                .AsNoTracking()
                .OrderBy(c => c.Id)
                .FirstOrDefaultAsync(ct);

            if (config is not null)
                return config;

            // Primera vez: crear con defaults seguros (simulación activa, automatizaciones apagadas).
            config = new MercadoLibreConfiguracion { CreatedBy = "Sistema" };

            // Si hay una única cuenta activa, usarla como default razonable.
            var cuentaUnica = await context.MercadoLibreAccounts
                .Where(a => a.Activa)
                .Select(a => (int?)a.Id)
                .ToListAsync(ct);

            if (cuentaUnica.Count == 1)
                config.AccountId = cuentaUnica[0];

            context.MercadoLibreConfiguraciones.Add(config);
            await context.SaveChangesAsync(ct);

            _logger.LogInformation("Configuración de Mercado Libre creada con defaults seguros (Id {Id})", config.Id);
            return config;
        }

        public async Task<MercadoLibreConfiguracionViewModel> GetViewModelAsync(CancellationToken ct = default)
        {
            var config = await GetAsync(ct);

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var vm = new MercadoLibreConfiguracionViewModel
            {
                AccountId = config.AccountId,
                ListaPrecioId = config.ListaPrecioId,
                SucursalId = config.SucursalId,
                ClienteMercadoLibreId = config.ClienteMercadoLibreId,
                AjusteCanalPorcentaje = config.AjusteCanalPorcentaje,
                ComisionEstimadaPorcentaje = config.ComisionEstimadaPorcentaje,
                CostoEnvioEstimado = config.CostoEnvioEstimado,
                MargenMinimoPorcentaje = config.MargenMinimoPorcentaje,
                ReglaRedondeo = config.ReglaRedondeo,
                OrigenStock = config.OrigenStock,
                SyncAutomaticaStock = config.SyncAutomaticaStock,
                SyncAutomaticaPrecio = config.SyncAutomaticaPrecio,
                ImportacionAutomaticaOrdenes = config.ImportacionAutomaticaOrdenes,
                CrearVentaAutomatica = config.CrearVentaAutomatica,
                PermitirPublicacionDesdeErp = config.PermitirPublicacionDesdeErp,
                ModoSimulacion = config.ModoSimulacion,
                PoliticaDevolucion = config.PoliticaDevolucion
            };

            vm.CuentasDisponibles = await context.MercadoLibreAccounts
                .AsNoTracking()
                .Where(a => a.Activa)
                .OrderBy(a => a.Nickname)
                .Select(a => new ValueTuple<int, string>(a.Id, a.Nickname))
                .ToListAsync(ct);

            vm.ListasPreciosDisponibles = await context.ListasPrecios
                .AsNoTracking()
                .Where(l => l.Activa && !l.IsDeleted)
                .OrderBy(l => l.Orden)
                .Select(l => new ValueTuple<int, string>(l.Id, l.Nombre))
                .ToListAsync(ct);

            vm.SucursalesDisponibles = await context.Sucursales
                .AsNoTracking()
                .Where(s => s.Activa && !s.IsDeleted)
                .OrderBy(s => s.Nombre)
                .Select(s => new ValueTuple<int, string>(s.Id, s.Nombre))
                .ToListAsync(ct);

            if (config.ClienteMercadoLibreId.HasValue)
            {
                vm.ClienteMercadoLibreNombre = await context.Clientes
                    .AsNoTracking()
                    .Where(c => c.Id == config.ClienteMercadoLibreId.Value)
                    .Select(c => c.Apellido + ", " + c.Nombre + " (" + c.NumeroDocumento + ")")
                    .FirstOrDefaultAsync(ct);
            }

            return vm;
        }

        public async Task GuardarAsync(
            MercadoLibreConfiguracionViewModel viewModel, string usuario, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var config = await context.MercadoLibreConfiguraciones
                .OrderBy(c => c.Id)
                .FirstOrDefaultAsync(ct);

            if (config is null)
            {
                config = new MercadoLibreConfiguracion { CreatedBy = usuario };
                context.MercadoLibreConfiguraciones.Add(config);
            }

            // Validar referencias antes de persistir.
            if (viewModel.AccountId.HasValue &&
                !await context.MercadoLibreAccounts.AnyAsync(a => a.Id == viewModel.AccountId.Value && a.Activa, ct))
                throw new InvalidOperationException("La cuenta de Mercado Libre seleccionada no existe o está inactiva.");

            if (viewModel.ListaPrecioId.HasValue &&
                !await context.ListasPrecios.AnyAsync(l => l.Id == viewModel.ListaPrecioId.Value && l.Activa && !l.IsDeleted, ct))
                throw new InvalidOperationException("La lista de precios seleccionada no existe o está inactiva.");

            if (viewModel.SucursalId.HasValue &&
                !await context.Sucursales.AnyAsync(s => s.Id == viewModel.SucursalId.Value && s.Activa && !s.IsDeleted, ct))
                throw new InvalidOperationException("La sucursal seleccionada no existe o está inactiva.");

            if (viewModel.ClienteMercadoLibreId.HasValue &&
                !await context.Clientes.AnyAsync(c => c.Id == viewModel.ClienteMercadoLibreId.Value && !c.IsDeleted, ct))
                throw new InvalidOperationException("El cliente seleccionado para ventas de Mercado Libre no existe.");

            var regla = (viewModel.ReglaRedondeo ?? "ninguno").Trim().ToLowerInvariant();
            if (!ReglasRedondeoValidas.Contains(regla))
                throw new InvalidOperationException($"Regla de redondeo inválida: '{viewModel.ReglaRedondeo}'.");

            if (viewModel.OrigenStock == MercadoLibreOrigenStock.DepositoSucursal)
                throw new InvalidOperationException(
                    "El origen 'depósito/sucursal' no está disponible: el ERP no maneja stock por sucursal.");

            if (viewModel.OrigenStock == MercadoLibreOrigenStock.UnidadFisicaEspecifica)
                throw new InvalidOperationException(
                    "'Unidad física específica' solo puede configurarse por publicación, no como origen global.");

            config.AccountId = viewModel.AccountId;
            config.ListaPrecioId = viewModel.ListaPrecioId;
            config.SucursalId = viewModel.SucursalId;
            config.ClienteMercadoLibreId = viewModel.ClienteMercadoLibreId;
            config.AjusteCanalPorcentaje = viewModel.AjusteCanalPorcentaje;
            config.ComisionEstimadaPorcentaje = viewModel.ComisionEstimadaPorcentaje;
            config.CostoEnvioEstimado = viewModel.CostoEnvioEstimado;
            config.MargenMinimoPorcentaje = viewModel.MargenMinimoPorcentaje;
            config.ReglaRedondeo = regla;
            config.OrigenStock = viewModel.OrigenStock;
            config.SyncAutomaticaStock = viewModel.SyncAutomaticaStock;
            config.SyncAutomaticaPrecio = viewModel.SyncAutomaticaPrecio;
            config.ImportacionAutomaticaOrdenes = viewModel.ImportacionAutomaticaOrdenes;
            config.CrearVentaAutomatica = viewModel.CrearVentaAutomatica;
            config.PermitirPublicacionDesdeErp = viewModel.PermitirPublicacionDesdeErp;
            // ModoSimulacion ya NO se controla desde esta pantalla (Checkpoint 2/3): dejó de
            // ser un toggle visible que compite con "Publicación REAL" del borrador. Sigue
            // existiendo como cerrojo interno de sync/precio/mensajes y se preserva tal cual
            // está persistido (nace en true). No se sobrescribe desde el viewModel.
            config.PoliticaDevolucion = viewModel.PoliticaDevolucion;
            config.UpdatedAt = DateTime.UtcNow;
            config.UpdatedBy = usuario;

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Configuración ML guardada por {Usuario}. Simulación:{Simulacion} VentaAuto:{VentaAuto} SyncStock:{SyncStock} SyncPrecio:{SyncPrecio}",
                usuario, config.ModoSimulacion, config.CrearVentaAutomatica, config.SyncAutomaticaStock, config.SyncAutomaticaPrecio);
        }
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Data
{
    /// <summary>
    /// Contexto principal de la base de datos del sistema.
    /// </summary>
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        private readonly IHttpContextAccessor? _httpContextAccessor;

        private static readonly DateTime SeedCreatedAtUtc =
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public AppDbContext(
            DbContextOptions<AppDbContext> options,
            IHttpContextAccessor? httpContextAccessor = null)
            : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public DbSet<Categoria> Categorias { get; set; }
        public DbSet<Marca> Marcas { get; set; }
        public DbSet<Producto> Productos { get; set; }
        public DbSet<AlicuotaIVA> AlicuotasIVA { get; set; }
        public DbSet<ProductoCaracteristica> ProductosCaracteristicas { get; set; }
        public DbSet<PrecioHistorico> PreciosHistoricos { get; set; }

        public DbSet<Proveedor> Proveedores { get; set; }
        public DbSet<ProveedorProducto> ProveedorProductos { get; set; }
        public DbSet<ProveedorMarca> ProveedorMarcas { get; set; }
        public DbSet<ProveedorCategoria> ProveedorCategorias { get; set; }

        public DbSet<OrdenCompra> OrdenesCompra { get; set; }
        public DbSet<OrdenCompraDetalle> OrdenCompraDetalles { get; set; }

        public DbSet<MovimientoStock> MovimientosStock { get; set; }
        public DbSet<Cheque> Cheques { get; set; }

        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<ClienteCreditoConfiguracion> ClientesCreditoConfiguraciones { get; set; }
        public DbSet<ClientePuntajeHistorial> ClientesPuntajeHistorial { get; set; }
        public DbSet<PuntajeCreditoLimite> PuntajesCreditoLimite { get; set; }
        public DbSet<Credito> Creditos { get; set; }
        public DbSet<Cuota> Cuotas { get; set; }
        public DbSet<Garante> Garantes { get; set; }
        public DbSet<DocumentoCliente> DocumentosCliente { get; set; }
        public DbSet<EvaluacionCredito> EvaluacionesCredito { get; set; } = null!;

        public DbSet<Venta> Ventas { get; set; }
        public DbSet<VentaDetalle> VentaDetalles { get; set; }
        public DbSet<Factura> Facturas { get; set; }
        public DbSet<PlantillaContratoCredito> PlantillasContratoCredito { get; set; }
        public DbSet<ContratoVentaCredito> ContratosVentaCredito { get; set; }
        public DbSet<ConfiguracionPago> ConfiguracionesPago { get; set; }
        public DbSet<ConfiguracionTarjeta> ConfiguracionesTarjeta { get; set; }
        public DbSet<ConfiguracionPagoPlan> ConfiguracionPagoPlanes { get; set; }
        public DbSet<ProductoCondicionPago> ProductoCondicionesPago { get; set; }
        public DbSet<ProductoCondicionPagoTarjeta> ProductoCondicionesPagoTarjeta { get; set; }
        public DbSet<ProductoCondicionPagoPlan> ProductoCondicionPagoPlanes { get; set; }
        public DbSet<PerfilCredito> PerfilesCredito { get; set; }
        public DbSet<DatosTarjeta> DatosTarjeta { get; set; }
        public DbSet<DatosCheque> DatosCheque { get; set; }
        public DbSet<VentaCreditoCuota> VentaCreditoCuotas { get; set; }

        public DbSet<ConfiguracionMora> ConfiguracionesMora { get; set; }
        public DbSet<AlertaMora> AlertasMora { get; set; }
        public DbSet<ConfiguracionCredito> ConfiguracionesCredito { get; set; }
        public DbSet<ConfiguracionRentabilidad> ConfiguracionesRentabilidad { get; set; }
        public DbSet<LogMora> LogsMora { get; set; }
        public DbSet<AlertaCobranza> AlertasCobranza { get; set; }
        public DbSet<HistorialContacto> HistorialContactos { get; set; }
        public DbSet<AcuerdoPago> AcuerdosPago { get; set; }
        public DbSet<CuotaAcuerdo> CuotasAcuerdo { get; set; }
        public DbSet<PlantillaNotificacionMora> PlantillasNotificacionMora { get; set; }
        public DbSet<AlertaStock> AlertasStock { get; set; }

        public DbSet<UmbralAutorizacion> UmbralesAutorizacion { get; set; }
        public DbSet<SolicitudAutorizacion> SolicitudesAutorizacion { get; set; }

        public DbSet<Devolucion> Devoluciones { get; set; }
        public DbSet<DevolucionDetalle> DevolucionDetalles { get; set; }
        public DbSet<Garantia> Garantias { get; set; }
        public DbSet<RMA> RMAs { get; set; }
        public DbSet<NotaCredito> NotasCredito { get; set; }

        public DbSet<Caja> Cajas { get; set; }
        public DbSet<AperturaCaja> AperturasCaja { get; set; }
        public DbSet<MovimientoCaja> MovimientosCaja { get; set; }
        public DbSet<CierreCaja> CierresCaja { get; set; }

        public DbSet<Notificacion> Notificaciones { get; set; }

        public DbSet<Sucursal> Sucursales { get; set; }
        public DbSet<RolPermiso> RolPermisos { get; set; }
        public DbSet<RolMetadata> RolMetadatas { get; set; }
        public DbSet<SeguridadEventoAuditoria> SeguridadEventosAuditoria { get; set; }
        public DbSet<ModuloSistema> ModulosSistema { get; set; }
        public DbSet<AccionModulo> AccionesModulo { get; set; }

        public DbSet<ListaPrecio> ListasPrecios { get; set; }
        public DbSet<ProductoPrecioLista> ProductosPrecios { get; set; }
        public DbSet<PriceChangeBatch> PriceChangeBatches { get; set; }
        public DbSet<PriceChangeItem> PriceChangeItems { get; set; }
        public DbSet<CambioPrecioEvento> CambioPrecioEventos { get; set; }
        public DbSet<CambioPrecioDetalle> CambioPrecioDetalles { get; set; }

        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<TicketAdjunto> TicketAdjuntos { get; set; }
        public DbSet<TicketChecklistItem> TicketChecklistItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ==========================================================
            // Provider compatibility: RowVersion
            // - En SQL Server, [Timestamp] se mapea a rowversion automáticamente.
            // - En otros providers (ej. SQLite in-memory para tests), necesitamos un default
            //   para evitar inserts fallidos por NOT NULL (seed data + altas normales).
            // ==========================================================
            if (!Database.IsSqlServer())
            {
                foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                {
                    if (entityType.ClrType == null)
                        continue;

                    if (!typeof(AuditableEntity).IsAssignableFrom(entityType.ClrType))
                        continue;

                    modelBuilder.Entity(entityType.ClrType)
                        .Property<byte[]>(nameof(AuditableEntity.RowVersion))
                        .IsRequired()
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAdd()
                        .HasDefaultValueSql("randomblob(8)");
                }

                // ClienteCreditoConfiguracion tiene su propio RowVersion ([Timestamp])
                // que no se auto-genera en SQLite sin este default explícito.
                modelBuilder.Entity<ClienteCreditoConfiguracion>()
                    .Property(e => e.RowVersion)
                    .ValueGeneratedOnAddOrUpdate()
                    .HasDefaultValueSql("randomblob(8)");
            }

            var userRowVersion = modelBuilder.Entity<ApplicationUser>()
                .Property(u => u.RowVersion)
                .IsRequired()
                .IsConcurrencyToken();

            if (Database.IsSqlServer())
            {
                userRowVersion.IsRowVersion();
            }
            else
            {
                userRowVersion
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("randomblob(8)");
            }

            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(u => u.Sucursal)
                    .HasMaxLength(120);

                entity.HasOne(u => u.SucursalNavigation)
                    .WithMany(s => s.Usuarios)
                    .HasForeignKey(u => u.SucursalId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Sucursal>(entity =>
            {
                entity.ToTable("Sucursales");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Nombre)
                    .IsRequired()
                    .HasMaxLength(120);

                entity.Property(e => e.Activa)
                    .HasDefaultValue(true)
                    .ValueGeneratedNever();

                entity.HasIndex(e => e.Nombre)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // Devolucion

            modelBuilder.Entity<Devolucion>(entity =>
            {
                entity.HasOne(d => d.RMA)
                    .WithOne(r => r.Devolucion)
                    .HasForeignKey<RMA>(r => r.DevolucionId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.NotaCredito)
                    .WithOne(nc => nc.Devolucion)
                    .HasForeignKey<NotaCredito>(nc => nc.DevolucionId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(d => d.TotalDevolucion)
                    .HasPrecision(18, 2);

                entity.HasIndex(d => d.VentaId);
                entity.HasIndex(d => d.ClienteId);
                entity.HasIndex(d => d.Estado);
            });

            // =======================
            // Alicuotas IVA
            // =======================
            modelBuilder.Entity<AlicuotaIVA>(entity =>
            {
                entity.ToTable("AlicuotasIVA");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Codigo)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(e => e.Nombre)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Porcentaje)
                    .HasPrecision(5, 2);

                entity.Property(e => e.Activa)
                    .HasDefaultValue(true);

                entity.Property(e => e.EsPredeterminada)
                    .HasDefaultValue(false);

                entity.HasIndex(e => e.Codigo)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasIndex(e => e.Activa);
                entity.HasIndex(e => e.EsPredeterminada);
                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // =======================
            // Configuración de Categoria
            // =======================
            modelBuilder.Entity<Categoria>(entity =>
            {
                entity.HasIndex(e => e.Codigo)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.Property(e => e.Codigo)
                    .IsRequired()
                    .HasMaxLength(20)
                    .HasDefaultValue(string.Empty);

                entity.Property(e => e.Nombre)
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasDefaultValue(string.Empty);

                entity.Property(e => e.Activo)
                    .HasDefaultValue(true)
                    .ValueGeneratedNever();

                entity.HasOne(e => e.Parent)
                    .WithMany(e => e.Children)
                    .HasForeignKey(e => e.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.AlicuotaIVA)
                    .WithMany()
                    .HasForeignKey(e => e.AlicuotaIVAId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // =======================
            // Configuración de Marca
            // =======================
            modelBuilder.Entity<Marca>(entity =>
            {
                entity.HasIndex(e => e.Codigo)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.Property(e => e.Activo)
                    .HasDefaultValue(true)
                    .ValueGeneratedNever();

                entity.HasOne(e => e.Parent)
                    .WithMany(e => e.Children)
                    .HasForeignKey(e => e.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // Configuración de Producto
            // (IMPORTANTE: sin QueryFilter para evitar warnings con históricos)
            // =======================
            modelBuilder.Entity<Producto>(entity =>
            {
                entity.HasIndex(e => e.Codigo)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasOne(e => e.Categoria)
                    .WithMany()
                    .HasForeignKey(e => e.CategoriaId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Marca)
                    .WithMany()
                    .HasForeignKey(e => e.MarcaId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(e => e.PrecioCompra).HasPrecision(18, 2);
                entity.Property(e => e.PrecioVenta).HasPrecision(18, 2);
                entity.Property(e => e.PorcentajeIVA).HasPrecision(5, 2);
                entity.Property(e => e.ComisionPorcentaje)
                    .HasPrecision(5, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.StockActual).HasPrecision(18, 2);
                entity.Property(e => e.StockMinimo).HasPrecision(18, 2);

                entity.Property(e => e.UnidadMedida)
                    .HasMaxLength(10)
                    .HasDefaultValue("UN");

                entity.Property(e => e.Activo)
                    .HasDefaultValue(true)
                    .ValueGeneratedNever();

                entity.HasMany(e => e.Caracteristicas)
                    .WithOne(c => c.Producto)
                    .HasForeignKey(c => c.ProductoId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.CondicionesPago)
                    .WithOne(c => c.Producto)
                    .HasForeignKey(c => c.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.AlicuotaIVA)
                    .WithMany()
                    .HasForeignKey(e => e.AlicuotaIVAId)
                    .OnDelete(DeleteBehavior.SetNull);

                // SIN: entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<ProductoCaracteristica>(entity =>
            {
                entity.ToTable("ProductosCaracteristicas");

                entity.Property(e => e.Nombre)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Valor)
                    .IsRequired()
                    .HasMaxLength(300);

                entity.HasIndex(e => e.ProductoId);
                entity.HasIndex(e => new { e.ProductoId, e.Nombre });
            });

            modelBuilder.Entity<ProductoCondicionPago>(entity =>
            {
                var productoPorcentajesCheck = Database.IsSqlServer()
                    ? "([PorcentajeRecargo] IS NULL OR ([PorcentajeRecargo] >= 0 AND [PorcentajeRecargo] <= 100)) AND ([PorcentajeDescuentoMaximo] IS NULL OR ([PorcentajeDescuentoMaximo] >= 0 AND [PorcentajeDescuentoMaximo] <= 100))"
                    : "([PorcentajeRecargo] IS NULL OR (CAST([PorcentajeRecargo] AS REAL) >= 0 AND CAST([PorcentajeRecargo] AS REAL) <= 100)) AND ([PorcentajeDescuentoMaximo] IS NULL OR (CAST([PorcentajeDescuentoMaximo] AS REAL) >= 0 AND CAST([PorcentajeDescuentoMaximo] AS REAL) <= 100))";

                entity.ToTable("ProductoCondicionesPago", t =>
                {
                    t.HasCheckConstraint(
                        "CK_ProductoCondicionesPago_Cuotas",
                        "([MaxCuotasSinInteres] IS NULL OR [MaxCuotasSinInteres] >= 1) AND ([MaxCuotasConInteres] IS NULL OR [MaxCuotasConInteres] >= 1) AND ([MaxCuotasCredito] IS NULL OR [MaxCuotasCredito] >= 1)");

                    t.HasCheckConstraint(
                        "CK_ProductoCondicionesPago_Porcentajes",
                        productoPorcentajesCheck);
                });

                entity.HasKey(e => e.Id);

                entity.Property(e => e.PorcentajeRecargo).HasPrecision(5, 2);
                entity.Property(e => e.PorcentajeDescuentoMaximo).HasPrecision(5, 2);
                entity.Property(e => e.Observaciones).HasMaxLength(500);
                entity.Property(e => e.Activo)
                    .HasDefaultValue(true)
                    .ValueGeneratedNever();

                entity.HasIndex(e => e.ProductoId);
                entity.HasIndex(e => e.TipoPago);
                entity.HasIndex(e => new { e.ProductoId, e.TipoPago })
                    .IsUnique()
                    .HasFilter("[IsDeleted] = 0 AND [Activo] = 1");

                entity.HasQueryFilter(e => !e.IsDeleted);

                entity.HasMany(e => e.Tarjetas)
                    .WithOne(t => t.ProductoCondicionPago)
                    .HasForeignKey(t => t.ProductoCondicionPagoId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ProductoCondicionPagoTarjeta>(entity =>
            {
                var tarjetaPorcentajesCheck = Database.IsSqlServer()
                    ? "([PorcentajeRecargo] IS NULL OR ([PorcentajeRecargo] >= 0 AND [PorcentajeRecargo] <= 100)) AND ([PorcentajeDescuentoMaximo] IS NULL OR ([PorcentajeDescuentoMaximo] >= 0 AND [PorcentajeDescuentoMaximo] <= 100))"
                    : "([PorcentajeRecargo] IS NULL OR (CAST([PorcentajeRecargo] AS REAL) >= 0 AND CAST([PorcentajeRecargo] AS REAL) <= 100)) AND ([PorcentajeDescuentoMaximo] IS NULL OR (CAST([PorcentajeDescuentoMaximo] AS REAL) >= 0 AND CAST([PorcentajeDescuentoMaximo] AS REAL) <= 100))";

                entity.ToTable("ProductoCondicionesPagoTarjeta", t =>
                {
                    t.HasCheckConstraint(
                        "CK_ProductoCondicionesPagoTarjeta_Cuotas",
                        "([MaxCuotasSinInteres] IS NULL OR [MaxCuotasSinInteres] >= 1) AND ([MaxCuotasConInteres] IS NULL OR [MaxCuotasConInteres] >= 1)");

                    t.HasCheckConstraint(
                        "CK_ProductoCondicionesPagoTarjeta_Porcentajes",
                        tarjetaPorcentajesCheck);
                });

                entity.HasKey(e => e.Id);

                entity.Property(e => e.PorcentajeRecargo).HasPrecision(5, 2);
                entity.Property(e => e.PorcentajeDescuentoMaximo).HasPrecision(5, 2);
                entity.Property(e => e.Observaciones).HasMaxLength(500);
                entity.Property(e => e.Activo)
                    .HasDefaultValue(true)
                    .ValueGeneratedNever();

                entity.HasIndex(e => e.ProductoCondicionPagoId);
                entity.HasIndex(e => e.ConfiguracionTarjetaId);
                entity.HasIndex(e => e.ProductoCondicionPagoId)
                    .IsUnique()
                    .HasDatabaseName("UX_ProductoCondicionesPagoTarjeta_General")
                    .HasFilter("[IsDeleted] = 0 AND [Activo] = 1 AND [ConfiguracionTarjetaId] IS NULL");
                entity.HasIndex(e => new { e.ProductoCondicionPagoId, e.ConfiguracionTarjetaId })
                    .IsUnique()
                    .HasDatabaseName("UX_ProductoCondicionesPagoTarjeta_Especifica")
                    .HasFilter("[IsDeleted] = 0 AND [Activo] = 1 AND [ConfiguracionTarjetaId] IS NOT NULL");

                entity.HasQueryFilter(e => !e.IsDeleted);

                entity.HasOne(e => e.ConfiguracionTarjeta)
                    .WithMany()
                    .HasForeignKey(e => e.ConfiguracionTarjetaId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // =======================
            // ProductoCondicionPagoPlan
            // =======================
            modelBuilder.Entity<ProductoCondicionPagoPlan>(entity =>
            {
                var cuotasCheck = "[CantidadCuotas] >= 1";
                var ajusteCheck = Database.IsSqlServer()
                    ? "[AjustePorcentaje] >= -100.0000 AND [AjustePorcentaje] <= 999.9999"
                    : "CAST([AjustePorcentaje] AS REAL) >= -100.0 AND CAST([AjustePorcentaje] AS REAL) <= 999.9999";

                entity.ToTable("ProductoCondicionPagoPlanes", t =>
                {
                    t.HasCheckConstraint("CK_ProductoCondicionPagoPlanes_Cuotas", cuotasCheck);
                    t.HasCheckConstraint("CK_ProductoCondicionPagoPlanes_Ajuste", ajusteCheck);
                });

                entity.HasKey(e => e.Id);

                entity.Property(e => e.AjustePorcentaje).HasPrecision(8, 4);
                entity.Property(e => e.Observaciones).HasMaxLength(500);
                entity.Property(e => e.Activo)
                    .HasDefaultValue(true)
                    .ValueGeneratedNever();

                entity.HasIndex(e => e.ProductoCondicionPagoId);
                entity.HasIndex(e => e.ProductoCondicionPagoTarjetaId);

                // Plan general del medio (tarjetaId = null): único por condición + cuotas
                entity.HasIndex(e => new { e.ProductoCondicionPagoId, e.CantidadCuotas })
                    .IsUnique()
                    .HasDatabaseName("UX_ProductoCondicionPagoPlanes_General")
                    .HasFilter("[IsDeleted] = 0 AND [ProductoCondicionPagoTarjetaId] IS NULL");

                // Plan de tarjeta específica/general tipo tarjeta: único por condición + tarjeta + cuotas
                entity.HasIndex(e => new { e.ProductoCondicionPagoId, e.ProductoCondicionPagoTarjetaId, e.CantidadCuotas })
                    .IsUnique()
                    .HasDatabaseName("UX_ProductoCondicionPagoPlanes_Tarjeta")
                    .HasFilter("[IsDeleted] = 0 AND [ProductoCondicionPagoTarjetaId] IS NOT NULL");

                entity.HasQueryFilter(e => !e.IsDeleted);

                entity.HasOne(e => e.ProductoCondicionPago)
                    .WithMany(c => c.Planes)
                    .HasForeignKey(e => e.ProductoCondicionPagoId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.ProductoCondicionPagoTarjeta)
                    .WithMany(t => t.Planes)
                    .HasForeignKey(e => e.ProductoCondicionPagoTarjetaId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // Configuración de PrecioHistorico
            // =======================
            modelBuilder.Entity<PrecioHistorico>(entity =>
            {
                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(e => e.PrecioCompraAnterior).HasPrecision(18, 2);
                entity.Property(e => e.PrecioCompraNuevo).HasPrecision(18, 2);
                entity.Property(e => e.PrecioVentaAnterior).HasPrecision(18, 2);
                entity.Property(e => e.PrecioVentaNuevo).HasPrecision(18, 2);

                entity.HasIndex(e => e.ProductoId);
                entity.HasIndex(e => e.FechaCambio);
                entity.HasIndex(e => e.UsuarioModificacion);
            });

            // =======================
            // EvaluacionCredito
            // =======================
            modelBuilder.Entity<EvaluacionCredito>(entity =>
            {
                entity.ToTable("EvaluacionesCredito");

                entity.Property(e => e.MontoSolicitado).HasPrecision(18, 2);
                entity.Property(e => e.PuntajeRiesgoCliente).HasPrecision(18, 2);
                entity.Property(e => e.RelacionCuotaIngreso).HasPrecision(18, 4);
                entity.Property(e => e.PuntajeFinal).HasPrecision(18, 2);

                // Fix de warnings de precisión
                entity.Property(e => e.SueldoCliente).HasPrecision(18, 2);

                entity.Property(e => e.Motivo).HasMaxLength(1000);
                entity.Property(e => e.Observaciones).HasMaxLength(2000);

                entity.HasIndex(e => e.ClienteId);
                entity.HasIndex(e => e.CreditoId);
                entity.HasIndex(e => e.FechaEvaluacion);
                entity.HasIndex(e => e.Resultado);

                entity.HasOne(e => e.Cliente)
                    .WithMany()
                    .HasForeignKey(e => e.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Credito)
                    .WithMany()
                    .HasForeignKey(e => e.CreditoId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // Proveedor
            // =======================
            modelBuilder.Entity<Proveedor>(entity =>
            {
                entity.HasIndex(e => e.Cuit)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");
            });

            // =======================
            // ProveedorProducto (N:N)
            // =======================
            modelBuilder.Entity<ProveedorProducto>(entity =>
            {
                entity.HasOne(e => e.Proveedor)
                    .WithMany(p => p.ProveedorProductos)
                    .HasForeignKey(e => e.ProveedorId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.ProveedorId, e.ProductoId }).IsUnique();
            });

            // =======================
            // ProveedorMarca (N:N)
            // =======================
            modelBuilder.Entity<ProveedorMarca>(entity =>
            {
                entity.HasOne(e => e.Proveedor)
                    .WithMany(p => p.ProveedorMarcas)
                    .HasForeignKey(e => e.ProveedorId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Marca)
                    .WithMany()
                    .HasForeignKey(e => e.MarcaId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.ProveedorId, e.MarcaId }).IsUnique();
            });

            // =======================
            // ProveedorCategoria (N:N)
            // =======================
            modelBuilder.Entity<ProveedorCategoria>(entity =>
            {
                entity.HasOne(e => e.Proveedor)
                    .WithMany(p => p.ProveedorCategorias)
                    .HasForeignKey(e => e.ProveedorId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Categoria)
                    .WithMany()
                    .HasForeignKey(e => e.CategoriaId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.ProveedorId, e.CategoriaId }).IsUnique();
            });

            // =======================
            // OrdenCompra
            // =======================
            modelBuilder.Entity<OrdenCompra>(entity =>
            {
                entity.HasIndex(e => e.Numero)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasOne(e => e.Proveedor)
                    .WithMany(p => p.OrdenesCompra)
                    .HasForeignKey(e => e.ProveedorId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(e => e.Subtotal).HasPrecision(18, 2);
                entity.Property(e => e.Descuento).HasPrecision(18, 2);
                entity.Property(e => e.Iva).HasPrecision(18, 2);
                entity.Property(e => e.Total).HasPrecision(18, 2);
            });

            // =======================
            // OrdenCompraDetalle
            // =======================
            modelBuilder.Entity<OrdenCompraDetalle>(entity =>
            {
                entity.ToTable("OrdenCompraDetalle");
                
                entity.HasOne(e => e.OrdenCompra)
                    .WithMany(o => o.Detalles)
                    .HasForeignKey(e => e.OrdenCompraId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(e => e.PrecioUnitario).HasPrecision(18, 2);
                entity.Property(e => e.Subtotal).HasPrecision(18, 2);
            });

            // =======================
            // Cheque
            // =======================
            modelBuilder.Entity<Cheque>(entity =>
            {
                entity.HasIndex(e => e.Numero);

                entity.HasOne(e => e.Proveedor)
                    .WithMany(p => p.Cheques)
                    .HasForeignKey(e => e.ProveedorId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.OrdenCompra)
                    .WithMany()
                    .HasForeignKey(e => e.OrdenCompraId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.Property(e => e.Monto).HasPrecision(18, 2);
            });

            // =======================
            // MovimientoStock
            // =======================
            modelBuilder.Entity<MovimientoStock>(entity =>
            {
                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.OrdenCompra)
                    .WithMany()
                    .HasForeignKey(e => e.OrdenCompraId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(e => e.Cantidad).HasPrecision(18, 2);
                entity.Property(e => e.StockAnterior).HasPrecision(18, 2);
                entity.Property(e => e.StockNuevo).HasPrecision(18, 2);
                entity.Property(e => e.CostoUnitarioAlMomento)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.CostoTotalAlMomento)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.FuenteCosto)
                    .HasMaxLength(50)
                    .HasDefaultValue("NoInformado");
            });

            // =======================
            // Cliente (IMPORTANTE: sin QueryFilter para evitar warnings con Venta)
            // =======================
            modelBuilder.Entity<Cliente>(entity =>
            {
                entity.HasIndex(e => new { e.TipoDocumento, e.NumeroDocumento })
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.Property(e => e.Sueldo).HasPrecision(18, 2);
                entity.Property(e => e.ConyugeSueldo).HasPrecision(18, 2);

                entity.Property(e => e.TieneReciboSueldo)
                    .IsRequired()
                    .HasDefaultValue(false);

                entity.Property(e => e.PuntajeRiesgo).HasPrecision(5, 2);

                entity.Property(e => e.LimiteCredito).HasPrecision(18, 2);

                // Configuración personalizada de crédito por cliente
                entity.Property(e => e.TasaInteresMensualPersonalizada).HasPrecision(8, 4);
                entity.Property(e => e.GastosAdministrativosPersonalizados).HasPrecision(8, 4);
                entity.Property(e => e.MontoMinimoPersonalizado).HasPrecision(18, 2);
                entity.Property(e => e.MontoMaximoPersonalizado).HasPrecision(18, 2);

                // Relación con Perfil de Crédito Preferido
                entity.HasOne(e => e.PerfilCreditoPreferido)
                    .WithMany()
                    .HasForeignKey(e => e.PerfilCreditoPreferidoId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Garante)
                    .WithMany()
                    .HasForeignKey(e => e.GaranteId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(e => e.CreditoConfiguracion)
                    .WithOne(c => c.Cliente)
                    .HasForeignKey<ClienteCreditoConfiguracion>(c => c.ClienteId)
                    .OnDelete(DeleteBehavior.Cascade);

                // SIN: entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // =======================
            // ClienteCreditoConfiguracion (1:1)
            // =======================
            modelBuilder.Entity<ClienteCreditoConfiguracion>(entity =>
            {
                entity.ToTable("ClientesCreditoConfiguraciones");

                entity.HasKey(e => e.ClienteId);

                entity.Property(e => e.LimiteOverride).HasPrecision(18, 2);
                entity.Property(e => e.ExcepcionDelta).HasPrecision(18, 2);

                entity.Property(e => e.MotivoExcepcion).HasMaxLength(1000);
                entity.Property(e => e.MotivoOverride).HasMaxLength(1000);
                entity.Property(e => e.AprobadoPor).HasMaxLength(200);
                entity.Property(e => e.OverrideAprobadoPor).HasMaxLength(200);

                entity.Property(e => e.RowVersion)
                    .IsRowVersion()
                    .IsConcurrencyToken();

                entity.HasOne(e => e.CreditoPreset)
                    .WithMany()
                    .HasForeignKey(e => e.CreditoPresetId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.CreditoPresetId);
                entity.HasIndex(e => e.ExcepcionHasta);

                entity.ToTable(t =>
                {
                    t.HasCheckConstraint(
                        "CK_ClientesCreditoConfiguraciones_ExcepcionVigencia",
                        "[ExcepcionDesde] IS NULL OR [ExcepcionHasta] IS NULL OR [ExcepcionDesde] <= [ExcepcionHasta]");

                    t.HasCheckConstraint(
                        "CK_ClientesCreditoConfiguraciones_MontosNoNegativos",
                        "([LimiteOverride] IS NULL OR [LimiteOverride] >= 0) AND ([ExcepcionDelta] IS NULL OR [ExcepcionDelta] >= 0)");
                });
            });

            // =======================
            // ClientePuntajeHistorial
            // =======================
            modelBuilder.Entity<ClientePuntajeHistorial>(entity =>
            {
                entity.ToTable("ClientesPuntajeHistorial");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Puntaje).HasPrecision(5, 2);
                entity.Property(e => e.Origen).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Observacion).HasMaxLength(500);
                entity.Property(e => e.RegistradoPor).HasMaxLength(200);

                entity.HasOne(e => e.Cliente)
                    .WithMany(c => c.PuntajeHistorial)
                    .HasForeignKey(e => e.ClienteId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.ClienteId);
                entity.HasIndex(e => new { e.ClienteId, e.Fecha });
            });

            // =======================
            // PuntajeCreditoLimite
            // =======================
            modelBuilder.Entity<PuntajeCreditoLimite>(entity =>
            {
                entity.ToTable("PuntajeCreditoLimites", t =>
                {
                    t.HasCheckConstraint("CK_PuntajeCreditoLimites_Puntaje", "[Puntaje] >= 1 AND [Puntaje] <= 5");
                });

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Puntaje)
                    .HasConversion<int>()
                    .IsRequired();

                entity.Property(e => e.LimiteMonto)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);

                entity.Property(e => e.Activo)
                    .HasDefaultValue(true);

                entity.Property(e => e.FechaActualizacion)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.Property(e => e.UsuarioActualizacion)
                    .HasMaxLength(100);

                entity.HasIndex(e => e.Puntaje)
                    .IsUnique();
            });

            // =======================
            // Credito (IMPORTANTE: sin QueryFilter para evitar warnings con VentaCreditoCuota)
            // =======================
            modelBuilder.Entity<Credito>(entity =>
            {
                entity.HasIndex(e => e.Numero)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasOne(e => e.Cliente)
                    .WithMany(c => c.Creditos)
                    .HasForeignKey(e => e.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.PerfilCreditoAplicado)
                    .WithMany()
                    .HasForeignKey(e => e.PerfilCreditoAplicadoId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.Property(e => e.MontoSolicitado).HasPrecision(18, 2);
                entity.Property(e => e.MontoAprobado).HasPrecision(18, 2);
                entity.Property(e => e.TasaInteres).HasPrecision(5, 2);
                entity.Property(e => e.MontoCuota).HasPrecision(18, 2);
                entity.Property(e => e.PuntajeRiesgoInicial).HasPrecision(5, 2);

                entity.Property(e => e.CFTEA).HasPrecision(5, 2);
                entity.Property(e => e.TotalAPagar).HasPrecision(18, 2);
                entity.Property(e => e.SaldoPendiente).HasPrecision(18, 2);

                entity.Property(e => e.GastosAdministrativos).HasPrecision(18, 2);
                entity.Property(e => e.TasaInteresAplicada).HasPrecision(8, 4);

                entity.HasOne(e => e.Garante)
                    .WithMany()
                    .HasForeignKey(e => e.GaranteId)
                    .OnDelete(DeleteBehavior.Restrict);

                // SIN: entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // =======================
            // Garante
            // =======================
            modelBuilder.Entity<Garante>(entity =>
            {
                entity.HasOne(e => e.Cliente)
                    .WithMany(c => c.ComoGarante)
                    .HasForeignKey(e => e.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.GaranteCliente)
                    .WithMany()
                    .HasForeignKey(e => e.GaranteClienteId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // DocumentoCliente
            // =======================
            modelBuilder.Entity<DocumentoCliente>(entity =>
            {
                entity.HasOne(e => e.Cliente)
                    .WithMany(c => c.Documentos)
                    .HasForeignKey(e => e.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => e.ClienteId);
                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.FechaSubida);
                entity.HasIndex(e => e.FechaVencimiento);
                entity.HasIndex(e => e.TipoDocumento);
            });

            // =======================
            // Cuota
            // =======================
            modelBuilder.Entity<Cuota>(entity =>
            {
                entity.HasOne(e => e.Credito)
                    .WithMany(c => c.Cuotas)
                    .HasForeignKey(e => e.CreditoId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.CreditoId, e.NumeroCuota }).IsUnique();

                entity.Property(e => e.MontoCapital).HasPrecision(18, 2);
                entity.Property(e => e.MontoInteres).HasPrecision(18, 2);
                entity.Property(e => e.MontoTotal).HasPrecision(18, 2);
                entity.Property(e => e.MontoPagado).HasPrecision(18, 2);
                entity.Property(e => e.MontoPunitorio).HasPrecision(18, 2);
            });

            // =======================
            // VentaCreditoCuota
            // =======================
            modelBuilder.Entity<VentaCreditoCuota>(entity =>
            {
                entity.ToTable("VentaCreditoCuotas");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Monto).HasPrecision(18, 2);
                entity.Property(e => e.Saldo).HasPrecision(18, 2);
                entity.Property(e => e.MontoPagado).HasPrecision(18, 2);

                entity.HasIndex(e => new { e.VentaId, e.NumeroCuota });
                entity.HasIndex(e => e.FechaVencimiento);
                entity.HasIndex(e => e.Pagada);

                entity.HasOne(e => e.Venta)
                    .WithMany(v => v.VentaCreditoCuotas)
                    .HasForeignKey(e => e.VentaId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Credito)
                    .WithMany()
                    .HasForeignKey(e => e.CreditoId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // Venta
            // =======================
            modelBuilder.Entity<Venta>(entity =>
            {
                entity.ToTable("Ventas");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Numero)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(e => e.Subtotal).HasPrecision(18, 2);
                entity.Property(e => e.Descuento).HasPrecision(18, 2);
                entity.Property(e => e.IVA).HasPrecision(18, 2);
                entity.Property(e => e.Total).HasPrecision(18, 2);
                entity.Property(e => e.LimiteAplicado).HasPrecision(18, 2);
                entity.Property(e => e.PuntajeAlMomento).HasPrecision(5, 2);
                entity.Property(e => e.OverrideAlMomento).HasPrecision(18, 2);
                entity.Property(e => e.ExcepcionAlMomento).HasPrecision(18, 2);
                entity.Property(e => e.VendedorUserId).HasMaxLength(450);

                entity.HasIndex(e => e.Numero).IsUnique();
                entity.HasIndex(e => e.FechaVenta);
                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.AperturaCajaId);
                entity.HasIndex(e => e.VendedorUserId);
                entity.HasIndex(e => e.PresetIdAlMomento);

                entity.HasOne(e => e.Cliente)
                    .WithMany()
                    .HasForeignKey(e => e.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Credito)
                    .WithMany()
                    .HasForeignKey(e => e.CreditoId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired(false);

                entity.HasOne<PuntajeCreditoLimite>()
                    .WithMany()
                    .HasForeignKey(e => e.PresetIdAlMomento)
                    .OnDelete(DeleteBehavior.SetNull)
                    .IsRequired(false);

                entity.HasOne(e => e.AperturaCaja)
                    .WithMany()
                    .HasForeignKey(e => e.AperturaCajaId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired(false);

                entity.HasOne(e => e.VendedorUser)
                    .WithMany()
                    .HasForeignKey(e => e.VendedorUserId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired(false);

                entity.HasMany(e => e.Detalles)
                    .WithOne(d => d.Venta)
                    .HasForeignKey(d => d.VentaId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.Facturas)
                    .WithOne(f => f.Venta)
                    .HasForeignKey(f => f.VentaId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =======================
            // VentaDetalle
            // =======================
            modelBuilder.Entity<VentaDetalle>(entity =>
            {
                entity.ToTable("VentaDetalles");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.PrecioUnitario).HasPrecision(18, 2);
                entity.Property(e => e.Descuento).HasPrecision(18, 2);
                entity.Property(e => e.Subtotal).HasPrecision(18, 2);
                entity.Property(e => e.PorcentajeIVA)
                    .HasPrecision(5, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.AlicuotaIVANombre)
                    .HasMaxLength(100);
                entity.Property(e => e.PrecioUnitarioNeto)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.IVAUnitario)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.SubtotalNeto)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.SubtotalIVA)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.DescuentoGeneralProrrateado)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.SubtotalFinalNeto)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.SubtotalFinalIVA)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.SubtotalFinal)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.CostoUnitarioAlMomento)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.CostoTotalAlMomento)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.ComisionPorcentajeAplicada)
                    .HasPrecision(5, 2)
                    .HasDefaultValue(0m);
                entity.Property(e => e.ComisionMonto)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0m);

                // Forma de pago por ítem (Fase 16.2 — nullable, sin default)
                entity.Property(e => e.PorcentajeAjustePlanAplicado).HasPrecision(5, 2);
                entity.Property(e => e.MontoAjustePlanAplicado).HasPrecision(18, 2);

                entity.HasIndex(e => e.ProductoCondicionPagoPlanId)
                    .HasDatabaseName("IX_VentaDetalles_ProductoCondicionPagoPlanId");

                entity.HasOne(e => e.Venta)
                    .WithMany(v => v.Detalles)
                    .HasForeignKey(e => e.VentaId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ProductoCondicionPagoPlan)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoCondicionPagoPlanId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // Factura
            // =======================
            modelBuilder.Entity<Factura>(entity =>
            {
                entity.ToTable("Facturas");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Numero)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(e => e.CAE).HasMaxLength(50);

                entity.HasIndex(e => e.Numero)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");
                entity.HasIndex(e => e.CAE);

                entity.HasOne(e => e.Venta)
                    .WithMany(v => v.Facturas)
                    .HasForeignKey(e => e.VentaId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =======================
            // PlantillaContratoCredito
            // =======================
            modelBuilder.Entity<PlantillaContratoCredito>(entity =>
            {
                entity.ToTable("PlantillasContratoCredito");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Nombre)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(e => e.NombreVendedor)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.DomicilioVendedor)
                    .IsRequired()
                    .HasMaxLength(300);

                entity.Property(e => e.DniVendedor)
                    .HasMaxLength(20);

                entity.Property(e => e.CuitVendedor)
                    .HasMaxLength(20);

                entity.Property(e => e.CiudadFirma)
                    .IsRequired()
                    .HasMaxLength(120);

                entity.Property(e => e.Jurisdiccion)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.InteresMoraDiarioPorcentaje)
                    .HasPrecision(8, 4);

                entity.Property(e => e.TextoContrato)
                    .IsRequired()
                    .HasColumnType(Database.IsSqlServer() ? "nvarchar(max)" : "TEXT");

                entity.Property(e => e.TextoPagare)
                    .IsRequired()
                    .HasColumnType(Database.IsSqlServer() ? "nvarchar(max)" : "TEXT");

                entity.HasIndex(e => e.Activa);
                entity.HasIndex(e => e.VigenteDesde);
                entity.HasIndex(e => e.VigenteHasta);
                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // =======================
            // ContratoVentaCredito
            // =======================
            modelBuilder.Entity<ContratoVentaCredito>(entity =>
            {
                entity.ToTable("ContratosVentaCredito");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.NumeroContrato)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.NumeroPagare)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.UsuarioGeneracion)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.RutaArchivo)
                    .HasMaxLength(500);

                entity.Property(e => e.NombreArchivo)
                    .HasMaxLength(200);

                entity.Property(e => e.ContentHash)
                    .HasMaxLength(128);

                entity.Property(e => e.TextoContratoSnapshot)
                    .IsRequired()
                    .HasColumnType(Database.IsSqlServer() ? "nvarchar(max)" : "TEXT");

                entity.Property(e => e.TextoPagareSnapshot)
                    .IsRequired()
                    .HasColumnType(Database.IsSqlServer() ? "nvarchar(max)" : "TEXT");

                entity.Property(e => e.DatosSnapshotJson)
                    .IsRequired()
                    .HasColumnType(Database.IsSqlServer() ? "nvarchar(max)" : "TEXT");

                entity.HasIndex(e => e.VentaId)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");
                entity.HasIndex(e => e.CreditoId);
                entity.HasIndex(e => e.ClienteId);
                entity.HasIndex(e => e.PlantillaContratoCreditoId);
                entity.HasIndex(e => e.NumeroContrato)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");
                entity.HasIndex(e => e.NumeroPagare)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");
                entity.HasIndex(e => e.FechaGeneracionUtc);
                entity.HasQueryFilter(e => !e.IsDeleted);

                entity.HasOne(e => e.Venta)
                    .WithMany()
                    .HasForeignKey(e => e.VentaId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Credito)
                    .WithMany()
                    .HasForeignKey(e => e.CreditoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Cliente)
                    .WithMany()
                    .HasForeignKey(e => e.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.PlantillaContratoCredito)
                    .WithMany()
                    .HasForeignKey(e => e.PlantillaContratoCreditoId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // ConfiguracionPago
            // =======================
            modelBuilder.Entity<ConfiguracionPago>(entity =>
            {
                entity.ToTable("ConfiguracionesPago");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Nombre)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.PorcentajeDescuentoMaximo).HasPrecision(5, 2);
                entity.Property(e => e.PorcentajeRecargo).HasPrecision(5, 2);
                
                // Configuración de crédito personal defaults globales
                entity.Property(e => e.TasaInteresMensualCreditoPersonal).HasPrecision(8, 4);
                entity.Property(e => e.GastosAdministrativosDefaultCreditoPersonal).HasPrecision(18, 2);

                entity.HasIndex(e => e.TipoPago).IsUnique();

                entity.HasMany(e => e.ConfiguracionesTarjeta)
                    .WithOne(t => t.ConfiguracionPago)
                    .HasForeignKey(t => t.ConfiguracionPagoId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.PlanesPago)
                    .WithOne(p => p.ConfiguracionPago)
                    .HasForeignKey(p => p.ConfiguracionPagoId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =======================
            // ConfiguracionPagoPlan
            // =======================
            modelBuilder.Entity<ConfiguracionPagoPlan>(entity =>
            {
                var cuotasCheck = "[CantidadCuotas] >= 1";
                var ajusteCheck = Database.IsSqlServer()
                    ? "[AjustePorcentaje] >= -100.0000 AND [AjustePorcentaje] <= 999.9999"
                    : "CAST([AjustePorcentaje] AS REAL) >= -100.0 AND CAST([AjustePorcentaje] AS REAL) <= 999.9999";

                entity.ToTable("ConfiguracionPagoPlanes", t =>
                {
                    t.HasCheckConstraint("CK_ConfiguracionPagoPlanes_Cuotas", cuotasCheck);
                    t.HasCheckConstraint("CK_ConfiguracionPagoPlanes_Ajuste", ajusteCheck);
                });

                entity.HasKey(e => e.Id);

                entity.Property(e => e.AjustePorcentaje).HasPrecision(8, 4);
                entity.Property(e => e.Etiqueta).HasMaxLength(100);
                entity.Property(e => e.Observaciones).HasMaxLength(500);
                entity.Property(e => e.Activo)
                    .HasDefaultValue(true)
                    .ValueGeneratedNever();

                entity.HasIndex(e => e.ConfiguracionPagoId);
                entity.HasIndex(e => e.ConfiguracionTarjetaId);
                entity.HasIndex(e => new { e.TipoPago, e.Activo, e.Orden });

                entity.HasIndex(e => new { e.ConfiguracionPagoId, e.TipoPago, e.CantidadCuotas })
                    .IsUnique()
                    .HasDatabaseName("UX_ConfiguracionPagoPlanes_General")
                    .HasFilter("[IsDeleted] = 0 AND [Activo] = 1 AND [ConfiguracionTarjetaId] IS NULL");

                entity.HasIndex(e => new { e.ConfiguracionPagoId, e.TipoPago, e.ConfiguracionTarjetaId, e.CantidadCuotas })
                    .IsUnique()
                    .HasDatabaseName("UX_ConfiguracionPagoPlanes_Tarjeta")
                    .HasFilter("[IsDeleted] = 0 AND [Activo] = 1 AND [ConfiguracionTarjetaId] IS NOT NULL");

                entity.HasQueryFilter(e => !e.IsDeleted);

                entity.HasOne(e => e.ConfiguracionTarjeta)
                    .WithMany(t => t.PlanesPago)
                    .HasForeignKey(e => e.ConfiguracionTarjetaId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // PerfilCredito
            modelBuilder.Entity<PerfilCredito>(entity =>
            {
                entity.ToTable("PerfilesCredito");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Nombre)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.TasaMensual).HasPrecision(8, 4);
                entity.Property(e => e.GastosAdministrativos).HasPrecision(18, 2);

                entity.HasIndex(e => e.Nombre).IsUnique();
                entity.HasIndex(e => e.Orden);
            });

            // =======================
            // ConfiguracionTarjeta
            // =======================
            modelBuilder.Entity<ConfiguracionTarjeta>(entity =>
            {
                entity.ToTable("ConfiguracionesTarjeta");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.NombreTarjeta)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.TasaInteresesMensual).HasPrecision(5, 2);
                entity.Property(e => e.PorcentajeRecargoDebito).HasPrecision(5, 2);

                entity.HasIndex(e => new { e.ConfiguracionPagoId, e.NombreTarjeta });
            });

            // =======================
            // DatosTarjeta
            // =======================
            modelBuilder.Entity<DatosTarjeta>(entity =>
            {
                entity.ToTable("DatosTarjeta");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.NombreTarjeta)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.TasaInteres).HasPrecision(5, 2);
                entity.Property(e => e.MontoCuota).HasPrecision(18, 2);
                entity.Property(e => e.MontoTotalConInteres).HasPrecision(18, 2);
                entity.Property(e => e.RecargoAplicado).HasPrecision(18, 2);
                entity.Property(e => e.PorcentajeAjustePlanAplicado).HasPrecision(5, 2);
                entity.Property(e => e.MontoAjustePlanAplicado).HasPrecision(18, 2);

                entity.HasOne(e => e.Venta)
                    .WithOne(v => v.DatosTarjeta)
                    .HasForeignKey<DatosTarjeta>(e => e.VentaId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.ConfiguracionTarjeta)
                    .WithMany()
                    .HasForeignKey(e => e.ConfiguracionTarjetaId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.ProductoCondicionPagoPlan)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoCondicionPagoPlanId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // =======================
            // DatosCheque
            // =======================
            modelBuilder.Entity<DatosCheque>(entity =>
            {
                entity.ToTable("DatosCheque");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.NumeroCheque)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.Banco)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Titular)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.Monto).HasPrecision(18, 2);

                entity.HasIndex(e => e.NumeroCheque);

                entity.HasOne(e => e.Venta)
                    .WithOne(v => v.DatosCheque)
                    .HasForeignKey<DatosCheque>(e => e.VentaId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =======================
            // AlertaCobranza
            // =======================
            modelBuilder.Entity<AlertaCobranza>(entity =>
            {
                entity.ToTable("AlertasCobranza");
                entity.HasKey(e => e.Id);

                // Propiedades de precisión decimal
                entity.Property(e => e.MontoVencido).HasPrecision(18, 2);
                entity.Property(e => e.MontoMoraCalculada).HasPrecision(18, 2);
                entity.Property(e => e.MontoTotal).HasPrecision(18, 2);
                entity.Property(e => e.MontoPromesaPago).HasPrecision(18, 2);

                // Ignorar propiedades calculadas
                entity.Ignore(e => e.Leida);
                entity.Ignore(e => e.DiasDesdeAlerta);
                entity.Ignore(e => e.PromesaVencida);

                // Relaciones
                entity.HasOne(e => e.Cliente)
                    .WithMany()
                    .HasForeignKey(e => e.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Credito)
                    .WithMany()
                    .HasForeignKey(e => e.CreditoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Cuota)
                    .WithMany()
                    .HasForeignKey(e => e.CuotaId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Índices
                entity.HasIndex(e => e.FechaAlerta);
                entity.HasIndex(e => e.Tipo);
                entity.HasIndex(e => e.Prioridad);
                entity.HasIndex(e => e.Resuelta);
                entity.HasIndex(e => e.EstadoGestion);
                entity.HasIndex(e => e.GestorAsignadoId);
                entity.HasIndex(e => new { e.ClienteId, e.Resuelta });
                entity.HasIndex(e => new { e.CreditoId, e.Tipo, e.Resuelta })
                    .HasDatabaseName("IX_AlertasCobranza_Credito_Tipo_Resuelta");
            });

            // =======================
            // HistorialContacto
            // =======================
            modelBuilder.Entity<HistorialContacto>(entity =>
            {
                entity.ToTable("HistorialContactos");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.MontoPromesaPago).HasPrecision(18, 2);

                entity.HasOne(e => e.AlertaCobranza)
                    .WithMany(a => a.HistorialContactos)
                    .HasForeignKey(e => e.AlertaCobranzaId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Cliente)
                    .WithMany()
                    .HasForeignKey(e => e.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => e.AlertaCobranzaId);
                entity.HasIndex(e => e.ClienteId);
                entity.HasIndex(e => e.FechaContacto);
                entity.HasIndex(e => e.TipoContacto);
                entity.HasIndex(e => e.Resultado);
            });

            // =======================
            // AcuerdoPago
            // =======================
            modelBuilder.Entity<AcuerdoPago>(entity =>
            {
                entity.ToTable("AcuerdosPago");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.MontoDeudaOriginal).HasPrecision(18, 2);
                entity.Property(e => e.MontoMoraOriginal).HasPrecision(18, 2);
                entity.Property(e => e.MontoCondonado).HasPrecision(18, 2);
                entity.Property(e => e.MontoTotalAcuerdo).HasPrecision(18, 2);
                entity.Property(e => e.MontoEntregaInicial).HasPrecision(18, 2);
                entity.Property(e => e.MontoCuotaAcuerdo).HasPrecision(18, 2);

                entity.HasOne(e => e.AlertaCobranza)
                    .WithMany(a => a.Acuerdos)
                    .HasForeignKey(e => e.AlertaCobranzaId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Cliente)
                    .WithMany()
                    .HasForeignKey(e => e.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Credito)
                    .WithMany()
                    .HasForeignKey(e => e.CreditoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => e.NumeroAcuerdo).IsUnique();
                entity.HasIndex(e => e.AlertaCobranzaId);
                entity.HasIndex(e => e.ClienteId);
                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.FechaCreacion);
            });

            // =======================
            // CuotaAcuerdo
            // =======================
            modelBuilder.Entity<CuotaAcuerdo>(entity =>
            {
                entity.ToTable("CuotasAcuerdo");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.MontoCapital).HasPrecision(18, 2);
                entity.Property(e => e.MontoMora).HasPrecision(18, 2);
                entity.Property(e => e.MontoTotal).HasPrecision(18, 2);
                entity.Property(e => e.MontoPagado).HasPrecision(18, 2);

                entity.HasOne(e => e.AcuerdoPago)
                    .WithMany(a => a.Cuotas)
                    .HasForeignKey(e => e.AcuerdoPagoId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.AcuerdoPagoId);
                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.FechaVencimiento);
                entity.HasIndex(e => new { e.AcuerdoPagoId, e.NumeroCuota }).IsUnique();
            });

            // =======================
            // PlantillaNotificacionMora
            // =======================
            modelBuilder.Entity<PlantillaNotificacionMora>(entity =>
            {
                entity.ToTable("PlantillasNotificacionMora");
                entity.HasKey(e => e.Id);

                entity.HasIndex(e => new { e.Tipo, e.Canal, e.Activa });
                entity.HasIndex(e => e.Activa);
            });

            // =======================
            // AlertaStock
            // =======================
            modelBuilder.Entity<AlertaStock>(entity =>
            {
                entity.ToTable("AlertasStock");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(e => e.StockActual).HasPrecision(18, 2);
                entity.Property(e => e.StockMinimo).HasPrecision(18, 2);
                entity.Property(e => e.CantidadSugeridaReposicion).HasPrecision(18, 2);

                entity.HasIndex(e => e.ProductoId)
                    .HasDatabaseName("IX_AlertasStock_ProductoId");

                // A lo sumo 1 alerta activa por producto: IsDeleted = 0 AND FechaResolucion IS NULL
                entity.HasIndex(e => e.ProductoId)
                    .HasDatabaseName("UX_AlertasStock_Producto_Activa")
                    .IsUnique()
                    .HasFilter("[IsDeleted] = 0 AND [FechaResolucion] IS NULL");

                entity.HasIndex(e => e.FechaAlerta);
                entity.HasIndex(e => e.Tipo);
                entity.HasIndex(e => e.Prioridad);
                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.NotificacionUrgente);
            });

            // =======================
            // ConfiguracionMora
            // =======================
            modelBuilder.Entity<ConfiguracionMora>(entity =>
            {
                entity.ToTable("ConfiguracionesMora");
                entity.HasKey(e => e.Id);

                // Cálculo de mora
                entity.Property(e => e.TasaMoraBase).HasPrecision(8, 4);
                entity.Property(e => e.TasaPrimerMes).HasPrecision(8, 4);
                entity.Property(e => e.TasaSegundoMes).HasPrecision(8, 4);
                entity.Property(e => e.TasaTercerMesEnAdelante).HasPrecision(8, 4);
                entity.Property(e => e.ValorTopeMora).HasPrecision(18, 2);
                entity.Property(e => e.MoraMinima).HasPrecision(18, 2);

                // Clasificación
                entity.Property(e => e.MontoParaPrioridadMedia).HasPrecision(18, 2);
                entity.Property(e => e.MontoParaPrioridadAlta).HasPrecision(18, 2);
                entity.Property(e => e.MontoParaPrioridadCritica).HasPrecision(18, 2);

                // Gestión
                entity.Property(e => e.PorcentajeMinimoEntrega).HasPrecision(5, 2);
                entity.Property(e => e.PorcentajeMaximoCondonacion).HasPrecision(5, 2);

                // Bloqueos
                entity.Property(e => e.MontoMoraParaBloquear).HasPrecision(18, 2);

                // Score
                entity.Property(e => e.PuntosRestarPorDiaMora).HasPrecision(8, 4);
                entity.Property(e => e.PorcentajeRecuperacionScore).HasPrecision(5, 2);

                // Campos deprecated (compatibilidad con DB existente)
#pragma warning disable CS0618 // Type or member is obsolete
                entity.Property(e => e.PorcentajeRecargo).HasPrecision(5, 2);
#pragma warning restore CS0618
            });

            // =======================
            // AlertaMora
            // =======================
            modelBuilder.Entity<AlertaMora>(entity =>
            {
                entity.ToTable("AlertasMora");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Descripcion)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.ColorAlerta)
                    .IsRequired()
                    .HasMaxLength(7); // #RRGGBB

                entity.HasOne(e => e.ConfiguracionMora)
                    .WithMany()
                    .HasForeignKey(e => e.ConfiguracionMoraId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // ConfiguracionCredito
            modelBuilder.Entity<ConfiguracionCredito>(entity =>
            {
                entity.ToTable("ConfiguracionesCredito");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.LimiteCreditoDefault).HasPrecision(18, 2);
                entity.Property(e => e.LimiteCreditoMinimo).HasPrecision(18, 2);
                entity.Property(e => e.MontoMoraParaNoApto).HasPrecision(18, 2);
                entity.Property(e => e.MontoMoraParaRequerirAutorizacion).HasPrecision(18, 2);
                entity.Property(e => e.PorcentajeCupoMinimoRequerido).HasPrecision(5, 2);
                entity.Property(e => e.PuntajeRiesgoMinimo).HasPrecision(5, 2);
                entity.Property(e => e.PuntajeRiesgoMedio).HasPrecision(5, 2);
                entity.Property(e => e.PuntajeRiesgoExcelente).HasPrecision(5, 2);
                entity.Property(e => e.RelacionCuotaIngresoMax).HasPrecision(5, 4);
                entity.Property(e => e.UmbralCuotaIngresoBajo).HasPrecision(5, 4);
                entity.Property(e => e.UmbralCuotaIngresoAlto).HasPrecision(5, 4);
                entity.Property(e => e.MontoRequiereGarante).HasPrecision(18, 2);
                entity.Property(e => e.PuntajeMinimoParaAprobacion).HasPrecision(5, 2);
                entity.Property(e => e.PuntajeMinimoParaAnalisis).HasPrecision(5, 2);
                entity.Property(e => e.SemaforoFinancieroRatioVerdeMax).HasPrecision(5, 4);
                entity.Property(e => e.SemaforoFinancieroRatioAmarilloMax).HasPrecision(5, 4);
            });

            modelBuilder.Entity<ConfiguracionRentabilidad>(entity =>
            {
                entity.ToTable("ConfiguracionesRentabilidad");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.MargenBajoMax)
                    .HasPrecision(5, 2)
                    .HasDefaultValue(20m);

                entity.Property(e => e.MargenAltoMin)
                    .HasPrecision(5, 2)
                    .HasDefaultValue(35m);
            });

            // =======================
            // LogMora
            // =======================
            modelBuilder.Entity<LogMora>(entity =>
            {
                entity.ToTable("LogsMora");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.TotalMora).HasPrecision(18, 2);
                entity.Property(e => e.TotalRecargosAplicados).HasPrecision(18, 2);

                entity.HasIndex(e => e.FechaEjecucion);
                entity.HasIndex(e => e.Exitoso);
            });

            // =======================
            // Caja
            // =======================
            modelBuilder.Entity<Caja>(entity =>
            {
                entity.HasIndex(e => e.Codigo)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");
            });

            // =======================
            // AperturaCaja
            // =======================
            modelBuilder.Entity<AperturaCaja>(entity =>
            {
                entity.HasOne(e => e.Caja)
                    .WithMany(c => c.Aperturas)
                    .HasForeignKey(e => e.CajaId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // MovimientoCaja
            // =======================
            modelBuilder.Entity<MovimientoCaja>(entity =>
            {
                entity.HasOne(e => e.AperturaCaja)
                    .WithMany(a => a.Movimientos)
                    .HasForeignKey(e => e.AperturaCajaId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Venta)
                    .WithMany(v => v.MovimientosCaja)
                    .HasForeignKey(e => e.VentaId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => e.FechaMovimiento);
                entity.HasIndex(e => e.Tipo);
                entity.HasIndex(e => e.Concepto);
                entity.HasIndex(e => e.AperturaCajaId);
                entity.HasIndex(e => e.VentaId);
                entity.HasIndex(e => new { e.AperturaCajaId, e.TipoPago });
            });

            // =======================
            // CierreCaja
            // =======================
            modelBuilder.Entity<CierreCaja>(entity =>
            {
                entity.HasOne(e => e.AperturaCaja)
                    .WithOne(a => a.Cierre)
                    .HasForeignKey<CierreCaja>(e => e.AperturaCajaId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => e.FechaCierre);
                entity.HasIndex(e => e.TieneDiferencia);
            });

            // =======================
            // Notificacion
            // =======================
            modelBuilder.Entity<Notificacion>(entity =>
            {
                entity.HasIndex(e => e.UsuarioDestino);
                entity.HasIndex(e => e.Leida);
                entity.HasIndex(e => e.FechaNotificacion);
                entity.HasIndex(e => e.Tipo);
                entity.HasIndex(e => e.Prioridad);
            });

            // =======================
            // SeguridadEventoAuditoria
            // =======================
            modelBuilder.Entity<SeguridadEventoAuditoria>(entity =>
            {
                entity.ToTable("SeguridadEventosAuditoria");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.FechaEvento)
                    .HasColumnType("datetime2");

                entity.Property(e => e.UsuarioId)
                    .HasMaxLength(450);

                entity.Property(e => e.UsuarioNombre)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(e => e.Modulo)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Accion)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Entidad)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.Detalle)
                    .HasMaxLength(1000);

                entity.Property(e => e.DireccionIp)
                    .HasMaxLength(64);

                entity.HasIndex(e => e.FechaEvento);
                entity.HasIndex(e => e.Modulo);
                entity.HasIndex(e => e.Accion);
                entity.HasIndex(e => e.UsuarioNombre);
                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // =======================
            // RolMetadata
            // =======================
            modelBuilder.Entity<RolMetadata>(entity =>
            {
                entity.ToTable("RolesMetadata");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.RoleId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(e => e.Descripcion)
                    .HasMaxLength(500);

                entity.Property(e => e.Activo)
                    .HasDefaultValue(true)
                    .ValueGeneratedNever();

                entity.HasIndex(e => e.RoleId)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasOne(e => e.Role)
                    .WithOne()
                    .HasForeignKey<RolMetadata>(e => e.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // DevolucionDetalle
            modelBuilder.Entity<DevolucionDetalle>(entity =>
            {
                entity.ToTable("DevolucionDetalles");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.PrecioUnitario).HasPrecision(18, 2);
                entity.Property(e => e.Subtotal).HasPrecision(18, 2);
            });

            // NotaCredito
            modelBuilder.Entity<NotaCredito>(entity =>
            {
                entity.ToTable("NotasCredito");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.MontoTotal).HasPrecision(18, 2);
                entity.Property(e => e.MontoUtilizado).HasPrecision(18, 2);
            });

            // RMA
            modelBuilder.Entity<RMA>(entity =>
            {
                entity.ToTable("RMAs");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.MontoReembolso).HasPrecision(18, 2);
            });

            // SolicitudAutorizacion
            modelBuilder.Entity<SolicitudAutorizacion>(entity =>
            {
                entity.ToTable("SolicitudesAutorizacion");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ValorPermitido).HasPrecision(18, 2);
                entity.Property(e => e.ValorSolicitado).HasPrecision(18, 2);
            });

            // UmbralAutorizacion
            modelBuilder.Entity<UmbralAutorizacion>(entity =>
            {
                entity.ToTable("UmbralesAutorizacion");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ValorMaximo).HasPrecision(18, 2);
            });

            // =======================
            // ListaPrecio
            // =======================
            modelBuilder.Entity<ListaPrecio>(entity =>
            {
                entity.ToTable("ListasPrecios");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Nombre).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Codigo).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Descripcion).HasMaxLength(500);

                entity.Property(e => e.MargenPorcentaje).HasPrecision(5, 2);
                entity.Property(e => e.RecargoPorcentaje).HasPrecision(5, 2);

                // Fix de warning de precisión
                entity.Property(e => e.MargenMinimoPorcentaje).HasPrecision(5, 2);

                entity.Property(e => e.ReglasJson)
                    .HasColumnType(Database.IsSqlServer() ? "nvarchar(max)" : "TEXT");

                entity.HasIndex(e => e.Codigo)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasIndex(e => e.Activa);
                entity.HasIndex(e => e.EsPredeterminada);
                entity.HasIndex(e => e.Orden);
            });

            // =======================
            // ProductoPrecioLista
            // =======================
            modelBuilder.Entity<ProductoPrecioLista>(entity =>
            {
                entity.ToTable("ProductosPrecios");
                entity.HasKey(e => e.Id);

                entity.HasIndex(e => new { e.ProductoId, e.ListaId, e.VigenciaDesde })
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.Property(e => e.Costo).IsRequired().HasPrecision(18, 2);
                entity.Property(e => e.Precio).IsRequired().HasPrecision(18, 2);

                entity.Property(e => e.MargenPorcentaje).HasPrecision(5, 2);
                entity.Property(e => e.MargenValor).HasPrecision(18, 2);

                entity.Property(e => e.CreadoPor).HasMaxLength(50);
                entity.Property(e => e.Notas).HasMaxLength(500);

                // Enforce invariant: only one vigente price per (ProductoId, ListaId)
                // (soft-deleted rows do not participate)
                var vigenteFilter = Database.IsSqlServer()
                    ? "[IsDeleted] = 0 AND [EsVigente] = 1"
                    : "IsDeleted = 0 AND EsVigente = 1";

                entity.HasIndex(e => new { e.ProductoId, e.ListaId })
                    .IsUnique()
                    .HasFilter(vigenteFilter);

                // Supporting index for common lookups
                entity.HasIndex(e => new { e.ProductoId, e.ListaId, e.EsVigente });
                entity.HasIndex(e => e.VigenciaDesde);
                entity.HasIndex(e => e.VigenciaHasta);
                entity.HasIndex(e => e.BatchId);

                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Lista)
                    .WithMany(l => l.Precios)
                    .HasForeignKey(e => e.ListaId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Batch)
                    .WithMany()
                    .HasForeignKey(e => e.BatchId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // =======================
            // PriceChangeBatch
            // =======================
            modelBuilder.Entity<PriceChangeBatch>(entity =>
            {
                entity.ToTable("PriceChangeBatches");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Nombre).IsRequired().HasMaxLength(200);
                entity.Property(e => e.ValorCambio).HasPrecision(18, 2);

                entity.Property(e => e.AlcanceJson)
                    .IsRequired()
                    .HasColumnType(Database.IsSqlServer() ? "nvarchar(max)" : "TEXT");

                entity.Property(e => e.ListasAfectadasJson)
                    .IsRequired()
                    .HasColumnType(Database.IsSqlServer() ? "nvarchar(max)" : "TEXT");

                entity.Property(e => e.SimulacionJson)
                    .HasColumnType(Database.IsSqlServer() ? "nvarchar(max)" : "TEXT");

                entity.Property(e => e.SolicitadoPor).IsRequired().HasMaxLength(50);
                entity.Property(e => e.AprobadoPor).HasMaxLength(50);
                entity.Property(e => e.AplicadoPor).HasMaxLength(50);
                entity.Property(e => e.RevertidoPor).HasMaxLength(50);
                entity.Property(e => e.MotivoRechazo).HasMaxLength(500);
                entity.Property(e => e.Notas).HasMaxLength(1000);

                entity.Property(e => e.PorcentajePromedioCambio).HasPrecision(5, 2);

                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.TipoCambio);
                entity.HasIndex(e => e.FechaSolicitud);
                entity.HasIndex(e => e.FechaAplicacion);
                entity.HasIndex(e => e.SolicitadoPor);
                entity.HasIndex(e => e.BatchPadreId);

                // Relación self-referencing para reversiones
                entity.HasOne(e => e.BatchPadre)
                    .WithOne(e => e.BatchReversion)
                    .HasForeignKey<PriceChangeBatch>(e => e.BatchPadreId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // PriceChangeItem
            // =======================
            modelBuilder.Entity<PriceChangeItem>(entity =>
            {
                entity.ToTable("PriceChangeItems");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ProductoCodigo).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ProductoNombre).IsRequired().HasMaxLength(200);

                entity.Property(e => e.PrecioAnterior).HasPrecision(18, 2);
                entity.Property(e => e.PrecioNuevo).HasPrecision(18, 2);
                entity.Property(e => e.DiferenciaValor).HasPrecision(18, 2);
                entity.Property(e => e.DiferenciaPorcentaje).HasPrecision(5, 2);

                entity.Property(e => e.Costo).HasPrecision(18, 2);
                entity.Property(e => e.MargenAnterior).HasPrecision(5, 2);
                entity.Property(e => e.MargenNuevo).HasPrecision(5, 2);

                entity.Property(e => e.MensajeAdvertencia).HasMaxLength(500);

                entity.HasIndex(e => e.BatchId);
                entity.HasIndex(e => e.ProductoId);
                entity.HasIndex(e => e.ListaId);
                entity.HasIndex(e => e.TieneAdvertencia);

                entity.HasOne(e => e.Batch)
                    .WithMany(b => b.Items)
                    .HasForeignKey(e => e.BatchId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Lista)
                    .WithMany()
                    .HasForeignKey(e => e.ListaId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // CambioPrecioEvento
            // =======================
            modelBuilder.Entity<CambioPrecioEvento>(entity =>
            {
                entity.ToTable("CambioPrecioEventos");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Usuario).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Alcance).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ValorPorcentaje).HasPrecision(18, 2);
                entity.Property(e => e.Motivo).HasMaxLength(1000);
                entity.Property(e => e.RevertidoPor).HasMaxLength(100);

                entity.HasIndex(e => e.Fecha);
                entity.HasIndex(e => e.Usuario);
                entity.HasIndex(e => e.RevertidoEn);
            });

            // =======================
            // CambioPrecioDetalle
            // =======================
            modelBuilder.Entity<CambioPrecioDetalle>(entity =>
            {
                entity.ToTable("CambioPrecioDetalles");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.PrecioAnterior).HasPrecision(18, 2);
                entity.Property(e => e.PrecioNuevo).HasPrecision(18, 2);

                entity.HasIndex(e => e.EventoId);
                entity.HasIndex(e => e.ProductoId);

                entity.HasOne(e => e.Evento)
                    .WithMany(e => e.Detalles)
                    .HasForeignKey(e => e.EventoId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =======================
            // Ticket
            // =======================
            modelBuilder.Entity<Ticket>(entity =>
            {
                entity.ToTable("Tickets");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Titulo).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Descripcion).IsRequired().HasMaxLength(4000);
                entity.Property(e => e.ModuloOrigen).HasMaxLength(100);
                entity.Property(e => e.VistaOrigen).HasMaxLength(200);
                entity.Property(e => e.UrlOrigen).HasMaxLength(500);
                entity.Property(e => e.ContextKey).HasMaxLength(200);
                entity.Property(e => e.Resolucion).HasMaxLength(4000);
                entity.Property(e => e.ResueltoPor).HasMaxLength(100);

                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.Tipo);
                entity.HasIndex(e => e.CreatedAt);
            });

            // =======================
            // TicketAdjunto
            // =======================
            modelBuilder.Entity<TicketAdjunto>(entity =>
            {
                entity.ToTable("TicketAdjuntos");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.NombreArchivo).IsRequired().HasMaxLength(200);
                entity.Property(e => e.RutaArchivo).IsRequired().HasMaxLength(500);
                entity.Property(e => e.TipoMIME).HasMaxLength(100);

                entity.HasOne(e => e.Ticket)
                    .WithMany(t => t.Adjuntos)
                    .HasForeignKey(e => e.TicketId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =======================
            // TicketChecklistItem
            // =======================
            modelBuilder.Entity<TicketChecklistItem>(entity =>
            {
                entity.ToTable("TicketChecklistItems");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Descripcion).IsRequired().HasMaxLength(500);
                entity.Property(e => e.CompletadoPor).HasMaxLength(100);

                entity.HasIndex(e => new { e.TicketId, e.Orden });

                entity.HasOne(e => e.Ticket)
                    .WithMany(t => t.ChecklistItems)
                    .HasForeignKey(e => e.TicketId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Seed de datos inicial
            SeedData(modelBuilder);
        }

        // Seed
        // =======================

        private void SeedData(ModelBuilder modelBuilder)
        {
            var seedUtc = SeedCreatedAtUtc;

            modelBuilder.Entity<AlicuotaIVA>().HasData(
                new AlicuotaIVA
                {
                    Id = 1,
                    Codigo = "IVA_21",
                    Nombre = "IVA 21%",
                    Porcentaje = 21m,
                    Activa = true,
                    EsPredeterminada = true,
                    CreatedAt = seedUtc,
                    CreatedBy = "System",
                    IsDeleted = false
                },
                new AlicuotaIVA
                {
                    Id = 2,
                    Codigo = "IVA_10_5",
                    Nombre = "IVA 10.5%",
                    Porcentaje = 10.5m,
                    Activa = true,
                    EsPredeterminada = false,
                    CreatedAt = seedUtc,
                    CreatedBy = "System",
                    IsDeleted = false
                },
                new AlicuotaIVA
                {
                    Id = 3,
                    Codigo = "IVA_27",
                    Nombre = "IVA 27%",
                    Porcentaje = 27m,
                    Activa = true,
                    EsPredeterminada = false,
                    CreatedAt = seedUtc,
                    CreatedBy = "System",
                    IsDeleted = false
                },
                new AlicuotaIVA
                {
                    Id = 4,
                    Codigo = "IVA_EXENTO",
                    Nombre = "Exento 0%",
                    Porcentaje = 0m,
                    Activa = true,
                    EsPredeterminada = false,
                    CreatedAt = seedUtc,
                    CreatedBy = "System",
                    IsDeleted = false
                }
            );

            modelBuilder.Entity<Categoria>().HasData(
                new Categoria
                {
                    Id = 1,
                    Codigo = "ELEC",
                    Nombre = "Electrónica",
                    Descripcion = "Productos electrónicos",
                    ControlSerieDefault = true,
                    Activo = true,
                    CreatedAt = seedUtc,
                    CreatedBy = "System",
                    IsDeleted = false
                },
                new Categoria
                {
                    Id = 2,
                    Codigo = "FRIO",
                    Nombre = "Refrigeración",
                    Descripcion = "Heladeras, freezers y aire acondicionado",
                    ControlSerieDefault = true,
                    Activo = true,
                    CreatedAt = seedUtc,
                    CreatedBy = "System",
                    IsDeleted = false
                }
            );

            modelBuilder.Entity<PuntajeCreditoLimite>().HasData(
                new PuntajeCreditoLimite
                {
                    Id = 1,
                    Puntaje = Models.Enums.NivelRiesgoCredito.Rechazado,
                    LimiteMonto = 0m,
                    Activo = true,
                    FechaActualizacion = seedUtc,
                    UsuarioActualizacion = "System"
                },
                new PuntajeCreditoLimite
                {
                    Id = 2,
                    Puntaje = Models.Enums.NivelRiesgoCredito.RechazadoRevisar,
                    LimiteMonto = 0m,
                    Activo = true,
                    FechaActualizacion = seedUtc,
                    UsuarioActualizacion = "System"
                },
                new PuntajeCreditoLimite
                {
                    Id = 3,
                    Puntaje = Models.Enums.NivelRiesgoCredito.AprobadoCondicional,
                    LimiteMonto = 0m,
                    Activo = true,
                    FechaActualizacion = seedUtc,
                    UsuarioActualizacion = "System"
                },
                new PuntajeCreditoLimite
                {
                    Id = 4,
                    Puntaje = Models.Enums.NivelRiesgoCredito.AprobadoLimitado,
                    LimiteMonto = 0m,
                    Activo = true,
                    FechaActualizacion = seedUtc,
                    UsuarioActualizacion = "System"
                },
                new PuntajeCreditoLimite
                {
                    Id = 5,
                    Puntaje = Models.Enums.NivelRiesgoCredito.AprobadoTotal,
                    LimiteMonto = 0m,
                    Activo = true,
                    FechaActualizacion = seedUtc,
                    UsuarioActualizacion = "System"
                }
            );
        }

        /// <summary>
        /// Interceptor para auditoría automática antes de guardar cambios
        /// </summary>
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var currentUser =
                _httpContextAccessor?.HttpContext?.User?.Identity?.Name ?? "System";

            foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    // Preservar valores explícitos (ej. imports) si ya vienen seteados.
                    if (entry.Entity.CreatedAt == default)
                        entry.Entity.CreatedAt = DateTime.UtcNow;

                    if (string.IsNullOrWhiteSpace(entry.Entity.CreatedBy))
                        entry.Entity.CreatedBy = currentUser;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedBy = currentUser;

                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Property(e => e.CreatedBy).IsModified = false;
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}

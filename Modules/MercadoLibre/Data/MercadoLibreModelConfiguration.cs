using Microsoft.EntityFrameworkCore;
using TheBuryProject.Modules.MercadoLibre.Entities;

namespace TheBuryProject.Modules.MercadoLibre.Data
{
    /// <summary>
    /// Configuración EF Core del módulo MercadoLibre, aislada del AppDbContext
    /// para no engordar el OnModelCreating central.
    /// </summary>
    public static class MercadoLibreModelConfiguration
    {
        public static void Configure(ModelBuilder modelBuilder, bool isSqlServer)
        {
            var textType = isSqlServer ? "nvarchar(max)" : "TEXT";

            modelBuilder.Entity<MercadoLibreAccount>(entity =>
            {
                entity.ToTable("MercadoLibreAccounts");
                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.MeliUserId)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibreListing>(entity =>
            {
                entity.ToTable("MercadoLibreListings");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Precio).HasPrecision(18, 2);
                entity.Property(e => e.RawJson).HasColumnType(textType);

                // Una publicación de ML solo puede existir una vez en el ERP.
                entity.HasIndex(e => e.ItemId)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasIndex(e => e.AccountId);
                entity.HasIndex(e => e.ProductoId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.SellerSku);

                entity.HasOne(e => e.Account)
                    .WithMany(a => a.Listings)
                    .HasForeignKey(e => e.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                // La vinculación no toca al Producto: FK opcional, sin cascada.
                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                // Unidad física específica (origen UnidadFisicaEspecifica).
                entity.HasOne(e => e.ProductoUnidad)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoUnidadId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibreListingVariation>(entity =>
            {
                entity.ToTable("MercadoLibreListingVariations");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Precio).HasPrecision(18, 2);
                entity.Property(e => e.AttributesJson).HasColumnType(textType);

                entity.HasIndex(e => new { e.ListingId, e.VariationId })
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasIndex(e => e.ProductoId);
                entity.HasIndex(e => e.ProductoUnidadId);
                entity.HasIndex(e => e.SellerSku);

                entity.HasOne(e => e.Listing)
                    .WithMany(l => l.Variaciones)
                    .HasForeignKey(e => e.ListingId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.ProductoUnidad)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoUnidadId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibreOrder>(entity =>
            {
                entity.ToTable("MercadoLibreOrders");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
                entity.Property(e => e.PaidAmount).HasPrecision(18, 2);
                entity.Property(e => e.MontoComision).HasPrecision(18, 2);
                entity.Property(e => e.MontoEnvio).HasPrecision(18, 2);
                entity.Property(e => e.NetoEstimado).HasPrecision(18, 2);
                entity.Property(e => e.NetoReal).HasPrecision(18, 2);
                entity.Property(e => e.RawJson).HasColumnType(textType);
                entity.Property(e => e.RawShipmentJson).HasColumnType(textType);

                // Idempotencia: una orden ML no puede importarse dos veces.
                entity.HasIndex(e => e.MeliOrderId)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasIndex(e => e.AccountId);
                entity.HasIndex(e => e.VentaId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.EstadoInterno);
                entity.HasIndex(e => e.ShipmentId);
                entity.HasIndex(e => e.EstadoEnvioInterno);

                entity.HasOne(e => e.MovimientoCaja)
                    .WithMany()
                    .HasForeignKey(e => e.MovimientoCajaId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Account)
                    .WithMany(a => a.Orders)
                    .HasForeignKey(e => e.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Venta)
                    .WithMany()
                    .HasForeignKey(e => e.VentaId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibreOrderItem>(entity =>
            {
                entity.ToTable("MercadoLibreOrderItems");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.PrecioUnitario).HasPrecision(18, 2);
                entity.Property(e => e.SaleFee).HasPrecision(18, 2);

                entity.HasIndex(e => e.OrderId);
                entity.HasIndex(e => e.ItemId);

                entity.HasOne(e => e.Order)
                    .WithMany(o => o.Items)
                    .HasForeignKey(e => e.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibreClaim>(entity =>
            {
                entity.ToTable("MercadoLibreClaims");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.RawJson).HasColumnType(textType);

                entity.HasIndex(e => e.MercadoLibreOrderId);
                entity.HasIndex(e => e.MercadoLibreClaimId)
                    .IsUnique()
                    .HasFilter("MercadoLibreClaimId IS NOT NULL AND IsDeleted = 0");
                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.Tipo);
                entity.HasIndex(e => e.FechaCreacionUtc);

                entity.HasOne(e => e.Order)
                    .WithMany(o => o.Claims)
                    .HasForeignKey(e => e.MercadoLibreOrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.MovimientoStock)
                    .WithMany()
                    .HasForeignKey(e => e.MovimientoStockId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.MovimientoCaja)
                    .WithMany()
                    .HasForeignKey(e => e.MovimientoCajaId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibreConfiguracion>(entity =>
            {
                entity.ToTable("MercadoLibreConfiguraciones");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.AjusteCanalPorcentaje).HasPrecision(8, 2);
                entity.Property(e => e.ComisionEstimadaPorcentaje).HasPrecision(8, 2);
                entity.Property(e => e.CostoEnvioEstimado).HasPrecision(18, 2);
                entity.Property(e => e.MargenMinimoPorcentaje).HasPrecision(8, 2);

                entity.HasOne(e => e.Account)
                    .WithMany()
                    .HasForeignKey(e => e.AccountId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.ListaPrecio)
                    .WithMany()
                    .HasForeignKey(e => e.ListaPrecioId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Sucursal)
                    .WithMany()
                    .HasForeignKey(e => e.SucursalId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.ClienteMercadoLibre)
                    .WithMany()
                    .HasForeignKey(e => e.ClienteMercadoLibreId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibrePriceBatch>(entity =>
            {
                entity.ToTable("MercadoLibrePriceBatches");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ValorAjustePorcentaje).HasPrecision(8, 2);
                entity.Property(e => e.FiltrosJson).HasColumnType(textType);
                entity.Property(e => e.SimulacionJson).HasColumnType(textType);

                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.CreatedAt);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibrePriceBatchItem>(entity =>
            {
                entity.ToTable("MercadoLibrePriceBatchItems");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.PrecioAnterior).HasPrecision(18, 2);
                entity.Property(e => e.PrecioNuevo).HasPrecision(18, 2);
                entity.Property(e => e.DiferenciaPorcentaje).HasPrecision(8, 2);
                entity.Property(e => e.PayloadAplicacionJson).HasColumnType(textType);

                entity.HasIndex(e => e.BatchId);
                entity.HasIndex(e => e.ListingId);

                entity.HasOne(e => e.Batch)
                    .WithMany(b => b.Items)
                    .HasForeignKey(e => e.BatchId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Listing)
                    .WithMany()
                    .HasForeignKey(e => e.ListingId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibrePublicacionBorrador>(entity =>
            {
                entity.ToTable("MercadoLibrePublicacionBorradores");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Precio).HasPrecision(18, 2);
                entity.Property(e => e.Descripcion).HasColumnType(textType);
                entity.Property(e => e.PayloadSimuladoJson).HasColumnType(textType);
                entity.Property(e => e.ImagenesJson).HasColumnType(textType);
                entity.Property(e => e.AtributosCompletadosJson).HasColumnType(textType);

                entity.HasIndex(e => e.ProductoId);
                entity.HasIndex(e => e.Estado);

                // El borrador exige Producto: si el producto se elimina, el
                // borrador deja de tener sentido (Restrict obliga a resolverlo).
                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibreWebhookEvent>(entity =>
            {
                entity.ToTable("MercadoLibreWebhookEvents");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.RawBody).HasColumnType(textType);

                entity.HasIndex(e => e.Topic);
                entity.HasIndex(e => e.Procesado);
                entity.HasIndex(e => e.RecibidoUtc);
            });

            modelBuilder.Entity<MercadoLibreSyncLog>(entity =>
            {
                entity.ToTable("MercadoLibreSyncLogs");
                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.AccountId);
                entity.HasIndex(e => e.Operacion);
                entity.HasIndex(e => e.CreatedAt);
            });

            modelBuilder.Entity<MercadoLibreQuestion>(entity =>
            {
                entity.ToTable("MercadoLibreQuestions");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.RawJson).HasColumnType(textType);

                // Idempotencia: una pregunta de ML no puede importarse dos veces.
                entity.HasIndex(e => e.QuestionId)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasIndex(e => e.AccountId);
                entity.HasIndex(e => e.ListingId);
                entity.HasIndex(e => e.ProductoId);
                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.FechaPreguntaUtc);

                entity.HasOne(e => e.Account)
                    .WithMany()
                    .HasForeignKey(e => e.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                // El vínculo a publicación/producto es informativo: no cascada.
                entity.HasOne(e => e.Listing)
                    .WithMany()
                    .HasForeignKey(e => e.ListingId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<MercadoLibreMessage>(entity =>
            {
                entity.ToTable("MercadoLibreMessages");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.RawJson).HasColumnType(textType);

                // Idempotencia: un mensaje de ML no puede importarse dos veces.
                entity.HasIndex(e => e.MessageId)
                    .IsUnique()
                    .HasFilter("IsDeleted = 0");

                entity.HasIndex(e => e.AccountId);
                entity.HasIndex(e => e.OrderId);
                entity.HasIndex(e => e.MeliOrderId);
                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.FechaMensajeUtc);

                entity.HasOne(e => e.Account)
                    .WithMany()
                    .HasForeignKey(e => e.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Order)
                    .WithMany()
                    .HasForeignKey(e => e.OrderId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // ── Catálogo local de categorías ML con atributos ──────────────────
            // Caché de lectura reconstruido wholesale en cada importación: no usa
            // query filter de soft-delete (el importador reemplaza el contenido).
            modelBuilder.Entity<MercadoLibreCategory>(entity =>
            {
                entity.ToTable("MercadoLibreCategories");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.PathFromRootJson).HasColumnType(textType);
                entity.Property(e => e.ChildrenJson).HasColumnType(textType);
                entity.Property(e => e.ItemConditionsJson).HasColumnType(textType);
                entity.Property(e => e.BuyingModesJson).HasColumnType(textType);
                entity.Property(e => e.ShippingOptionsJson).HasColumnType(textType);
                entity.Property(e => e.RawJson).HasColumnType(textType);

                entity.HasIndex(e => new { e.SiteId, e.CategoryId }).IsUnique();
                entity.HasIndex(e => e.ParentCategoryId);
                entity.HasIndex(e => e.IsLeaf);
                entity.HasIndex(e => e.ListingAllowed);
                entity.HasIndex(e => e.Name);
            });

            modelBuilder.Entity<MercadoLibreCategoryAttribute>(entity =>
            {
                entity.ToTable("MercadoLibreCategoryAttributes");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ValuesJson).HasColumnType(textType);
                entity.Property(e => e.AllowedUnitsJson).HasColumnType(textType);
                entity.Property(e => e.Tooltip).HasColumnType(textType);
                entity.Property(e => e.RawJson).HasColumnType(textType);

                entity.HasIndex(e => new { e.SiteId, e.CategoryId, e.AttributeId }).IsUnique();
                entity.HasIndex(e => e.CategoryId);
                entity.HasIndex(e => e.Required);
                entity.HasIndex(e => e.ConditionalRequired);

                entity.HasOne(e => e.Category)
                    .WithMany(c => c.Attributes)
                    .HasForeignKey(e => e.CategoryFk)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MercadoLibreCategorySyncState>(entity =>
            {
                entity.ToTable("MercadoLibreCategorySyncStates");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.LastError).HasColumnType(textType);

                entity.HasIndex(e => e.SiteId).IsUnique();
            });
        }
    }
}

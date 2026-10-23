using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// DESCUENTO-PERMISO-02: fixtures sintéticas y reproducibles para <c>e2e/venta-descuento-permiso.spec.js</c>, que en CI
    /// corre con <c>E2E_REQUIRE_DESCUENTO_PERMISO=1</c> (falta de datos = fallo, nunca omisión).
    ///
    /// Aporta lo que el spec necesita y la base recién migrada no trae:
    /// 1) una caja abierta para el usuario SIN permiso (<see cref="UsuarioSinPermiso"/>) y para el CON permiso
    ///    (<see cref="UsuarioConPermiso"/>), porque la apertura de caja es por usuario;
    /// 2) una venta en Presupuesto con 2 × 1.000, 10 % de línea (1.800) y 200 de descuento general → total
    ///    <see cref="TotalVenta"/>, sobre el cliente/producto genéricos de <see cref="GenericE2ESeeder"/> (sembrar antes).
    ///
    /// Los usuarios no se crean acá: son los de <c>DbInitializer.CreateTestUsersAsync</c>, de modo que el spec ejercita
    /// la asignación REAL del seeder de permisos. Por eso <see cref="VerificarAsignacionInicialAsync"/> falla si
    /// Gerente tiene <c>ventas.discount</c> o si Administrador no lo tiene (decisión funcional vigente).
    ///
    /// Idempotente (por número de venta y por caja abierta existente). Nunca apuntar a la base de desarrollo — ver el runner.
    /// </summary>
    public static class DescuentoPermisoE2ESeeder
    {
        public const string Marcador = "E2EDESCPERM";
        public const string UsuarioSinPermiso = "gerente";
        public const string UsuarioConPermiso = "administrador";
        public const decimal TotalVenta = 1600m;
        public const string NumeroVenta = "E2EDESCPERM-0001";

        private const string ClienteDocumentoGenerico = "90000001";
        private const string ProductoCodigoGenerico = "E2EGENERIC-01";
        private const string ClaimDescuento = "ventas.discount";

        public static async Task<int> SembrarAsync(AppDbContext db)
        {
            await VerificarAsignacionInicialAsync(db);

            await AsegurarCajaAbiertaAsync(db, UsuarioSinPermiso);
            await AsegurarCajaAbiertaAsync(db, UsuarioConPermiso);

            var existente = await db.Ventas.Where(v => v.Numero == NumeroVenta).Select(v => (int?)v.Id).FirstOrDefaultAsync();
            if (existente.HasValue)
                return existente.Value;

            var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.NumeroDocumento == ClienteDocumentoGenerico)
                ?? throw new InvalidOperationException($"Falta el cliente genérico {ClienteDocumentoGenerico}: ejecutar GenericE2ESeedRunner.Sembrar antes.");
            var producto = await db.Productos.FirstOrDefaultAsync(p => p.Codigo == ProductoCodigoGenerico)
                ?? throw new InvalidOperationException($"Falta el producto genérico {ProductoCodigoGenerico}: ejecutar GenericE2ESeedRunner.Sembrar antes.");

            var venta = new Venta
            {
                ClienteId = cliente.Id,
                Numero = NumeroVenta,
                FechaVenta = DateTime.UtcNow,
                Estado = EstadoVenta.Presupuesto,
                TipoPago = TipoPago.Efectivo,
                Subtotal = 1800m,
                Descuento = 200m,
                IVA = 0m,
                Total = TotalVenta,
                EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere
            };
            venta.Detalles.Add(new VentaDetalle
            {
                ProductoId = producto.Id,
                Cantidad = 2,
                PrecioUnitario = 1000m,
                Descuento = 10m,
                Subtotal = 1800m,
                DescuentoGeneralProrrateado = 200m,
                PorcentajeIVA = 0m,
                SubtotalFinalNeto = TotalVenta,
                SubtotalFinalIVA = 0m,
                SubtotalFinal = TotalVenta
            });
            db.Ventas.Add(venta);
            await db.SaveChangesAsync();
            return venta.Id;
        }

        /// <summary>
        /// Falla si el seeder de permisos no dejó la asignación inicial esperada: Administrador CON ventas.discount y
        /// Gerente SIN él. Sin esto el spec "sin permiso" validaría contra un usuario que en realidad lo tiene.
        /// </summary>
        public static async Task VerificarAsignacionInicialAsync(AppDbContext db)
        {
            if (await UsuarioTienePermisoAsync(db, UsuarioSinPermiso))
                throw new InvalidOperationException($"'{UsuarioSinPermiso}' tiene {ClaimDescuento}: el fixture 'sin permiso' no es válido.");
            if (!await UsuarioTienePermisoAsync(db, UsuarioConPermiso))
                throw new InvalidOperationException($"'{UsuarioConPermiso}' no tiene {ClaimDescuento}: el seeder de permisos no lo concedió.");
        }

        private static async Task<bool> UsuarioTienePermisoAsync(AppDbContext db, string userName)
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == userName)
                ?? throw new InvalidOperationException($"No existe el usuario de prueba '{userName}' (DbInitializer.CreateTestUsersAsync).");
            var roleIds = await db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync();
            return await db.RolPermisos.AnyAsync(rp => roleIds.Contains(rp.RoleId) && rp.ClaimValue == ClaimDescuento && !rp.IsDeleted);
        }

        private static async Task AsegurarCajaAbiertaAsync(AppDbContext db, string usuario)
        {
            if (await db.AperturasCaja.AnyAsync(a => !a.Cerrada && !a.IsDeleted && a.UsuarioApertura == usuario))
                return;

            var codigo = $"{Marcador}-{usuario}".ToUpperInvariant();
            var caja = await db.Cajas.FirstOrDefaultAsync(c => c.Codigo == codigo);
            if (caja == null)
            {
                caja = new Caja { Codigo = codigo, Nombre = $"Caja E2E descuento ({usuario})", Activa = true };
                db.Cajas.Add(caja);
                await db.SaveChangesAsync();
            }

            caja.Estado = EstadoCaja.Abierta;
            db.AperturasCaja.Add(new AperturaCaja
            {
                CajaId = caja.Id,
                UsuarioApertura = usuario,
                MontoInicial = 0m,
                Cerrada = false
            });
            await db.SaveChangesAsync();
        }
    }
}

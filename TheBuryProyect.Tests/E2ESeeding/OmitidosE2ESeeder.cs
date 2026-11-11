using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// C-03: fixtures para que corran en CI specs E2E que antes se omitían por falta de datos o de permisos:
    /// 1) <c>configuracion-punitorio.spec.js</c> (6 escenarios de gestión): exige <c>configuracion.managepunitorio</c> y
    ///    <c>configuracion.retroactivepunitorio</c>, que el seeder de permisos NO concede a Administrador a propósito
    ///    (<c>docs/permisos-matriz-roles.md</c>). Se otorgan solo en esta base efímera; la matriz real queda cubierta por
    ///    <c>ConfiguracionPunitorioHttpTests</c>.
    /// 2) <c>cotizacion-credito-personal-aptitud-contradiccion.spec.js</c>: cliente con DNI numérico, BCRA "OK" y mora de capital
    ///    ≥ 30 días (evaluación real "No apto", sin depender de internet). Se exporta como <c>E2E_CLIENTE_NOAPTO_DNI</c>.
    /// 4) <c>cotizacion-conversion.spec.js</c> T9–T12: cuatro cotizaciones «Emitida» con cliente y sin convertir (Detalles → modal de
    ///    conversión). Desde el simulador ya no se pueden crear: guardar con cliente del sistema encadena a Venta/Edit
    ///    (COTIZACION-SIMULAR-GUARDAR-DIRECTO-01). Cada test convierte la suya (T11 con descuento por importe), por eso se siembran 4.
    ///    <b>No idempotente respecto de la suite</b>: una cotización ya convertida no sirve para repetir el test (base nueva por corrida).
    /// 3) <c>caja-abrir-monto-inicial.spec.js</c>: una caja activa sin turno abierto, para que <c>/Caja/Abrir</c> muestre el selector.
    ///
    /// Idempotente. Sembrar DESPUÉS de <see cref="ClienteAptitudPunitorioE2ESeeder"/> (configura DiasParaNoApto = 30).
    /// Nunca apuntar a la base de desarrollo — ver el runner.
    /// </summary>
    public static class OmitidosE2ESeeder
    {
        public const string UsuarioConPermisos = "administrador";
        public const string DniNoApto = "90000502";
        public const string CodigoCajaLibre = "E2ECAJALIBRE";
        public const string PrefijoCotizacionConversion = "E2ECONV-";
        private const string ClienteDocumentoGenerico = "90000001";
        private const string ProductoCodigoGenerico = "E2EGENERIC-01";
        private static readonly string[] ClaimsPunitorio =
            { "configuracion.managepunitorio", "configuracion.retroactivepunitorio" };

        public static async Task<IReadOnlyDictionary<string, string>> SembrarAsync(AppDbContext db)
        {
            await OtorgarPermisosPunitorioAsync(db);
            await AsegurarClienteNoAptoAsync(db);
            await AsegurarCajaLibreAsync(db);
            var cotizaciones = await AsegurarCotizacionesConversionAsync(db);
            return new Dictionary<string, string>
            {
                ["E2E_CLIENTE_NOAPTO_DNI"] = DniNoApto,
                ["E2E_COTIZACION_CONV_IDS"] = string.Join(",", cotizaciones)
            };
        }

        private static async Task OtorgarPermisosPunitorioAsync(AppDbContext db)
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == UsuarioConPermisos)
                ?? throw new InvalidOperationException($"No existe el usuario de prueba '{UsuarioConPermisos}'.");
            var roleId = await db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).FirstAsync();

            foreach (var claim in ClaimsPunitorio)
            {
                if (await db.RolPermisos.AnyAsync(rp => rp.RoleId == roleId && rp.ClaimValue == claim && !rp.IsDeleted))
                    continue;
                var partes = claim.Split('.');
                var accion = await db.AccionesModulo.Include(a => a.Modulo)
                    .FirstOrDefaultAsync(a => a.Clave == partes[1] && a.Modulo.Clave == partes[0])
                    ?? throw new InvalidOperationException($"El seeder de permisos no creó {claim}.");
                db.RolPermisos.Add(new RolPermiso
                {
                    RoleId = roleId,
                    ModuloId = accion.ModuloId,
                    AccionId = accion.Id,
                    ClaimValue = claim,
                    Observaciones = "Fixture E2E C-03 (base efímera)"
                });
            }
            await db.SaveChangesAsync();
        }

        private static async Task AsegurarClienteNoAptoAsync(AppDbContext db)
        {
            if (await db.Clientes.AnyAsync(c => c.NumeroDocumento == DniNoApto))
                return;

            var ahora = DateTime.UtcNow;
            var cliente = new Cliente
            {
                FechaUltimaActualizacionDatos = ahora,
                Nombre = "E2E NoApto",
                Apellido = "Cotizacion",
                TipoDocumento = "DNI",
                NumeroDocumento = DniNoApto,
                CuilCuit = "20900005025",
                Telefono = "1122334455",
                Domicilio = "Calle Falsa NoApto 123",
                Localidad = "CABA",
                Provincia = "Buenos Aires",
                Email = "e2e.noapto.cotizacion@example.invalid",
                Activo = true,
                PuntajeCliente = 3,
                // BCRA "OK": el único motivo de "No apto" es la mora de capital (ver ClienteAptitudPunitorioE2ESeeder).
                SituacionCrediticiaBcra = 1,
                SituacionCrediticiaConsultaOk = true,
                SituacionCrediticiaUltimaConsultaUtc = ahora.AddDays(-1),
                SituacionCrediticiaBcraUltimoExito = 1,
                SituacionCrediticiaUltimoExitoUtc = ahora.AddDays(-1),
                CreatedAt = ahora.AddDays(-400)
            };
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();

            var credito = new Credito
            {
                ClienteId = cliente.Id,
                Numero = "E2E-NOAPTO-COT",
                MontoSolicitado = 1000m,
                MontoAprobado = 1000m,
                TasaInteres = 0m,
                CantidadCuotas = 1,
                MontoCuota = 1000m,
                CFTEA = 0m,
                TotalAPagar = 1000m,
                SaldoPendiente = 1000m,
                Estado = EstadoCredito.Activo,
                FechaSolicitud = ahora.AddDays(-120),
                FechaAprobacion = ahora.AddDays(-119),
                FechaPrimeraCuota = ahora.AddDays(-90)
            };
            db.Creditos.Add(credito);
            await db.SaveChangesAsync();

            db.Cuotas.Add(new Cuota
            {
                CreditoId = credito.Id,
                NumeroCuota = 1,
                MontoCapital = 1000m,
                MontoInteres = 0m,
                MontoTotal = 1000m,
                MontoPagado = 0m,
                FechaVencimiento = ahora.AddDays(-90),
                Estado = EstadoCuota.Vencida
            });
            await db.SaveChangesAsync();
        }

        private static async Task<List<int>> AsegurarCotizacionesConversionAsync(AppDbContext db)
        {
            var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.NumeroDocumento == ClienteDocumentoGenerico)
                ?? throw new InvalidOperationException($"Falta el cliente genérico {ClienteDocumentoGenerico}: ejecutar GenericE2ESeedRunner.Sembrar antes.");
            var producto = await db.Productos.FirstOrDefaultAsync(p => p.Codigo == ProductoCodigoGenerico)
                ?? throw new InvalidOperationException($"Falta el producto genérico {ProductoCodigoGenerico}: ejecutar GenericE2ESeedRunner.Sembrar antes.");

            var ids = new List<int>();
            for (var i = 1; i <= 4; i++)
            {
                var numero = $"{PrefijoCotizacionConversion}{i:0000}";
                var existente = await db.Cotizaciones.Where(c => c.Numero == numero).Select(c => (int?)c.Id).FirstOrDefaultAsync();
                if (existente.HasValue) { ids.Add(existente.Value); continue; }

                // T11 (índice 3) lleva un descuento por importe en la línea; el resto, sin descuento.
                var descuento = i == 3 ? 50m : 0m;
                var subtotal = producto.PrecioVenta - descuento;
                var cotizacion = new Cotizacion
                {
                    Numero = numero,
                    Fecha = DateTime.UtcNow,
                    FechaVencimiento = DateTime.UtcNow.AddDays(30),
                    Estado = EstadoCotizacion.Emitida,
                    ClienteId = cliente.Id,
                    Subtotal = producto.PrecioVenta,
                    DescuentoTotal = descuento,
                    TotalBase = subtotal,
                    MedioPagoSeleccionado = CotizacionMedioPagoTipo.Efectivo,
                    PlanSeleccionado = "Efectivo",
                    TotalSeleccionado = subtotal
                };
                cotizacion.Detalles.Add(new CotizacionDetalle
                {
                    ProductoId = producto.Id,
                    CodigoProductoSnapshot = producto.Codigo,
                    NombreProductoSnapshot = producto.Nombre,
                    Cantidad = 1m,
                    PrecioUnitarioSnapshot = producto.PrecioVenta,
                    DescuentoImporteSnapshot = i == 3 ? 50m : null,
                    Subtotal = subtotal
                });
                db.Cotizaciones.Add(cotizacion);
                await db.SaveChangesAsync();
                ids.Add(cotizacion.Id);
            }
            return ids;
        }

        private static async Task AsegurarCajaLibreAsync(AppDbContext db)
        {
            if (await db.Cajas.AnyAsync(c => c.Codigo == CodigoCajaLibre))
                return;
            db.Cajas.Add(new Caja { Codigo = CodigoCajaLibre, Nombre = "Caja E2E libre (sin turno)", Activa = true });
            await db.SaveChangesAsync();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// PUN-ML10-G: infraestructura de seed VERSIONADA y reproducible para los 9 escenarios reales
    /// requeridos por <c>e2e/cliente-aptitud-punitorio.spec.js</c>. A diferencia de los seeds
    /// temporales <c>_Scratch_*</c> usados en cierres anteriores (PUN-ML9-E/F — ver
    /// [[punitorios-pun-ml-estado]]), este archivo se conserva en el repositorio: puede volver a
    /// ejecutarse contra cualquier LocalDB descartable nueva sin reescribirlo.
    ///
    /// No debe apuntarse nunca a la base de desarrollo ni a una base real — ver
    /// <see cref="ClienteAptitudPunitorioE2ESeedRunner"/> para el punto de entrada que exige la
    /// variable de entorno <c>E2E_SEED_CONNECTION</c> explícita (nunca usa el connection string por
    /// defecto de la app).
    ///
    /// Todas las entidades creadas quedan marcadas con <see cref="MarcadorApellido"/> en
    /// <c>Cliente.Apellido</c> para que <see cref="LimpiarAsync"/> pueda borrarlas de forma
    /// selectiva sin tocar datos ajenos. Ningún escenario depende de ids fijos preexistentes: cada
    /// corrida crea clientes nuevos con documento aleatorio y devuelve sus ids reales.
    /// </summary>
    public static class ClienteAptitudPunitorioE2ESeeder
    {
        public const string MarcadorApellido = "E2EPUNML10G";

        /// <summary>Ids resultantes de <see cref="SembrarAsync"/>, uno por escenario del goal.</summary>
        public sealed class ResultadoSeed
        {
            public int ClienteSinDeudaId { get; init; }
            public int ClienteMoraCapitalId { get; init; }
            public int ClientePunitorioId { get; init; }
            public int ClienteMoraYPunitorioId { get; init; }
            public int ClienteNoAptoPunitorioId { get; init; }
            public int ClientePunitorioParcialId { get; init; }
            public int ClientePunitorioPagadoId { get; init; }
            public int ClientePunitorioAnuladoId { get; init; }

            /// <summary>
            /// "Punitorio calculado no aplicado": deliberadamente el MISMO cliente que
            /// <see cref="ClienteMoraCapitalId"/> — ver nota en <see cref="SembrarAsync"/> sobre por
            /// qué no hace falta una entidad separada para probar esta regla.
            /// </summary>
            public int ClientePunitorioCalculadoNoAplicadoId => ClienteMoraCapitalId;

            /// <summary>Documento único del cliente Mora+Punitorio, usado por el spec para buscarlo por nombre/documento en Venta/Create.</summary>
            public string ClienteMoraYPunitorioDocumento { get; init; } = string.Empty;

            /// <summary>
            /// Crédito de 5 cuotas vencidas sin punitorio aplicado, para los specs legacy PUN-ML9
            /// (aplicar/anular, pago individual, returnUrl, adelanto/pago múltiple).
            /// </summary>
            public int ClienteOperacionesId { get; init; }
            public int CreditoOperacionesId { get; init; }
            public IReadOnlyList<int> CuotasOperacionesIds { get; init; } = Array.Empty<int>();
        }

        /// <summary>
        /// Siembra los 9 escenarios. Re-ejecutable: cada corrida genera documentos/nombres nuevos
        /// (sufijo aleatorio), nunca reutiliza ni depende de ids de una corrida anterior.
        /// </summary>
        public static async Task<ResultadoSeed> SembrarAsync(AppDbContext db)
        {
            var ahora = DateTime.UtcNow;
            var sufijo = Guid.NewGuid().ToString("N")[..8];

            // Config global mínima para que los 9 escenarios se comporten de forma determinística,
            // sin ruido de documentación/cupo/BCRA (fuera del alcance de este spec) — seguro porque
            // esta es una LocalDB descartable propia de esta corrida, nunca la de desarrollo.
            var config = await db.ConfiguracionesCredito
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync();
            if (config == null)
            {
                config = new ConfiguracionCredito();
                db.ConfiguracionesCredito.Add(config);
            }
            config.ValidarDocumentacion = false;
            config.ValidarLimiteCredito = false;
            config.ValidarMora = true;
            config.DiasParaRequerirAutorizacion = 1;
            config.DiasParaNoApto = 30; // escenario NoApto necesita mora de capital >= 30 días
            config.MontoMoraParaNoApto = null;
            config.CuotasVencidasParaNoApto = null;
            await db.SaveChangesAsync();

            // Configuración de punitorio activa y calculable (10%/20 días/5 gracia) — necesaria para
            // que el escenario "calculado no aplicado" sea real (un punitorio SÍ podría calcularse
            // sobre una cuota vencida) y no un caso vacío por falta de configuración.
            var configPunitorioVigente = await db.ConfiguracionesPunitorio
                .Where(p => p.Activa)
                .OrderByDescending(p => p.VigenteDesde)
                .FirstOrDefaultAsync();
            if (configPunitorioVigente == null)
            {
                configPunitorioVigente = new ConfiguracionPunitorio
                {
                    Porcentaje = 10m,
                    PeriodoDias = 20,
                    DiasGracia = 5,
                    ProrrateoDiario = true,
                    AplicacionRetroactiva = false,
                    VigenteDesde = DateOnly.FromDateTime(ahora).AddYears(-1),
                    Activa = true,
                    MotivoCambio = $"Seed E2E {MarcadorApellido} ({sufijo})"
                };
                db.ConfiguracionesPunitorio.Add(configPunitorioVigente);
                await db.SaveChangesAsync();
            }

            // ConfiguracionPago activa para Crédito Personal: sin esto, Venta/Create muestra "No hay
            // medios activos en la configuración global" y el wizard nunca dispara la verificación
            // de elegibilidad crediticia (hallazgo real de esta sesión, mismo patrón que el gap de
            // AperturaCaja/ConfiguracionPago de Transferencia documentado en el cierre de PUN-ML9-F).
            // Idempotente: no duplica en re-corridas.
            var tieneConfigPagoCredito = await db.ConfiguracionesPago.AnyAsync(c => c.TipoPago == TipoPago.CreditoPersonal);
            if (!tieneConfigPagoCredito)
            {
                db.ConfiguracionesPago.Add(new ConfiguracionPago
                {
                    TipoPago = TipoPago.CreditoPersonal,
                    Nombre = "Crédito Personal",
                    Activo = true,
                    TasaInteresMensualCreditoPersonal = 0m,
                    GastosAdministrativosDefaultCreditoPersonal = 0m
                });
                await db.SaveChangesAsync();
            }

            // Caja abierta para "administrador" (usuario E2E de este spec): Venta/Create rechaza
            // todo el flujo con la pantalla "Sin caja abierta" si no hay una AperturaCaja vigente —
            // DbInitializer no siembra ninguna (mismo gap ya documentado en el cierre de PUN-ML9-F).
            // Idempotente: no abre una segunda caja si ya existe una abierta con este marcador.
            var marcadorCaja = $"{MarcadorApellido}-CAJA";
            var cajaAbierta = await db.Cajas.FirstOrDefaultAsync(c => c.Codigo == marcadorCaja && c.Estado == EstadoCaja.Abierta);
            if (cajaAbierta == null)
            {
                var caja = await db.Cajas.FirstOrDefaultAsync(c => c.Codigo == marcadorCaja);
                if (caja == null)
                {
                    caja = new Caja
                    {
                        Codigo = marcadorCaja,
                        Nombre = "Caja Seed E2E PUN-ML10-G",
                        Activa = true,
                        Estado = EstadoCaja.Abierta
                    };
                    db.Cajas.Add(caja);
                }
                else
                {
                    caja.Estado = EstadoCaja.Abierta;
                }
                await db.SaveChangesAsync();

                db.AperturasCaja.Add(new AperturaCaja
                {
                    CajaId = caja.Id,
                    FechaApertura = ahora,
                    MontoInicial = 10000m,
                    UsuarioApertura = "administrador",
                    ObservacionesApertura = $"Seed E2E {MarcadorApellido} ({sufijo})",
                    Cerrada = false
                });
                await db.SaveChangesAsync();
            }

            // Producto mínimo vendible: el spec necesita poder agregar al menos un producto en
            // Venta/Create para llegar al step de pago y ejercitar la prevalidación de punitorio.
            // DbInitializer no siembra catálogo — sin esto, ese escenario quedaría con test.skip
            // (justo lo que este cierre prohíbe). Idempotente: no duplica en re-corridas.
            var marcadorProducto = $"{MarcadorApellido}-PRODUCTO";
            var tieneProducto = await db.Productos.AnyAsync(p => p.Codigo == marcadorProducto);
            if (!tieneProducto)
            {
                var categoria = await db.Categorias.FirstOrDefaultAsync(c => c.Codigo == marcadorProducto)
                    ?? new Categoria { Codigo = marcadorProducto, Nombre = "Seed E2E PUN-ML10-G", Activo = true };
                if (categoria.Id == 0)
                    db.Categorias.Add(categoria);

                var marca = await db.Marcas.FirstOrDefaultAsync(m => m.Codigo == marcadorProducto)
                    ?? new Marca { Codigo = marcadorProducto, Nombre = "Seed E2E PUN-ML10-G", Activo = true };
                if (marca.Id == 0)
                    db.Marcas.Add(marca);

                await db.SaveChangesAsync();

                db.Productos.Add(new Producto
                {
                    Codigo = marcadorProducto,
                    Nombre = "Producto Seed E2E PUN-ML10-G",
                    CategoriaId = categoria.Id,
                    MarcaId = marca.Id,
                    PrecioCompra = 1000m,
                    PrecioVenta = 2000m,
                    PorcentajeIVA = 21m,
                    StockActual = 100m,
                    UnidadMedida = "UN",
                    Activo = true
                });
                await db.SaveChangesAsync();
            }

            // A) Sin mora ni punitorio.
            var (sinDeuda, _) = await CrearClienteConCreditoAsync(db, "SinDeuda", sufijo, ahora,
                NuevaCuota(1, ahora.AddDays(30), montoTotal: 1000m, montoPagado: 0m, EstadoCuota.Pendiente));

            // B) Sólo mora de capital (también sirve para "punitorio calculado no aplicado":
            // la ConfiguracionPunitorio de arriba está activa y esta cuota está vencida más allá de
            // la gracia, así que un punitorio SÍ es calculable — pero nunca se aplicó ninguno. El
            // spec afirma explícitamente que no aparece ningún bloque de punitorio acá.
            var (moraCapital, _) = await CrearClienteConCreditoAsync(db, "MoraCapital", sufijo, ahora,
                NuevaCuota(1, ahora.AddDays(-40), montoTotal: 1000m, montoPagado: 0m, EstadoCuota.Vencida));

            // C) Sólo punitorio aplicado pendiente: capital saldado, punitorio aplicado sin pagos.
            var (soloPunitorio, soloPunitorioCuotas) = await CrearClienteConCreditoAsync(db, "SoloPunitorio", sufijo, ahora,
                NuevaCuota(1, ahora.AddDays(-40), montoTotal: 1000m, montoPagado: 1000m, EstadoCuota.Parcial));
            await AplicarPunitorioAsync(db, soloPunitorioCuotas[0], importe: 300m, ahora);

            // D) Mora de capital + punitorio aplicado pendiente (crédito con 2 cuotas).
            var (moraYPunitorio, moraYPunitorioCuotas) = await CrearClienteConCreditoAsync(db, "MoraYPunitorio", sufijo, ahora,
                NuevaCuota(1, ahora.AddDays(-20), montoTotal: 1000m, montoPagado: 0m, EstadoCuota.Vencida),
                NuevaCuota(2, ahora.AddDays(-45), montoTotal: 1000m, montoPagado: 1000m, EstadoCuota.Parcial));
            await AplicarPunitorioAsync(db, moraYPunitorioCuotas[1], importe: 250m, ahora);

            // E) Capital NoApto (mora >= DiasParaNoApto=30) + punitorio aplicado pendiente.
            var (noAptoPunitorio, noAptoCuotas) = await CrearClienteConCreditoAsync(db, "NoAptoPunitorio", sufijo, ahora,
                NuevaCuota(1, ahora.AddDays(-90), montoTotal: 1000m, montoPagado: 0m, EstadoCuota.Vencida),
                NuevaCuota(2, ahora.AddDays(-45), montoTotal: 1000m, montoPagado: 1000m, EstadoCuota.Parcial));
            await AplicarPunitorioAsync(db, noAptoCuotas[1], importe: 275m, ahora);

            // F) Punitorio parcialmente pagado: Importe=500, pago parcial de 200 => pendiente neto 300.
            var (punitorioParcial, parcialCuotas) = await CrearClienteConCreditoAsync(db, "PunitorioParcial", sufijo, ahora,
                NuevaCuota(1, ahora.AddDays(-40), montoTotal: 1000m, montoPagado: 1000m, EstadoCuota.Parcial));
            var aplicacionParcial = await AplicarPunitorioAsync(db, parcialCuotas[0], importe: 500m, ahora);
            db.PagosCuota.Add(new PagoCuota
            {
                CuotaId = parcialCuotas[0].Id,
                FechaPagoComercial = DateOnly.FromDateTime(ahora),
                ImporteTotal = 200m,
                ImporteAplicadoCuota = 0m,
                ImporteAplicadoPunitorio = 200m,
                Origen = OrigenPagoCuota.RegistradoPorSistema,
                Estado = EstadoPagoCuota.Aplicado,
                HistorialCompleto = true,
                PunitorioAplicadoId = aplicacionParcial.Id
            });
            await db.SaveChangesAsync();

            // G) Punitorio totalmente pagado: Estado=Pagado, no debe contar como pendiente.
            var (punitorioPagado, pagadoCuotas) = await CrearClienteConCreditoAsync(db, "PunitorioPagado", sufijo, ahora,
                NuevaCuota(1, ahora.AddDays(-40), montoTotal: 1000m, montoPagado: 1000m, EstadoCuota.Pagada));
            var aplicacionPagada = await AplicarPunitorioAsync(db, pagadoCuotas[0], importe: 400m, ahora);
            aplicacionPagada.Estado = EstadoPunitorioAplicado.Pagado;
            db.PagosCuota.Add(new PagoCuota
            {
                CuotaId = pagadoCuotas[0].Id,
                FechaPagoComercial = DateOnly.FromDateTime(ahora),
                ImporteTotal = 400m,
                ImporteAplicadoCuota = 0m,
                ImporteAplicadoPunitorio = 400m,
                Origen = OrigenPagoCuota.RegistradoPorSistema,
                Estado = EstadoPagoCuota.Aplicado,
                HistorialCompleto = true,
                PunitorioAplicadoId = aplicacionPagada.Id
            });
            await db.SaveChangesAsync();

            // H) Punitorio anulado: no debe contar como pendiente.
            var (punitorioAnulado, anuladoCuotas) = await CrearClienteConCreditoAsync(db, "PunitorioAnulado", sufijo, ahora,
                NuevaCuota(1, ahora.AddDays(-40), montoTotal: 1000m, montoPagado: 1000m, EstadoCuota.Pagada));
            var aplicacionAnulada = await AplicarPunitorioAsync(db, anuladoCuotas[0], importe: 350m, ahora);
            aplicacionAnulada.Estado = EstadoPunitorioAplicado.Anulado;
            aplicacionAnulada.FechaAnulacion = ahora;
            aplicacionAnulada.UsuarioAnulacion = "seed-e2e";
            aplicacionAnulada.MotivoAnulacion = $"Seed E2E {MarcadorApellido} ({sufijo}): anulado deliberadamente para probar que no cuenta como pendiente.";
            await db.SaveChangesAsync();

            // I) Crédito de "operaciones" para los specs legacy PUN-ML9 (aplicar/anular punitorio,
            // pago individual, returnUrl, adelanto/pago múltiple): 5 cuotas vencidas, capital
            // pendiente, SIN ningún punitorio aplicado — cada test de esos specs usa una cuota
            // distinta para no chocar con el índice único "una aplicación activa por cuota"
            // (Modelo A). También sirve para "pago múltiple" (2+ cuotas pagables) y "adelanto"
            // (última cuota pendiente), sin necesitar un crédito aparte.
            var (clienteOperaciones, cuotasOperaciones) = await CrearClienteConCreditoAsync(db, "Operaciones", sufijo, ahora,
                NuevaCuota(1, ahora.AddDays(-10), montoTotal: 1000m, montoPagado: 0m, EstadoCuota.Vencida),
                NuevaCuota(2, ahora.AddDays(-15), montoTotal: 1000m, montoPagado: 0m, EstadoCuota.Vencida),
                NuevaCuota(3, ahora.AddDays(-20), montoTotal: 1000m, montoPagado: 0m, EstadoCuota.Vencida),
                NuevaCuota(4, ahora.AddDays(-25), montoTotal: 1000m, montoPagado: 0m, EstadoCuota.Vencida),
                NuevaCuota(5, ahora.AddDays(-30), montoTotal: 1000m, montoPagado: 0m, EstadoCuota.Vencida),
                // 6ª cuota, ya con punitorio aplicado — dedicada a CUOTA_CON_APLICACION
                // (credito-pago-cuota-individual.spec.js) sin consumir ninguna de las 5 "limpias".
                NuevaCuota(6, ahora.AddDays(-35), montoTotal: 1000m, montoPagado: 0m, EstadoCuota.Vencida));
            var creditoOperacionesId = cuotasOperaciones[0].CreditoId;
            await AplicarPunitorioAsync(db, cuotasOperaciones[5], importe: 150m, ahora);

            return new ResultadoSeed
            {
                ClienteSinDeudaId = sinDeuda.Id,
                ClienteMoraCapitalId = moraCapital.Id,
                ClientePunitorioId = soloPunitorio.Id,
                ClienteMoraYPunitorioId = moraYPunitorio.Id,
                ClienteNoAptoPunitorioId = noAptoPunitorio.Id,
                ClientePunitorioParcialId = punitorioParcial.Id,
                ClientePunitorioPagadoId = punitorioPagado.Id,
                ClientePunitorioAnuladoId = punitorioAnulado.Id,
                ClienteMoraYPunitorioDocumento = moraYPunitorio.NumeroDocumento,
                ClienteOperacionesId = clienteOperaciones.Id,
                CreditoOperacionesId = creditoOperacionesId,
                CuotasOperacionesIds = cuotasOperaciones.Select(c => c.Id).ToList()
            };
        }

        /// <summary>
        /// Borra únicamente lo que este seeder creó (clientes marcados con
        /// <see cref="MarcadorApellido"/> y toda su cadena de crédito/cuotas/pagos/punitorios).
        /// No toca <c>ConfiguracionCredito</c>/<c>ConfiguracionPunitorio</c> (streams globales
        /// append-only de una sola fila / pocas filas — inofensivos en una LocalDB descartable que
        /// de todos modos se dropea al cerrar la sesión).
        /// </summary>
        public static async Task LimpiarAsync(AppDbContext db)
        {
            var clienteIds = await db.Clientes
                .Where(c => c.Apellido == MarcadorApellido)
                .Select(c => c.Id)
                .ToListAsync();

            if (clienteIds.Count == 0)
                return;

            var creditoIds = await db.Creditos
                .Where(c => clienteIds.Contains(c.ClienteId))
                .Select(c => c.Id)
                .ToListAsync();

            var cuotaIds = await db.Cuotas
                .Where(c => creditoIds.Contains(c.CreditoId))
                .Select(c => c.Id)
                .ToListAsync();

            var punitorioIds = await db.PunitoriosAplicados
                .Where(p => cuotaIds.Contains(p.CuotaId))
                .Select(p => p.Id)
                .ToListAsync();

            // Orden de borrado respeta las FK Restrict documentadas (PagoCuota -> PunitorioAplicado).
            var pagos = await db.PagosCuota.Where(p => cuotaIds.Contains(p.CuotaId)).ToListAsync();
            db.PagosCuota.RemoveRange(pagos);
            await db.SaveChangesAsync();

            var punitorios = await db.PunitoriosAplicados.Where(p => punitorioIds.Contains(p.Id)).ToListAsync();
            db.PunitoriosAplicados.RemoveRange(punitorios);
            await db.SaveChangesAsync();

            var cuotas = await db.Cuotas.Where(c => cuotaIds.Contains(c.Id)).ToListAsync();
            db.Cuotas.RemoveRange(cuotas);
            await db.SaveChangesAsync();

            var creditos = await db.Creditos.Where(c => creditoIds.Contains(c.Id)).ToListAsync();
            db.Creditos.RemoveRange(creditos);
            await db.SaveChangesAsync();

            var clientes = await db.Clientes.Where(c => clienteIds.Contains(c.Id)).ToListAsync();
            db.Clientes.RemoveRange(clientes);
            await db.SaveChangesAsync();
        }

        private static Cuota NuevaCuota(int numero, DateTime vencimiento, decimal montoTotal, decimal montoPagado, EstadoCuota estado) => new()
        {
            NumeroCuota = numero,
            MontoCapital = montoTotal,
            MontoInteres = 0m,
            MontoTotal = montoTotal,
            MontoPagado = montoPagado,
            FechaVencimiento = vencimiento,
            FechaPago = estado == EstadoCuota.Pagada ? vencimiento : null,
            Estado = estado
        };

        private static async Task<(Cliente Cliente, List<Cuota> Cuotas)> CrearClienteConCreditoAsync(
            AppDbContext db, string escenario, string sufijo, DateTime ahora, params Cuota[] cuotas)
        {
            // Único por cliente (no por escenario): dos nombres de escenario distintos podían
            // truncar al mismo prefijo (bug real encontrado al sembrar por primera vez — "SinDeuda"
            // y "SoloPunitorio" colisionaban en "E2E...S" antes de este fix).
            var documento = $"E2E{sufijo}{Guid.NewGuid():N}"[..16];
            var cliente = new Cliente
            {
                Nombre = "E2E ML10G",
                Apellido = MarcadorApellido,
                TipoDocumento = "DNI",
                NumeroDocumento = documento,
                CuilCuit = $"20{documento}5"[..11],
                Telefono = "1122334455",
                Domicilio = $"Calle Falsa {escenario} 123",
                Localidad = "CABA",
                Provincia = "Buenos Aires",
                Email = $"e2e.{sufijo}.{escenario}@example.invalid".ToLowerInvariant(),
                Activo = true,
                PuntajeCliente = 3,
                // BCRA "OK" para no contaminar los escenarios con el bloqueo obligatorio de BCRA
                // (ver ClienteAptitudService.ConstruirBcraDetalle: sin CUIL/CUIT o sin consulta
                // exitosa, cualquier cliente es NoApto por BCRA, sin importar mora/punitorio).
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
                Numero = $"E2E-{sufijo}-{escenario}",
                MontoSolicitado = cuotas.Sum(c => c.MontoTotal),
                MontoAprobado = cuotas.Sum(c => c.MontoTotal),
                TasaInteres = 0m,
                CantidadCuotas = cuotas.Length,
                MontoCuota = cuotas.Length > 0 ? cuotas[0].MontoTotal : 0m,
                CFTEA = 0m,
                TotalAPagar = cuotas.Sum(c => c.MontoTotal),
                SaldoPendiente = cuotas.Sum(c => c.MontoTotal - c.MontoPagado),
                Estado = EstadoCredito.Activo,
                FechaSolicitud = ahora.AddDays(-60),
                FechaAprobacion = ahora.AddDays(-59),
                FechaPrimeraCuota = cuotas.Length > 0 ? cuotas[0].FechaVencimiento : ahora
            };
            db.Creditos.Add(credito);
            await db.SaveChangesAsync();

            foreach (var cuota in cuotas)
                cuota.CreditoId = credito.Id;
            db.Cuotas.AddRange(cuotas);
            await db.SaveChangesAsync();

            return (cliente, cuotas.ToList());
        }

        private static async Task<PunitorioAplicado> AplicarPunitorioAsync(AppDbContext db, Cuota cuota, decimal importe, DateTime ahora)
        {
            var aplicado = new PunitorioAplicado
            {
                CuotaId = cuota.Id,
                FechaCalculo = DateOnly.FromDateTime(ahora),
                SaldoBase = cuota.MontoTotal,
                DiasComputados = 20,
                Importe = importe,
                Estado = EstadoPunitorioAplicado.Aplicado,
                DesgloseSnapshotJson = "{}",
                MotivoAplicacion = $"Seed E2E {MarcadorApellido}",
                FechaAplicacion = ahora,
                UsuarioAplicacion = "seed-e2e"
            };
            db.PunitoriosAplicados.Add(aplicado);
            await db.SaveChangesAsync();
            return aplicado;
        }
    }
}

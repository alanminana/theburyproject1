using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// PR #75: dataset SINTÉTICO para la evidencia visual de <c>/Mora/ClientesMora</c> y
    /// <c>/Mora/FichaCliente</c> (<c>e2e/pr75-mora-visual-evidence.spec.js</c>).
    ///
    /// La bandeja se alimenta de <c>AlertasCobranza</c> (no de las cuotas), que ningún otro seeder
    /// crea. Este seeder inserta directamente clientes, créditos con cuotas vencidas, alertas,
    /// contactos y un acuerdo. No pasa por ProcesarMora ni calcula nada: los montos son datos fijos
    /// de prueba. Pensado únicamente para la base efímera de CI (<c>E2E_SEED_CONNECTION</c>
    /// explícita, ver <see cref="MoraVisualE2ESeedRunner"/>); nunca contra una base persistente.
    /// </summary>
    public static class MoraVisualE2ESeeder
    {
        public const string MarcadorApellido = "PR75";
        // 26 > 20 (tamaño de página de la bandeja): fuerza segunda página.
        public const int TotalClientes = 26;

        public sealed class ResultadoSeed
        {
            public int ClienteFichaRicaId { get; init; }
            public string ClienteFichaRicaDocumento { get; init; } = string.Empty;
            public int ClienteSinContactosId { get; init; }
            public int ClienteNombreLargoId { get; init; }
        }

        public static async Task<ResultadoSeed> SembrarAsync(AppDbContext db)
        {
            var ahora = DateTime.UtcNow;
            var prioridades = new[] { PrioridadAlerta.Baja, PrioridadAlerta.Media, PrioridadAlerta.Alta, PrioridadAlerta.Critica };
            var apellidos = new[] { "Acosta", "Benítez", "Cabrera", "Domínguez", "Escobar", "Fernández", "Gómez", "Herrera", "Ibarra", "Juárez", "Luna", "Molina", "Navarro" };

            int fichaRicaId = 0, sinContactosId = 0, nombreLargoId = 0;
            string fichaRicaDoc = string.Empty;

            for (var i = 0; i < TotalClientes; i++)
            {
                var diasAtraso = 8 + i * 6;                       // 8..158 días
                var prioridad = i == 0 ? PrioridadAlerta.Critica : prioridades[i % prioridades.Length];
                var documento = $"9075{i:0000}";
                var nombreLargo = i == 3;

                var cliente = new Cliente
                {
                    Nombre = nombreLargo ? "María de los Ángeles Guadalupe" : $"Cliente {i + 1:00}",
                    Apellido = nombreLargo
                        ? "Rodríguez de la Fuente Villanueva"
                        : $"{apellidos[i % apellidos.Length]} {MarcadorApellido}",
                    TipoDocumento = "DNI",
                    NumeroDocumento = documento,
                    CuilCuit = $"2075{i:0000}5",
                    Telefono = $"11{40000000 + i * 137:00000000}",
                    TelefonoLaboral = i == 0 ? "1155550100" : null,
                    Domicilio = $"Calle Sintética {100 + i}",
                    Localidad = "CABA",
                    Provincia = "Buenos Aires",
                    Email = $"mora.pr75.{i:00}@example.invalid",
                    Activo = true,
                    PuntajeCliente = 3,
                    CreatedAt = ahora.AddDays(-400)
                };
                db.Clientes.Add(cliente);
                await db.SaveChangesAsync();

                // El cliente 0 (ficha rica) tiene 2 créditos en mora; el resto 1.
                var cuotasPorCredito = i == 0 ? new[] { 3, 2 } : new[] { 1 + i % 3 };
                var alertasCliente = new List<AlertaCobranza>();
                var creditoIdx = 0;
                foreach (var nCuotas in cuotasPorCredito)
                {
                    creditoIdx++;
                    var montoCuota = 15000m + (i * 2350m) + (creditoIdx * 1000m);
                    var credito = new Credito
                    {
                        ClienteId = cliente.Id,
                        Numero = $"PR75-{i:00}-{creditoIdx}",
                        MontoSolicitado = montoCuota * (nCuotas + 3),
                        MontoAprobado = montoCuota * (nCuotas + 3),
                        TasaInteres = 0m,
                        CantidadCuotas = nCuotas + 3,
                        MontoCuota = montoCuota,
                        CFTEA = 0m,
                        TotalAPagar = montoCuota * (nCuotas + 3),
                        SaldoPendiente = montoCuota * (nCuotas + 3),
                        Estado = EstadoCredito.Activo,
                        FechaSolicitud = ahora.AddDays(-(diasAtraso + 120)),
                        FechaAprobacion = ahora.AddDays(-(diasAtraso + 119)),
                        FechaPrimeraCuota = ahora.AddDays(-(diasAtraso + (nCuotas - 1) * 30))
                    };
                    db.Creditos.Add(credito);
                    await db.SaveChangesAsync();

                    for (var c = 1; c <= nCuotas; c++)
                    {
                        db.Cuotas.Add(new Cuota
                        {
                            CreditoId = credito.Id,
                            NumeroCuota = c,
                            MontoCapital = montoCuota,
                            MontoInteres = 0m,
                            MontoTotal = montoCuota,
                            MontoPagado = 0m,
                            FechaVencimiento = ahora.AddDays(-(diasAtraso + (nCuotas - c) * 30)),
                            Estado = EstadoCuota.Vencida
                        });
                    }
                    await db.SaveChangesAsync();

                    var alerta = new AlertaCobranza
                    {
                        CreditoId = credito.Id,
                        ClienteId = cliente.Id,
                        Tipo = TipoAlertaCobranza.CuotaVencida,
                        Prioridad = creditoIdx == 1 ? prioridad : PrioridadAlerta.Alta,
                        Mensaje = $"Crédito {credito.Numero}: {nCuotas} cuota(s) vencida(s), {diasAtraso} días de atraso.",
                        MontoVencido = montoCuota * nCuotas,
                        MontoMoraCalculada = Math.Round(montoCuota * nCuotas * 0.0015m * diasAtraso, 2),
                        MontoTotal = montoCuota * nCuotas,
                        DiasAtraso = diasAtraso,
                        CuotasVencidas = nCuotas,
                        EstadoGestion = EstadoGestionCobranza.Pendiente,
                        FechaAlerta = ahora.AddDays(-2)
                    };
                    db.AlertasCobranza.Add(alerta);
                    alertasCliente.Add(alerta);
                    await db.SaveChangesAsync();
                }

                if (i == 0)
                {
                    // Ficha rica: promesa vigente, 4 contactos de distinto tipo/resultado y un acuerdo activo.
                    fichaRicaId = cliente.Id;
                    fichaRicaDoc = documento;
                    var a0 = alertasCliente[0];
                    a0.EstadoGestion = EstadoGestionCobranza.PromesaPago;
                    a0.FechaPromesaPago = ahora.Date.AddDays(7);
                    a0.MontoPromesaPago = 30000m;
                    alertasCliente[1].EstadoGestion = EstadoGestionCobranza.AcuerdoActivo;
                    await db.SaveChangesAsync();

                    var contactos = new (TipoContacto Tipo, ResultadoContacto Res, int DiasAtras, string Obs, DateTime? Prox)[]
                    {
                        (TipoContacto.LlamadaTelefonica, ResultadoContacto.NoContesta, 20, "No atiende. Se deja mensaje en contestador.", null),
                        (TipoContacto.WhatsApp, ResultadoContacto.ContactoExitoso, 14, "Responde por WhatsApp; reconoce la deuda y pide plazo para regularizar.", null),
                        (TipoContacto.LlamadaTelefonica, ResultadoContacto.PromesaPago, 6, "Promete pago parcial dentro de la semana. Observación sintética un poco más larga para verificar que el texto se acomoda sin cortarse ni desbordar la tarjeta del historial en pantallas angostas.", ahora.Date.AddDays(7)),
                        (TipoContacto.NotaInterna, ResultadoContacto.ContactoExitoso, 1, "Nota interna de seguimiento.", null),
                    };
                    foreach (var c in contactos)
                    {
                        db.HistorialContactos.Add(new HistorialContacto
                        {
                            AlertaCobranzaId = a0.Id,
                            ClienteId = cliente.Id,
                            GestorId = "seed-e2e-pr75",
                            FechaContacto = ahora.AddDays(-c.DiasAtras),
                            TipoContacto = c.Tipo,
                            Resultado = c.Res,
                            Telefono = c.Tipo == TipoContacto.LlamadaTelefonica || c.Tipo == TipoContacto.WhatsApp ? cliente.Telefono : null,
                            Observaciones = c.Obs,
                            DuracionMinutos = c.Tipo == TipoContacto.LlamadaTelefonica ? 3 : null,
                            ProximoContacto = c.Prox,
                            FechaPromesaPago = c.Res == ResultadoContacto.PromesaPago ? ahora.Date.AddDays(7) : null,
                            MontoPromesaPago = c.Res == ResultadoContacto.PromesaPago ? 30000m : null,
                            CreatedAt = ahora.AddDays(-c.DiasAtras)
                        });
                    }

                    var acuerdo = new AcuerdoPago
                    {
                        AlertaCobranzaId = alertasCliente[1].Id,
                        ClienteId = cliente.Id,
                        CreditoId = alertasCliente[1].CreditoId,
                        NumeroAcuerdo = "AC-PR75-0001",
                        FechaCreacion = ahora.AddDays(-5),
                        FechaActivacion = ahora.AddDays(-4),
                        Estado = EstadoAcuerdo.Activo,
                        MontoDeudaOriginal = 40000m,
                        MontoMoraOriginal = 3000m,
                        MontoCondonado = 1000m,
                        MontoTotalAcuerdo = 42000m,
                        MontoEntregaInicial = 12000m,
                        EntregaInicialPagada = true,
                        FechaPagoEntrega = ahora.AddDays(-4),
                        CantidadCuotas = 3,
                        FechaPrimeraCuota = ahora.Date.AddDays(20),
                        MontoCuotaAcuerdo = 10000m
                    };
                    db.AcuerdosPago.Add(acuerdo);
                    await db.SaveChangesAsync();
                    for (var c = 1; c <= 3; c++)
                    {
                        db.CuotasAcuerdo.Add(new CuotaAcuerdo
                        {
                            AcuerdoPagoId = acuerdo.Id,
                            NumeroCuota = c,
                            MontoCapital = 10000m,
                            MontoTotal = 10000m,
                            FechaVencimiento = ahora.Date.AddDays(20 + (c - 1) * 30),
                            Estado = EstadoCuotaAcuerdo.Pendiente
                        });
                    }
                    await db.SaveChangesAsync();
                }
                else if (i == 1)
                {
                    sinContactosId = cliente.Id; // alertas sin contactos ni promesa ni acuerdo
                }

                if (nombreLargo)
                    nombreLargoId = cliente.Id;
            }

            return new ResultadoSeed
            {
                ClienteFichaRicaId = fichaRicaId,
                ClienteFichaRicaDocumento = fichaRicaDoc,
                ClienteSinContactosId = sinContactosId,
                ClienteNombreLargoId = nombreLargoId
            };
        }
    }
}

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
    /// C-03 (2026-11-11): configuracion global que SOLO necesitan dos tests E2E y que el dataset compartido no puede tener sin
    /// alterar al resto de la suite (por eso se activa y se restaura alrededor de un paso aislado del workflow, nunca en el seed comun):
    /// <list type="bullet">
    /// <item><c>cotizacion-credito-personal-excepcion.spec.js</c>: <c>ValidarDocumentacion=true</c> (el dataset la apaga a proposito,
    ///   <see cref="ClienteAptitudPunitorioE2ESeeder"/>) y Credito personal configurado (tasa global y planes de cuotas), sin los cuales
    ///   la fila de Credito personal no existe en el simulador.</item>
    /// <item><c>cotizacion-simulador.spec.js</c> T5: un medio con varios planes (Tarjeta de credito con una tarjeta y planes de 1, 3 y 6
    ///   cuotas con recargo) para el grupo expandible.</item>
    /// </list>
    /// No hay ruta HTTP/UI que edite <c>ConfiguracionCredito</c>, por eso se usa el mismo mecanismo que los seeders. Playwright corre con
    /// 1 worker y el paso aislado ejecuta solo los tests marcados <c>@config-aislada</c>: nada mas corre mientras esta activa.
    /// <see cref="RestaurarAsync"/> deja la base en el estado del dataset comun (idempotente; solo borra lo que marco este seeder).
    /// </summary>
    public static class ConfiguracionAisladaE2ESeeder
    {
        public const string Marca = "e2e-config-aislada";
        public const string NombreTarjeta = "E2E Tarjeta Planes";

        public static async Task ActivarAsync(AppDbContext db)
        {
            var config = await db.ConfiguracionesCredito.OrderByDescending(c => c.Id).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("No existe ConfiguracionCredito: ejecutar ClienteAptitudPunitorioE2ESeedRunner.Sembrar antes.");
            config.ValidarDocumentacion = true;
            await db.SaveChangesAsync();

            await ActivarCreditoPersonalAsync(db);
            await ActivarPlanesTarjetaCreditoAsync(db);
        }

        public static async Task RestaurarAsync(AppDbContext db)
        {
            var config = await db.ConfiguracionesCredito.OrderByDescending(c => c.Id).FirstOrDefaultAsync();
            if (config != null)
            {
                config.ValidarDocumentacion = false;
            }

            // El dataset comun no tiene planes ni tasa global de Credito personal: si hay planes marcados, esta clase los activo.
            var planesCredito = await db.ConfiguracionCreditoPersonalCuotas.Where(c => c.UsuarioActualizacion == Marca).ToListAsync();
            if (planesCredito.Count > 0)
            {
                db.ConfiguracionCreditoPersonalCuotas.RemoveRange(planesCredito);
                var medioCredito = await db.ConfiguracionesPago.FirstOrDefaultAsync(c => c.TipoPago == TipoPago.CreditoPersonal && !c.IsDeleted);
                if (medioCredito != null)
                {
                    medioCredito.TasaInteresMensualCreditoPersonal = null;
                }
            }

            var medioTarjeta = await db.ConfiguracionesPago.FirstOrDefaultAsync(c => c.TipoPago == TipoPago.TarjetaCredito && c.Descripcion == Marca);
            if (medioTarjeta != null)
            {
                db.ConfiguracionPagoPlanes.RemoveRange(await db.ConfiguracionPagoPlanes.Where(p => p.ConfiguracionPagoId == medioTarjeta.Id).ToListAsync());
                db.ConfiguracionesTarjeta.RemoveRange(await db.ConfiguracionesTarjeta.Where(t => t.ConfiguracionPagoId == medioTarjeta.Id).ToListAsync());
                db.ConfiguracionesPago.Remove(medioTarjeta);
            }
            await db.SaveChangesAsync();
        }

        private static async Task ActivarCreditoPersonalAsync(AppDbContext db)
        {
            var medio = await db.ConfiguracionesPago.FirstOrDefaultAsync(c => c.TipoPago == TipoPago.CreditoPersonal && !c.IsDeleted)
                ?? throw new InvalidOperationException("El dataset no tiene el medio Credito personal (seed de produccion).");
            if (medio.TasaInteresMensualCreditoPersonal == null)
            {
                medio.TasaInteresMensualCreditoPersonal = 10m;
            }

            var orden = 1;
            foreach (var (cuotas, recargo) in new[] { (3, 10m), (6, 15m), (12, 25m) })
            {
                if (!await db.ConfiguracionCreditoPersonalCuotas.AnyAsync(c => c.CantidadCuotas == cuotas))
                {
                    db.ConfiguracionCreditoPersonalCuotas.Add(new ConfiguracionCreditoPersonalCuota
                    {
                        CantidadCuotas = cuotas,
                        TasaMensual = recargo,
                        Activo = true,
                        Orden = orden,
                        UsuarioActualizacion = Marca
                    });
                }
                orden++;
            }
            await db.SaveChangesAsync();
        }

        private static async Task ActivarPlanesTarjetaCreditoAsync(AppDbContext db)
        {
            var medio = await db.ConfiguracionesPago.FirstOrDefaultAsync(c => c.TipoPago == TipoPago.TarjetaCredito && !c.IsDeleted);
            if (medio == null)
            {
                medio = new ConfiguracionPago
                {
                    TipoPago = TipoPago.TarjetaCredito,
                    Nombre = "Tarjeta de crédito",
                    Descripcion = Marca,
                    Activo = true
                };
                db.ConfiguracionesPago.Add(medio);
                await db.SaveChangesAsync();
            }

            var tarjeta = await db.ConfiguracionesTarjeta.FirstOrDefaultAsync(t => t.ConfiguracionPagoId == medio.Id && t.NombreTarjeta == NombreTarjeta);
            if (tarjeta == null)
            {
                tarjeta = new ConfiguracionTarjeta
                {
                    ConfiguracionPagoId = medio.Id,
                    NombreTarjeta = NombreTarjeta,
                    TipoTarjeta = TipoTarjeta.Credito,
                    Activa = true,
                    PermiteCuotas = true,
                    CantidadMaximaCuotas = 6,
                    TipoCuota = TipoCuotaTarjeta.ConInteres
                };
                db.ConfiguracionesTarjeta.Add(tarjeta);
                await db.SaveChangesAsync();
            }

            var orden = 1;
            foreach (var (cuotas, recargo) in new[] { (1, 5m), (3, 12m), (6, 20m) })
            {
                if (!await db.ConfiguracionPagoPlanes.AnyAsync(p => p.ConfiguracionPagoId == medio.Id && p.ConfiguracionTarjetaId == tarjeta.Id
                        && p.CantidadCuotas == cuotas && !p.IsDeleted))
                {
                    db.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
                    {
                        ConfiguracionPagoId = medio.Id,
                        ConfiguracionTarjetaId = tarjeta.Id,
                        TipoPago = TipoPago.TarjetaCredito,
                        CantidadCuotas = cuotas,
                        Activo = true,
                        AjustePorcentaje = recargo,
                        Orden = orden
                    });
                }
                orden++;
            }
            await db.SaveChangesAsync();
        }
    }
}

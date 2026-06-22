using System;
using System.Collections.Generic;
using System.Linq;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Lógica pura del scoring de comportamiento del cliente. Sin dependencias de DB:
    /// recibe los datos ya cargados y devuelve snapshot/puntaje. Es la autoridad del cálculo.
    /// </summary>
    public static class ClienteScoringCalculator
    {
        // Aproximación de días por mes para traducir umbrales configurados en meses.
        private const int DiasPorMes = 30;

        // Estados de venta que cuentan como "venta real" para la última venta.
        private static readonly HashSet<EstadoVenta> EstadosVentaReal = new()
        {
            EstadoVenta.Confirmada,
            EstadoVenta.Facturada,
            EstadoVenta.Entregada
        };

        /// <summary>
        /// Calcula el snapshot de comportamiento a partir de los datos del cliente.
        /// </summary>
        public static ClienteScoringSnapshot CalcularSnapshot(
            DateTime clienteCreatedAt,
            IEnumerable<Venta> ventas,
            IEnumerable<Credito> creditos,
            DateTime ahora)
        {
            ventas ??= Enumerable.Empty<Venta>();
            creditos ??= Enumerable.Empty<Credito>();

            var snapshot = new ClienteScoringSnapshot
            {
                AntiguedadDias = Math.Max(0, (ahora.Date - clienteCreatedAt.Date).Days)
            };

            var ventasReales = ventas
                .Where(v => !v.IsDeleted && EstadosVentaReal.Contains(v.Estado))
                .ToList();
            if (ventasReales.Count > 0)
                snapshot.UltimaVentaFecha = ventasReales.Max(v => v.FechaVenta);

            foreach (var credito in creditos.Where(c => !c.IsDeleted))
            {
                var cuotas = credito.Cuotas
                    .Where(q => q.Estado != EstadoCuota.Cancelada)
                    .ToList();

                // Crédito sin cuotas reales: todavía no entró en repago, no lo contamos.
                if (cuotas.Count == 0)
                    continue;

                if (cuotas.Any(q => EsCuotaConAtraso(q, ahora)))
                {
                    snapshot.CreditosConAtraso++;
                    continue;
                }

                // Sólo cuenta como "en término" si efectivamente pagó algo (historial demostrado).
                if (cuotas.Any(EsCuotaPagada))
                    snapshot.CreditosEnTermino++;
            }

            return snapshot;
        }

        /// <summary>
        /// Aplica la configuración sobre el snapshot y devuelve el puntaje final acotado.
        /// </summary>
        public static int CalcularPuntaje(
            ClienteScoringSnapshot snapshot,
            decimal? sueldo,
            ConfiguracionScoringCliente config,
            DateTime ahora)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(config);

            var puntaje = config.PuntajeBase;

            if (config.AntiguedadActiva &&
                snapshot.AntiguedadDias >= config.AntiguedadMesesUmbral * DiasPorMes)
            {
                puntaje += config.AntiguedadPuntos;
            }

            if (config.ActividadActiva &&
                snapshot.UltimaVentaFecha.HasValue &&
                (ahora.Date - snapshot.UltimaVentaFecha.Value.Date).Days <= config.ActividadMesesUmbral * DiasPorMes)
            {
                puntaje += config.ActividadPuntos;
            }

            if (config.PagoEnTerminoActivo)
            {
                if (snapshot.CreditosConAtraso > 0)
                    puntaje += config.PagoConAtrasoPuntos;
                else if (snapshot.CreditosEnTermino > 0)
                    puntaje += config.PagoEnTerminoPuntos;
            }

            if (config.SueldoActivo && sueldo.HasValue && sueldo.Value >= config.SueldoUmbral)
            {
                puntaje += config.SueldoPuntos;
            }

            var min = Math.Min(config.PuntajeMinimo, config.PuntajeMaximo);
            var max = Math.Max(config.PuntajeMinimo, config.PuntajeMaximo);
            return Math.Clamp(puntaje, min, max);
        }

        private static bool EsCuotaConAtraso(Cuota cuota, DateTime ahora)
        {
            if (cuota.Estado == EstadoCuota.Vencida)
                return true;

            // Pagada tarde.
            if (cuota.FechaPago.HasValue && cuota.FechaPago.Value.Date > cuota.FechaVencimiento.Date)
                return true;

            // Pendiente/parcial y ya vencida (aún no marcada como Vencida).
            if ((cuota.Estado == EstadoCuota.Pendiente || cuota.Estado == EstadoCuota.Parcial) &&
                cuota.FechaVencimiento.Date < ahora.Date)
                return true;

            return false;
        }

        private static bool EsCuotaPagada(Cuota cuota) =>
            cuota.Estado == EstadoCuota.Pagada || cuota.FechaPago.HasValue || cuota.MontoPagado > 0;
    }
}

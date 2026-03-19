using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Motor de cálculo de mora.
    /// - Idempotente: mismo input + fecha = mismo output
    /// - Puro: no modifica entidades ni base de datos
    /// - Sin notificaciones: solo cálculo
    /// - Basado en configuración: usa ConfiguracionMora
    /// - Sin dependencias de DbContext
    /// </summary>
    public sealed class CalculoMoraService : ICalculoMoraService
    {
        private const int DiasEnMes = 30;
        private const int DiasEnAnio = 365;

        #region Métodos Públicos

        /// <inheritdoc />
        public CalculoMoraResult CalcularMoraCuota(
            Cuota cuota,
            ConfiguracionMora configuracion,
            DateTime? fechaCalculo = null)
        {
            ArgumentNullException.ThrowIfNull(cuota);
            ArgumentNullException.ThrowIfNull(configuracion);

            var fecha = fechaCalculo ?? DateTime.Today;

            // Validar configuración mínima
            if (!TieneConfiguracionValida(configuracion))
            {
                return CalculoMoraResult.Vacio(fecha);
            }

            var detalle = CalcularDetalleCuota(cuota, configuracion, fecha);

            return new CalculoMoraResult
            {
                FechaCalculo = fecha,
                TotalMora = detalle.MoraFinal,
                TotalCapitalVencido = detalle.DiasAtrasoEfectivos > 0 ? detalle.MontoCapital : 0,
                TotalDeuda = detalle.TotalAPagar,
                CuotasProcesadas = 1,
                CuotasConMora = detalle.MoraFinal > 0 ? 1 : 0,
                Detalles = new[] { detalle }
            };
        }

        /// <inheritdoc />
        public CalculoMoraResult CalcularMoraCuotas(
            IEnumerable<Cuota> cuotas,
            ConfiguracionMora configuracion,
            DateTime? fechaCalculo = null)
        {
            ArgumentNullException.ThrowIfNull(cuotas);
            ArgumentNullException.ThrowIfNull(configuracion);

            var fecha = fechaCalculo ?? DateTime.Today;
            var listaCuotas = cuotas.ToList();

            if (listaCuotas.Count == 0)
            {
                return CalculoMoraResult.Vacio(fecha);
            }

            // Validar configuración mínima
            if (!TieneConfiguracionValida(configuracion))
            {
                return CalculoMoraResult.Vacio(fecha);
            }

            var detalles = listaCuotas
                .Select(c => CalcularDetalleCuota(c, configuracion, fecha))
                .ToList();

            return new CalculoMoraResult
            {
                FechaCalculo = fecha,
                TotalMora = detalles.Sum(d => d.MoraFinal),
                TotalCapitalVencido = detalles.Where(d => d.DiasAtrasoEfectivos > 0).Sum(d => d.MontoCapital),
                TotalDeuda = detalles.Sum(d => d.TotalAPagar),
                CuotasProcesadas = detalles.Count,
                CuotasConMora = detalles.Count(d => d.MoraFinal > 0),
                Detalles = detalles
            };
        }

        /// <inheritdoc />
        public CalculoMoraResult CalcularMoraCredito(
            Credito credito,
            ConfiguracionMora configuracion,
            DateTime? fechaCalculo = null)
        {
            ArgumentNullException.ThrowIfNull(credito);
            ArgumentNullException.ThrowIfNull(configuracion);

            var cuotasVencidas = credito.Cuotas?
                .Where(c => !c.IsDeleted &&
                           c.Estado == EstadoCuota.Pendiente &&
                           c.FechaPago == null)
                .ToList() ?? new List<Cuota>();

            return CalcularMoraCuotas(cuotasVencidas, configuracion, fechaCalculo);
        }

        /// <inheritdoc />
        public bool EstaVencida(Cuota cuota, int diasGracia, DateTime? fechaCalculo = null)
        {
            ArgumentNullException.ThrowIfNull(cuota);

            // Si ya está pagada, no está vencida
            if (cuota.FechaPago.HasValue || cuota.Estado == EstadoCuota.Pagada)
                return false;

            var fecha = fechaCalculo ?? DateTime.Today;
            var fechaLimite = cuota.FechaVencimiento.AddDays(diasGracia);

            return fecha > fechaLimite;
        }

        /// <inheritdoc />
        public int CalcularDiasAtrasoEfectivos(
            DateTime fechaVencimiento,
            int diasGracia,
            DateTime? fechaCalculo = null)
        {
            var fecha = fechaCalculo ?? DateTime.Today;
            var diasAtraso = (fecha - fechaVencimiento).Days;

            // Restar días de gracia
            var diasEfectivos = diasAtraso - diasGracia;

            return Math.Max(0, diasEfectivos);
        }

        #endregion

        #region Métodos Privados

        /// <summary>
        /// Calcula el detalle de mora para una cuota específica.
        /// Función pura: no modifica la cuota.
        /// </summary>
        private DetalleMoraCuota CalcularDetalleCuota(
            Cuota cuota,
            ConfiguracionMora config,
            DateTime fechaCalculo)
        {
            var diasGracia = config.DiasGracia ?? 0;
            var diasAtraso = (fechaCalculo - cuota.FechaVencimiento).Days;
            var diasEfectivos = Math.Max(0, diasAtraso - diasGracia);

            // Si no hay días de atraso efectivos o está pagada, mora = 0
            if (diasEfectivos <= 0 || cuota.FechaPago.HasValue || cuota.Estado == EstadoCuota.Pagada)
            {
                return CrearDetalleSinMora(cuota, diasAtraso, diasEfectivos);
            }

            // Determinar base de cálculo
            var baseCalculo = CalcularBaseCalculo(cuota, config);

            // Determinar tasa a aplicar
            var tasaDiaria = CalcularTasaDiaria(diasEfectivos, config);

            // Calcular mora bruta
            var moraBruta = baseCalculo * tasaDiaria * diasEfectivos;

            // Aplicar topes y mínimos
            var (moraFinal, topeAplicado, minimoAplicado) = AplicarTopesYMinimos(moraBruta, baseCalculo, config);

            return new DetalleMoraCuota
            {
                CuotaId = cuota.Id,
                NumeroCuota = cuota.NumeroCuota,
                FechaVencimiento = cuota.FechaVencimiento,
                DiasAtraso = diasAtraso,
                DiasAtrasoEfectivos = diasEfectivos,
                BaseCalculo = baseCalculo,
                TasaAplicada = tasaDiaria,
                MoraBruta = Math.Round(moraBruta, 2),
                MoraFinal = Math.Round(moraFinal, 2),
                TopeAplicado = topeAplicado,
                MinimoAplicado = minimoAplicado,
                MontoCapital = cuota.MontoCapital,
                MontoInteres = cuota.MontoInteres,
                MontoPagado = cuota.MontoPagado
            };
        }

        /// <summary>
        /// Crea un detalle de cuota sin mora (no vencida o ya pagada).
        /// </summary>
        private static DetalleMoraCuota CrearDetalleSinMora(Cuota cuota, int diasAtraso, int diasEfectivos)
        {
            return new DetalleMoraCuota
            {
                CuotaId = cuota.Id,
                NumeroCuota = cuota.NumeroCuota,
                FechaVencimiento = cuota.FechaVencimiento,
                DiasAtraso = Math.Max(0, diasAtraso),
                DiasAtrasoEfectivos = diasEfectivos,
                BaseCalculo = 0,
                TasaAplicada = 0,
                MoraBruta = 0,
                MoraFinal = 0,
                TopeAplicado = false,
                MinimoAplicado = false,
                MontoCapital = cuota.MontoCapital,
                MontoInteres = cuota.MontoInteres,
                MontoPagado = cuota.MontoPagado
            };
        }

        /// <summary>
        /// Calcula la base sobre la cual se aplica la mora.
        /// </summary>
        private static decimal CalcularBaseCalculo(Cuota cuota, ConfiguracionMora config)
        {
            var baseCalculo = config.BaseCalculoMora ?? BaseCalculoMora.Capital;

            return baseCalculo switch
            {
                BaseCalculoMora.Capital => cuota.MontoCapital - cuota.MontoPagado,
                BaseCalculoMora.CapitalMasInteres => cuota.MontoCapital + cuota.MontoInteres - cuota.MontoPagado,
                _ => cuota.MontoCapital - cuota.MontoPagado
            };
        }

        /// <summary>
        /// Calcula la tasa diaria a aplicar según configuración.
        /// Soporta tasa fija o escalonada por antigüedad.
        /// </summary>
        private static decimal CalcularTasaDiaria(int diasEfectivos, ConfiguracionMora config)
        {
            decimal tasaPorcentaje;

            if (config.EscalonamientoActivo)
            {
                // Escalonamiento por antigüedad
                tasaPorcentaje = diasEfectivos switch
                {
                    <= 30 => config.TasaPrimerMes ?? config.TasaMoraBase ?? 0,
                    <= 60 => config.TasaSegundoMes ?? config.TasaPrimerMes ?? config.TasaMoraBase ?? 0,
                    _ => config.TasaTercerMesEnAdelante ?? config.TasaSegundoMes ?? config.TasaPrimerMes ?? config.TasaMoraBase ?? 0
                };
            }
            else
            {
                tasaPorcentaje = config.TasaMoraBase ?? 0;
            }

            if (tasaPorcentaje == 0)
                return 0;

            // Convertir a tasa diaria según tipo
            var tipoTasa = config.TipoTasaMora ?? TipoTasaMora.Mensual;

            return tipoTasa switch
            {
                TipoTasaMora.Diaria => tasaPorcentaje / 100m, // Ya es diaria, convertir de % a decimal
                TipoTasaMora.Mensual => (tasaPorcentaje / 100m) / DiasEnMes, // Mensual a diaria
                _ => (tasaPorcentaje / 100m) / DiasEnMes
            };
        }

        /// <summary>
        /// Aplica topes máximos y mínimos a la mora calculada.
        /// </summary>
        private static (decimal moraFinal, bool topeAplicado, bool minimoAplicado) AplicarTopesYMinimos(
            decimal moraBruta,
            decimal baseCalculo,
            ConfiguracionMora config)
        {
            var moraFinal = moraBruta;
            var topeAplicado = false;
            var minimoAplicado = false;

            // Aplicar tope máximo
            if (config.TopeMaximoMoraActivo && config.TipoTopeMora.HasValue && config.ValorTopeMora.HasValue)
            {
                var topeMaximo = config.TipoTopeMora switch
                {
                    TipoTopeMora.Porcentaje => baseCalculo * (config.ValorTopeMora.Value / 100m),
                    TipoTopeMora.MontoFijo => config.ValorTopeMora.Value,
                    _ => decimal.MaxValue
                };

                if (moraFinal > topeMaximo)
                {
                    moraFinal = topeMaximo;
                    topeAplicado = true;
                }
            }

            // Aplicar mora mínima
            if (config.MoraMinima.HasValue && config.MoraMinima.Value > 0)
            {
                if (moraFinal > 0 && moraFinal < config.MoraMinima.Value)
                {
                    moraFinal = config.MoraMinima.Value;
                    minimoAplicado = true;
                }
            }

            return (moraFinal, topeAplicado, minimoAplicado);
        }

        /// <summary>
        /// Valida que la configuración tenga los datos mínimos para calcular mora.
        /// </summary>
        private static bool TieneConfiguracionValida(ConfiguracionMora config)
        {
            // Si no hay tipo de tasa o tasa base, no se puede calcular
            if (!config.TipoTasaMora.HasValue)
                return false;

            // Al menos debe tener tasa base o escalonamiento con alguna tasa
            if (config.EscalonamientoActivo)
            {
                return config.TasaPrimerMes.HasValue ||
                       config.TasaSegundoMes.HasValue ||
                       config.TasaTercerMesEnAdelante.HasValue ||
                       config.TasaMoraBase.HasValue;
            }

            return config.TasaMoraBase.HasValue && config.TasaMoraBase.Value > 0;
        }

        #endregion
    }
}

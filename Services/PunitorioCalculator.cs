using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Implementación de <see cref="IPunitorioCalculator"/> (PUN-ML4). Sin dependencias — no recibe
    /// <c>AppDbContext</c>, reloj ni ningún otro servicio: todo lo que necesita viaja en
    /// <see cref="PunitorioCalculoEntrada"/>.
    ///
    /// Algoritmo, en tres pasos:
    ///
    /// 1. Reconstruir el saldo en el tiempo a partir de <see cref="PunitorioCalculoEntrada.MontoOriginalCuota"/>
    ///    y los pagos "efectivos" (<see cref="EstadoPagoCuota.Aplicado"/> — Anulado/Revertido se
    ///    ignoran por completo). Los pagos con fecha &lt;= vencimiento se netean en
    ///    <c>SaldoInicial</c>; el resto son puntos de corte dentro de la ventana de devengo. Si algún
    ///    pago no es reconstruible (<c>HistorialCompleto=false</c> o <c>ImporteAplicadoCuota=null</c>),
    ///    el saldo deja de ser confiable a partir de esa fecha: no se construye nada más allá.
    ///
    /// 2. Cortar la ventana [FechaVencimiento, límite) en tramos cada vez que cambia el saldo (un
    ///    pago) o la configuración vigente (<c>VigenteDesde</c> de una nueva versión). Cada tramo
    ///    resuelve su propia configuración ("última con VigenteDesde &lt;= tramo.Desde") de forma
    ///    independiente — un tramo sin configuración aplicable es un hueco, nunca se inventa una tasa.
    ///
    /// 3. Por tramo con configuración: si la configuración está inactiva, o si los días transcurridos
    ///    desde el vencimiento (globales, no los del tramo) no superan la gracia de ESA configuración,
    ///    el importe es 0. Si no, <c>saldoBase × porcentaje/100 × díasDelTramo ÷ periodoDías</c>, sin
    ///    redondear. El total es la suma exacta de los tramos, redondeada una única vez.
    ///
    /// Ventana autoritativa (PUN-ML4): el resultado corresponde exclusivamente a
    /// [FechaVencimiento, FechaCalculo). El caller puede pasar <c>Cuota.Pagos</c> y el historial
    /// completo de <c>ConfiguracionPunitorio</c> tal cual están — datos posteriores a FechaCalculo
    /// nunca alteran ni invalidan un cálculo histórico: ver <see cref="ConfiguracionesRelevantes"/>
    /// (qué versiones se validan a nivel de campo) y el filtro por fecha sobre
    /// <see cref="PunitorioCalculoEntrada.PagosAplicados"/> en <see cref="Validar"/>.
    /// </summary>
    public sealed class PunitorioCalculator : IPunitorioCalculator
    {
        public PunitorioCalculoResultado Calcular(PunitorioCalculoEntrada entrada)
        {
            ArgumentNullException.ThrowIfNull(entrada);

            var invalida = Validar(entrada);
            if (invalida is not null)
                return invalida;

            var configuraciones = entrada.Configuraciones.OrderBy(c => c.VigenteDesde).ToList();

            var gruposPorFecha = entrada.PagosAplicados
                .Where(p => p.Estado == EstadoPagoCuota.Aplicado && p.FechaPagoComercial <= entrada.FechaCalculo)
                .GroupBy(p => p.FechaPagoComercial)
                .OrderBy(g => g.Key)
                .Select(g => new GrupoPago(
                    g.Key,
                    Confiable: g.All(EsPagoConfiable),
                    Importe: g.All(EsPagoConfiable) ? g.Sum(p => p.ImporteAplicadoCuota!.Value) : null))
                .ToList();

            var primerGrupoAmbiguo = gruposPorFecha.FirstOrDefault(g => !g.Confiable);

            // Paso 1: reconstruir saldo confiable (nunca más allá del primer grupo ambiguo).
            var saldo = entrada.MontoOriginalCuota;
            var saldoInicial = saldo;
            var eventosPosterioresAlVencimiento = new List<(DateOnly Fecha, decimal SaldoDespues)>();

            foreach (var grupo in gruposPorFecha)
            {
                if (!grupo.Confiable)
                    break;

                saldo -= grupo.Importe!.Value;
                if (grupo.Fecha <= entrada.FechaVencimiento)
                    saldoInicial = saldo;
                else
                    eventosPosterioresAlVencimiento.Add((grupo.Fecha, saldo));
            }

            var diasTranscurridos = entrada.FechaCalculo.DayNumber - entrada.FechaVencimiento.DayNumber;

            // Ni siquiera el saldo al vencimiento es confiable.
            if (primerGrupoAmbiguo is not null && primerGrupoAmbiguo.Fecha <= entrada.FechaVencimiento)
            {
                return new PunitorioCalculoResultado
                {
                    EstadoResultado = EstadoResultadoPunitorio.HistorialIncompleto,
                    SaldoInicial = entrada.MontoOriginalCuota,
                    SaldoFinal = entrada.MontoOriginalCuota,
                    FechaVencimiento = entrada.FechaVencimiento,
                    FechaCalculo = entrada.FechaCalculo,
                    DiasTranscurridos = diasTranscurridos,
                    PunitorioExacto = null,
                    PunitorioRedondeado = null,
                    Segmentos = Array.Empty<SegmentoPunitorio>(),
                    Motivo = MotivoIncompleto(primerGrupoAmbiguo.Fecha)
                };
            }

            if (saldoInicial <= 0m)
            {
                return new PunitorioCalculoResultado
                {
                    EstadoResultado = EstadoResultadoPunitorio.SinSaldo,
                    SaldoInicial = saldoInicial,
                    SaldoFinal = saldoInicial,
                    FechaVencimiento = entrada.FechaVencimiento,
                    FechaCalculo = entrada.FechaCalculo,
                    DiasTranscurridos = diasTranscurridos,
                    PunitorioExacto = 0m,
                    PunitorioRedondeado = 0m,
                    Segmentos = Array.Empty<SegmentoPunitorio>(),
                    Motivo = "El saldo ya estaba cancelado al vencimiento: nunca hubo deuda sobre la que devengar punitorio."
                };
            }

            decimal SaldoEn(DateOnly fecha)
            {
                var actual = saldoInicial;
                foreach (var evento in eventosPosterioresAlVencimiento)
                {
                    if (evento.Fecha > fecha)
                        break;
                    actual = evento.SaldoDespues;
                }
                return actual;
            }

            var limiteFin = primerGrupoAmbiguo?.Fecha ?? entrada.FechaCalculo;

            // Ventana de devengo vacía (FechaCalculo == FechaVencimiento, o el corte cae ahí mismo):
            // con gracia >= 0, 0 días transcurridos nunca supera ninguna gracia.
            if (limiteFin <= entrada.FechaVencimiento)
            {
                var configAlVencimiento = ResolverVigente(configuraciones, entrada.FechaVencimiento);
                if (configAlVencimiento is null)
                {
                    return new PunitorioCalculoResultado
                    {
                        EstadoResultado = EstadoResultadoPunitorio.SinConfiguracion,
                        SaldoInicial = saldoInicial,
                        SaldoFinal = saldoInicial,
                        FechaVencimiento = entrada.FechaVencimiento,
                        FechaCalculo = entrada.FechaCalculo,
                        DiasTranscurridos = diasTranscurridos,
                        PunitorioExacto = null,
                        PunitorioRedondeado = null,
                        Segmentos = Array.Empty<SegmentoPunitorio>(),
                        Motivo = "No existe ninguna configuración de punitorio vigente en la fecha de vencimiento."
                    };
                }

                return new PunitorioCalculoResultado
                {
                    EstadoResultado = EstadoResultadoPunitorio.DentroDeGracia,
                    SaldoInicial = saldoInicial,
                    SaldoFinal = saldoInicial,
                    FechaVencimiento = entrada.FechaVencimiento,
                    FechaCalculo = entrada.FechaCalculo,
                    DiasTranscurridos = diasTranscurridos,
                    PunitorioExacto = 0m,
                    PunitorioRedondeado = 0m,
                    Segmentos = Array.Empty<SegmentoPunitorio>(),
                    Motivo = $"{diasTranscurridos} día(s) transcurridos no supera(n) la gracia de {configAlVencimiento.DiasGracia} día(s)."
                };
            }

            // Paso 2: boundaries = vencimiento + pagos confiables + nuevas vigencias, todos dentro de
            // (vencimiento, límite), más el propio límite. Truncar apenas el saldo llegue a cero: no
            // se generan tramos posteriores a la cancelación.
            var boundaries = new SortedSet<DateOnly> { entrada.FechaVencimiento, limiteFin };
            foreach (var evento in eventosPosterioresAlVencimiento)
            {
                if (evento.Fecha < limiteFin)
                    boundaries.Add(evento.Fecha);
            }
            foreach (var config in configuraciones)
            {
                if (config.VigenteDesde > entrada.FechaVencimiento && config.VigenteDesde < limiteFin)
                    boundaries.Add(config.VigenteDesde);
            }

            var ordenadas = new List<DateOnly> { boundaries.Min };
            DateOnly? finPorSaldoCancelado = null;
            foreach (var b in boundaries.Skip(1))
            {
                ordenadas.Add(b);
                if (SaldoEn(b) <= 0m)
                {
                    finPorSaldoCancelado = b;
                    break;
                }
            }

            var segmentos = new List<SegmentoPunitorio>();
            var huboHueco = false;

            for (var i = 0; i < ordenadas.Count - 1; i++)
            {
                var desde = ordenadas[i];
                var hasta = ordenadas[i + 1];
                var dias = hasta.DayNumber - desde.DayNumber;
                var saldoBase = SaldoEn(desde);

                var motivoInicio = i == 0
                    ? MotivoLimiteSegmento.Vencimiento
                    : MotivoDelBoundary(desde, configuraciones);

                MotivoLimiteSegmento motivoFin;
                var esUltimoTramo = i == ordenadas.Count - 2;
                if (esUltimoTramo)
                {
                    motivoFin = finPorSaldoCancelado == hasta
                        ? MotivoLimiteSegmento.SaldoCancelado
                        : limiteFin == entrada.FechaCalculo
                            ? MotivoLimiteSegmento.FechaCalculo
                            : MotivoLimiteSegmento.HistorialIncompleto;
                }
                else
                {
                    motivoFin = MotivoDelBoundary(hasta, configuraciones);
                }

                var config = ResolverVigente(configuraciones, desde);
                if (config is null)
                {
                    huboHueco = true;
                    segmentos.Add(new SegmentoPunitorio
                    {
                        Desde = desde,
                        Hasta = hasta,
                        Dias = dias,
                        SaldoBase = saldoBase,
                        ConfiguracionPunitorioId = null,
                        Porcentaje = null,
                        PeriodoDias = null,
                        DiasGracia = null,
                        ImporteExacto = null,
                        MotivoDeInicio = motivoInicio,
                        MotivoDeFin = motivoFin
                    });
                    continue;
                }

                var pasaGracia = diasTranscurridos > config.DiasGracia;
                var importe = config.Activa && pasaGracia
                    ? saldoBase * config.Porcentaje / 100m * dias / config.PeriodoDias
                    : 0m;

                segmentos.Add(new SegmentoPunitorio
                {
                    Desde = desde,
                    Hasta = hasta,
                    Dias = dias,
                    SaldoBase = saldoBase,
                    ConfiguracionPunitorioId = config.Id,
                    Porcentaje = config.Porcentaje,
                    PeriodoDias = config.PeriodoDias,
                    DiasGracia = config.DiasGracia,
                    ImporteExacto = importe,
                    MotivoDeInicio = motivoInicio,
                    MotivoDeFin = motivoFin
                });
            }

            // Paso 3: estado final. Prioridad fija y documentada: historial incompleto > hueco de
            // configuración > (config única para todo el período: inactiva / dentro de gracia) > calculado.
            EstadoResultadoPunitorio estado;
            decimal? punitorioExacto = null;
            decimal? punitorioRedondeado = null;
            string? motivo;

            if (primerGrupoAmbiguo is not null)
            {
                estado = EstadoResultadoPunitorio.HistorialIncompleto;
                motivo = MotivoIncompleto(primerGrupoAmbiguo.Fecha);
            }
            else if (huboHueco)
            {
                estado = EstadoResultadoPunitorio.SinConfiguracion;
                motivo = "No existe configuración de punitorio vigente para una parte del período de mora.";
            }
            else
            {
                var idsDistintos = segmentos.Select(s => s.ConfiguracionPunitorioId).Distinct().ToList();
                if (idsDistintos.Count == 1 && idsDistintos[0] is int unicoId)
                {
                    var unicaConfig = configuraciones.First(c => c.Id == unicoId);
                    if (!unicaConfig.Activa)
                    {
                        estado = EstadoResultadoPunitorio.ConfiguracionInactiva;
                        motivo = $"La única configuración vigente en todo el período (Id={unicoId}) está inactiva.";
                    }
                    else if (diasTranscurridos <= unicaConfig.DiasGracia)
                    {
                        estado = EstadoResultadoPunitorio.DentroDeGracia;
                        motivo = $"{diasTranscurridos} día(s) transcurridos no supera(n) la gracia de {unicaConfig.DiasGracia} día(s) de la configuración vigente.";
                    }
                    else
                    {
                        estado = EstadoResultadoPunitorio.Calculado;
                        motivo = null;
                    }
                }
                else
                {
                    estado = EstadoResultadoPunitorio.Calculado;
                    motivo = null;
                }

                punitorioExacto = segmentos.Sum(s => s.ImporteExacto ?? 0m);
                punitorioRedondeado = Math.Round(punitorioExacto.Value, 2, MidpointRounding.AwayFromZero);
            }

            var saldoFinal = finPorSaldoCancelado is not null ? 0m : SaldoEn(ordenadas[^1]);

            return new PunitorioCalculoResultado
            {
                EstadoResultado = estado,
                SaldoInicial = saldoInicial,
                SaldoFinal = saldoFinal,
                FechaVencimiento = entrada.FechaVencimiento,
                FechaCalculo = entrada.FechaCalculo,
                DiasTranscurridos = diasTranscurridos,
                PunitorioExacto = punitorioExacto,
                PunitorioRedondeado = punitorioRedondeado,
                Segmentos = segmentos,
                Motivo = motivo
            };
        }

        private sealed record GrupoPago(DateOnly Fecha, bool Confiable, decimal? Importe);

        /// <summary>
        /// Filtro explícito de "pago confiable": solo un pago cuya composición cuota/punitorio es
        /// reconstruible participa del saldo. <c>HistorialCompleto=false</c> o
        /// <c>ImporteAplicadoCuota=null</c> (aisladamente, no hace falta que se den los dos juntos)
        /// bastan para descartarlo del saldo — nunca se estima ni se reparte proporcionalmente.
        /// </summary>
        private static bool EsPagoConfiable(PagoAplicadoPunitorioEntrada p) =>
            p.HistorialCompleto && p.ImporteAplicadoCuota is not null;

        /// <summary>
        /// Subconjunto de <paramref name="configuraciones"/> que puede gobernar algún tramo de
        /// [<paramref name="fechaVencimiento"/>, <paramref name="fechaCalculo"/>): la última con
        /// <c>VigenteDesde &lt;= fechaVencimiento</c> (más cualquier otra que empate esa misma
        /// fecha, para que la ambigüedad se detecte igual) y las que caen estrictamente en
        /// (fechaVencimiento, fechaCalculo). Usa el mismo criterio que <see cref="ResolverVigente"/>
        /// y la construcción de boundaries en <see cref="Calcular"/> — solo sirve para acotar qué
        /// se valida a nivel de campo, no participa del cálculo en sí.
        /// </summary>
        private static List<ConfiguracionPunitorioEntrada> ConfiguracionesRelevantes(
            IReadOnlyList<ConfiguracionPunitorioEntrada> configuraciones, DateOnly fechaVencimiento, DateOnly fechaCalculo)
        {
            var relevantes = new List<ConfiguracionPunitorioEntrada>();

            var vigenteAlVencimiento = configuraciones
                .Where(c => c.VigenteDesde <= fechaVencimiento)
                .OrderByDescending(c => c.VigenteDesde)
                .FirstOrDefault();
            if (vigenteAlVencimiento is not null)
                relevantes.AddRange(configuraciones.Where(c => c.VigenteDesde == vigenteAlVencimiento.VigenteDesde));

            relevantes.AddRange(configuraciones.Where(c => c.VigenteDesde > fechaVencimiento && c.VigenteDesde < fechaCalculo));

            return relevantes;
        }

        private static ConfiguracionPunitorioEntrada? ResolverVigente(
            IReadOnlyList<ConfiguracionPunitorioEntrada> configuracionesOrdenadas, DateOnly fecha) =>
            configuracionesOrdenadas
                .Where(c => c.VigenteDesde <= fecha)
                .OrderByDescending(c => c.VigenteDesde)
                .FirstOrDefault();

        /// <summary>
        /// Un boundary intermedio (ni el vencimiento ni el límite final) es, por construcción, una
        /// fecha de pago o una nueva vigencia de configuración (o ambas el mismo día: en ese caso se
        /// prioriza reportar el cambio de configuración, el evento estructuralmente más significativo).
        /// </summary>
        private static MotivoLimiteSegmento MotivoDelBoundary(
            DateOnly fecha, IReadOnlyList<ConfiguracionPunitorioEntrada> configuraciones) =>
            configuraciones.Any(c => c.VigenteDesde == fecha)
                ? MotivoLimiteSegmento.NuevaConfiguracion
                : MotivoLimiteSegmento.Pago;

        private static string MotivoIncompleto(DateOnly fecha) =>
            $"Pago del {fecha:yyyy-MM-dd} sin composición confiable (HistorialCompleto=false o " +
            "ImporteAplicadoCuota sin valor): no se puede fijar un total autoritativo más allá de esa fecha.";

        private static PunitorioCalculoResultado? Validar(PunitorioCalculoEntrada entrada)
        {
            if (entrada.MontoOriginalCuota <= 0m)
                return Invalida(entrada, "MontoOriginalCuota debe ser mayor a cero.");

            if (entrada.FechaCalculo < entrada.FechaVencimiento)
                return Invalida(entrada, "FechaCalculo no puede ser anterior a FechaVencimiento.");

            // Solo las configuraciones que pueden gobernar algún tramo de [FechaVencimiento,
            // FechaCalculo) se validan a nivel de campo: la última con VigenteDesde <=
            // FechaVencimiento (más cualquier otra que empate esa misma fecha, para no perder la
            // ambigüedad) y las que caen estrictamente dentro de la ventana. El resto del
            // historial — versiones ya superadas antes del vencimiento, o versiones con
            // VigenteDesde >= FechaCalculo — nunca participa de este cálculo (ver
            // <see cref="ResolverVigente"/> y la construcción de boundaries más abajo, que usan el
            // mismo criterio): validarlas invalidaría retroactivamente un cálculo histórico cuando
            // el caller pasa el historial completo de configuraciones, que es el bug que corrige
            // PUN-ML4.
            var relevantes = ConfiguracionesRelevantes(entrada.Configuraciones, entrada.FechaVencimiento, entrada.FechaCalculo);

            foreach (var c in relevantes)
            {
                if (c.PeriodoDias <= 0)
                    return Invalida(entrada, $"Configuración Id={c.Id}: PeriodoDias debe ser mayor a cero.");
                if (c.Porcentaje < 0m)
                    return Invalida(entrada, $"Configuración Id={c.Id}: Porcentaje no puede ser negativo.");
                if (c.DiasGracia < 0)
                    return Invalida(entrada, $"Configuración Id={c.Id}: DiasGracia no puede ser negativo.");
                if (!c.ProrrateoDiario)
                    return Invalida(entrada, $"Configuración Id={c.Id}: ProrrateoDiario=false no está soportado (PUN-ML4 exige prorrateo diario).");
            }

            // Vigencias duplicadas: validación estructural que se mantiene (no se elimina — afecta
            // la selección temporal si el empate cae dentro de la ventana), pero igual restringida
            // a las relevantes: un empate entre dos versiones que ninguna gobierna este cálculo no
            // es un problema PARA este cálculo en particular.
            var vigenciasDuplicadas = relevantes
                .GroupBy(c => c.VigenteDesde)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (vigenciasDuplicadas.Count > 0)
                return Invalida(entrada,
                    $"Configuraciones con VigenteDesde duplicado: {string.Join(", ", vigenciasDuplicadas.Select(d => d.ToString("yyyy-MM-dd")))}.");

            foreach (var p in entrada.PagosAplicados)
            {
                if (p.Estado != EstadoPagoCuota.Aplicado)
                    continue;
                // Un pago posterior a FechaCalculo no participa de este cálculo (mismo motivo: el
                // caller pasa Cuota.Pagos completo) — ni reduce saldo ni puede invalidarlo.
                if (p.FechaPagoComercial > entrada.FechaCalculo)
                    continue;
                if (p.HistorialCompleto && p.ImporteAplicadoCuota is null)
                    return Invalida(entrada,
                        $"Pago del {p.FechaPagoComercial:yyyy-MM-dd}: HistorialCompleto=true pero ImporteAplicadoCuota es null (entrada contradictoria).");
                if (p.ImporteAplicadoCuota is < 0m)
                    return Invalida(entrada,
                        $"Pago del {p.FechaPagoComercial:yyyy-MM-dd}: ImporteAplicadoCuota no puede ser negativo.");
            }

            var saldoCheck = entrada.MontoOriginalCuota;
            foreach (var grupo in entrada.PagosAplicados
                         .Where(p => p.Estado == EstadoPagoCuota.Aplicado
                                     && p.FechaPagoComercial <= entrada.FechaCalculo
                                     && EsPagoConfiable(p))
                         .GroupBy(p => p.FechaPagoComercial)
                         .OrderBy(g => g.Key))
            {
                saldoCheck -= grupo.Sum(p => p.ImporteAplicadoCuota!.Value);
                if (saldoCheck < 0m)
                    return Invalida(entrada,
                        $"Los pagos aplicados superan el saldo disponible (detectado en la fecha {grupo.Key:yyyy-MM-dd}).");
            }

            return null;
        }

        private static PunitorioCalculoResultado Invalida(PunitorioCalculoEntrada entrada, string motivo) => new()
        {
            EstadoResultado = EstadoResultadoPunitorio.EntradaInvalida,
            SaldoInicial = 0m,
            SaldoFinal = 0m,
            FechaVencimiento = entrada.FechaVencimiento,
            FechaCalculo = entrada.FechaCalculo,
            DiasTranscurridos = 0,
            PunitorioExacto = null,
            PunitorioRedondeado = null,
            Segmentos = Array.Empty<SegmentoPunitorio>(),
            Motivo = motivo
        };
    }
}

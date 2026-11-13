using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// P-02 / P5 (versionado fiscal por operación). Conjunto de reglas fiscales con las que se CALCULA y se RE-LEE una operación:
    /// alícuota de los servicios (armados/envío), truncado a pesos del total y discriminación neto/IVA del ajuste del medio de pago.
    /// Cada <c>Venta</c> persiste la versión con la que nació (<c>Venta.CriterioFiscalVersion</c>); los cálculos de lectura
    /// (reporte de IVA, comprobante, ventas netas, devoluciones) consultan la versión de la operación y no las reglas «vigentes».
    /// Una versión es inmutable: cambiar una regla fiscal = registrar una versión NUEVA y subir la vigente; las operaciones ya
    /// emitidas conservan su versión. Ninguna versión nueva se registra sin validación del contador (ver docs/p02-versionado-fiscal-diseno.md).
    /// </summary>
    public interface ICriterioFiscal
    {
        /// <summary>Número de versión (1 = reglas vigentes hasta el 2026-11-12, congeladas byte a byte).</summary>
        int Version { get; }

        /// <summary>Alícuota (%) de los servicios de venta (armados y envío).</summary>
        decimal AlicuotaServicios { get; }

        string NombreAlicuotaServicios { get; }

        /// <summary>Separa un precio final de servicios en neto e IVA.</summary>
        (decimal Neto, decimal Iva) SepararServicios(decimal bruto);

        /// <summary>Redondeo del total cobrado a pesos enteros.</summary>
        decimal TruncarAPesos(decimal valor);

        decimal CalcularRecargo(decimal totalCobrado, decimal brutoItems);

        decimal[] Prorratear(IReadOnlyList<decimal> pesos, decimal importe);

        List<RecargoMedioPagoIva.ParteAlicuota> Repartir(IEnumerable<(decimal Porcentaje, decimal Bruto)> bases, decimal recargo);

        RecargoMedioPagoIva.Discriminacion Discriminar(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas);

        List<RecargoMedioPagoIva.ParteAlicuota> ResumenPorAlicuota(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas);

        (decimal Neto, decimal Iva) AbsorberTruncado(decimal neto, decimal iva, decimal truncado);
    }

    /// <summary>
    /// Versión 1: comportamiento exacto de <see cref="RecargoMedioPagoIva"/>, <see cref="ServiciosVentaIva"/> y del truncado
    /// <c>Math.Floor</c> a pesos tal como estaban al 2026-11-12. Solo delega: NO contiene reglas propias.
    /// </summary>
    public sealed class CriterioFiscalV1 : ICriterioFiscal
    {
        public int Version => CriterioFiscalRegistry.VersionInicial;
        public decimal AlicuotaServicios => ServiciosVentaIva.Porcentaje;
        public string NombreAlicuotaServicios => ServiciosVentaIva.NombreAlicuota;
        public (decimal Neto, decimal Iva) SepararServicios(decimal bruto) => ServiciosVentaIva.Separar(bruto);
        public decimal TruncarAPesos(decimal valor) => Math.Floor(valor);
        public decimal CalcularRecargo(decimal totalCobrado, decimal brutoItems) => RecargoMedioPagoIva.CalcularRecargo(totalCobrado, brutoItems);
        public decimal[] Prorratear(IReadOnlyList<decimal> pesos, decimal importe) => RecargoMedioPagoIva.Prorratear(pesos, importe);
        public List<RecargoMedioPagoIva.ParteAlicuota> Repartir(IEnumerable<(decimal Porcentaje, decimal Bruto)> bases, decimal recargo) => RecargoMedioPagoIva.Repartir(bases, recargo);
        public RecargoMedioPagoIva.Discriminacion Discriminar(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas) => RecargoMedioPagoIva.Discriminar(total, lineas);
        public List<RecargoMedioPagoIva.ParteAlicuota> ResumenPorAlicuota(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas) => RecargoMedioPagoIva.ResumenPorAlicuota(total, lineas);
        public (decimal Neto, decimal Iva) AbsorberTruncado(decimal neto, decimal iva, decimal truncado) => RecargoMedioPagoIva.AbsorberTruncado(neto, iva, truncado);
    }

    /// <summary>La versión de criterio fiscal pedida no está registrada (dato corrupto o código desactualizado).</summary>
    public sealed class CriterioFiscalDesconocidoException(int version)
        : InvalidOperationException($"Versión de criterio fiscal {version} no registrada.")
    {
        public int Version { get; } = version;
    }

    /// <summary>Decorador: la misma versión, pero con la alícuota de servicios congelada en la operación.</summary>
    internal sealed class CriterioConAlicuotaServicios(ICriterioFiscal inner, decimal alicuota) : ICriterioFiscal
    {
        public int Version => inner.Version;
        public decimal AlicuotaServicios => alicuota;
        public string NombreAlicuotaServicios => ServiciosVentaIva.NombreDe(alicuota);
        public (decimal Neto, decimal Iva) SepararServicios(decimal bruto) => ServiciosVentaIva.Separar(bruto, alicuota);
        public decimal TruncarAPesos(decimal valor) => inner.TruncarAPesos(valor);
        public decimal CalcularRecargo(decimal totalCobrado, decimal brutoItems) => inner.CalcularRecargo(totalCobrado, brutoItems);
        public decimal[] Prorratear(IReadOnlyList<decimal> pesos, decimal importe) => inner.Prorratear(pesos, importe);
        public List<RecargoMedioPagoIva.ParteAlicuota> Repartir(IEnumerable<(decimal Porcentaje, decimal Bruto)> bases, decimal recargo) => inner.Repartir(bases, recargo);
        public RecargoMedioPagoIva.Discriminacion Discriminar(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas) => inner.Discriminar(total, lineas);
        public List<RecargoMedioPagoIva.ParteAlicuota> ResumenPorAlicuota(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas) => inner.ResumenPorAlicuota(total, lineas);
        public (decimal Neto, decimal Iva) AbsorberTruncado(decimal neto, decimal iva, decimal truncado) => inner.AbsorberTruncado(neto, iva, truncado);
    }

    /// <summary>
    /// Registro de versiones de criterio fiscal. En producción solo existe la versión 1 (también la vigente). Las versiones
    /// son inmutables una vez usadas: una operación con versión N se calcula y se relee SIEMPRE con la versión N.
    /// </summary>
    public static class CriterioFiscalRegistry
    {
        /// <summary>Versión que se asigna a toda operación histórica (backfill) y a las nuevas mientras no exista otra.</summary>
        public const int VersionInicial = 1;

        private static readonly ICriterioFiscal V1 = new CriterioFiscalV1();
        private static readonly AsyncLocal<Escenario?> EscenarioPrueba = new();

        private sealed record Escenario(IReadOnlyDictionary<int, ICriterioFiscal> Versiones, int Vigente);

        /// <summary>Versión con la que se estampan las operaciones nuevas.</summary>
        public static int VersionVigente => EscenarioPrueba.Value?.Vigente ?? VersionInicial;

        public static ICriterioFiscal Vigente => Resolver(VersionVigente);

        /// <summary>
        /// Reglas de la versión pedida. Una versión desconocida es un error (nunca se «adivina» otra): si apareciera una venta con
        /// una versión no registrada, calcular con otras reglas alteraría un importe histórico.
        /// </summary>
        public static ICriterioFiscal Resolver(int version)
        {
            var escenario = EscenarioPrueba.Value;
            if (escenario != null && escenario.Versiones.TryGetValue(version, out var enPrueba))
                return enPrueba;
            if (version == VersionInicial)
                return V1;
            throw new CriterioFiscalDesconocidoException(version);
        }

        /// <summary>
        /// Criterio con el que se relee una operación: su versión persistida y, si la operación congeló la alícuota de sus
        /// servicios al nacer (<see cref="Venta.AlicuotaServiciosSnapshot"/>), ESA alícuota y no la configuración actual.
        /// Una operación sin snapshot (histórica anterior al snapshot) conserva el comportamiento previo (alícuota de la versión).
        /// </summary>
        public static ICriterioFiscal Resolver(Venta venta)
        {
            ArgumentNullException.ThrowIfNull(venta);
            return Resolver(venta.CriterioFiscalVersion, venta.AlicuotaServiciosSnapshot);
        }

        public static ICriterioFiscal Resolver(int version, decimal? alicuotaServiciosSnapshot)
        {
            var criterio = Resolver(version);
            return alicuotaServiciosSnapshot is { } alicuota
                ? new CriterioConAlicuotaServicios(criterio, alicuota)
                : criterio;
        }

        /// <summary>Fija en una operación NUEVA la versión vigente y congela la alícuota de servicios con la que se calcula.</summary>
        public static void Estampar(Venta venta)
        {
            ArgumentNullException.ThrowIfNull(venta);
            venta.CriterioFiscalVersion = VersionVigente;
            venta.AlicuotaServiciosSnapshot = Vigente.AlicuotaServicios;
        }

        /// <summary>
        /// SOLO PARA TESTS: registra versiones adicionales y fija la vigente dentro del contexto asíncrono actual; al hacer
        /// Dispose vuelve al estado de producción (solo v1). No existe ningún camino de producción que lo invoque.
        /// </summary>
        internal static IDisposable UsarParaPruebas(int vigente, params ICriterioFiscal[] versiones)
        {
            var previo = EscenarioPrueba.Value;
            var mapa = versiones.ToDictionary(v => v.Version);
            EscenarioPrueba.Value = new Escenario(mapa, vigente);
            return new Restaurar(previo);
        }

        private sealed class Restaurar(Escenario? previo) : IDisposable
        {
            public void Dispose() => EscenarioPrueba.Value = previo;
        }
    }
}

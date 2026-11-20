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

        /// <summary>
        /// P-02 (hallazgo de Codex). Orden único de cálculo del importe cobrado: la base (productos con descuentos + servicios, con
        /// centavos) se redondea a pesos ANTES de aplicar el descuento o recargo del medio de pago. Lo usan venta directa, cotizador y
        /// conversión de cotización para cobrar exactamente el mismo importe. V1 conserva su comportamiento histórico (la conversión y
        /// el cotizador aplicaban el ajuste sobre la base con centavos): devuelve la base sin tocar.
        /// </summary>
        decimal RedondearBaseAntesDeAjuste(decimal baseConCentavos) => baseConCentavos;

        /// <summary>
        /// P-02 / C5 (contador, 2026-11-19): el descuento de Efectivo/Transferencia se aplica solo sobre los productos; envío y armado no
        /// lo reciben. V1 conserva su comportamiento histórico (el descuento alcanza a toda la base). El recargo sigue sobre toda la base.
        /// </summary>
        bool DescuentoDeMedioSoloSobreProductos => false;

        /// <summary>
        /// Igual que <see cref="Discriminar(decimal, IReadOnlyList{ValueTuple{decimal, decimal, decimal, decimal}})"/> cuando la venta
        /// conserva el monto del ajuste del medio de pago (Efectivo/Transferencia). Con ese dato la base cobrada es exacta
        /// (<c>total - ajuste</c>) y no se infiere por la presencia de centavos. V1 lo ignora (conserva su heurística histórica).
        /// </summary>
        RecargoMedioPagoIva.Discriminacion Discriminar(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas, RecargoMedioPagoIva.AjusteMedioContexto? contexto) =>
            Discriminar(total, lineas);

        List<RecargoMedioPagoIva.ParteAlicuota> ResumenPorAlicuota(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas, RecargoMedioPagoIva.AjusteMedioContexto? contexto) =>
            ResumenPorAlicuota(total, lineas);
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

    /// <summary>
    /// Versión 2 (P-02, 2026-11-19). <b>Registrada pero NO vigente</b>: <see cref="CriterioFiscalRegistry.VersionVigente"/> sigue en 1
    /// hasta que el propietario autorice la activación y su fecha de corte (P6). Una operación nace con V2 solo si la vigente es 2.
    /// Contiene exclusivamente reglas con respuesta inequívoca del contador (2026-11-19), todo lo demás delega en V1:
    /// <list type="bullet">
    /// <item><b>C1/C2 redondeo del total a pesos</b>: hasta $0,50 inclusive se redondea hacia abajo y desde $0,51 hacia arriba
    /// (1.000,50 → 1.000; 1.000,51 y 1.000,99 → 1.001). V1 trunca siempre.</item>
    /// </list>
    /// <b>C3</b>: el contador indicó que la diferencia de redondeo no se registra. Para que Neto + IVA == Total se siga cumpliendo
    /// (invariante técnica de comprobantes y reportes) el residuo, que puede ser negativo, se absorbe en el IVA igual que en V1; no
    /// se crea ningún concepto ni línea nueva. Si el contador exige otro tratamiento, es un cambio local en
    /// <see cref="RecargoMedioPagoIva.AbsorberRedondeo"/>.
    /// NO incluye (sin criterio firme, ver docs/p02-respuestas-contador-20261119.md): C5 descuento solo sobre productos, C7/C8
    /// recargos, C9–C11 cuotas/punitorios/intereses, C12 crédito fiscal, C13/C14 devoluciones.
    /// </summary>
    public sealed class CriterioFiscalV2 : ICriterioFiscal
    {
        public int Version => CriterioFiscalRegistry.VersionDosRedondeoComercial;
        public decimal AlicuotaServicios => ServiciosVentaIva.Porcentaje;
        public string NombreAlicuotaServicios => ServiciosVentaIva.NombreAlicuota;
        public (decimal Neto, decimal Iva) SepararServicios(decimal bruto) => ServiciosVentaIva.Separar(bruto);

        /// <summary>C1/C2: ≤ $0,50 hacia abajo; &gt; $0,50 hacia arriba (no es el «redondeo bancario» ni el de mitad hacia arriba).</summary>
        public decimal TruncarAPesos(decimal valor)
        {
            var piso = Math.Floor(valor);
            return valor - piso > 0.50m ? piso + 1m : piso;
        }

        public decimal CalcularRecargo(decimal totalCobrado, decimal brutoItems) => RecargoMedioPagoIva.CalcularRecargo(totalCobrado, brutoItems, TruncarAPesos);
        public decimal[] Prorratear(IReadOnlyList<decimal> pesos, decimal importe) => RecargoMedioPagoIva.Prorratear(pesos, importe);
        public List<RecargoMedioPagoIva.ParteAlicuota> Repartir(IEnumerable<(decimal Porcentaje, decimal Bruto)> bases, decimal recargo) => RecargoMedioPagoIva.Repartir(bases, recargo);
        public RecargoMedioPagoIva.Discriminacion Discriminar(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas) => RecargoMedioPagoIva.Discriminar(total, lineas, TruncarAPesos);
        public List<RecargoMedioPagoIva.ParteAlicuota> ResumenPorAlicuota(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas) => RecargoMedioPagoIva.ResumenPorAlicuota(total, lineas, TruncarAPesos);
        public (decimal Neto, decimal Iva) AbsorberTruncado(decimal neto, decimal iva, decimal truncado) => RecargoMedioPagoIva.AbsorberRedondeo(neto, iva, truncado);

        /// <summary>P-02: la base se redondea a pesos (C1/C2) antes del ajuste del medio de pago, en todos los circuitos.</summary>
        public decimal RedondearBaseAntesDeAjuste(decimal baseConCentavos) => TruncarAPesos(baseConCentavos);

        public bool DescuentoDeMedioSoloSobreProductos => true;

        public RecargoMedioPagoIva.Discriminacion Discriminar(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas, RecargoMedioPagoIva.AjusteMedioContexto? contexto) =>
            RecargoMedioPagoIva.Discriminar(total, lineas, TruncarAPesos, contexto);

        public List<RecargoMedioPagoIva.ParteAlicuota> ResumenPorAlicuota(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas, RecargoMedioPagoIva.AjusteMedioContexto? contexto) =>
            RecargoMedioPagoIva.ResumenPorAlicuota(total, lineas, TruncarAPesos, contexto);
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
        public decimal RedondearBaseAntesDeAjuste(decimal baseConCentavos) => inner.RedondearBaseAntesDeAjuste(baseConCentavos);
        public bool DescuentoDeMedioSoloSobreProductos => inner.DescuentoDeMedioSoloSobreProductos;
        public RecargoMedioPagoIva.Discriminacion Discriminar(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas, RecargoMedioPagoIva.AjusteMedioContexto? contexto) => inner.Discriminar(total, lineas, contexto);
        public List<RecargoMedioPagoIva.ParteAlicuota> ResumenPorAlicuota(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas, RecargoMedioPagoIva.AjusteMedioContexto? contexto) => inner.ResumenPorAlicuota(total, lineas, contexto);
    }

    /// <summary>
    /// Registro de versiones de criterio fiscal. En producción solo existe la versión 1 (también la vigente). Las versiones
    /// son inmutables una vez usadas: una operación con versión N se calcula y se relee SIEMPRE con la versión N.
    /// </summary>
    public static class CriterioFiscalRegistry
    {
        /// <summary>Versión que se asigna a toda operación histórica (backfill) y a las nuevas mientras no exista otra.</summary>
        public const int VersionInicial = 1;

        /// <summary>Versión 2: redondeo comercial a pesos (C1/C2). Registrada, NO vigente hasta autorización del propietario (P6).</summary>
        public const int VersionDosRedondeoComercial = 2;

        private static readonly ICriterioFiscal V1 = new CriterioFiscalV1();
        private static readonly ICriterioFiscal V2 = new CriterioFiscalV2();
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
            if (version == VersionDosRedondeoComercial)
                return V2;
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

namespace TheBuryProject.Services.Models;

/// <summary>
/// De dónde salieron los planes efectivos de una venta.
/// </summary>
public enum OrigenPlanesCredito
{
    /// <summary>
    /// No hay tabla de planes aplicable (ni global ni por producto). Rige la configuración
    /// única global: tasa de <c>ConfiguracionesPago</c> y rango mín/máx del método de cálculo.
    /// </summary>
    SinTablaDePlanes = 0,

    /// <summary>Todas las cantidades salen de la tabla global.</summary>
    Global = 1,

    /// <summary>Todos los productos financiados tienen configuración propia.</summary>
    Producto = 2,

    /// <summary>Conviven productos con configuración propia y productos que heredan la global.</summary>
    Mixto = 3
}

/// <summary>
/// Motivo por el que una venta no puede financiarse con Crédito Personal.
/// </summary>
public enum MotivoSinPlanesCredito
{
    Ninguno = 0,

    /// <summary>Los productos de la venta no comparten ninguna cantidad de cuotas.</summary>
    SinInterseccionEntreProductos = 1,

    /// <summary>
    /// No hay planes globales activos y la venta depende de la configuración global. La única
    /// fuente de cantidades disponibles es la tabla de planes: sin planes activos, Crédito
    /// Personal no ofrece cuotas (no se cae a un rango mín/máx).
    /// </summary>
    SinPlanesGlobalesActivos = 2
}

/// <summary>
/// Plan efectivo de la venta para una cantidad de cuotas.
/// </summary>
/// <param name="CantidadCuotas">Cantidad de cuotas habilitada para todos los productos.</param>
/// <param name="TasaMensual">
/// Porcentaje de recargo del plan. Única autoridad (ML2.1): el valor de la cuota global
/// (<c>ConfiguracionCreditoPersonalCuota</c>) para esta cantidad, tal cual. Si no existe cuota
/// global para esta cantidad, <c>null</c> — nunca la tasa propia del producto, que dejó de ser
/// fuente de porcentaje. <c>null</c> también cuando la cuota global existe pero no tiene
/// porcentaje explícito: en ambos casos significa "configuración inválida", NUNCA se hereda de
/// Producto, Perfil, Cliente, Manual ni de la tasa única global (que dejó de ser fallback del
/// porcentaje).
/// </param>
/// <param name="ProductosConPlanPropio">Productos que aportaron una configuración personalizada.</param>
/// <param name="IncluyeConfiguracionGlobal">Si algún producto aportó su plan heredando la global.</param>
public sealed record PlanCuotaCreditoPersonal(
    int CantidadCuotas,
    decimal? TasaMensual,
    IReadOnlyList<int> ProductosConPlanPropio,
    bool IncluyeConfiguracionGlobal);

/// <summary>
/// Resultado canónico de resolver los planes de Crédito Personal de una venta.
/// </summary>
/// <remarks>
/// Los planes activos son la ÚNICA fuente de cantidades disponibles. Estados posibles:
/// <list type="bullet">
///   <item>hay planes compatibles → <see cref="EsValido"/> true y <see cref="Planes"/> no vacío;</item>
///   <item>no hay planes globales activos (y la venta los necesita) → <see cref="EsValido"/> false,
///         motivo <see cref="MotivoSinPlanesCredito.SinPlanesGlobalesActivos"/>;</item>
///   <item>la intersección entre productos es vacía → <see cref="EsValido"/> false,
///         motivo <see cref="MotivoSinPlanesCredito.SinInterseccionEntreProductos"/>.</item>
/// </list>
/// Ni "sin planes" ni "intersección vacía" se reinterpretan como "usar el rango global": no existe
/// fallback a un rango mín/máx. <see cref="SinTablaDePlanes"/> / <see cref="RigeConfiguracionUnicaGlobal"/>
/// quedan solo por compatibilidad de dobles de test; el resolutor productivo no los emite.
/// </remarks>
public sealed class PlanesCreditoPersonalResultado
{
    private static readonly IReadOnlyList<PlanCuotaCreditoPersonal> SinPlanes =
        Array.Empty<PlanCuotaCreditoPersonal>();

    private PlanesCreditoPersonalResultado(
        bool esValido,
        MotivoSinPlanesCredito motivo,
        string? mensajeRechazo,
        OrigenPlanesCredito origen,
        IReadOnlyList<PlanCuotaCreditoPersonal> planes,
        IReadOnlyDictionary<int, IReadOnlyList<int>> cantidadesPorProducto)
    {
        EsValido = esValido;
        Motivo = motivo;
        MensajeRechazo = mensajeRechazo;
        Origen = origen;
        Planes = planes;
        CantidadesPorProducto = cantidadesPorProducto;
    }

    public bool EsValido { get; }

    public MotivoSinPlanesCredito Motivo { get; }

    /// <summary>Mensaje funcional listo para mostrar. Solo cuando <see cref="EsValido"/> es false.</summary>
    public string? MensajeRechazo { get; }

    public OrigenPlanesCredito Origen { get; }

    /// <summary>Cantidades comunes a todos los productos, ordenadas. Vacío si no rige tabla de planes.</summary>
    public IReadOnlyList<PlanCuotaCreditoPersonal> Planes { get; }

    /// <summary>Cantidades que admite cada producto por separado. Para diagnóstico y mensajes.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> CantidadesPorProducto { get; }

    /// <summary>
    /// No hay tabla de planes que restrinja la venta: rige la tasa única global y el rango
    /// mín/máx del método de cálculo. NO equivale a "sin planes compatibles".
    /// </summary>
    public bool RigeConfiguracionUnicaGlobal => EsValido && Origen == OrigenPlanesCredito.SinTablaDePlanes;

    public PlanCuotaCreditoPersonal? BuscarPlan(int cantidadCuotas) =>
        Planes.FirstOrDefault(p => p.CantidadCuotas == cantidadCuotas);

    public static PlanesCreditoPersonalResultado SinTablaDePlanes() =>
        new(true, MotivoSinPlanesCredito.Ninguno, null, OrigenPlanesCredito.SinTablaDePlanes,
            SinPlanes, new Dictionary<int, IReadOnlyList<int>>());

    public static PlanesCreditoPersonalResultado Resuelto(
        IReadOnlyList<PlanCuotaCreditoPersonal> planes,
        OrigenPlanesCredito origen,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? cantidadesPorProducto = null) =>
        new(true, MotivoSinPlanesCredito.Ninguno, null, origen, planes,
            cantidadesPorProducto ?? new Dictionary<int, IReadOnlyList<int>>());

    public static PlanesCreditoPersonalResultado SinInterseccion(
        string mensajeRechazo,
        IReadOnlyDictionary<int, IReadOnlyList<int>> cantidadesPorProducto) =>
        new(false, MotivoSinPlanesCredito.SinInterseccionEntreProductos, mensajeRechazo,
            OrigenPlanesCredito.Producto, SinPlanes, cantidadesPorProducto);

    /// <summary>
    /// No hay planes globales activos y la venta los necesita: Crédito Personal no ofrece cuotas.
    /// Rechazo explícito, sin fallback a un rango mín/máx.
    /// </summary>
    public static PlanesCreditoPersonalResultado SinPlanesGlobales(
        string mensajeRechazo,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? cantidadesPorProducto = null) =>
        new(false, MotivoSinPlanesCredito.SinPlanesGlobalesActivos, mensajeRechazo,
            OrigenPlanesCredito.SinTablaDePlanes, SinPlanes,
            cantidadesPorProducto ?? new Dictionary<int, IReadOnlyList<int>>());
}

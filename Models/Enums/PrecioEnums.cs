namespace TheBuryProject.Models.Enums;

/// <summary>
/// Tipo de cambio de precio masivo (sobre qué base se calcula)
/// </summary>
public enum TipoCambio
{
    /// <summary>
    /// Cambio porcentual sobre el precio actual
    /// </summary>
    PorcentajeSobrePrecioActual = 1,

    /// <summary>
    /// Cambio porcentual sobre el costo
    /// </summary>
    PorcentajeSobreCosto = 2,

    /// <summary>
    /// Cambio por valor absoluto (suma/resta valor fijo)
    /// </summary>
    ValorAbsoluto = 3,

    /// <summary>
    /// Asignación directa de precio
    /// </summary>
    AsignacionDirecta = 4
}

/// <summary>
/// Estado del batch de cambio de precios
/// </summary>
public enum EstadoBatch
{
    /// <summary>
    /// Batch creado en modo simulación (preview)
    /// </summary>
    Simulado = 1,

    /// <summary>
    /// Batch aprobado y autorizado, listo para aplicar
    /// </summary>
    Aprobado = 2,

    /// <summary>
    /// Batch aplicado, precios actualizados en la base de datos
    /// </summary>
    Aplicado = 3,

    /// <summary>
    /// Batch revertido (undo), se restauró vigencia anterior
    /// </summary>
    Revertido = 4,

    /// <summary>
    /// Batch rechazado por autorización
    /// </summary>
    Rechazado = 5,

    /// <summary>
    /// Batch cancelado antes de aplicar
    /// </summary>
    Cancelado = 6
}

/// <summary>
/// Tipo de aplicación del cambio de precio (dirección del cambio)
/// </summary>
public enum TipoAplicacion
{
    /// <summary>
    /// Aumento de precios
    /// </summary>
    Aumento = 1,

    /// <summary>
    /// Disminución de precios
    /// </summary>
    Disminucion = 2
}

/// <summary>
/// Tipo de lista de precios
/// </summary>
public enum TipoListaPrecio
{
    /// <summary>
    /// Lista de precios de contado
    /// </summary>
    Contado = 1,

    /// <summary>
    /// Lista de precios con tarjeta de crédito
    /// </summary>
    Tarjeta = 2,

    /// <summary>
    /// Lista de precios para distribuidores/mayoristas
    /// </summary>
    Mayorista = 3,

    /// <summary>
    /// Lista de precios especial o promocional
    /// </summary>
    Especial = 4
}
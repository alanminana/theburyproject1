namespace TheBuryProject.ViewModels;

/// <summary>Pestaña activa del encabezado común de la ficha de proveedor.</summary>
public enum PestanaProveedor
{
    Resumen,
    Ordenes,
    Cuenta
}

/// <summary>
/// Encabezado común de las pantallas de UN proveedor (resumen, órdenes de compra y cuenta corriente).
/// Los permisos se resuelven en la vista; el servidor sigue autorizando cada destino.
/// </summary>
public sealed record ProveedorContextoViewModel(
    int ProveedorId,
    string Nombre,
    PestanaProveedor Activa,
    string? Detalle = null,
    bool? Vigente = null,
    bool MostrarEditar = false);

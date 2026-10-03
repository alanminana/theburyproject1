using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers;

/// <summary>
/// Circuito de estados de una orden de compra: solo hacia adelante, sin volver a Borrador.
/// Borrador → Enviada → Confirmada → EnTransito → Recibida; Cancelada desde cualquier estado no terminal.
/// </summary>
public static class OrdenCompraTransiciones
{
    public static IReadOnlyList<EstadoOrdenCompra> Siguientes(EstadoOrdenCompra actual) => actual switch
    {
        EstadoOrdenCompra.Borrador => new[] { EstadoOrdenCompra.Enviada, EstadoOrdenCompra.Cancelada },
        EstadoOrdenCompra.Enviada => new[] { EstadoOrdenCompra.Confirmada, EstadoOrdenCompra.Cancelada },
        EstadoOrdenCompra.Confirmada => new[] { EstadoOrdenCompra.EnTransito, EstadoOrdenCompra.Recibida, EstadoOrdenCompra.Cancelada },
        EstadoOrdenCompra.EnTransito => new[] { EstadoOrdenCompra.Recibida, EstadoOrdenCompra.Cancelada },
        _ => Array.Empty<EstadoOrdenCompra>()
    };

    public static bool EsValida(EstadoOrdenCompra actual, EstadoOrdenCompra nuevo) =>
        Siguientes(actual).Contains(nuevo);
}

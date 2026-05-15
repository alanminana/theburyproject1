namespace TheBuryProject.Services.Models;

public enum CotizacionOpcionPagoEstado
{
    Disponible = 0,
    NoDisponible = 1,
    RequiereCliente = 2,
    RequiereEvaluacion = 3,
    BloqueadoPorProducto = 4,
    PlanInactivo = 5,
    CuotaInactiva = 6
}

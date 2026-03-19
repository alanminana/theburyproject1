using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Validators
{
    public interface IVentaValidator
    {
        void ValidarEstadoParaEdicion(Venta venta);
        void ValidarEstadoParaEliminacion(Venta venta);
        void ValidarEstadoParaConfirmacion(Venta venta);
        void ValidarEstadoParaFacturacion(Venta venta);
        void ValidarAutorizacion(Venta venta);
        void ValidarStock(Venta venta);
        void ValidarNoEstaCancelada(Venta venta);
        void ValidarEstadoAutorizacion(Venta venta, EstadoAutorizacionVenta estadoEsperado);
    }
    
}
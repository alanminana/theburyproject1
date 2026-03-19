using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Validators
{

    public class VentaValidator : IVentaValidator
    {
        public void ValidarEstadoParaEdicion(Venta venta)
        {
            if (venta.Estado != EstadoVenta.Cotizacion && 
                venta.Estado != EstadoVenta.Presupuesto &&
                venta.Estado != EstadoVenta.PendienteRequisitos &&
                venta.Estado != EstadoVenta.PendienteFinanciacion)
            {
                throw new InvalidOperationException(
                    $"Solo se pueden editar ventas en estado Cotización, Presupuesto, Pendiente Requisitos o Pendiente Financiación. Estado actual: {venta.Estado}");
            }
        }

        public void ValidarEstadoParaEliminacion(Venta venta)
        {
            if (venta.Estado != EstadoVenta.Cotizacion && 
                venta.Estado != EstadoVenta.Presupuesto &&
                venta.Estado != EstadoVenta.PendienteRequisitos &&
                venta.Estado != EstadoVenta.PendienteFinanciacion)
            {
                throw new InvalidOperationException(
                    $"Solo se pueden eliminar ventas en estado Cotización, Presupuesto, Pendiente Requisitos o Pendiente Financiación. Estado actual: {venta.Estado}");
            }
        }

        public void ValidarEstadoParaConfirmacion(Venta venta)
        {
            if (venta.Estado != EstadoVenta.Presupuesto && venta.Estado != EstadoVenta.PendienteRequisitos)
            {
                throw new InvalidOperationException(
                    $"Solo se pueden confirmar ventas en estado Presupuesto o Pendiente Requisitos. Estado actual: {venta.Estado}");
            }
        }

        public void ValidarEstadoParaFacturacion(Venta venta)
        {
            if (venta.Estado != EstadoVenta.Confirmada)
            {
                throw new InvalidOperationException(
                    $"Solo se pueden facturar ventas confirmadas. Estado actual: {venta.Estado}");
            }
        }

        public void ValidarAutorizacion(Venta venta)
        {
            if (venta.RequiereAutorizacion && venta.EstadoAutorizacion != EstadoAutorizacionVenta.Autorizada)
            {
                throw new InvalidOperationException(
                    $"La venta requiere autorizaci�n antes de continuar. Estado actual: {venta.EstadoAutorizacion}");
            }
        }

        public void ValidarStock(Venta venta)
        {
            var productosInsuficientes = venta.Detalles
                .Where(d => !d.IsDeleted)
                .Where(d => d.Producto != null)
                .Where(d => d.Producto!.StockActual < d.Cantidad)
                .Select(d => new
                {
                    Nombre = d.Producto!.Nombre,
                    d.Cantidad,
                    Disponible = d.Producto!.StockActual
                })
                .ToList();

            if (productosInsuficientes.Any())
            {
                var detalles = string.Join(", ", productosInsuficientes.Select(p =>
                    $"{p.Nombre} (necesita: {p.Cantidad}, disponible: {p.Disponible})"));

                throw new InvalidOperationException($"Stock insuficiente para: {detalles}");
            }
        }

        public void ValidarNoEstaCancelada(Venta venta)
        {
            if (venta.Estado == EstadoVenta.Cancelada)
            {
                throw new InvalidOperationException(VentaConstants.ErrorMessages.VENTA_YA_CANCELADA);
            }
        }

        public void ValidarEstadoAutorizacion(Venta venta, EstadoAutorizacionVenta estadoEsperado)
        {
            if (venta.EstadoAutorizacion != estadoEsperado)
            {
                throw new InvalidOperationException(
                    $"La venta debe estar en estado de autorizaci�n {estadoEsperado}. Estado actual: {venta.EstadoAutorizacion}");
            }
        }
    }
}
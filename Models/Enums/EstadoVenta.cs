namespace TheBuryProject.Models.Enums
{
    public enum EstadoVenta
    {
        Cotizacion = 0,             // Cotización sin compromiso
        Presupuesto = 1,            // Presupuesto formal
        Confirmada = 2,             // Venta confirmada
        Facturada = 3,              // Facturada
        Entregada = 4,              // Entregada
        Cancelada = 5,              // Cancelada
        PendienteRequisitos = 6,    // Esperando documentación, autorización u otros requisitos
        PendienteFinanciacion = 7   // Crédito personal: esperando configuración del financiamiento
    }
}
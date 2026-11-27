using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

/// <summary>
/// Simulador de la ayuda de Valoración de stock. No lee ni escribe datos: arma un Kardex ficticio (compra, venta,
/// compra) y llama a la misma función de cálculo que usa la pantalla
/// (<see cref="ValoracionStockGerencialService.CalcularProducto"/>). No define reglas propias.
/// </summary>
public static class ValoracionStockSimulador
{
    public const int CantidadMaxima = 100000;

    public static ValoracionStockSimulacion Simular(int cantidad1, decimal costo1, int vendidas, int cantidad2, decimal costo2)
    {
        var inicio = new DateTime(2026, 1, 1);
        var movimientos = new List<MovimientoCosteo>
        {
            new(1, inicio, 0, cantidad1, costo1, true)
        };

        var stock = cantidad1;
        if (vendidas > 0)
        {
            movimientos.Add(new MovimientoCosteo(2, inicio.AddDays(1), stock, stock - vendidas, 0, false));
            stock -= vendidas;
        }

        movimientos.Add(new MovimientoCosteo(3, inicio.AddDays(2), stock, stock + cantidad2, costo2, true));
        stock += cantidad2;

        var item = ValoracionStockGerencialService.CalcularProducto(stock, movimientos);

        return new ValoracionStockSimulacion
        {
            Stock = stock,
            CostoPromedio = item.CostoPromedioPonderado,
            UltimoCosto = item.UltimoCostoCompra,
            ValorGerencial = item.ValorGerencial,
            ValorAUltimoCosto = item.ValorAUltimoCosto,
            Diferencia = item.DiferenciaVsUltimoCosto,
            Estado = ValoracionStockGerencialService.EtiquetaEstado(item.Estado)
        };
    }
}

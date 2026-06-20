using System.Globalization;

namespace TheBuryProject.Models.DTOs
{
    public class VentasPorDiaDto
    {
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
        public int Cantidad { get; set; }
    }

    public class VentasPorMesDto
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string MesNombre { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public int Cantidad { get; set; }
    }

    public class ProductoMasVendidoDto
    {
        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal TotalVendido { get; set; }
    }

    public class EstadoCreditoDto
    {
        public string Estado { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal Monto { get; set; }
    }

    public class CobranzaPorMesDto
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string MesNombre { get; set; } = string.Empty;
        public decimal MontoEsperado { get; set; }
        public decimal MontoRecaudado { get; set; }
        public decimal PorcentajeEfectividad { get; set; }
    }

    /// <summary>
    /// Producto con stock bajo/crítico para el panel "Alertas de stock" del dashboard.
    /// </summary>
    public class StockAlertaDto
    {
        public int ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal StockActual { get; set; }
        public decimal StockMinimo { get; set; }
        /// <summary>"agotado" | "critico" | "bajo" — clave de severidad para estilos.</summary>
        public string Severidad { get; set; } = "bajo";
        /// <summary>Texto visible del badge ("Agotado" / "Stock crítico" / "Reponer pronto").</summary>
        public string SeveridadTexto { get; set; } = string.Empty;
        /// <summary>Cantidad sugerida de reposición (3x el mínimo - actual).</summary>
        public int CantidadSugerida { get; set; }
    }

    /// <summary>
    /// Evento reciente (venta, alta de cliente o alerta de stock) para el feed "Actividad reciente".
    /// </summary>
    public class ActividadRecienteDto
    {
        /// <summary>"venta" | "cliente" | "stock" — clave de tipo para el ícono/color.</summary>
        public string Tipo { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        /// <summary>Color del bullet ("emerald" / "primary" / "orange").</summary>
        public string Color { get; set; } = "emerald";
        /// <summary>Texto relativo precalculado ("Hace 15 min", "Hace 2 h", ...).</summary>
        public string TiempoRelativo { get; set; } = string.Empty;
    }
}
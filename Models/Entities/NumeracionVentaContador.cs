namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Contador atómico de la numeración de ventas y cotizaciones (FS2-NUM). Una fila por prefijo y período
    /// (p. ej. "VTA-202610"); <see cref="Valor"/> es el último número asignado. Reemplaza el cálculo "máximo + 1", que con
    /// altas simultáneas entregaba el mismo número a dos solicitudes.
    /// </summary>
    public class NumeracionVentaContador
    {
        public string Clave { get; set; } = string.Empty;

        public int Valor { get; set; }
    }
}

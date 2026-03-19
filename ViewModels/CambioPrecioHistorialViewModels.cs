namespace TheBuryProject.ViewModels;

public class CambioPrecioHistorialViewModel
{
    public List<CambioPrecioEventoItemViewModel> Eventos { get; set; } = new();
}

public class CambioPrecioEventoItemViewModel
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public decimal ValorPorcentaje { get; set; }
    public string Alcance { get; set; } = string.Empty;
    public int CantidadProductos { get; set; }
    public string? Motivo { get; set; }
    public bool Revertido { get; set; }
    public DateTime? RevertidoEn { get; set; }
    public string? RevertidoPor { get; set; }
}

public class CambioPrecioDetalleViewModel
{
    public int EventoId { get; set; }
    public DateTime Fecha { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public decimal ValorPorcentaje { get; set; }
    public string Alcance { get; set; } = string.Empty;
    public int CantidadProductos { get; set; }
    public string? Motivo { get; set; }
    public bool Revertido { get; set; }
    public DateTime? RevertidoEn { get; set; }
    public string? RevertidoPor { get; set; }
    public List<CambioPrecioDetalleItemViewModel> Detalles { get; set; } = new();
}

public class CambioPrecioDetalleItemViewModel
{
    public int ProductoId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal PrecioAnterior { get; set; }
    public decimal PrecioNuevo { get; set; }
}

/// <summary>
/// Fila de historial de cambios de precio de un producto individual.
/// </summary>
public class HistorialPrecioProductoItemViewModel
{
    public int EventoId { get; set; }
    public DateTime Fecha { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public decimal PrecioAnterior { get; set; }
    public decimal PrecioNuevo { get; set; }
    public bool PuedeRevertir { get; set; }
}

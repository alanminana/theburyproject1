using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;

/// <summary>Fila del listado de listas de precios (D-01).</summary>
public class ListaPrecioResumen
{
    public ListaPrecio Lista { get; set; } = null!;
    public int PreciosVigentes { get; set; }
}

public class ListaPrecioFilaProducto
{
    public int ProductoId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal CostoProducto { get; set; }
    public decimal PrecioBase { get; set; }
    public decimal? PrecioEnLista { get; set; }
    public decimal? CostoEnLista { get; set; }
    public decimal? MargenPorcentaje { get; set; }
    public DateTime? VigenciaDesde { get; set; }
    public bool? EsManual { get; set; }
}

public class ListaPreciosPagina
{
    public List<ListaPrecioFilaProducto> Items { get; set; } = new();
    public int Pagina { get; set; } = 1;
    public int TotalPaginas { get; set; } = 1;
    public int Total { get; set; }
}

public class ListaPrecioIndexViewModel
{
    public List<ListaPrecioResumen> Listas { get; set; } = new();
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }
    public bool PuedeAplicar { get; set; }
}

/// <summary>Formulario de alta/edición de una lista de precios. Los límites coinciden con las columnas y con el servicio.</summary>
public class ListaPrecioFormViewModel
{
    public int Id { get; set; }

    public byte[]? RowVersion { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(100, ErrorMessage = "El nombre no puede superar los 100 caracteres.")]
    [Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(20, ErrorMessage = "El código no puede superar los 20 caracteres.")]
    [Display(Name = "Código")]
    public string Codigo { get; set; } = string.Empty;

    [Display(Name = "Tipo")]
    public TipoListaPrecio Tipo { get; set; } = TipoListaPrecio.Contado;

    [StringLength(500, ErrorMessage = "La descripción no puede superar los 500 caracteres.")]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [Range(0, 999.99, ErrorMessage = "El margen debe estar entre 0 y 999,99.")]
    [Display(Name = "Margen sobre costo (%)")]
    public decimal? MargenPorcentaje { get; set; }

    [Range(0, 999.99, ErrorMessage = "El margen mínimo debe estar entre 0 y 999,99.")]
    [Display(Name = "Margen mínimo (%)")]
    public decimal? MargenMinimoPorcentaje { get; set; }

    [Range(0, 999.99, ErrorMessage = "El recargo debe estar entre 0 y 999,99.")]
    [Display(Name = "Recargo (%)")]
    public decimal? RecargoPorcentaje { get; set; }

    [Range(1, 60, ErrorMessage = "La cantidad de cuotas debe estar entre 1 y 60.")]
    [Display(Name = "Cantidad de cuotas")]
    public int? CantidadCuotas { get; set; }

    [StringLength(20)]
    [Display(Name = "Redondeo")]
    public string? ReglaRedondeo { get; set; }

    [Display(Name = "Orden")]
    [Range(0, 9999, ErrorMessage = "El orden debe estar entre 0 y 9999.")]
    public int Orden { get; set; }

    [StringLength(1000, ErrorMessage = "Las notas no pueden superar los 1000 caracteres.")]
    [Display(Name = "Notas")]
    public string? Notas { get; set; }

    [Display(Name = "Activa")]
    public bool Activa { get; set; } = true;

    [Display(Name = "Lista predeterminada")]
    public bool EsPredeterminada { get; set; }

    /// <summary>Solo lectura en la vista: la predeterminada no se desmarca ni se desactiva desde su propio formulario.</summary>
    public bool EraPredeterminada { get; set; }

    public bool EsNueva => Id == 0;
}

/// <summary>Resultado del simulador de la ayuda (solo cálculo, no se guarda nada).</summary>
public class ListaPrecioSimulacion
{
    public decimal Costo { get; set; }
    public decimal MargenImporte { get; set; }
    public decimal Subtotal { get; set; }
    public decimal RecargoImporte { get; set; }
    public decimal AntesDeRedondeo { get; set; }
    public decimal AjusteRedondeo { get; set; }
    public decimal Sugerido { get; set; }
    public decimal? MargenSugerido { get; set; }
    public decimal? PrecioMinimo { get; set; }
    public bool SugeridoBajoMinimo { get; set; }
    public decimal? PrecioPrueba { get; set; }
    public decimal? MargenPrueba { get; set; }
    public bool PruebaRechazada { get; set; }
    public bool PruebaConDecimalesDeMas { get; set; }
}

/// <summary>Pantalla para fijar el precio manual de un producto en una lista.</summary>
public class FijarPrecioViewModel
{
    public int ListaId { get; set; }
    public string ListaNombre { get; set; } = string.Empty;
    public string ListaCodigo { get; set; } = string.Empty;
    public decimal? MargenMinimoPorcentaje { get; set; }
    public decimal? MargenPorcentaje { get; set; }
    public decimal? RecargoPorcentaje { get; set; }
    public string? ReglaRedondeo { get; set; }

    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public decimal CostoProducto { get; set; }
    public decimal PrecioBase { get; set; }
    public decimal? PrecioSugerido { get; set; }

    public decimal? Precio { get; set; }

    [StringLength(500, ErrorMessage = "Las notas no pueden superar los 500 caracteres.")]
    public string? Notas { get; set; }

    public List<PrecioHistorialItemViewModel> Historial { get; set; } = new();
}

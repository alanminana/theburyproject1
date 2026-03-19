using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Define los umbrales de autorización por rol de usuario
/// </summary>
public class UmbralAutorizacion : AuditableEntity
{
    [Required]
    [StringLength(50)]
    public string Rol { get; set; } = string.Empty;

    [Required]
    public TipoUmbral TipoUmbral { get; set; }

    [Required]
    public decimal ValorMaximo { get; set; }

    [StringLength(500)]
    public string? Descripcion { get; set; }

    public bool Activo { get; set; } = true;
}

/// <summary>
/// Tipos de umbral para autorización
/// </summary>
public enum TipoUmbral
{
    [Display(Name = "Descuento en Venta")]
    DescuentoVenta = 0,

    [Display(Name = "Monto Total de Venta")]
    MontoTotalVenta = 1,

    [Display(Name = "Monto de Crédito")]
    MontoCredito = 2,

    [Display(Name = "Descuento en Compra")]
    DescuentoCompra = 3
}
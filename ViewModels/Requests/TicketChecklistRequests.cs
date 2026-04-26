using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels.Requests;

/// <summary>Request para agregar un ítem al checklist de un ticket.</summary>
public class AgregarChecklistItemRequest
{
    [Required]
    [StringLength(500)]
    public string Descripcion { get; set; } = string.Empty;

    public int Orden { get; set; }
}

/// <summary>Request para marcar o desmarcar un ítem del checklist.</summary>
public class MarcarChecklistItemRequest
{
    public bool Completado { get; set; }
}

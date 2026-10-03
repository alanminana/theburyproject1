using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

public enum NotaRapidaTipo
{
    /// <summary>Nota de texto libre.</summary>
    Texto = 0,

    /// <summary>Lista de ítems tildables; <see cref="NotaRapida.Texto"/> es el título.</summary>
    Checklist = 1
}

/// <summary>
/// Nota rápida personal de un usuario: texto libre o checklist con ítems.
/// Es privada: solo la ve y modifica su dueño.
/// </summary>
public class NotaRapida : AuditableEntity
{
    [Required]
    [StringLength(450)]
    public string UsuarioId { get; set; } = string.Empty;

    public NotaRapidaTipo Tipo { get; set; } = NotaRapidaTipo.Texto;

    [Required]
    [StringLength(1000)]
    public string Texto { get; set; } = string.Empty;

    public virtual ICollection<NotaRapidaItem> Items { get; set; } = new List<NotaRapidaItem>();
}

/// <summary>Ítem tildable de una nota rápida de tipo checklist.</summary>
public class NotaRapidaItem : AuditableEntity
{
    public int NotaRapidaId { get; set; }

    [Required]
    [StringLength(500)]
    public string Texto { get; set; } = string.Empty;

    public bool Completado { get; set; }

    public DateTime? FechaCompletado { get; set; }

    public virtual NotaRapida NotaRapida { get; set; } = null!;
}

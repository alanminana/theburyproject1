using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Enums;

public enum EstadoTicket
{
    [Display(Name = "Pendiente")]
    Pendiente = 0,

    [Display(Name = "En Curso")]
    EnCurso = 1,

    [Display(Name = "Resuelto")]
    Resuelto = 2,

    [Display(Name = "Cancelado")]
    Cancelado = 3
}

using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Enums;

public enum TipoTicket
{
    [Display(Name = "Error de Texto")]
    ErrorTexto = 0,

    [Display(Name = "Error Funcional")]
    ErrorFuncional = 1,

    [Display(Name = "Mejora")]
    Mejora = 2,

    [Display(Name = "Nueva Funcionalidad")]
    NuevaFuncionalidad = 3,

    [Display(Name = "Eliminación")]
    Eliminacion = 4,

    [Display(Name = "Fusión")]
    Fusion = 5,

    [Display(Name = "Otro")]
    Otro = 6
}

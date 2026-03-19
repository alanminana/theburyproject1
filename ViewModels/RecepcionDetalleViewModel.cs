using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para recepcionar un detalle de orden de compra
    /// </summary>
    public class RecepcionDetalleViewModel
    {
        public int DetalleId { get; set; }
        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; } = string.Empty;
        public int CantidadSolicitada { get; set; }
        public int CantidadYaRecibida { get; set; }
        public int CantidadPendiente => CantidadSolicitada - CantidadYaRecibida;
        public int CantidadARecepcionar { get; set; }
    }
}
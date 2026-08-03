using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Administra la configuración de Crédito Personal específica de un producto:
    /// permitido/bloqueado, máximo de cuotas y planes de cuotas propios (cantidad + recargo).
    /// </summary>
    public interface IProductoCreditoPersonalConfigService
    {
        /// <summary>
        /// Carga la configuración de crédito personal del producto, incluyendo filas
        /// plantilla (candidatas no activas) para edición en la UI. <paramref name="productoId"/>
        /// puede ser 0 para obtener solo las plantillas candidatas de un producto aún no creado
        /// (sección Crédito personal del modal de alta).
        /// </summary>
        Task<ProductoCreditoPersonalConfigViewModel> ObtenerAsync(int productoId);

        /// <summary>
        /// Valida la configuración sin tocar la base de datos: modo declarado consistente con
        /// <c>AdmiteCreditoPersonal</c> y con los planes activos, cantidades sin duplicar, rango
        /// 1–120, recargos no negativos. Se usa para rechazar datos inválidos antes de crear o
        /// actualizar el producto (evita persistencia parcial).
        /// </summary>
        List<string> Validar(ProductoCreditoPersonalConfigViewModel config);

        /// <summary>
        /// Valida (ver <see cref="Validar"/>) y, si es válida, persiste: upsert de
        /// ProductoCreditoRestriccion y alta/actualización de los planes de cuotas propios.
        /// </summary>
        Task<(bool Ok, List<string> Errores)> GuardarAsync(int productoId, ProductoCreditoPersonalConfigViewModel config, string usuario);
    }
}

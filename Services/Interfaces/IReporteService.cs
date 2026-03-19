using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Interfaz para el servicio de reportes
    /// </summary>
    public interface IReporteService
    {
        /// <summary>
        /// Genera reporte de ventas con filtros
        /// </summary>
        Task<ReporteVentasResultadoViewModel> GenerarReporteVentasAsync(ReporteVentasFiltroViewModel filtro);

        /// <summary>
        /// Genera reporte de márgenes de productos
        /// </summary>
        Task<ReporteMargenesViewModel> GenerarReporteMargenesAsync(int? categoriaId = null, int? marcaId = null);

        /// <summary>
        /// Genera reporte de morosidad de créditos
        /// </summary>
        Task<ReporteMorosidadViewModel> GenerarReporteMorosidadAsync();

        /// <summary>
        /// Obtiene datos para gráfico de ventas agrupadas
        /// </summary>
        Task<List<VentasAgrupadasViewModel>> ObtenerVentasAgrupadasAsync(
            DateTime fechaDesde,
            DateTime fechaHasta,
            string agruparPor);

        /// <summary>
        /// Exporta reporte de ventas a Excel
        /// </summary>
        Task<byte[]> ExportarVentasExcelAsync(ReporteVentasFiltroViewModel filtro);

        /// <summary>
        /// Exporta reporte de márgenes a Excel
        /// </summary>
        Task<byte[]> ExportarMargenesExcelAsync(int? categoriaId = null, int? marcaId = null);

        /// <summary>
        /// Exporta reporte de morosidad a Excel
        /// </summary>
        Task<byte[]> ExportarMorosidadExcelAsync();

        /// <summary>
        /// Genera PDF del reporte de ventas
        /// </summary>
        Task<byte[]> GenerarVentasPdfAsync(ReporteVentasFiltroViewModel filtro);

        /// <summary>
        /// Genera PDF del reporte de morosidad
        /// </summary>
        Task<byte[]> GenerarMorosidadPdfAsync();
    }
}
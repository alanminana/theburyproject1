using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Orquesta <see cref="IPunitorioCalculator"/> (PUN-ML4) con datos reales de una cuota (PUN-ML5).
    /// Separa inequívocamente dos operaciones: consultar (nunca persiste) y aplicar/anular (requieren
    /// confirmación humana autorizada y persisten un snapshot auditable). El cobro de un punitorio
    /// aplicado es PUN-ML6 — este servicio nunca registra caja ni modifica <c>Cuota.MontoPagado</c>.
    /// </summary>
    public interface IPunitorioService
    {
        /// <summary>
        /// Punitorio calculado al día para una cuota real. Solo lectura: no persiste ni modifica
        /// ninguna entidad, sin importar cuántas veces se llame.
        /// </summary>
        /// <param name="fechaCalculo">
        /// Fecha explícita para un cálculo histórico. <c>null</c> usa <c>IRelojComercial.HoyComercial</c>.
        /// Permitir que el caller fuerce una fecha arbitraria es una puerta administrativa/de
        /// diagnóstico: la gating real (permiso) es responsabilidad del controller, fuera de alcance
        /// en PUN-ML5 (mismo deferimiento que <c>ConfiguracionPunitorioComando.AutorizadoParaRetroactivo</c>).
        /// </param>
        /// <exception cref="KeyNotFoundException">No existe una cuota no eliminada con ese Id.</exception>
        Task<PunitorioConsultaResultado> CalcularCuotaAsync(
            int cuotaId, DateOnly? fechaCalculo = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Punitorio aplicado pendiente de cobro de la cuota (PUN-ML6): <c>Importe</c> de la
        /// aplicación activa (Modelo A: a lo sumo una) neto de los pagos efectivos ya atribuidos a
        /// ella (<c>PagoCuota.ImporteAplicadoPunitorio</c> con <c>PunitorioAplicadoId</c> igual y
        /// <c>Estado == Aplicado</c>). Cero si no hay aplicación activa. Nunca negativo. Solo lectura.
        /// </summary>
        Task<decimal> ObtenerPunitorioAplicadoPendienteAsync(int cuotaId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Aplicación activa de la cuota (Modelo A: a lo sumo una) junto con su progreso de cobro
        /// (PUN-ML6), como entidad <b>tracked</b> del <c>AppDbContext</c> del caller — pensado para que
        /// el cobro (<c>CreditoService</c>, mismo <c>AppDbContext</c> por alcance de request) pueda
        /// mutar <see cref="PunitorioAplicado.Estado"/> dentro de su propia transacción sin una
        /// consulta adicional. <c>null</c> si no hay aplicación activa.
        /// </summary>
        Task<PunitorioAplicadoProgreso?> ObtenerAplicacionActivaConProgresoAsync(
            int cuotaId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Aplica el punitorio de una cuota: recalcula server-side dentro de una transacción
        /// inmediatamente antes de persistir (el cálculo mostrado antes en UI nunca es autoridad),
        /// ignora cualquier importe/saldo/porcentaje/período/configuración/fecha que hubiera llegado
        /// del navegador, y guarda un snapshot auditable. Rechaza si ya existe una aplicación activa
        /// para la cuota (Modelo A: como máximo una activa por cuota — anular primero para reaplicar)
        /// o si el estado de cálculo no es aplicable (ver <see cref="Exceptions.PunitorioAplicadoRechazadoException"/>).
        /// </summary>
        /// <remarks>
        /// El actor y el permiso se resuelven exclusivamente desde <see cref="ICurrentUserService"/>
        /// (infraestructura confiable de servidor), nunca desde <paramref name="comando"/>: el DTO no
        /// expone ningún campo de usuario/permiso/autoridad. Rechaza con
        /// <see cref="Models.Enums.MotivoRechazoPunitorioAplicado.NoAutorizado"/> si el usuario no está
        /// autenticado o no tiene el permiso <c>cobranzas.applyfine</c>. Esta verificación ocurre antes
        /// de abrir cualquier transacción: un rechazo de autorización nunca toca la base.
        /// </remarks>
        /// <exception cref="KeyNotFoundException">No existe una cuota no eliminada con ese Id.</exception>
        /// <exception cref="Exceptions.PunitorioAplicadoRechazadoException">No autorizado, solicitud inválida, estado no aplicable, o conflicto.</exception>
        Task<PunitorioAplicado> AplicarAsync(
            int cuotaId, PunitorioAplicarComando comando, CancellationToken cancellationToken = default);

        /// <summary>
        /// Anula una aplicación activa. No borra la fila: registra usuario, fecha comercial y
        /// motivo, y cambia el estado. Rechaza si ya está anulada o si no está en estado <c>Aplicado</c>.
        /// </summary>
        /// <remarks>
        /// Mismo criterio de autorización que <see cref="AplicarAsync"/>: actor y permiso resueltos
        /// desde <see cref="ICurrentUserService"/>, exigiendo el permiso <c>cobranzas.revertfine</c>
        /// (distinto del de aplicar — un usuario puede tener uno sin el otro).
        /// </remarks>
        /// <exception cref="KeyNotFoundException">No existe un <see cref="PunitorioAplicado"/> con ese Id.</exception>
        /// <exception cref="Exceptions.PunitorioAplicadoRechazadoException">No autorizado, solicitud inválida o conflicto (estado/concurrencia).</exception>
        Task<PunitorioAplicado> AnularAsync(
            int punitorioAplicadoId, PunitorioAnularComando comando, CancellationToken cancellationToken = default);
    }
}

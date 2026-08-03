namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Estado de una fila de <see cref="Entities.PunitorioAplicado"/> (PUN-ML5). El ciclo de vida
    /// completo incluye <see cref="Pagado"/> y <see cref="Revertido"/> para que el modelo admita la
    /// transición futura sin migración destructiva, pero en PUN-ML5 solo se producen y consumen
    /// <see cref="Aplicado"/> y <see cref="Anulado"/>: el cobro (que produciría <see cref="Pagado"/>)
    /// es PUN-ML6.
    /// </summary>
    public enum EstadoPunitorioAplicado
    {
        /// <summary>Aplicación vigente: cuenta como punitorio aplicado pendiente.</summary>
        Aplicado = 1,

        /// <summary>Reservado para PUN-ML6 (cobro). Ninguna fila alcanza este estado todavía.</summary>
        Pagado = 2,

        /// <summary>Anulada por un usuario autorizado antes de ser pagada. No cuenta como pendiente.</summary>
        Anulado = 3,

        /// <summary>Reservado para una futura reversión posterior al cobro (fuera de alcance de PUN-ML5/ML6).</summary>
        Revertido = 4
    }

    /// <summary>
    /// Motivo funcional por el que el servidor rechaza aplicar o anular un punitorio. Permite al
    /// controller (fuera de alcance en PUN-ML5) responder con el estado HTTP correcto sin filtrar
    /// detalles internos — mismo patrón que <see cref="MotivoRechazoConfiguracionPunitorio"/>.
    /// </summary>
    public enum MotivoRechazoPunitorioAplicado
    {
        /// <summary>Datos del pedido inválidos (motivo vacío, usuario vacío, cuota inexistente) (HTTP 400).</summary>
        SolicitudInvalida = 0,

        /// <summary>
        /// El cálculo server-side no admite una aplicación persistida: estado de cálculo distinto
        /// de <c>Calculado</c>, o <c>Calculado</c> con importe cero (HTTP 422/409 según el mapeo del controller).
        /// </summary>
        NoAplicable = 1,

        /// <summary>Ya existe una aplicación activa para la cuota, o conflicto de concurrencia (HTTP 409).</summary>
        Conflicto = 2,

        /// <summary>
        /// El actor resuelto por el servidor (<see cref="Services.Interfaces.ICurrentUserService"/>) no está
        /// autenticado, o está autenticado pero no tiene el permiso requerido para la operación (HTTP 401/403).
        /// Nunca se produce por datos del comando: la identidad y el permiso se resuelven exclusivamente
        /// desde infraestructura confiable del lado del servidor.
        /// </summary>
        NoAutorizado = 3
    }
}

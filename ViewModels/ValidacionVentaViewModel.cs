using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Resultado unificado de validación para ventas con crédito personal.
    /// Consolida todas las razones por las que una venta puede requerir autorización
    /// o estar pendiente de requisitos.
    /// </summary>
    public class ValidacionVentaResult
    {
        /// <summary>
        /// Indica si la venta puede proceder sin restricciones
        /// </summary>
        public bool PuedeProceeder => !RequiereAutorizacion && !PendienteRequisitos && !NoViable;

        /// <summary>
        /// Indica si la venta NO puede guardarse bajo ninguna circunstancia.
        /// Cuando es true, el sistema debe rechazar el guardado completamente.
        /// </summary>
        public bool NoViable { get; set; }

        /// <summary>
        /// Indica si la venta requiere autorización de un superior
        /// </summary>
        public bool RequiereAutorizacion { get; set; }

        /// <summary>
        /// Indica si faltan requisitos previos (documentación, cupo asignado, etc.)
        /// </summary>
        public bool PendienteRequisitos { get; set; }

        /// <summary>
        /// Lista de razones por las que se requiere autorización
        /// </summary>
        public List<RazonAutorizacion> RazonesAutorizacion { get; set; } = new();

        /// <summary>
        /// Lista de requisitos pendientes que bloquean la venta
        /// </summary>
        public List<RequisitoPendiente> RequisitosPendientes { get; set; } = new();

        /// <summary>
        /// Mensaje consolidado para mostrar al usuario
        /// </summary>
        public string MensajeResumen => GenerarMensajeResumen();

        /// <summary>
        /// Estado de autorización sugerido para la venta
        /// </summary>
        public EstadoAutorizacionVenta EstadoAutorizacionSugerido => DeterminarEstadoAutorizacion();

        /// <summary>
        /// Estado de aptitud crediticia del cliente (del semáforo)
        /// </summary>
        public EstadoCrediticioCliente EstadoAptitud { get; set; } = EstadoCrediticioCliente.NoEvaluado;

        private string GenerarMensajeResumen()
        {
            if (PuedeProceeder)
                return "La venta puede proceder normalmente.";

            var mensajes = new List<string>();

            if (NoViable)
            {
                mensajes.Add("OPERACIÓN NO VIABLE: El cliente no cumple los requisitos mínimos para crédito.");
            }

            if (RequisitosPendientes.Any())
            {
                var requisitosTexto = string.Join(", ", RequisitosPendientes.Select(r => r.Descripcion));
                mensajes.Add($"Requisitos pendientes: {requisitosTexto}");
            }

            if (RazonesAutorizacion.Any())
            {
                var razonesTexto = string.Join(", ", RazonesAutorizacion.Select(r => r.Descripcion));
                mensajes.Add($"Requiere autorización: {razonesTexto}");
            }

            return string.Join(". ", mensajes);
        }

        private EstadoAutorizacionVenta DeterminarEstadoAutorizacion()
        {
            if (!RequiereAutorizacion)
                return EstadoAutorizacionVenta.NoRequiere;

            return EstadoAutorizacionVenta.PendienteAutorizacion;
        }
    }

    /// <summary>
    /// Razón por la que una venta requiere autorización de un superior
    /// </summary>
    public class RazonAutorizacion
    {
        public TipoRazonAutorizacion Tipo { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string? DetalleAdicional { get; set; }

        /// <summary>
        /// Valor numérico asociado (ej: monto que excede el límite, días de mora)
        /// </summary>
        public decimal? ValorAsociado { get; set; }

        /// <summary>
        /// Valor límite configurado
        /// </summary>
        public decimal? ValorLimite { get; set; }
    }

    /// <summary>
    /// Requisito pendiente que bloquea la continuación de la venta
    /// </summary>
    public class RequisitoPendiente
    {
        public TipoRequisitoPendiente Tipo { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string? AccionRequerida { get; set; }

        /// <summary>
        /// URL o ruta para completar el requisito
        /// </summary>
        public string? UrlAccion { get; set; }
    }

    /// <summary>
    /// Tipos de razones por las que se requiere autorización
    /// </summary>
    public enum TipoRazonAutorizacion
    {
        /// <summary>
        /// El monto excede el cupo disponible del cliente
        /// </summary>
        ExcedeCupo = 1,

        /// <summary>
        /// El cliente tiene mora activa
        /// </summary>
        MoraActiva = 2,

        /// <summary>
        /// Documentación vencida pero existente
        /// </summary>
        DocumentacionVencida = 3,

        /// <summary>
        /// El monto excede el umbral permitido para el rol del vendedor
        /// </summary>
        ExcedeUmbralRol = 4,

        /// <summary>
        /// El cliente está marcado como "Requiere Autorización" en su aptitud
        /// </summary>
        ClienteRequiereAutorizacion = 5
    }

    /// <summary>
    /// Tipos de requisitos pendientes que bloquean la venta
    /// </summary>
    public enum TipoRequisitoPendiente
    {
        /// <summary>
        /// Falta documentación obligatoria
        /// </summary>
        DocumentacionFaltante = 1,

        /// <summary>
        /// El cliente no tiene límite de crédito asignado
        /// </summary>
        SinLimiteCredito = 2,

        /// <summary>
        /// El cliente no ha sido evaluado crediticiamente
        /// </summary>
        SinEvaluacionCrediticia = 3,

        /// <summary>
        /// El cliente está marcado como No Apto para crédito
        /// </summary>
        ClienteNoApto = 4,

        /// <summary>
        /// No existe crédito aprobado para el cliente
        /// </summary>
        SinCreditoAprobado = 5
    }
}

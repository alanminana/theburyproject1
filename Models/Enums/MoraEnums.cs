namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Tipo de tasa de mora: diaria o mensual
    /// </summary>
    public enum TipoTasaMora
    {
        /// <summary>
        /// La tasa se aplica por cada día de atraso
        /// </summary>
        Diaria = 1,

        /// <summary>
        /// La tasa se aplica mensualmente (prorrateada por días)
        /// </summary>
        Mensual = 2
    }

    /// <summary>
    /// Base sobre la cual se calcula la mora
    /// </summary>
    public enum BaseCalculoMora
    {
        /// <summary>
        /// Mora se calcula solo sobre el capital de la cuota
        /// </summary>
        Capital = 1,

        /// <summary>
        /// Mora se calcula sobre capital + intereses de la cuota
        /// </summary>
        CapitalMasInteres = 2
    }

    /// <summary>
    /// Tipo de tope máximo para la mora
    /// </summary>
    public enum TipoTopeMora
    {
        /// <summary>
        /// El tope es un porcentaje del capital
        /// </summary>
        Porcentaje = 1,

        /// <summary>
        /// El tope es un monto fijo en pesos
        /// </summary>
        MontoFijo = 2
    }

    /// <summary>
    /// Estado del workflow de gestión de cobranza
    /// </summary>
    public enum EstadoGestionCobranza
    {
        /// <summary>
        /// Alerta generada, sin gestión iniciada
        /// </summary>
        Pendiente = 1,

        /// <summary>
        /// Gestor asignado, contactando al cliente
        /// </summary>
        EnGestion = 2,

        /// <summary>
        /// Cliente prometió pagar en fecha específica
        /// </summary>
        PromesaPago = 3,

        /// <summary>
        /// Se firmó acuerdo de pago y está vigente
        /// </summary>
        AcuerdoActivo = 4,

        /// <summary>
        /// Cliente pagó toda la deuda o cumplió el acuerdo
        /// </summary>
        Regularizado = 5,

        /// <summary>
        /// Se agotaron las opciones de cobro
        /// </summary>
        Incobrable = 6
    }

    /// <summary>
    /// Tipo de contacto con el cliente moroso
    /// </summary>
    public enum TipoContacto
    {
        /// <summary>
        /// Llamada telefónica de voz
        /// </summary>
        LlamadaTelefonica = 1,

        /// <summary>
        /// Mensaje por WhatsApp
        /// </summary>
        WhatsApp = 2,

        /// <summary>
        /// Correo electrónico
        /// </summary>
        Email = 3,

        /// <summary>
        /// Visita presencial al cliente
        /// </summary>
        VisitaPresencial = 4,

        /// <summary>
        /// Mensaje de texto SMS
        /// </summary>
        SMS = 5,

        /// <summary>
        /// Nota interna sin contacto con cliente
        /// </summary>
        NotaInterna = 6
    }

    /// <summary>
    /// Resultado del intento de contacto
    /// </summary>
    public enum ResultadoContacto
    {
        /// <summary>
        /// Se logró comunicación con el cliente
        /// </summary>
        ContactoExitoso = 1,

        /// <summary>
        /// Cliente no respondió al contacto
        /// </summary>
        NoContesta = 2,

        /// <summary>
        /// El teléfono/email no corresponde al cliente
        /// </summary>
        NumeroEquivocado = 3,

        /// <summary>
        /// Cliente se comprometió a pagar
        /// </summary>
        PromesaPago = 4,

        /// <summary>
        /// Cliente se niega a pagar
        /// </summary>
        RechazaPagar = 5,

        /// <summary>
        /// Cliente pide refinanciar la deuda
        /// </summary>
        SolicitaAcuerdo = 6,

        /// <summary>
        /// Se dejó mensaje en buzón/contestador/WhatsApp
        /// </summary>
        MensajeDejado = 7,

        /// <summary>
        /// El cliente no cumplió una promesa de pago previa
        /// </summary>
        PromesaIncumplida = 8,

        /// <summary>
        /// El cliente realizó el pago
        /// </summary>
        PagoRealizado = 9
    }

    /// <summary>
    /// Estado del acuerdo de pago
    /// </summary>
    public enum EstadoAcuerdo
    {
        /// <summary>
        /// Acuerdo en negociación, no confirmado
        /// </summary>
        Borrador = 1,

        /// <summary>
        /// Acuerdo firmado y en curso
        /// </summary>
        Activo = 2,

        /// <summary>
        /// Cliente pagó todas las cuotas del acuerdo
        /// </summary>
        Cumplido = 3,

        /// <summary>
        /// Cliente dejó de pagar el acuerdo
        /// </summary>
        Incumplido = 4,

        /// <summary>
        /// Acuerdo anulado por decisión del negocio
        /// </summary>
        Cancelado = 5
    }

    /// <summary>
    /// Estado de una cuota del acuerdo de pago
    /// </summary>
    public enum EstadoCuotaAcuerdo
    {
        /// <summary>
        /// Cuota pendiente de pago
        /// </summary>
        Pendiente = 1,

        /// <summary>
        /// Cuota pagada completamente
        /// </summary>
        Pagada = 2,

        /// <summary>
        /// Cuota pasó su fecha de vencimiento sin pagar
        /// </summary>
        Vencida = 3,

        /// <summary>
        /// Cuota pagada parcialmente
        /// </summary>
        Parcial = 4
    }

    /// <summary>
    /// Canal de notificación preferido
    /// </summary>
    public enum CanalNotificacion
    {
        /// <summary>
        /// Solo WhatsApp
        /// </summary>
        WhatsApp = 1,

        /// <summary>
        /// Solo Email
        /// </summary>
        Email = 2,

        /// <summary>
        /// Ambos canales
        /// </summary>
        Ambos = 3
    }

    /// <summary>
    /// Tipo de plantilla de notificación de mora
    /// </summary>
    public enum TipoPlantillaMora
    {
        /// <summary>
        /// Aviso antes del vencimiento
        /// </summary>
        AvisoPreventivo = 1,

        /// <summary>
        /// Notificación de cuota vencida
        /// </summary>
        CuotaVencida = 2,

        /// <summary>
        /// Recordatorio periódico de mora acumulada
        /// </summary>
        RecordatorioMora = 3,

        /// <summary>
        /// Recordatorio de cuota de acuerdo de pago
        /// </summary>
        RecordatorioAcuerdo = 4
    }

    /// <summary>
    /// Tipo de bloqueo a aplicar al cliente moroso
    /// </summary>
    public enum TipoBloqueoCliente
    {
        /// <summary>
        /// Bloquea solo la solicitud de nuevos créditos
        /// </summary>
        NuevosCreditos = 1,

        /// <summary>
        /// Bloquea todas las operaciones
        /// </summary>
        TodasOperaciones = 2,

        /// <summary>
        /// Bloquea solo ventas a crédito (permite contado)
        /// </summary>
        SoloVentasCredito = 3
    }
}

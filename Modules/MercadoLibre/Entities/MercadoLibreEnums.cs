namespace TheBuryProject.Modules.MercadoLibre.Entities
{
    /// <summary>
    /// Estado interno de procesamiento de una orden ML dentro del ERP.
    /// Independiente del status de Mercado Libre (paid, cancelled, etc.).
    /// </summary>
    public enum MercadoLibreOrderEstadoInterno
    {
        /// <summary>Orden importada, aún sin procesar contra el ERP.</summary>
        Importada = 0,

        /// <summary>No se pudo crear la venta: hay ítems sin producto vinculado o falta configuración.</summary>
        PendienteVinculacion = 1,

        /// <summary>Venta interna creada y stock descontado.</summary>
        VentaCreada = 2,

        /// <summary>Liquidación registrada en caja (neto real acreditado).</summary>
        Liquidada = 3,

        /// <summary>Error al procesar (ver ErrorProcesamiento).</summary>
        Error = 4,

        /// <summary>Marcada manualmente para no procesar (ej: orden de prueba).</summary>
        Ignorada = 5,

        /// <summary>
        /// Hay productos trazables sin unidades físicas suficientes (o sin stock
        /// no trazado): un operador debe asignar unidades antes de crear la venta.
        /// </summary>
        PendienteAsignarUnidad = 6
    }

    /// <summary>
    /// Origen del stock que el ERP publica/valida hacia Mercado Libre.
    /// Configurable global (MercadoLibreConfiguracion) y por publicación (override).
    /// </summary>
    public enum MercadoLibreOrigenStock
    {
        /// <summary>Producto.StockActual (stock lógico agregado del ERP).</summary>
        StockLogicoProducto = 0,

        /// <summary>Cantidad de ProductoUnidades en estado EnStock (stock físico trazado).</summary>
        StockFisicoDisponible = 1,

        /// <summary>La publicación representa UNA unidad física concreta (ProductoUnidadId): stock 1 o 0.</summary>
        UnidadFisicaEspecifica = 2,

        /// <summary>Stock por depósito/sucursal. NO disponible: el ERP no maneja stock por sucursal.</summary>
        DepositoSucursal = 3
    }

    /// <summary>
    /// Estado de un borrador de publicación creado desde el ERP (Fase F).
    /// </summary>
    public enum MercadoLibreBorradorEstado
    {
        Borrador = 0,
        Validado = 1,
        Publicado = 2,
        Descartado = 3
    }

    /// <summary>
    /// Estado de devolución/reclamo de una orden ML.
    /// El stock NUNCA se reingresa automáticamente: siempre pasa por revisión manual.
    /// </summary>
    public enum MercadoLibreDevolucionEstado
    {
        Ninguna = 0,
        PendienteRevision = 1,
        StockReingresado = 2,
        Danado = 3,
        Garantia = 4,
        Merma = 5,
        NoReingresa = 6
    }

    public enum MercadoLibreClaimTipo
    {
        Reclamo = 0,
        Devolucion = 1,
        Garantia = 2,
        Cancelacion = 3
    }

    public enum MercadoLibreClaimEstado
    {
        PendienteRevision = 0,
        Aprobado = 1,
        Rechazado = 2,
        Resuelto = 3,
        Error = 4
    }

    public enum MercadoLibreClaimAccionStock
    {
        NoReingresar = 0,
        ReingresarStock = 1,
        Danado = 2,
        Garantia = 3,
        Merma = 4
    }

    public enum MercadoLibreClaimAccionEconomica
    {
        SinImpacto = 0,
        DevolucionPendiente = 1,
        DevolucionRegistrada = 2
    }

    /// <summary>
    /// Estado logistico operativo derivado del status/substatus del shipment ML.
    /// No dispara efectos contables ni de stock: solo informa seguimiento.
    /// </summary>
    public enum MercadoLibreShipmentEstadoInterno
    {
        Pendiente = 0,
        ListoParaDespachar = 1,
        Despachado = 2,
        EnCamino = 3,
        Entregado = 4,
        Cancelado = 5,
        Demorado = 6,
        Desconocido = 7
    }

    /// <summary>
    /// Política configurable ante devoluciones detectadas por webhook/claim.
    /// Solo define el estado inicial sugerido; la decisión final es siempre manual.
    /// </summary>
    public enum MercadoLibrePoliticaDevolucion
    {
        PendienteRevision = 0,
        SugerirReingreso = 1,
        SugerirNoReingreso = 2
    }

    /// <summary>
    /// Estado de un lote de cambio masivo de precios ML.
    /// </summary>
    public enum MercadoLibrePriceBatchEstado
    {
        Simulado = 0,
        Aplicado = 1,
        AplicadoParcial = 2,
        Revertido = 3,
        Cancelado = 4
    }

    /// <summary>
    /// Origen del precio nuevo en un lote masivo ML.
    /// </summary>
    public enum MercadoLibrePriceBatchOrigen
    {
        /// <summary>Precio ERP de la lista configurada + ajuste canal + redondeo.</summary>
        DesdePrecioErp = 0,

        /// <summary>Porcentaje directo sobre el precio ML actual (no toca precios internos).</summary>
        PorcentajeSobrePrecioMl = 1
    }

    /// <summary>
    /// Estado interno de una pregunta preventa de Mercado Libre dentro del ERP.
    /// La respuesta es siempre manual: el estado nunca avanza por automatización.
    /// </summary>
    public enum MercadoLibreQuestionEstado
    {
        /// <summary>Pregunta recibida, sin responder.</summary>
        Pendiente = 0,

        /// <summary>Respondida (real o simulada local) desde el ERP.</summary>
        Respondida = 1,

        /// <summary>Cerrada en ML (borrada o deshabilitada) sin requerir acción.</summary>
        Cerrada = 2,

        /// <summary>No se pudo procesar (ver ErrorProcesamiento).</summary>
        Error = 3
    }

    /// <summary>
    /// Estado interno de un mensaje postventa de Mercado Libre dentro del ERP.
    /// </summary>
    public enum MercadoLibreMessageEstado
    {
        /// <summary>Mensaje entrante recibido, sin responder.</summary>
        Recibido = 0,

        /// <summary>Mensaje saliente enviado (real o simulado local) desde el ERP.</summary>
        Enviado = 1,

        /// <summary>No se pudo procesar/enviar (ver ErrorProcesamiento).</summary>
        Error = 2
    }

    /// <summary>
    /// Dirección de un mensaje postventa respecto del vendedor.
    /// </summary>
    public enum MercadoLibreMessageDireccion
    {
        /// <summary>El comprador (u otro) escribió al vendedor.</summary>
        Entrante = 0,

        /// <summary>El vendedor (ERP) respondió al comprador.</summary>
        Saliente = 1
    }
}

(function () {
    'use strict';

    const root = document.querySelector('[data-cotizacion-simulador]');
    if (!root) return;

    const theBury = window.TheBury || {};
    const formatCurrencyBase = theBury.formatCurrency || function (value) {
        return new Intl.NumberFormat('es-AR', {
            style: 'currency',
            currency: 'ARS',
            minimumFractionDigits: 2
        }).format(value || 0);
    };
    // COTIZACION-MOCKUP-01: el mockup muestra los importes pegados al símbolo
    // ("$180.758,90"), igual que el resto del ERP en listados; Intl es-AR intercala un
    // espacio (a veces no separable) tras el "$". Sólo cambia la presentación, no el valor.
    const formatCurrency = function (value) {
        return String(formatCurrencyBase(value)).replace(/^(-?)\$[\s\u00a0\u202f]+/, '$1$');
    };

    const state = {
        productos: [],
        productoSeleccionado: null,
        clienteSeleccionado: null,
        ultimaSimulacion: null,
        opcionSeleccionada: null,
        // Fila completa de la alternativa elegida (opcion + plan): la barra de
        // selección y el drawer necesitan el medio/plan/total, no sólo las claves
        // que viajan al backend en opcionSeleccionada.
        seleccionRow: null,
        planAbiertoKey: null,
        // VENTA-COTIZACION-EXCEPCION-01: "plan objetivo para excepción" — deliberadamente
        // separado de opcionSeleccionada/seleccionRow (§6 del pedido del usuario): un plan de
        // Crédito personal No apto nunca debe volverse una selección válida sólo por haber
        // pedido la excepción; se vuelve real (o se rechaza) recién cuando el backend la
        // autoriza, al convertir (ver continuarConExcepcion). Shape: { key, medioPago, plan,
        // cantidadCuotas, motivo }. Se descarta en cualquier invalidarSimulacion() (cambio de
        // cliente/producto/monto/plan) — nunca sobrevive a un contexto distinto al que se pidió.
        excepcion: null,
        // Modo del botón único del pie del drawer ('elegir' | 'excepcion' | 'bloqueado') — ver
        // openPlanDrawer/el listener de [data-cotizacion-elegir-drawer].
        planDrawerMode: 'elegir',
        // COTIZACION-MIVENTA-01 (POC "Mi Venta"): datos reales de envío guardados desde el
        // modal (mismos campos que VentaEnvio) — null hasta que se guarda el modal al menos
        // una vez. Igual que excepcion, deliberadamente separado de lo que se persiste en
        // Cotizacion.TieneEnvio (sólo bool): la intención puede guardarse, el detalle recién
        // se manda al backend al confirmar la venta (mismo criterio que ya se usó para
        // excepcion — ver [[cotizador-excepcion-documental-estado]]).
        envio: null,
        // COTIZACION-MIVENTA-02: mismo patrón que `envio` — null hasta que se guarda el modal
        // real de facturación (#modal-facturar-config, mismo partial que Venta/Details) al
        // menos una vez. Shape: { tipo, puntoVenta, fechaEmision }. La factura NUNCA se emite
        // acá — sólo se guarda la intención/configuración para el momento de confirmar.
        facturarConfig: null,
        // Cache del último preview de IVA/alícuotas pedido a /conversion/factura-preview (mismo
        // cálculo que usará la Venta real) — evita repetir el fetch si el modal se reabre sin
        // haber cambiado productos/cliente.
        facturaPreview: null,
        facturaPreviewCotizacionId: null,
        // Reentrancia real de "Confirmar Mi Venta" (§DOBLE SUBMIT) — ver continuarConOpcion().
        confirmando: false,
        // COTIZACION-MIVENTA-02: reentrancia del click EXTERNO "Confirmar Mi Venta" (que corre
        // el preflight antes de abrir el modal de confirmación) — distinta de `confirmando`,
        // que guarda el click de aceptar DENTRO del modal (crear/confirmar la Venta).
        verificando: false,
        // COTIZACION-MIVENTA-02: reentrancia de "Continuar con wizard" — camino totalmente
        // separado de Confirmar Mi Venta, con su propia protección de doble-submit.
        continuandoWizard: false,
        bestKey: null,
        pendingDeleteIndex: null,
        cotizacionGuardadaId: null,
        cotizacionGuardadaClienteId: null,
        busy: false,
        // Aptitud crediticia (preview, Crédito personal): cache por clienteId+monto
        // para no repetir la consulta en cada render (ver evaluarAptitudCredito).
        aptitud: null,
        aptitudClienteId: null,
        aptitudMonto: null,
        aptitudToken: 0
    };

    // VENTA-COTIZACION-EXCEPCION-01: mismo gate de permiso que ya usa Venta/Create para
    // renderizar el bloque de excepción documental (@if (User.TienePermiso("ventas",
    // "authorize")) en _VentaWizardForm.cshtml) — ver el mismo data-* en _CotizadorForm.cshtml.
    // El backend (IVentaService.AplicarExcepcionDocumentalSiCorresponde) revalida este permiso
    // igual: esto sólo evita ofrecer una acción que el servidor va a rechazar.
    const puedeExcepcionDocumental = root.dataset.puedeExcepcionDocumental === 'true';
    // COTIZACION-MIVENTA-01: mismo criterio — sólo evita ofrecer "Facturar" a quien el
    // backend igual rechazaría (VentaController.Facturar/ConfirmarYFacturar exigen
    // ventas/invoice); CotizacionConversionService revalida este permiso de nuevo.
    const puedeFacturar = root.dataset.puedeFacturar === 'true';

    const urls = {
        simular: root.dataset.simularUrl || '/api/cotizacion/simular',
        guardar: root.dataset.guardarUrl || '/api/cotizacion/guardar',
        productos: root.dataset.productosUrl || '/Cotizacion/BuscarProductos',
        productoResumen: root.dataset.productoResumenUrl || '/Cotizacion/ProductoResumen',
        clientes: root.dataset.clientesUrl || '/Cotizacion/BuscarClientes',
        convertirBase: root.dataset.convertirBaseUrl || '/api/cotizacion',
        ventaEdit: root.dataset.ventaEditUrl || '/Venta/Edit/',
        ventaDetails: root.dataset.ventaDetailsUrl || '/Venta/Details/',
        ventaFacturar: root.dataset.ventaFacturarUrl || '/Venta/Facturar/',
        aptitudCredito: root.dataset.aptitudCreditoUrl || '/api/cotizacion/aptitud-credito',
        caja: root.dataset.cajaUrl || '/Caja'
    };

    const $ = (selector) => root.querySelector(selector);
    const $$ = (selector) => Array.from(root.querySelectorAll(selector));

    const els = {
        feedback: $('#cotizacion-feedback'),
        productoBuscar: $('#cotizacion-producto-buscar'),
        productosDropdown: $('#cotizacion-productos-dropdown'),
        productoSeleccionado: $('#cotizacion-producto-seleccionado'),
        cantidad: $('#cotizacion-cantidad'),
        agregarProducto: $('#cotizacion-agregar-producto'),
        productoIdManual: $('#cotizacion-producto-id-manual'),
        cantidadManual: $('#cotizacion-cantidad-manual'),
        agregarManual: $('#cotizacion-agregar-manual'),
        productosTbody: $('#cotizacion-productos-tbody'),
        productosVacio: $('#cotizacion-productos-vacio'),
        headerProdCount: $('#header-prod-count'),
        sideTotal: $('[data-side-total]'),
        clienteBuscador: $('#cotizacion-cliente-buscador'),
        clienteVacioHint: $('[data-cliente-vacio]'),
        clienteBuscar: $('#cotizacion-cliente-buscar'),
        clientesDropdown: $('#cotizacion-clientes-dropdown'),
        clienteId: $('#cotizacion-cliente-id'),
        clienteSeleccionado: $('#cotizacion-cliente-seleccionado'),
        clienteNombre: $('#cotizacion-cliente-nombre'),
        clienteDoc: $('#cotizacion-cliente-doc'),
        clienteAvatar: $('#cotizacion-cliente-avatar'),
        aptitudCredito: $('#cotizacion-aptitud-credito'),
        contactoLibre: $('#cotizacion-contacto-libre'),
        limpiarCliente: $('#cotizacion-limpiar-cliente'),
        nombreLibre: $('#cotizacion-nombre-libre'),
        telefonoLibre: $('#cotizacion-telefono-libre'),
        descuentoGralPct: $('#cotizacion-descuento-gral-pct'),
        descuentoGralImporte: $('#cotizacion-descuento-gral-importe'),
        anticipoBloque: $('#cotizacion-anticipo-bloque'),
        incluirCreditoPersonal: $('[data-cotizacion-medio="incluirCreditoPersonal"]'),
        anticipo: $('#cotizacion-anticipo'),
        fechaVencimiento: $('#cotizacion-fecha-vencimiento'),
        observaciones: $('#cotizacion-observaciones'),
        tieneEnvio: $('#cotizacion-tiene-envio'),
        envioResumen: $('#cotizacion-envio-resumen'),
        envioResumenTexto: $('#cotizacion-envio-resumen-texto'),
        envioEditar: $('#cotizacion-envio-editar'),
        envioDestinatario: $('#cotizacion-envio-destinatario'),
        envioDestinatarioError: $('#cotizacion-envio-destinatario-error'),
        envioDomicilio: $('#cotizacion-envio-domicilio'),
        envioDomicilioError: $('#cotizacion-envio-domicilio-error'),
        envioTelefono: $('#cotizacion-envio-telefono'),
        envioLocalidad: $('#cotizacion-envio-localidad'),
        envioProvincia: $('#cotizacion-envio-provincia'),
        envioCp: $('#cotizacion-envio-cp'),
        envioTransportista: $('#cotizacion-envio-transportista'),
        envioCosto: $('#cotizacion-envio-costo'),
        envioFecha: $('#cotizacion-envio-fecha'),
        envioObservaciones: $('#cotizacion-envio-observaciones'),
        envioGuardar: $('#cotizacion-envio-guardar'),
        envioCancelar: $('#cotizacion-envio-cancelar'),
        facturarBloque: $('[data-puede-facturar-bloque]'),
        facturarCheckbox: $('#cotizacion-facturar'),
        facturarResumen: $('#cotizacion-facturar-resumen'),
        facturarResumenTexto: $('#cotizacion-facturar-resumen-texto'),
        facturarEditar: $('#cotizacion-facturar-editar'),
        facturarCancelar: $('#cotizacion-facturar-cancelar'),
        facturarGuardar: $('#cotizacion-facturar-guardar'),
        // Modal de facturación (COTIZACION-MIVENTA-02): campos del partial compartido
        // Views/Venta/_FacturaCamposEmision.cshtml, con idPrefix "cotizacion-factura-".
        facturaSubtotal: $('#cotizacion-factura-subtotal'),
        facturaIva: $('#cotizacion-factura-iva'),
        facturaTotal: $('#cotizacion-factura-total'),
        facturaAlicuotasSection: $('#cotizacion-factura-alicuotas-section'),
        facturaAlicuotasTbody: $('#cotizacion-factura-alicuotas-tbody'),
        facturaTipo: $('#cotizacion-factura-tipo-factura'),
        facturaPuntoVenta: $('#cotizacion-factura-punto-venta'),
        facturaFecha: $('#cotizacion-factura-fecha-emision'),
        confirmarResumen: $('#cotizacion-confirmar-resumen'),
        confirmarAceptarLabel: $('[data-confirmar-aceptar-label]'),
        confirmarAceptar: $('#cotizacion-confirmar-aceptar'),
        confirmarCancelar: $('#cotizacion-confirmar-cancelar'),
        simular: $('#cotizacion-simular'),
        simularLabel: $('[data-simular-label]'),
        simularIcono: $('[data-simular-icon]'),
        guardar: $('#cotizacion-guardar'),
        // COTIZACION-WORKSTATION-01 (§16/§17): "Continuar con esta opción" es la
        // acción primaria del cierre; Guardar queda como secundaria y sólo persiste.
        continuar: $('#cotizacion-continuar'),
        continuarLabel: $('[data-continuar-label]'),
        // COTIZACION-MIVENTA-02: segundo camino explícito — crea la Venta sin confirmar y va
        // siempre al wizard tradicional (Venta/Edit).
        continuarWizard: $('#cotizacion-continuar-wizard'),
        bloqueos: $('#cotizacion-bloqueos'),
        bloqueosTitulo: $('#cotizacion-bloqueos-titulo'),
        bloqueosLista: $('#cotizacion-bloqueos-lista'),
        seleccionResumen: $('#cotizacion-seleccion-resumen'),
        accionesPre: $('#cotizacion-acciones-pre'),
        accionesPost: $('#cotizacion-acciones-post'),
        pasarVenta: $('#cotizacion-pasar-venta'),
        verGuardada: $('#cotizacion-ver-guardada'),
        nuevaCotizacion: $('#cotizacion-nueva'),
        quitarConfirm: $('#cotizacion-quitar-confirm'),
        simularEstado: $('#cotizacion-simular-estado'),
        resultadosVacio: $('#cotizacion-resultados-vacio'),
        resultados: $('#cotizacion-resultados'),
        totalesBar: $('#cotizacion-totales-bar'),
        subtotal: $('#cotizacion-subtotal'),
        descuento: $('#cotizacion-descuento'),
        totalBase: $('#cotizacion-total-base'),
        // VENTA-ENVIO-TOTAL-01: con envío cargado la franja separa Total productos / Envío / Total a cobrar.
        totalBaseLabel: $('#cotizacion-total-base-label'),
        segTotalBase: $('#cotizacion-seg-total-base'),
        segEnvio: $('#cotizacion-seg-envio'),
        envioImporte: $('#cotizacion-envio-importe'),
        segTotalACobrar: $('#cotizacion-seg-total-a-cobrar'),
        totalACobrar: $('#cotizacion-total-a-cobrar'),
        totalesStats: $('#cotizacion-totales-stats'),
        facturaResumenComercial: $('#cotizacion-factura-resumen-comercial'),
        facturaComercialProductos: $('#cotizacion-factura-comercial-productos'),
        facturaComercialEnvio: $('#cotizacion-factura-comercial-envio'),
        facturaComercialTotal: $('#cotizacion-factura-comercial-total'),
        resultadosTbody: $('#cotizacion-resultados-tbody'),
        // plan drawer
        planMedio: $('#plan-medio'),
        planCuotas: $('#plan-cuotas'),
        planTotal: $('#plan-total'),
        planDetalleCuotas: $('#plan-detalle-cuotas'),
        planValorCuota: $('#plan-valor-cuota'),
        planRecargo: $('#plan-recargo'),
        planRecargoLabel: $('#plan-recargo-label'),
        // desglose exclusivo de credito personal
        planCreditoDesglose: $('#plan-credito-desglose'),
        planCreditoFuente: $('#plan-credito-fuente'),
        planCreditoPrecio: $('#plan-credito-precio'),
        planCreditoAnticipo: $('#plan-credito-anticipo'),
        planCreditoSaldo: $('#plan-credito-saldo'),
        planCreditoImporteRecargo: $('#plan-credito-importe-recargo'),
        planCreditoTotalFinanciado: $('#plan-credito-total-financiado'),
        planCreditoVector: $('#plan-credito-vector'),
        // CSR-ML6: metadata del plan + tabla completa por cuota.
        planCreditoCuotasSinRecargo: $('#plan-credito-cuotas-sin-recargo'),
        planCreditoCuotasTablaBody: $('#plan-credito-cuotas-tabla-body'),
        planCreditoCuotasDetalle: $('#plan-credito-cuotas-detalle'),
        planElegibilidad: $('#plan-elegibilidad'),
        planElegirDrawer: $('[data-cotizacion-elegir-drawer]'),
        // VENTA-COTIZACION-EXCEPCION-01: formulario de excepción documental dentro del drawer
        // de Crédito personal (§4 del pedido: "dentro del drawer de detalle de Crédito
        // personal") — mismo copy que Venta/Create (#panel-excepcion-activa), ids propios.
        planExcepcionPanel: $('#plan-excepcion-panel'),
        planExcepcionFormulario: $('#plan-excepcion-formulario'),
        planExcepcionMotivo: $('#plan-excepcion-motivo'),
        planExcepcionMotivoError: $('#plan-excepcion-motivo-error'),
        planExcepcionConfirmar: $('[data-cotizacion-excepcion-confirmar]'),
        planExcepcionCancelar: $('[data-cotizacion-excepcion-cancelar]'),
        planExcepcionResumenWrap: $('#plan-excepcion-resumen-wrap'),
        planExcepcionResumen: $('#plan-excepcion-resumen')
    };

    const show = theBury.show || function (el) { el?.classList.remove('hidden'); };
    const hide = theBury.hide || function (el) { el?.classList.add('hidden'); };
    function setState(s) { if (typeof window.setQuoteState === 'function') window.setQuoteState(s); }

    function esc(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    function parsePositiveInt(value) {
        const number = parseInt(value, 10);
        return Number.isFinite(number) && number > 0 ? number : null;
    }

    function parseNonNegativeDecimal(value) {
        const n = parseFloat(value);
        return Number.isFinite(n) && n >= 0 ? n : null;
    }

    function normalize(value) {
        if (theBury.normalizeText) return theBury.normalizeText(value);
        return String(value || '').toLowerCase();
    }

    // Separadores de miles para la card de cliente ("35.996.614" en vez de
    // "35996614") — sólo si el documento es puramente numérico (DNI); un CUIT u
    // otro formato ya vendría con su propio separador del backend y se deja tal
    // cual (COTIZACION-SIMULAR-REDESIGN-VISUAL-POLISH-01).
    function formatDocumento(value) {
        const raw = String(value ?? '').trim();
        if (!raw) return '-';
        return /^\d+$/.test(raw) ? raw.replace(/\B(?=(\d{3})+(?!\d))/g, '.') : raw;
    }

    // Iniciales para el avatar de la ficha de Cliente (2 letras, sin acentos raros
    // por la fuente monoespaciada — sólo texto, sin lógica de negocio).
    function initialsFrom(nombre, apellido) {
        const a = String(nombre || '').trim().charAt(0);
        const b = String(apellido || '').trim().charAt(0);
        const initials = `${a}${b}`.toUpperCase();
        return initials || '—';
    }

    function debounce(fn, ms) {
        let timer = null;
        return function (...args) {
            window.clearTimeout(timer);
            timer = window.setTimeout(() => fn.apply(this, args), ms);
        };
    }

    async function fetchJson(url, options) {
        const response = await fetch(url, options);
        if (!response.ok) {
            let message = `HTTP ${response.status}`;
            try {
                const data = await response.json();
                message = data.error || data.title || message;
            } catch {
                // Keep HTTP fallback.
            }
            throw new Error(message);
        }
        return response.json();
    }

    function setBusy(value) {
        state.busy = value;
        if (els.simular) {
            els.simular.disabled = value;
            const ico = els.simular.querySelector('.material-symbols-outlined');
            if (ico) ico.textContent = value ? 'progress_activity' : simularIcono();
        }
        if (els.guardar) {
            els.guardar.disabled = value || !state.ultimaSimulacion?.exitoso;
            const ico = els.guardar.querySelector('.material-symbols-outlined');
            if (ico) ico.textContent = value ? 'progress_activity' : 'save';
        }
        // Continuar exige además una alternativa elegida (§16): sin selección no hay
        // "esta opción" con la que seguir. Envío/Facturar pendientes de completar el modal
        // también bloquean sólo el camino de Mi Venta — ver actualizarDisabledContinuar().
        const sinSeleccion = !state.ultimaSimulacion?.exitoso || (!state.seleccionRow?.plan && !state.excepcion);
        if (els.continuar) {
            els.continuar.disabled = value || sinSeleccion || tieneAlgunPendienteDeModal();
            const ico = els.continuar.querySelector('.material-symbols-outlined');
            if (ico) ico.textContent = value ? 'progress_activity' : 'check';
        }
        // COTIZACION-MIVENTA-02: "Continuar con wizard" comparte la misma base (simulación +
        // selección) pero NUNCA exige envío/facturar completos — el wizard tradicional recolecta
        // esos datos paso a paso, por eso existen dos caminos.
        if (els.continuarWizard) {
            els.continuarWizard.disabled = value || sinSeleccion;
            const ico = els.continuarWizard.querySelector('.material-symbols-outlined');
            if (ico) ico.textContent = value ? 'progress_activity' : 'arrow_forward';
        }
    }

    // COTIZACION-MIVENTA-02: Envío activado sin modal guardado, o Facturar activado sin
    // configuración guardada, bloquean "Confirmar Mi Venta" (nunca "Continuar con wizard") —
    // mismo criterio para ambos checkboxes (§PRE-FLIGHT del pedido: nunca dejar llegar a un
    // botón habilitado que falla recién después).
    function tieneAlgunPendienteDeModal() {
        const envioPendiente = !!(els.tieneEnvio?.checked) && !state.envio;
        const facturarPendiente = !!(els.facturarCheckbox?.checked) && !state.facturarConfig;
        return envioPendiente || facturarPendiente;
    }

    function showFeedback(message, tone) {
        if (!els.feedback || !message) return;
        const variant = {
            error: 'cotz-feedback--error',
            warning: 'cotz-feedback--warning',
            ok: 'cotz-feedback--ok',
            info: 'cotz-feedback--info'
        }[tone] || 'cotz-feedback--info';
        const icon = tone === 'error' ? 'error' : tone === 'warning' ? 'warning' : tone === 'ok' ? 'check_circle' : 'info';
        els.feedback.className = `cotz-feedback ${variant} z-[60] max-w-sm`;
        els.feedback.innerHTML = `<span class="material-symbols-outlined" style="font-size:18px">${icon}</span><span>${esc(message)}</span>`;
        clearTimeout(showFeedback._t);
        showFeedback._t = setTimeout(clearFeedback, 4000);
    }

    function clearFeedback() {
        if (!els.feedback) return;
        els.feedback.className = 'hidden';
        els.feedback.replaceChildren();
    }

    /* ---------------------------------------------------------------------
       Productos (cart-rows)
    --------------------------------------------------------------------- */
    // Modo de descuento visible de una línea: el explícito si el usuario ya alternó; si no, "$"
    // sólo cuando la línea trae importe y no porcentaje (p. ej. hidratada desde una cotización).
    function descModoDe(producto) {
        if (producto.descModo === 'pct' || producto.descModo === 'importe') return producto.descModo;
        return (Number(producto.descuentoImporte) > 0 && !(Number(producto.descuentoPorcentaje) > 0)) ? 'importe' : 'pct';
    }

    // Punto "tiene descuento" en el botón del selector (visible sólo cuando ese modo está inactivo).
    function marcarDescuentoCargado(input, modo, valor) {
        input.closest('.dto-control')
            ?.querySelector(`[data-cotizacion-dto-modo="${modo}"]`)
            ?.classList.toggle('has-value', Number(valor) > 0);
    }

    function renderProductos() {
        updateHeaderCounts();
        if (!els.productosTbody) return;
        els.productosTbody.replaceChildren();

        if (state.productos.length === 0) {
            show(els.productosVacio);
            return;
        }

        hide(els.productosVacio);
        state.productos.forEach((producto, index) => {
            const subtotal = Number(producto.precioUnitario) * Number(producto.cantidad);
            const article = document.createElement('article');
            article.className = 'cart-row';
            // COTIZACION-MOCKUP-01: nombre, cantidad, descuento y precio en UNA fila; el
            // Subtotal va debajo con su divisor. El descuento por línea es un solo campo con
            // selector % / $ (mockup): los dos <input> reales (mismos data-cotizacion-desc-*-index)
            // siguen en el DOM y el selector sólo decide cuál se ve — un descuento ya cargado en
            // el otro modo no se pierde al alternar (queda marcado con un punto en su botón).
            const modo = descModoDe(producto);
            const tienePct = Number(producto.descuentoPorcentaje) > 0;
            const tieneImporte = Number(producto.descuentoImporte) > 0;
            article.innerHTML = `
                <div class="cart-row__main">
                    <div class="cart-row__info">
                        <div class="cart-row__nombre truncate-1">${esc(producto.nombre || `Producto ${producto.productoId}`)}</div>
                        <div class="cart-row__meta truncate-1">ID ${producto.productoId}${producto.codigo ? ' · ' + esc(producto.codigo) : ''}</div>
                    </div>
                    <div class="cart-row__field cart-row__field--qty">
                        <span class="cart-row__label">Cant.</span>
                        <div class="qty-step">
                            <button type="button" aria-label="Restar" onclick="stepRow(this,-1)">−</button>
                            <input type="number" min="1" value="${producto.cantidad}" data-cotizacion-cantidad-index="${index}" aria-label="Cantidad">
                            <button type="button" aria-label="Sumar" onclick="stepRow(this,1)">+</button>
                        </div>
                    </div>
                    <div class="cart-row__field">
                        <span class="cart-row__label">Descuento</span>
                        <div class="dto-control">
                            <div class="dto-toggle" role="group" aria-label="Tipo de descuento">
                                <button type="button" data-cotizacion-dto-modo="pct" data-index="${index}" aria-pressed="${modo === 'pct'}" class="${tienePct ? 'has-value' : ''}" aria-label="Descuento en porcentaje">%</button>
                                <button type="button" data-cotizacion-dto-modo="importe" data-index="${index}" aria-pressed="${modo === 'importe'}" class="${tieneImporte ? 'has-value' : ''}" aria-label="Descuento en pesos">$</button>
                            </div>
                            <input type="number" value="${producto.descuentoPorcentaje ?? ''}" min="0" max="100" step="0.01" placeholder="0" data-cotizacion-desc-pct-index="${index}" aria-label="Descuento porcentaje producto" class="mini${modo === 'pct' ? '' : ' hidden'}">
                            <input type="number" value="${producto.descuentoImporte ?? ''}" min="0" step="0.01" placeholder="0" data-cotizacion-desc-importe-index="${index}" aria-label="Descuento importe producto" class="mini${modo === 'importe' ? '' : ' hidden'}">
                        </div>
                    </div>
                    <div class="cart-row__precio total-display">${formatCurrency(producto.precioUnitario)}</div>
                    <button type="button" data-cotizacion-eliminar-index="${index}" class="cart-row__quitar" aria-label="Quitar">
                        <span class="material-symbols-outlined" style="font-size:15px">close</span>
                    </button>
                </div>
                <div class="cart-row-subtotal">
                    <span class="cart-row-subtotal__label">Subtotal</span>
                    <span class="cart-row-subtotal__value total-display">${formatCurrency(subtotal)}</span>
                </div>`;
            els.productosTbody.appendChild(article);
        });
    }

    function previewBase() {
        return state.productos.reduce((acc, p) => acc + Number(p.precioUnitario) * Number(p.cantidad), 0);
    }

    function updateHeaderCounts() {
        const items = state.productos.length;
        if (els.headerProdCount) els.headerProdCount.textContent = String(items);
        // Resumen del carrito en el encabezado de Productos (§4): si hay simulación
        // vigente usamos la base real, si no, el preview bruto de precios de lista.
        const base = state.ultimaSimulacion?.totalBase ?? previewBase();
        if (els.sideTotal) els.sideTotal.textContent = formatCurrency(base);
    }

    function setProductoSeleccionado(producto) {
        state.productoSeleccionado = producto;
        if (els.productoBuscar) {
            els.productoBuscar.value = producto ? `${producto.codigo || producto.id} - ${producto.nombre}` : '';
            els.productoBuscar.setAttribute('aria-expanded', 'false');
        }
        if (els.productoSeleccionado) {
            if (producto) {
                els.productoSeleccionado.classList.remove('italic');
                const marcaSel = [producto.marca, producto.submarca].filter(Boolean).join(' ');
                els.productoSeleccionado.innerHTML = `<span class="material-symbols-outlined text-blue-400 shrink-0" style="font-size:14px">check_circle</span><span class="min-w-0">${esc(producto.nombre)}${marcaSel ? ' · ' + esc(marcaSel) : ''} · ${esc(formatCurrency(producto.precioVenta))} · Stock ${esc(producto.stockActual ?? '-')}</span>`;
            } else {
                els.productoSeleccionado.classList.add('italic');
                els.productoSeleccionado.textContent = 'Sin producto seleccionado';
            }
        }
        hide(els.productosDropdown);
    }

    function invalidarSimulacion() {
        const hadResults = !!state.ultimaSimulacion;
        state.ultimaSimulacion = null;
        state.opcionSeleccionada = null;
        state.seleccionRow = null;
        state.bestKey = null;
        // §13 del pedido: cualquier cambio de contexto (cliente/producto/monto/plan — todo lo
        // que ya invalida acá la simulación vigente) revalida la excepción: nunca se arrastra
        // silenciosamente a un contexto distinto al que se pidió.
        resetExcepcion();
        // COTIZACION-MIVENTA-02: el preview de IVA/alícuotas está atado a la última cotización
        // guardada (mismos productos/precios) — cualquier cambio que invalide la simulación
        // también invalida ese cache; se vuelve a pedir la próxima vez que se abra el modal.
        state.facturaPreview = null;
        state.facturaPreviewCotizacionId = null;
        if (els.guardar) els.guardar.disabled = true;
        resetGuardado();
        resetTotalesBar();
        // Cualquier cambio de contexto invalida un preflight previo — nunca se deja un
        // bloqueo desactualizado visible tras cambiar algo (no se limpia desde
        // renderSeleccionBar(): ese mismo método corre en el `finally` de
        // iniciarConfirmarMiVenta() justo después de mostrar un bloqueo real, y borrarlo ahí
        // sería un auto-borrado inmediato).
        renderBloqueos([]);
        // §9: una única acción primaria contextual. "Actualizar cotización" sólo
        // cuando hubo un resultado que quedó desactualizado por un cambio; en frío
        // (nunca se simuló) sigue siendo "Simular cotización".
        setSimularLabel(hadResults ? 'Actualizar cotización' : 'Simular cotización');
        renderSeleccionBar();
        if (hadResults) setState('pending');
    }

    // El CTA lleva el ícono del mockup: "sync" cuando actualiza una simulación vigente,
    // "calculate" cuando simula por primera vez.
    function simularIcono() {
        return els.simularLabel?.textContent?.trim().startsWith('Actualizar') ? 'sync' : 'calculate';
    }

    function setSimularLabel(texto) {
        if (els.simularLabel) els.simularLabel.textContent = texto;
        const ico = els.simularIcono;
        if (ico && !state.busy) ico.textContent = simularIcono();
    }

    // §16: el cierre del comparador dice qué se eligió y habilita las dos acciones
    // que siguen. Sin selección válida, Continuar queda deshabilitado — la regla real
    // no cambia (guardarYPasarAVenta ya exigía simulación válida): sólo se hace
    // visible en el botón en vez de fallar recién al clickear.
    function renderSeleccionBar() {
        renderTotalesEnvio();
        const row = state.seleccionRow;
        const hayOpcion = !!(row && row.plan);
        // VENTA-COTIZACION-EXCEPCION-01: sin selección normal vigente, un plan objetivo para
        // excepción (§6: nunca reemplaza a una selección válida real) pasa a ofrecer su propia
        // acción primaria — "Continuar con excepción" en vez de "Continuar con esta opción" —
        // para no confundir ambos caminos bajo el mismo copy.
        const excepcion = !hayOpcion ? state.excepcion : null;
        const sinOpcion = !hayOpcion && !excepcion;
        // COTIZACION-MIVENTA-01/02: envío ON o facturar ON sin sus modales guardados exigen
        // completarlos antes de habilitar "Confirmar Mi Venta" (§PRE-FLIGHT: nunca dejar
        // llegar a un botón habilitado que falla recién después) — "Continuar con wizard"
        // nunca exige esto, el wizard tradicional recolecta esos datos paso a paso.
        const envioPendiente = !!(els.tieneEnvio?.checked) && !state.envio;
        const facturarPendiente = !!(els.facturarCheckbox?.checked) && !state.facturarConfig;
        if (els.continuar) els.continuar.disabled = sinOpcion || envioPendiente || facturarPendiente;
        if (els.continuarWizard) els.continuarWizard.disabled = sinOpcion;
        if (els.continuarLabel) {
            els.continuarLabel.textContent = excepcion
                ? 'Continuar con excepción'
                : (state.facturarConfig ? 'Confirmar y facturar' : 'Confirmar Mi Venta');
        }
        if (!els.seleccionResumen) return;

        if ((envioPendiente || facturarPendiente) && (hayOpcion || excepcion)) {
            const falta = [envioPendiente && 'el envío', facturarPendiente && 'la facturación'].filter(Boolean).join(' y ');
            els.seleccionResumen.innerHTML = `
                <span class="seleccion-resumen__label">Falta completar ${esc(falta)}</span>
                <span class="seleccion-resumen__valor">Guardá la configuración pendiente para poder confirmar la venta. "Continuar con wizard" sigue disponible.</span>`;
            return;
        }

        if (!hayOpcion) {
            if (excepcion) {
                els.seleccionResumen.innerHTML = `
                    <span class="seleccion-resumen__label">Excepción solicitada</span>
                    <span class="seleccion-resumen__valor">Crédito personal · pendiente de autorización al continuar · Motivo: ${esc(excepcion.motivo)}</span>`;
                return;
            }
            els.seleccionResumen.innerHTML = `
                <span class="seleccion-resumen__label">Sin opción elegida</span>
                <span class="seleccion-resumen__valor">Simulá y elegí una alternativa para continuar.</span>`;
            return;
        }

        // Prioridad absoluta (auditoría UX del usuario, 2026-09-16): antes este resumen
        // repetía "Medio · N cuotas · Total" sin decir cuánto vale cada cuota ni el
        // recargo — para entenderlo había que volver a mirar la tabla. Ahora trae los
        // mismos 4 datos que sostienen la decisión (medio/marca, cuotas y su valor,
        // total, recargo) sin abrir nada más.
        const nombre = nombreParaResumen(row);
        const cuotasTxt = cuotasConValorTexto(row.plan);
        const r = recargoValor(row.plan);
        const recargoTxt = `${r > 0 ? '+' : ''}${pct(r)}`;
        // Crédito personal con autorización pendiente: el resumen lo dice acá también
        // (§16) — el botón sigue habilitado porque Venta SÍ deja continuar pidiendo
        // autorización de supervisor; NoApto es el único caso que Venta rechaza.
        const tone = esCreditoPersonalMedio(row.opcion.medioPago) ? aptitudTone(state.aptitud) : null;
        const nota = tone === 'requiere-autorizacion'
            ? ` <span class="rmedio-aptitud rmedio-aptitud--requiere-autorizacion">· Requiere autorización</span>`
            : tone === 'no-apto'
                ? ` <span class="rmedio-aptitud rmedio-aptitud--no-apto">· Cliente no apto</span>`
                : '';
        // VENTA-ENVIO-TOTAL-01 / COTIZACION-MOCKUP-01: el total de la opción (que ya incluye el
        // recargo del plan) y el envío (sin recargo) se muestran por separado y se suman en TOTAL A
        // COBRAR; sin envío guardado el envío es $0,00 y el total a cobrar es el de la opción.
        const envioSel = importeEnvioActual();
        els.seleccionResumen.innerHTML = `
            <span class="seleccion-resumen__label seleccion-resumen__label--ok">Opción seleccionada</span>
            <span class="seleccion-resumen__valor"><span class="cap-first">${esc(nombre)}</span> · ${esc(cuotasTxt)} · Total <span class="total-display">${formatCurrency(row.plan.total)}</span> · <span class="${r > 0 ? recargoClass(r) : ''}">${esc(recargoTxt)}</span> · Envío <span class="total-display">${formatCurrency(envioSel)}</span> · <strong>Total a cobrar <span id="cotizacion-seleccion-total-a-cobrar" class="total-display">${formatCurrency(Number(row.plan.total) + envioSel)}</span></strong>${nota}</span>`;
    }

    // Para tarjetas, `medioLabel` sólo da el nombre genérico del medio ("Tarjeta
    // crédito"): la marca real (Visa, Mastercard...) viaja en `plan.plan`, que
    // CotizacionPagoCalculator arma como "<marca> · <cuotas>". Se reusa el mismo
    // separador para extraer sólo la marca acá, sin duplicar esa lista en el front.
    function nombreParaResumen(row) {
        const medio = medioLabel(row.opcion.medioPago, row.opcion.nombreMedioPago);
        if (esTarjetaMedio(row.opcion.medioPago) && row.plan.plan) {
            const marca = row.plan.plan.split(' · ')[0].trim();
            if (marca) return marca;
        }
        return medio;
    }

    function cuotasConValorTexto(plan) {
        return Number(plan.cantidadCuotas) > 1
            ? `${plan.cantidadCuotas} cuotas de ${formatCurrency(plan.valorCuota)}`
            : '1 pago';
    }

    // VENTA-ENVIO-TOTAL-01: importe de envío vigente (0 si no hay envío guardado o no tiene costo;
    // un envío nunca resta). El backend normaliza igual (VentaMontos.NormalizarImporteEnvio) y es
    // la fuente persistida: Cotizacion.CostoEnvio → VentaEnvio.CostoEnvio → Venta.TotalACobrar.
    function importeEnvioActual() {
        const costo = Number(state.envio?.costoEnvio);
        return els.tieneEnvio?.checked && state.envio && Number.isFinite(costo) && costo > 0 ? costo : 0;
    }

    function hayEnvioGuardado() {
        return !!(els.tieneEnvio?.checked && state.envio);
    }

    // Franja de totales (COTIZACION-MOCKUP-01): SIEMPRE los cinco datos del mockup —
    // Subtotal · Descuento · Total productos · Envío · TOTAL A COBRAR. Sin envío guardado, Envío es
    // $0,00 y Total a cobrar coincide con Total productos. El total a cobrar de la franja es antes
    // de recargos/ajustes del medio de pago elegido (el envío no los recibe); con una opción elegida,
    // el resumen de selección muestra el total a cobrar final. Sin simulación vigente quedan en "—".
    function renderTotalesEnvio() {
        if (!state.ultimaSimulacion) {
            if (els.envioImporte) els.envioImporte.textContent = '—';
            if (els.totalACobrar) els.totalACobrar.textContent = '—';
            return;
        }

        const base = Number(state.ultimaSimulacion.totalBase) || 0;
        const envio = importeEnvioActual();
        if (els.envioImporte) els.envioImporte.textContent = formatCurrency(envio);
        if (els.totalACobrar) els.totalACobrar.textContent = formatCurrency(base + envio);
    }

    // Prioridad 2 (auditoría en vivo, 2026-09-15): vuelve la franja Subtotal/
    // Descuento/Total base a su estado neutral ("—", sin acento) cada vez que la
    // simulación vigente deja de ser válida — antes quedaba con el último valor
    // simulado (o "$0,00" inicial) aunque Productos ya mostrara un subtotal
    // distinto. No cambia ningún cálculo, sólo la representación.
    function resetTotalesBar() {
        if (els.subtotal) els.subtotal.textContent = '—';
        if (els.descuento) {
            els.descuento.textContent = '—';
            els.descuento.classList.remove('text-emerald-400');
            els.descuento.classList.add('text-white');
        }
        if (els.totalBase) els.totalBase.textContent = '—';
        els.totalesBar?.classList.add('is-pendiente');
        renderTotalesEnvio();
    }

    // Vuelve a las acciones de pre-guardado (Simular/Guardar) y descarta el
    // vínculo con la cotización persistida: tras editar, hay que re-simular y
    // re-guardar para que "Pasar a venta" refleje los cambios.
    function resetGuardado() {
        if (state.cotizacionGuardadaId === null) return;
        state.cotizacionGuardadaId = null;
        state.cotizacionGuardadaClienteId = null;
        hide(els.accionesPost);
        show(els.accionesPre);
    }

    function mostrarAccionesPostGuardado(data) {
        if (els.verGuardada && data?.detalleUrl) els.verGuardada.href = data.detalleUrl;
        hide(els.accionesPre);
        show(els.accionesPost);
    }

    async function agregarProducto(producto, cantidad) {
        if (!producto?.id) {
            showFeedback('Selecciona un producto valido.', 'warning');
            return;
        }

        const qty = parsePositiveInt(cantidad);
        if (!qty) {
            showFeedback('La cantidad debe ser mayor a cero.', 'warning');
            return;
        }

        const existing = state.productos.find(p => p.productoId === Number(producto.id));
        if (existing) {
            existing.cantidad += qty;
        } else {
            state.productos.push({
                productoId: Number(producto.id),
                codigo: producto.codigo || '',
                nombre: producto.nombre || `Producto ${producto.id}`,
                cantidad: qty,
                precioUnitario: Number(producto.precioVenta) || 0,
                descuentoPorcentaje: null,
                descuentoImporte: null
            });
        }

        invalidarSimulacion();
        setProductoSeleccionado(null);
        if (els.cantidad) els.cantidad.value = '1';
        clearFeedback();
        renderProductos();
    }

    async function agregarProductoManual() {
        const id = parsePositiveInt(els.productoIdManual?.value);
        const qty = parsePositiveInt(els.cantidadManual?.value);
        if (!id || !qty) {
            showFeedback('Ingresa ProductoId y cantidad validos.', 'warning');
            return;
        }

        try {
            const data = await fetchJson(`${urls.productoResumen}?id=${encodeURIComponent(id)}`);
            await agregarProducto({
                id: data.id,
                codigo: data.codigo,
                nombre: data.nombre,
                precioVenta: data.precioVenta,
                stockActual: data.stockActual
            }, qty);
            if (els.productoIdManual) els.productoIdManual.value = '';
            if (els.cantidadManual) els.cantidadManual.value = '1';
        } catch (error) {
            showFeedback(error.message || 'No se pudo obtener el producto.', 'error');
        }
    }

    async function buscarProductos() {
        const term = els.productoBuscar?.value?.trim() || '';
        if (term.length < 2) {
            hide(els.productosDropdown);
            return;
        }

        try {
            const data = await fetchJson(`${urls.productos}?term=${encodeURIComponent(term)}&take=12`);
            renderDropdownProductos(data || []);
        } catch {
            renderDropdownProductos([], 'No se pudieron buscar productos.');
        }
    }

    // Semáforo de stock del buscador: mismo criterio que la venta y que Dashboard
    // (sin stock en rojo, en o por debajo del mínimo en ámbar). Acá importa más que
    // en la venta porque el cotizador lista también productos sin stock.
    const UMBRAL_STOCK_BAJO_SIN_MINIMO = 3;

    function calcularEstadoStock(producto) {
        const disponible = producto.requiereNumeroSerie
            ? Number(producto.unidadesEnStock ?? 0)
            : Number(producto.stockActual ?? 0);

        if (!(disponible > 0)) return { estado: 'sin-stock', etiqueta: 'Sin stock' };

        const minimo = Number(producto.stockMinimo ?? 0);
        const umbral = minimo > 0 ? minimo : UMBRAL_STOCK_BAJO_SIN_MINIMO;
        if (disponible <= umbral) return { estado: 'stock-bajo', etiqueta: `Stock bajo (${disponible})` };

        return { estado: 'ok', etiqueta: '' };
    }

    function renderDropdownProductos(productos, emptyMessage) {
        if (!els.productosDropdown) return;
        els.productosDropdown.replaceChildren();

        if (!productos.length) {
            const item = document.createElement('div');
            item.className = 'dropdown-item text-sm text-slate-500';
            item.textContent = emptyMessage || 'Sin resultados.';
            els.productosDropdown.appendChild(item);
            show(els.productosDropdown);
            els.productoBuscar?.setAttribute('aria-expanded', 'true');
            return;
        }

        productos
            .sort((a, b) => Number(b.codigoExacto) - Number(a.codigoExacto) || normalize(a.nombre).localeCompare(normalize(b.nombre)))
            .forEach(producto => {
                const marcaTexto = [producto.marca, producto.submarca].filter(Boolean).join(' ');
                const estadoStock = calcularEstadoStock(producto);
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'dropdown-item cotz-producto-opcion flex w-full items-start justify-between gap-3 text-left';
                button.dataset.estadoStock = estadoStock.estado;
                button.innerHTML = `
                    <span class="min-w-0">
                        <span class="block text-sm font-medium text-white truncate-1">${esc(producto.nombre)}</span>
                        <span class="block text-[11px] text-slate-500">${esc(producto.codigo || `ID ${producto.id}`)} · ${esc(marcaTexto || 'Sin marca')} · ${esc(producto.categoria || 'Sin categoría')}</span>
                        ${estadoStock.etiqueta ? `<span class="cotz-producto-opcion__badge">${esc(estadoStock.etiqueta)}</span>` : ''}
                        ${producto.descripcion ? `<span class="block text-[11px] text-slate-400 truncate-1">${esc(producto.descripcion)}</span>` : ''}
                        ${producto.caracteristicasResumen ? `<span class="block text-[11px] text-slate-500 truncate-1">${esc(producto.caracteristicasResumen)}</span>` : ''}
                    </span>
                    <span class="shrink-0 text-right text-xs font-semibold text-slate-300 total-display">${formatCurrency(producto.precioVenta)}</span>`;
                button.addEventListener('click', () => setProductoSeleccionado(producto));
                els.productosDropdown.appendChild(button);
            });

        show(els.productosDropdown);
        els.productoBuscar?.setAttribute('aria-expanded', 'true');
    }

    // El cliente afecta el resultado real (RequiereCliente, Crédito personal): un
    // cambio de cliente invalida la simulación vigente igual que producto/
    // descuento/anticipo/medios (item 30 del lote).
    function setCliente(cliente) {
        state.clienteSeleccionado = cliente;
        if (els.clienteId) els.clienteId.value = cliente?.id || '';
        if (els.clienteBuscar) {
            els.clienteBuscar.value = cliente?.display || '';
            els.clienteBuscar.setAttribute('aria-expanded', 'false');
        }
        hide(els.clientesDropdown);
        invalidarSimulacion();

        // COTIZACION-MIVENTA-01 (§CAMBIOS DE CONTEXTO del pedido): los datos de envío
        // guardados están atados al cliente que estaba seleccionado (destinatario/domicilio
        // salían de ahí) — un cambio de cliente nunca debe arrastrarlos silenciosamente a
        // otro contexto. El checkbox vuelve a OFF; si el usuario todavía quiere envío,
        // reabre el modal con los datos del nuevo cliente.
        if (state.envio) {
            state.envio = null;
            if (els.tieneEnvio) els.tieneEnvio.checked = false;
            renderResumenEnvio();
        }

        if (!cliente) {
            hide(els.clienteSeleccionado);
            // "Cambiar" sólo tiene sentido con un cliente elegido (§5): con el buscador
            // visible sería una segunda forma de hacer lo mismo.
            hide(els.limpiarCliente);
            show(els.clienteBuscador);
            show(els.clienteVacioHint);
            show(els.contactoLibre);
            evaluarAptitudCredito();
            return;
        }

        // Nombre sin el sufijo "- DNI: ..." que ya trae cliente.display (pensado para
        // desambiguar en el buscador/dropdown, no para la card dedicada de abajo, que
        // ya muestra el documento en su propia línea): antes el DNI aparecía dos
        // veces y era lo que más empujaba el truncamiento del nombre a "miñana,
        // alan - DNI: 35…" (COTIZACION-SIMULAR-REDESIGN-VISUAL-POLISH-01).
        if (els.clienteNombre) {
            els.clienteNombre.textContent = (cliente.apellido && cliente.nombre)
                ? `${cliente.apellido}, ${cliente.nombre}`
                : (cliente.display || `${cliente.nombre} ${cliente.apellido}`);
        }
        if (els.clienteDoc) els.clienteDoc.textContent = `${cliente.tipoDocumento || 'DNI'} ${formatDocumento(cliente.numeroDocumento)}`;
        if (els.clienteAvatar) els.clienteAvatar.textContent = initialsFrom(cliente.nombre, cliente.apellido);
        // Con cliente seleccionado la card de abajo es la única representación
        // (antes nombre/DNI se repetían en el buscador y en la card) — item 23.
        hide(els.clienteBuscador);
        hide(els.clienteVacioHint);
        hide(els.contactoLibre);
        show(els.clienteSeleccionado);
        show(els.limpiarCliente);
        evaluarAptitudCredito();
    }

    async function buscarClientes() {
        const term = els.clienteBuscar?.value?.trim() || '';
        if (term.length < 2) {
            hide(els.clientesDropdown);
            return;
        }

        try {
            const data = await fetchJson(`${urls.clientes}?term=${encodeURIComponent(term)}&take=10`);
            renderDropdownClientes(data || []);
        } catch {
            renderDropdownClientes([], 'No se pudieron buscar clientes.');
        }
    }

    function renderDropdownClientes(clientes, emptyMessage) {
        if (!els.clientesDropdown) return;
        els.clientesDropdown.replaceChildren();

        if (!clientes.length) {
            const item = document.createElement('div');
            item.className = 'dropdown-item text-sm text-slate-500';
            item.textContent = emptyMessage || 'Sin resultados.';
            els.clientesDropdown.appendChild(item);
            show(els.clientesDropdown);
            els.clienteBuscar?.setAttribute('aria-expanded', 'true');
            return;
        }

        clientes.forEach(cliente => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'dropdown-item block w-full text-left';
            // Nombre + documento en líneas separadas (mismo criterio que la ficha de
            // Cliente ya seleccionado, item 10 del rework): cliente.display trae
            // "Apellido, Nombre - DNI: ..." pensado para exportar/listar, no para una
            // fila de dropdown que ya separa el documento en su propia línea.
            const nombreDropdown = (cliente.nombre || cliente.apellido)
                ? `${cliente.nombre} ${cliente.apellido}`.trim()
                : (cliente.display || 'Cliente');
            button.innerHTML = `
                <span class="block text-sm font-medium text-white truncate-1">${esc(nombreDropdown)}</span>
                <span class="block text-[11px] text-slate-500 font-mono">${esc(cliente.tipoDocumento || 'DNI')} ${esc(formatDocumento(cliente.numeroDocumento))}</span>`;
            button.addEventListener('click', () => setCliente(cliente));
            els.clientesDropdown.appendChild(button);
        });

        show(els.clientesDropdown);
        els.clienteBuscar?.setAttribute('aria-expanded', 'true');
    }

    function buildRequest() {
        const descPct = parseNonNegativeDecimal(els.descuentoGralPct?.value);
        const descImporte = parseNonNegativeDecimal(els.descuentoGralImporte?.value);

        const anticipo = parseNonNegativeDecimal(els.anticipo?.value);

        const request = {
            clienteId: state.clienteSeleccionado?.id || null,
            nombreClienteLibre: els.nombreLibre?.value?.trim() || null,
            descuentoGeneralPorcentaje: descPct !== null && descPct > 0 ? descPct : null,
            descuentoGeneralImporte: descImporte !== null && descImporte > 0 ? descImporte : null,
            // Solo Credito personal lo consume (saldo = total - anticipo, antes del recargo).
            // El servidor vuelve a validar 0 <= anticipo <= total: nunca se calcula acá.
            anticipo: anticipo !== null ? anticipo : 0,
            productos: state.productos.map(p => ({
                productoId: p.productoId,
                cantidad: p.cantidad,
                descuentoPorcentaje: (p.descuentoPorcentaje !== null && p.descuentoPorcentaje > 0) ? p.descuentoPorcentaje : null,
                descuentoImporte: (p.descuentoImporte !== null && p.descuentoImporte > 0) ? p.descuentoImporte : null
            }))
        };

        $$('[data-cotizacion-medio]').forEach(input => {
            request[input.dataset.cotizacionMedio] = input.checked;
        });

        return request;
    }

    async function simular() {
        clearFeedback();
        if (state.productos.length === 0) {
            showFeedback('Agrega al menos un producto para simular.', 'warning');
            return;
        }

        setBusy(true);
        try {
            const data = await fetchJson(urls.simular, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                },
                body: JSON.stringify(buildRequest())
            });
            state.ultimaSimulacion = data;
            state.opcionSeleccionada = null;
            renderResultado(data);
            // El monto financiable recién es real después de simular (antes es sólo un
            // preview bruto de precios de lista) — re-evalúa aptitud con el total base
            // definitivo cuando ya hay cliente seleccionado (no dispara si no lo hay).
            evaluarAptitudCredito();
            if (data.exitoso === false) {
                setState('error');
                showFeedback('La simulacion devolvio observaciones que requieren revision.', 'warning');
            } else {
                setState('simulated');
                showFeedback('Cotizacion simulada correctamente.', 'ok');
            }
        } catch (error) {
            setState('error');
            showFeedback(error.message || 'No se pudo simular la cotizacion.', 'error');
        } finally {
            setBusy(false);
        }
    }

    /* ---------------------------------------------------------------------
       COTIZACION-MIVENTA-01 (POC "Mi Venta"): Envío a domicilio (modal real) y
       Facturar. Mismos campos reales de VentaEnvio/VentaEnvioViewModel que ya usa el
       paso Envío de Venta/Create — nada inventado. El detalle sólo viaja al backend
       al confirmar la venta (CotizacionConversionRequest.Envio*), igual que motivo de
       excepción: la Cotización sólo persiste la intención (TieneEnvio bool).
    --------------------------------------------------------------------- */
    const ENVIO_CAMPOS = ['envioDestinatario', 'envioDomicilio', 'envioTelefono', 'envioLocalidad',
        'envioProvincia', 'envioCp', 'envioTransportista', 'envioCosto', 'envioFecha', 'envioObservaciones'];

    function renderResumenEnvio() {
        if (state.envio) {
            show(els.envioResumen);
            if (els.envioResumenTexto) {
                els.envioResumenTexto.textContent = `${state.envio.destinatario} · ${state.envio.domicilio}`;
            }
        } else {
            hide(els.envioResumen);
        }
    }

    // Sólo completa lo que el operador todavía no tocó (mismo criterio que
    // venta-envio.js en Venta/Create: no pisa lo ya tipeado).
    function precargarEnvioDesdeCliente() {
        const c = state.clienteSeleccionado;
        if (!c) return;
        if (els.envioDestinatario && !els.envioDestinatario.value.trim()) {
            const nombre = `${c.apellido || ''}, ${c.nombre || ''}`.replace(/^,\s*/, '').replace(/,\s*$/, '').trim();
            els.envioDestinatario.value = nombre || c.display || '';
        }
        if (els.envioDomicilio && !els.envioDomicilio.value.trim()) els.envioDomicilio.value = c.domicilio || '';
        if (els.envioTelefono && !els.envioTelefono.value.trim()) els.envioTelefono.value = c.telefono || '';
        if (els.envioLocalidad && !els.envioLocalidad.value.trim()) els.envioLocalidad.value = c.localidad || '';
        if (els.envioProvincia && !els.envioProvincia.value.trim()) els.envioProvincia.value = c.provincia || '';
        if (els.envioCp && !els.envioCp.value.trim()) els.envioCp.value = c.codigoPostal || '';
    }

    function abrirModalEnvio() {
        hide(els.envioDestinatarioError);
        hide(els.envioDomicilioError);
        if (state.envio) {
            if (els.envioDestinatario) els.envioDestinatario.value = state.envio.destinatario || '';
            if (els.envioDomicilio) els.envioDomicilio.value = state.envio.domicilio || '';
            if (els.envioTelefono) els.envioTelefono.value = state.envio.telefono || '';
            if (els.envioLocalidad) els.envioLocalidad.value = state.envio.localidad || '';
            if (els.envioProvincia) els.envioProvincia.value = state.envio.provincia || '';
            if (els.envioCp) els.envioCp.value = state.envio.codigoPostal || '';
            if (els.envioTransportista) els.envioTransportista.value = state.envio.transportista || '';
            if (els.envioCosto) els.envioCosto.value = state.envio.costoEnvio ?? '';
            if (els.envioFecha) els.envioFecha.value = state.envio.fechaProgramada || '';
            if (els.envioObservaciones) els.envioObservaciones.value = state.envio.observaciones || '';
        } else {
            ENVIO_CAMPOS.forEach(k => { if (els[k]) els[k].value = ''; });
            precargarEnvioDesdeCliente();
        }
        window.openModal?.('modal-envio');
    }

    function guardarModalEnvio() {
        const destinatario = els.envioDestinatario?.value.trim() || '';
        const domicilio = els.envioDomicilio?.value.trim() || '';
        let valido = true;
        if (!destinatario) {
            if (els.envioDestinatarioError) { els.envioDestinatarioError.textContent = 'El destinatario es obligatorio.'; show(els.envioDestinatarioError); }
            valido = false;
        } else {
            hide(els.envioDestinatarioError);
        }
        if (!domicilio) {
            if (els.envioDomicilioError) { els.envioDomicilioError.textContent = 'El domicilio es obligatorio.'; show(els.envioDomicilioError); }
            valido = false;
        } else {
            hide(els.envioDomicilioError);
        }
        if (!valido) return;

        state.envio = {
            destinatario,
            domicilio,
            telefono: els.envioTelefono?.value.trim() || null,
            localidad: els.envioLocalidad?.value.trim() || null,
            provincia: els.envioProvincia?.value.trim() || null,
            codigoPostal: els.envioCp?.value.trim() || null,
            transportista: els.envioTransportista?.value.trim() || null,
            costoEnvio: els.envioCosto?.value ? parseFloat(els.envioCosto.value) : null,
            fechaProgramada: els.envioFecha?.value || null,
            observaciones: els.envioObservaciones?.value.trim() || null
        };
        if (els.tieneEnvio) els.tieneEnvio.checked = true;
        renderResumenEnvio();
        window.closeModal?.('modal-envio');
        renderSeleccionBar();
    }

    // Si cancela sin haber guardado nunca una configuración válida, el checkbox
    // vuelve a OFF (comportamiento pedido explícitamente) — con una ya guardada,
    // cancelar sólo descarta la edición en curso.
    function cancelarModalEnvio() {
        window.closeModal?.('modal-envio');
        if (!state.envio && els.tieneEnvio) els.tieneEnvio.checked = false;
    }

    /* ---------------------------------------------------------------------
       COTIZACION-MIVENTA-02: "Facturar al confirmar" — mismo patrón que Envío
       (checkbox → modal real → guardar config → resumen compacto → Editar), pero el
       modal reutiliza EXACTAMENTE el partial de Venta/Details (_FacturaCamposEmision) y
       el preview de Subtotal/IVA/alícuotas se pide al backend (mismo cálculo que
       ConvertirAVentaAsync) — nunca se recalcula IVA en este archivo. La factura NUNCA
       se emite acá: sólo se guarda la intención (state.facturarConfig) hasta que
       "Confirmar Mi Venta"/"Confirmar y facturar" la use al convertir.
    --------------------------------------------------------------------- */
    function renderResumenFacturar() {
        if (state.facturarConfig) {
            show(els.facturarResumen);
            if (els.facturarResumenTexto) {
                const partes = [`Factura ${state.facturarConfig.tipo}`];
                if (state.facturarConfig.puntoVenta) partes.push(`PV ${state.facturarConfig.puntoVenta}`);
                els.facturarResumenTexto.textContent = partes.join(' · ');
            }
        } else {
            hide(els.facturarResumen);
        }
    }

    function renderPreviewFactura(preview) {
        if (els.facturaSubtotal) els.facturaSubtotal.textContent = formatCurrency(preview.subtotal);
        if (els.facturaIva) els.facturaIva.textContent = formatCurrency(preview.iva);
        if (els.facturaTotal) els.facturaTotal.textContent = formatCurrency(preview.total);
        // VENTA-ENVIO-TOTAL-01: si hay envío, el modal explica que se cobra pero no integra el comprobante.
        const envioFactura = importeEnvioActual();
        els.facturaResumenComercial?.classList.toggle('hidden', !(envioFactura > 0));
        if (envioFactura > 0) {
            if (els.facturaComercialProductos) els.facturaComercialProductos.textContent = formatCurrency(preview.total);
            if (els.facturaComercialEnvio) els.facturaComercialEnvio.textContent = formatCurrency(envioFactura);
            if (els.facturaComercialTotal) els.facturaComercialTotal.textContent = formatCurrency(Number(preview.total) + envioFactura);
        }
        const alicuotas = preview.resumenAlicuotas || [];
        if (els.facturaAlicuotasSection) els.facturaAlicuotasSection.classList.toggle('hidden', alicuotas.length === 0);
        if (els.facturaAlicuotasTbody) {
            els.facturaAlicuotasTbody.replaceChildren();
            alicuotas.forEach(item => {
                const tr = document.createElement('tr');
                tr.className = 'text-slate-300';
                tr.innerHTML = `
                    <td class="py-2 pr-3">
                        <p class="font-bold text-white">${esc(item.alicuotaIVANombre)}</p>
                        <p class="text-[10px] text-slate-500">${esc(Number(item.porcentajeIVA).toFixed(2))}%</p>
                    </td>
                    <td class="px-2 py-2 text-right font-semibold">${formatCurrency(item.baseImponible)}</td>
                    <td class="px-2 py-2 text-right font-semibold">${formatCurrency(item.iva)}</td>
                    <td class="py-2 pl-3 text-right font-black text-white">${formatCurrency(item.total)}</td>`;
                els.facturaAlicuotasTbody.appendChild(tr);
            });
        }
    }

    // El preview de IVA/alícuotas (GET /conversion/factura-preview) necesita una cotización
    // persistida con los mismos snapshots que usará la Venta real — se guarda primero si
    // todavía no existe (igual criterio que ya usa "Confirmar Mi Venta" antes de convertir).
    async function abrirModalFacturar() {
        clearFeedback();
        if (!state.cotizacionGuardadaId) {
            const data = await guardarCotizacion({ silencioso: true });
            if (!data) {
                if (els.facturarCheckbox) els.facturarCheckbox.checked = false;
                return;
            }
        }

        if (els.facturaTipo) els.facturaTipo.value = state.facturarConfig?.tipo || 'B';
        if (els.facturaPuntoVenta) els.facturaPuntoVenta.value = state.facturarConfig?.puntoVenta || '';
        if (els.facturaFecha) els.facturaFecha.value = state.facturarConfig?.fechaEmision || new Date().toISOString().slice(0, 10);

        if (state.facturaPreview && state.facturaPreviewCotizacionId === state.cotizacionGuardadaId) {
            renderPreviewFactura(state.facturaPreview);
        } else {
            try {
                const preview = await fetchJson(`${urls.convertirBase}/${state.cotizacionGuardadaId}/conversion/factura-preview`);
                state.facturaPreview = preview;
                state.facturaPreviewCotizacionId = state.cotizacionGuardadaId;
                renderPreviewFactura(preview);
            } catch (error) {
                showFeedback(error.message || 'No se pudo calcular el preview de facturación.', 'error');
            }
        }

        window.openModal?.('modal-facturar-config');
    }

    function guardarModalFacturar() {
        state.facturarConfig = {
            tipo: els.facturaTipo?.value || 'B',
            puntoVenta: els.facturaPuntoVenta?.value.trim() || null,
            fechaEmision: els.facturaFecha?.value || new Date().toISOString().slice(0, 10)
        };
        if (els.facturarCheckbox) els.facturarCheckbox.checked = true;
        renderResumenFacturar();
        window.closeModal?.('modal-facturar-config');
        renderSeleccionBar();
    }

    // Mismo criterio que Envío: sin configuración guardada nunca, cancelar apaga el checkbox;
    // con una ya guardada, cancelar sólo descarta la edición en curso (§14 del pedido).
    function cancelarModalFacturar() {
        window.closeModal?.('modal-facturar-config');
        if (!state.facturarConfig && els.facturarCheckbox) els.facturarCheckbox.checked = false;
    }

    /* ---------------------------------------------------------------------
       COTIZACION-MIVENTA-02: bloque de bloqueo del preflight (§4/§NUEVA REGLA
       FUNDAMENTAL del pedido) — "Confirmar Mi Venta" nunca crea la Venta a ciegas ni
       navega sola al wizard cuando ya sabe que no puede terminar. Reutilizado también
       para los dos casos residuales post-creación (rarísimos tras un preflight Listo,
       pero posibles por una condición de carrera: caja cerrada entre el preflight y la
       conversión, o falla de facturación después de confirmar) — mismo panel, título y
       acciones distintas, nunca un redirect automático.
    --------------------------------------------------------------------- */
    const ACCIONES_BLOQUEO = {
        'sin_caja': () => window.open(urls.caja, '_blank', 'noopener'),
        'credito_personal_requiere_wizard': () => continuarConWizard()
    };

    function renderBloqueos(bloqueos, titulo) {
        if (els.bloqueosTitulo) els.bloqueosTitulo.textContent = titulo || 'No se puede confirmar todavía';
        if (!els.bloqueosLista) return;
        els.bloqueosLista.replaceChildren();

        if (!bloqueos || bloqueos.length === 0) {
            hide(els.bloqueos);
            return;
        }

        bloqueos.forEach(b => {
            const li = document.createElement('li');
            li.className = 'cotz-bloqueos__item';
            const span = document.createElement('span');
            span.textContent = b.mensaje;
            li.appendChild(span);
            if (b.accionSugerida) {
                const btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'btn btn-soft btn-xs';
                btn.textContent = b.accionSugerida;
                btn.addEventListener('click', () => (ACCIONES_BLOQUEO[b.codigo] || (() => {}))());
                li.appendChild(btn);
            }
            els.bloqueosLista.appendChild(li);
        });
        show(els.bloqueos);
    }

    // Renderiza el caso residual "Venta creada pero no confirmada" / "confirmada pero sin
    // facturar" (condición de carrera post-preflight) reutilizando el mismo panel, con
    // enlaces reales en vez de botones (nunca navega sola).
    function renderResultadoParcial(titulo, mensaje, acciones) {
        if (els.bloqueosTitulo) els.bloqueosTitulo.textContent = titulo;
        if (!els.bloqueosLista) return;
        els.bloqueosLista.replaceChildren();

        const li = document.createElement('li');
        li.className = 'cotz-bloqueos__item';
        const span = document.createElement('span');
        span.textContent = mensaje;
        li.appendChild(span);
        acciones.forEach(a => {
            const link = document.createElement('a');
            link.href = a.href;
            link.className = 'btn btn-soft btn-xs no-underline';
            link.textContent = a.label;
            li.appendChild(link);
        });
        els.bloqueosLista.appendChild(li);
        show(els.bloqueos);
    }

    // Persiste la cotización con la alternativa elegida y deja la UI en el estado
    // "guardado" real (mostrarAccionesPostGuardado). Devuelve el payload guardado, o
    // null si falló — así el caller decide si sigue (continuarConOpcion) o no
    // (Guardar cotización a secas).
    //
    // COTIZACION-WORKSTATION-01 (§10/§16/§17): antes esto era un único
    // guardarYPasarAVenta() detrás de un solo botón verde a ancho completo que hacía
    // las dos cosas. Se separan las DOS intenciones distintas —persistir y convertir—
    // en dos botones con jerarquía explícita, sin cambiar endpoints, payload ni orden
    // de llamadas: "Continuar con esta opción" encadena exactamente la misma
    // secuencia que hacía el botón único.
    // COTIZACION-MIVENTA-02: `silencioso` evita el cambio de UI a "post-guardado"
    // (mostrarAccionesPostGuardado oculta #cotizacion-acciones-pre — Confirmar Mi Venta/
    // Continuar con wizard/Guardar — y muestra Pasar a venta/Ver cotización/Nueva). Antes de
    // esta iteración, guardarCotizacion() siempre terminaba en una navegación inmediata
    // (pasarAVenta) así que ese flip nunca se veía; ahora "Facturar al confirmar" necesita
    // guardar internamente para pedir el preview de IVA (abrirModalFacturar) SIN abandonar la
    // pantalla, y el preflight de "Confirmar Mi Venta"/"Continuar con wizard" tampoco debe
    // esconder sus propios botones mientras decide qué mostrar — sólo "Guardar cotización"
    // (guardarSolo, la única acción cuyo propósito ES persistir y quedarse) usa el modo normal.
    async function guardarCotizacion({ silencioso = false } = {}) {
        clearFeedback();
        if (!state.ultimaSimulacion?.exitoso) {
            showFeedback('Primero simulá una cotización válida.', 'warning');
            return null;
        }

        setBusy(true);
        let data;
        try {
            const payload = {
                simulacion: buildRequest(),
                opcionSeleccionada: state.opcionSeleccionada,
                observaciones: els.observaciones?.value?.trim() || null,
                nombreClienteLibre: els.nombreLibre?.value?.trim() || null,
                telefonoClienteLibre: els.telefonoLibre?.value?.trim() || null,
                fechaVencimiento: els.fechaVencimiento?.value || null,
                tieneEnvio: els.tieneEnvio?.checked || false,
                // VENTA-ENVIO-TOTAL-01: el importe viaja con la cotización (Cotizacion.CostoEnvio) para
                // que sobreviva a "Pasar a venta" desde una cotización ya guardada.
                costoEnvio: importeEnvioActual() > 0 ? importeEnvioActual() : null
            };
            // VENTA-COTIZACION-EXCEPCION-01: sin selección normal vigente, el plan objetivo
            // para excepción (state.excepcion) es lo que se guarda como elegido — así
            // Cotizacion.MedioPagoSeleccionado queda en CreditoPersonal y la conversión
            // (pasarAVenta) intenta esa alternativa en vez de caer al medio por defecto. El
            // motivo/permiso de la excepción viajan aparte, sólo en pasarAVenta — guardar la
            // cotización nunca autoriza nada por sí solo.
            if (!payload.opcionSeleccionada && state.excepcion) {
                payload.opcionSeleccionada = {
                    medioPago: state.excepcion.medioPago,
                    plan: state.excepcion.plan,
                    cantidadCuotas: state.excepcion.cantidadCuotas
                };
            }

            data = await fetchJson(urls.guardar, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                },
                body: JSON.stringify(payload)
            });
        } catch (error) {
            showFeedback(error.message || 'No se pudo guardar la cotización.', 'error');
            setBusy(false);
            return null;
        }

        state.cotizacionGuardadaId = data.id;
        state.cotizacionGuardadaClienteId = state.clienteSeleccionado?.id || null;
        if (!silencioso) {
            setState('saved');
            mostrarAccionesPostGuardado(data);
        }
        setBusy(false);
        return data;
    }

    // Acción secundaria "Guardar cotización" (§10): sólo persiste. El pie pasa al
    // estado post-guardado, donde "Pasar a venta" sigue disponible para continuar más
    // tarde — ese botón no cambió.
    async function guardarSolo() {
        const data = await guardarCotizacion();
        if (!data) return;
        showFeedback(`Cotización ${data.numero} guardada.`, 'ok');
    }

    // Payload compartido por preflight/convertir (confirmar) y convertir (wizard, sin
    // confirmar) — una sola fuente para los campos reales, cada caller sólo agrega
    // confirmarVenta/facturar según su camino (§9 del pedido: nunca confundir ambos).
    function construirPayloadConversion(overrides) {
        return {
            usarPrecioCotizado: true,
            confirmarAdvertencias: true,
            clienteIdOverride: null,
            observacionesAdicionales: null,
            // VENTA-COTIZACION-EXCEPCION-01: mismo mecanismo de excepción documental que
            // ya existe en Venta/Create — el backend (IVentaService.
            // AplicarExcepcionDocumentalSiCorresponde) vuelve a decidir esto de forma
            // independiente y authoritative; nunca se asume aprobada del lado cliente.
            aplicarExcepcionDocumental: !!state.excepcion,
            motivoExcepcionDocumental: state.excepcion?.motivo || null,
            confirmarVenta: false,
            facturar: false,
            tipoFactura: state.facturarConfig?.tipo || 'B',
            envioDestinatario: state.envio?.destinatario || null,
            envioDomicilio: state.envio?.domicilio || null,
            envioTelefono: state.envio?.telefono || null,
            envioLocalidad: state.envio?.localidad || null,
            envioProvincia: state.envio?.provincia || null,
            envioCodigoPostal: state.envio?.codigoPostal || null,
            envioTransportista: state.envio?.transportista || null,
            envioCostoEnvio: state.envio?.costoEnvio ?? null,
            envioFechaProgramada: state.envio?.fechaProgramada || null,
            envioObservaciones: state.envio?.observaciones || null,
            ...overrides
        };
    }

    async function postConversion(path, overrides) {
        const resp = await fetch(`${urls.convertirBase}/${state.cotizacionGuardadaId}/conversion/${path}`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
            },
            body: JSON.stringify(construirPayloadConversion(overrides))
        });
        return { resp, data: await resp.json().catch(() => ({})) };
    }

    // COTIZACION-MIVENTA-02 (§NUEVA REGLA FUNDAMENTAL/§3 del pedido): chequeo de sólo
    // lectura ANTES de crear la Venta — reutiliza las MISMAS reglas reales que
    // ConfirmarVentaAsync exige después de crear (stock, caja, permisos; Crédito personal
    // siempre se reporta como bloqueo real, nunca oculto). Nunca crea nada.
    async function ejecutarPreflight() {
        const { resp, data } = await postConversion('preflight', { facturar: !!state.facturarConfig });
        if (!resp.ok) return null;
        return data;
    }

    // COTIZACION-MIVENTA-01 (§CONFIRMACIÓN FINAL): resumen antes de disparar la conversión
    // real — se abre SÓLO cuando el preflight (iniciarConfirmarMiVenta) ya confirmó que
    // puede terminar; nunca decide nada por sí solo.
    function abrirModalConfirmarVenta() {
        const row = state.seleccionRow;
        const hayOpcion = !!(row && row.plan);
        const excepcion = !hayOpcion ? state.excepcion : null;
        if (!hayOpcion && !excepcion) return;

        const c = state.clienteSeleccionado;
        const clienteTxt = c ? `${c.apellido || ''}, ${c.nombre || ''}`.replace(/^,\s*/, '').replace(/,\s*$/, '').trim() || c.display : '—';
        const medioTxt = hayOpcion
            ? `${nombreParaResumen(row)} · ${cuotasConValorTexto(row.plan)}`
            : 'Crédito personal · excepción solicitada';
        const totalTxt = (hayOpcion && row.plan) ? formatCurrency(row.plan.total) : (els.totalBase?.textContent || '—');
        const envioTxt = state.envio ? `Sí · ${state.envio.domicilio}` : 'No';
        // VENTA-ENVIO-TOTAL-01: con envío guardado el modal separa Productos / Envío / TOTAL A COBRAR.
        const conEnvio = hayEnvioGuardado();
        const envioImp = importeEnvioActual();
        const totalNum = (hayOpcion && row.plan) ? Number(row.plan.total) : Number(state.ultimaSimulacion?.totalBase);
        const totalACobrarTxt = Number.isFinite(totalNum) ? formatCurrency(totalNum + envioImp) : '—';
        // COTIZACION-MIVENTA-02: refleja la configuración real guardada en el modal de
        // facturación (tipo + punto de venta), no un select simplificado.
        const facturarTxt = state.facturarConfig
            ? `Sí · Factura ${state.facturarConfig.tipo}${state.facturarConfig.puntoVenta ? ' · PV ' + state.facturarConfig.puntoVenta : ''}`
            : 'No';

        if (els.confirmarResumen) {
            const fila = (label, valor) => `<div class="flex justify-between gap-3"><dt class="text-slate-400">${esc(label)}</dt><dd class="text-white text-right">${esc(valor)}</dd></div>`;
            els.confirmarResumen.innerHTML =
                fila('Cliente', clienteTxt) +
                fila('Medio de pago', medioTxt) +
                (conEnvio ? fila('Productos', totalTxt) : fila('Total', totalTxt)) +
                fila('Envío', conEnvio ? `${formatCurrency(envioImp)} · ${state.envio.domicilio}` : envioTxt) +
                (conEnvio ? `<div class="flex justify-between gap-3 border-t border-slate-700 pt-2"><dt class="font-semibold text-white">Total a cobrar</dt><dd id="cotizacion-confirmar-total-a-cobrar" class="text-white text-right text-base font-black">${esc(totalACobrarTxt)}</dd></div>` : '') +
                fila('Facturación', facturarTxt);
        }
        // §23 del pedido: el CTA anticipa el resultado — Facturar OFF dice "Confirmar Mi
        // Venta", Facturar ON dice "Confirmar y facturar"; excepción de Crédito personal
        // sigue con su propio copy (nunca se trata como una selección normal).
        if (els.confirmarAceptarLabel) {
            els.confirmarAceptarLabel.textContent = excepcion
                ? 'Continuar con excepción'
                : (state.facturarConfig ? 'Confirmar y facturar' : 'Confirmar Mi Venta');
        }
        window.openModal?.('modal-confirmar-venta');
    }

    // COTIZACION-MIVENTA-02 (§CAMINO A del pedido): click en "Confirmar Mi Venta" — corre
    // el preflight ANTES de abrir el modal de confirmación. Si no puede terminar, se queda
    // en esta pantalla mostrando #cotizacion-bloqueos (nunca crea la Venta, nunca navega
    // sola al wizard); si puede, abre el resumen y continuarConOpcion() recién crea/confirma
    // cuando el operador acepta ahí.
    async function iniciarConfirmarMiVenta() {
        if (state.verificando || state.confirmando) return;
        state.verificando = true;
        if (els.continuar) els.continuar.disabled = true;
        clearFeedback();
        try {
            const data = await guardarCotizacion({ silencioso: true });
            if (!data) return;

            if (!state.cotizacionGuardadaClienteId) {
                showFeedback('Seleccioná un cliente del sistema para confirmar la venta.', 'warning');
                return;
            }

            const preflight = await ejecutarPreflight();
            if (!preflight) {
                showFeedback('No se pudo verificar la venta. Reintentá.', 'error');
                return;
            }

            if (!preflight.listo) {
                renderBloqueos(preflight.bloqueos, 'No se puede confirmar todavía');
                return;
            }

            renderBloqueos([]);
            abrirModalConfirmarVenta();
        } finally {
            state.verificando = false;
            renderSeleccionBar();
        }
    }

    // Acción primaria dentro del modal de confirmación: crea/confirma/factura vía
    // /conversion/convertir — sólo se llama después de un preflight Listo=true.
    //
    // §DOBLE SUBMIT del pedido: Venta/Create no tiene ninguna protección contra
    // doble-submit hoy (auditado antes de implementar — ni disabled-on-click en el
    // formulario clásico, ni idempotencia en el backend), así que acá no hay nada
    // existente que "reutilizar" — esto es una protección nueva, mínima y necesaria: el
    // botón queda disabled durante TODA la secuencia.
    async function continuarConOpcion() {
        // Reentrancia real (no sólo "disabled" visual): un segundo disparo sincrónico —
        // doble click físico antes de que el modal termine de ocultarse, o el propio
        // listener de #cotizacion-confirmar-aceptar reentrando — pasaría igual si sólo
        // chequeáramos els.continuar.disabled, porque ese botón no es el que dispara la
        // confirmación. state.confirmando se fija ANTES de cualquier await, así que un
        // segundo llamado sincrónico ve el flag ya en true y corta acá, sin tocar la red.
        if (state.confirmando) return;
        state.confirmando = true;
        if (els.continuar) els.continuar.disabled = true;
        try {
            const data = await guardarCotizacion({ silencioso: true });
            if (!data) return;

            if (state.cotizacionGuardadaClienteId) {
                showFeedback(`Cotización ${data.numero} guardada. Confirmando venta…`, 'ok');
                await pasarAVenta();
            } else {
                showFeedback(`Cotización ${data.numero} guardada. Seleccioná un cliente del sistema para confirmar la venta.`, 'warning');
            }
        } finally {
            state.confirmando = false;
            // Si pasarAVenta() tuvo éxito la página ya está navegando (window.location.assign);
            // si falló o no se llegó a intentar, renderSeleccionBar() vuelve a habilitar según
            // el estado real (nunca a mano) para permitir reintentar.
            renderSeleccionBar();
        }
    }

    // COTIZACION-MIVENTA-02 (§CAMINO A/§NUEVA REGLA FUNDAMENTAL/§20 del pedido): Confirmar +
    // Facturar (si corresponde) — llamada sólo tras un preflight Listo=true, así que el
    // camino esperado es siempre Venta/Details. El único caso en que puede NO confirmarse
    // (condición de carrera real: p. ej. la caja se cerró entre el preflight y esta llamada)
    // nunca redirige sola al wizard — se explica en #cotizacion-bloqueos con "Ver venta" y
    // "Continuar con wizard" como elecciones explícitas (§NO HACER del pedido).
    async function pasarAVenta() {
        if (!state.cotizacionGuardadaId) return;
        if (!state.cotizacionGuardadaClienteId) {
            showFeedback('Seleccioná un cliente del sistema para pasar a venta.', 'warning');
            return;
        }

        // COTIZACION-MIVENTA-01: además de llamarse desde continuarConOpcion() (donde
        // #cotizacion-continuar ya está disabled), esta función es el handler directo del
        // botón secundario "Pasar a venta" del estado post-guardado — ese botón sigue
        // gestionando su propio spinner acá.
        const btn = els.pasarVenta;
        if (btn) {
            btn.disabled = true;
            const ico = btn.querySelector('.material-symbols-outlined');
            if (ico) ico.textContent = 'progress_activity';
        }

        try {
            const { resp, data } = await postConversion('convertir', {
                confirmarVenta: true,
                facturar: !!state.facturarConfig
            });

            if (!resp.ok || !data.exitoso || !data.ventaId) {
                const mensaje = (data.errores && data.errores.length)
                    ? data.errores.join(' ')
                    : (data.error || 'No se pudo pasar la cotización a venta.');
                showFeedback(mensaje, 'error');
                return;
            }

            if (data.ventaConfirmada) {
                const partes = [`Venta ${data.numeroVenta || ''} ${data.facturada ? 'facturada' : 'confirmada'}`];
                if (data.excepcionDocumentalAplicada) partes.push('con excepción documental autorizada');
                showFeedback(`${partes.join(' ')}. Abriendo…`, 'ok');

                // §20 del pedido: Confirmar y Facturar no son una única transacción (arquitectura
                // real preexistente, no se cambia acá) — si se pidió facturar y la venta quedó
                // Confirmada SIN facturar, se explica en vez de asumir que todo salió bien.
                if (state.facturarConfig && !data.facturada) {
                    renderResultadoParcial(
                        'Venta confirmada — no se pudo facturar',
                        data.mensajeConfirmacion || 'La venta se confirmó pero no se pudo generar la factura.',
                        [
                            { label: 'Reintentar facturación', href: `${urls.ventaFacturar}${data.ventaId}` },
                            { label: 'Ver venta', href: `${urls.ventaDetails}${data.ventaId}` }
                        ]);
                    return;
                }

                window.location.assign(`${urls.ventaDetails}${data.ventaId}`);
                return;
            }

            // Residual: la Venta se creó pero no se pudo confirmar a pesar de un preflight
            // Listo=true (condición de carrera). Nunca se navega sola al wizard — se ofrecen
            // las dos salidas explícitas.
            renderResultadoParcial(
                'Venta creada — no se pudo confirmar',
                data.mensajeConfirmacion || 'La venta se creó pero no se pudo confirmar. Podés reintentar desde el wizard.',
                [
                    { label: 'Continuar con wizard', href: `${urls.ventaEdit}${data.ventaId}` },
                    { label: 'Ver venta', href: `${urls.ventaDetails}${data.ventaId}` }
                ]);
        } catch (error) {
            showFeedback(error.message || 'No se pudo pasar la cotización a venta.', 'error');
        } finally {
            if (btn) {
                btn.disabled = false;
                const ico = btn.querySelector('.material-symbols-outlined');
                if (ico) ico.textContent = 'point_of_sale';
            }
        }
    }

    // COTIZACION-MIVENTA-02 (§CAMINO B/§7/§8/§26 del pedido): "Continuar con wizard" — crea
    // la Venta SIN confirmar (mismo ConvertirAVentaAsync existente, ConfirmarVenta=false) y
    // va siempre a Venta/Edit, que ya reconstituye el wizard completo desde la Venta
    // persistida (cliente, productos, medio, plan, envío, observaciones) — no hace falta
    // ningún DTO ni mecanismo nuevo (confirmado en la auditoría antes de implementar).
    async function continuarConWizard() {
        if (state.continuandoWizard || state.confirmando || state.verificando) return;
        clearFeedback();
        // El wizard necesita un cliente del sistema: se avisa antes de guardar, para no dejar
        // una cotización nueva escrita cuando la continuación igual va a fallar.
        if (!state.clienteSeleccionado?.id) {
            showFeedback('Seleccioná un cliente del sistema para continuar con el wizard.', 'warning');
            return;
        }
        state.continuandoWizard = true;
        if (els.continuarWizard) els.continuarWizard.disabled = true;
        try {
            const data = await guardarCotizacion({ silencioso: true });
            if (!data) return;

            if (!state.cotizacionGuardadaClienteId) {
                showFeedback('Seleccioná un cliente del sistema para continuar con el wizard.', 'warning');
                return;
            }

            showFeedback(`Cotización ${data.numero} guardada. Abriendo el wizard…`, 'ok');

            const { resp, data: conversion } = await postConversion('convertir', {
                confirmarVenta: false,
                facturar: false
            });

            if (!resp.ok || !conversion.exitoso || !conversion.ventaId) {
                const mensaje = (conversion.errores && conversion.errores.length)
                    ? conversion.errores.join(' ')
                    : (conversion.error || 'No se pudo continuar con el wizard.');
                showFeedback(mensaje, 'error');
                return;
            }

            // Siempre Venta/Edit — "Continuar con wizard" nunca decide por resultado, a
            // diferencia de "Confirmar Mi Venta" (§9 del pedido: no confundir los caminos).
            window.location.assign(`${urls.ventaEdit}${conversion.ventaId}`);
        } catch (error) {
            showFeedback(error.message || 'No se pudo continuar con el wizard.', 'error');
        } finally {
            state.continuandoWizard = false;
            renderSeleccionBar();
        }
    }

    /* ---------------------------------------------------------------------
       Resultados (rtable: grupos por medio, expandibles por plan)
    --------------------------------------------------------------------- */
    function flattenOpciones(opciones) {
        const rows = [];
        opciones.forEach(opcion => {
            if (opcion.planes && opcion.planes.length) {
                opcion.planes.forEach(plan => rows.push({ opcion, plan }));
            } else {
                rows.push({ opcion, plan: null });
            }
        });
        return rows;
    }

    function optionKey(row) {
        return `${row.opcion.medioPago}|${row.plan?.plan || ''}|${row.plan?.cantidadCuotas || ''}`;
    }

    function toSeleccion(row) {
        if (!row.plan) return null;
        return {
            medioPago: row.opcion.medioPago,
            plan: row.plan.plan || null,
            cantidadCuotas: row.plan.cantidadCuotas || null
        };
    }

    function estadoLabel(estado) {
        if (typeof estado === 'string') return estado;
        return {
            0: 'Disponible',
            1: 'NoDisponible',
            2: 'RequiereCliente',
            3: 'RequiereEvaluacion',
            4: 'BloqueadoPorProducto',
            5: 'PlanInactivo',
            6: 'CuotaInactiva',
            7: 'AnticipoInvalido'
        }[estado] || 'NoDisponible';
    }

    const MEDIO_LABELS = {
        0: 'Efectivo',
        1: 'Transferencia',
        2: 'Tarjeta crédito',
        3: 'Tarjeta débito',
        4: 'MercadoPago',
        5: 'Crédito personal'
    };

    // `nombre` viene de la configuración real de la base (editable) y suele estar en minúscula
    // y sin tildes ("tarjeta credito"). Si sólo difiere del nombre canónico del medio en
    // mayúsculas/tildes, se muestra el canónico; cualquier otro nombre configurado se respeta.
    function medioLabel(medio, nombre) {
        const canonico = typeof medio === 'string' ? medio : MEDIO_LABELS[medio];
        if (nombre) return canonico && normalize(nombre) === normalize(canonico) ? canonico : nombre;
        if (typeof medio === 'string') return medio;
        return canonico || 'Medio';
    }

    function medioMeta(medio) {
        const key = typeof medio === 'string' ? medio : {
            0: 'Efectivo', 1: 'Transferencia', 2: 'Tarjeta crédito',
            3: 'Tarjeta débito', 4: 'MercadoPago', 5: 'Crédito personal'
        }[medio];
        const map = {
            'Efectivo': { icon: 'payments', tone: 'emerald' },
            'Transferencia': { icon: 'account_balance', tone: 'blue' },
            'Tarjeta crédito': { icon: 'credit_card', tone: 'purple' },
            'Tarjeta débito': { icon: 'credit_card', tone: 'blue' },
            'MercadoPago': { icon: 'smartphone', tone: 'cyan' },
            'Crédito personal': { icon: 'percent', tone: 'amber' }
        };
        return map[key] || { icon: 'payments', tone: 'slate' };
    }

    function esCreditoPersonalMedio(medio) {
        return medioLabel(medio, null) === 'Crédito personal';
    }

    function esTarjetaMedio(medio) {
        const label = medioLabel(medio, null);
        return label === 'Tarjeta crédito' || label === 'Tarjeta débito';
    }

    /* ---------------------------------------------------------------------
       Excepción documental de Crédito personal (VENTA-COTIZACION-EXCEPCION-01)
       Mismo mecanismo que ya existe en Venta/Create (ver esPrevalidacionExceptuable/
       tieneOtrasCondicionesPendientes en su JS del wizard y
       IVentaService.AplicarExcepcionDocumentalSiCorresponde en el backend, la ÚNICA
       autoridad real): documentación incompleta o cupo insuficiente pueden exceptuarse
       con motivo + permiso ventas.authorize; mora NUNCA se exceptúa. Esta pantalla no
       tiene el desglose por categoría que sí expone PrevalidacionResultViewModel
       (Motivos[].Categoria) — /api/cotizacion/aptitud-credito expone mora.tiene y
       documentacion.completa directamente (misma fuente, IClienteAptitudService) — así
       que la condición se deriva de esos dos campos. Es sólo una AFFORDANCE de UI: el
       backend vuelve a decidir esto mismo, de forma independiente, al convertir.
    --------------------------------------------------------------------- */
    function esExcepcionDisponible(aptitud) {
        if (!aptitud) return false;
        if (aptitudTone(aptitud) !== 'no-apto') return false;
        if (aptitud.mora?.tiene) return false; // mora: nunca exceptuable (igual que Venta/Create)
        const documentacionIncompleta = aptitud.documentacion?.completa === false;
        const cupoInsuficiente = Number(aptitud.faltante || 0) > 0;
        return documentacionIncompleta || cupoInsuficiente;
    }

    // "Plan objetivo para excepción" vigente para esta fila puntual, o null. Se usa para
    // decidir si la fila muestra "Solicitar excepción" o "Excepción solicitada" — nunca para
    // tratarla como una selección válida (ver comentario de state.excepcion).
    function excepcionParaRow(row) {
        return state.excepcion && state.excepcion.key === optionKey(row) ? state.excepcion : null;
    }

    function resetExcepcion() {
        state.excepcion = null;
    }

    // Prioridad 1 (auditoría en vivo del usuario, 2026-09-15 → rework funcional):
    // EstadoCrediticioCliente (backend) tiene 3 estados reales no-"Apto": NoApto (bloqueo
    // duro) y RequiereAutorizacion (mora moderada/BCRA situación 2/excepción documental —
    // Venta NO lo rechaza, le pide autorización de supervisor y continúa). Antes esta
    // pantalla sólo leía el booleano `apto` y trataba ambos casos como "No apto", ocultando
    // que uno de los dos SÍ tiene un camino real para seguir. Clasifica con el mismo campo
    // `estado` que ya devuelve /api/cotizacion/aptitud-credito (CotizacionApiController —
    // AptitudCredito ya expone aptitud.Estado.ToString(), no se tocó el backend).
    function aptitudTone(data) {
        if (!data) return null;
        if (data.estado === 'RequiereAutorizacion') return 'requiere-autorizacion';
        return data.apto === true ? 'apto' : 'no-apto';
    }

    // Nota inline de aptitud para cuando Crédito personal SÍ trae planes (el cálculo
    // de cuotas no depende de la aptitud real del cliente — son dos cosas separadas,
    // ver CotizacionPagoCalculator/CreditoSimulacionVentaService): sin esto, un
    // cliente No apto veía los mismos planes "Elegir" que uno apto, sin ninguna señal
    // de que Venta va a pedirle autorización o rechazar la operación más adelante
    // (item 19/23 del pedido — Venta no bloquea de plano, pasa a autorización, así
    // que Cotización tampoco deshabilita "Elegir": sólo lo advierte).
    function aptitudNotaHtml() {
        if (!state.aptitud || state.aptitudClienteId !== state.clienteSeleccionado?.id) return '';
        const tone = aptitudTone(state.aptitud);
        if (tone === 'apto' || !tone) return '';
        // §14: bajo el nombre del medio va el DATO que explica el estado (cupo vs.
        // monto solicitado), no una segunda copia del estado — ese ya vive en la
        // columna Estado de la misma fila. Antes acá decía "No apto para crédito" y
        // la pill de al lado decía "No apto": la misma fila repetía el veredicto dos
        // veces y no aportaba el número que permite decidir qué hacer.
        const clsTone = tone === 'requiere-autorizacion' ? 'rmedio-aptitud--requiere-autorizacion' : 'rmedio-aptitud--no-apto';
        return `<span class="rmedio-aptitud ${clsTone}">Cupo ${formatCurrency(state.aptitud.cupoDisponible)} · solicitado ${formatCurrency(state.aptitud.montoSolicitado)}</span>`;
    }

    /* ---------------------------------------------------------------------
       Aptitud crediticia (Crédito personal) — preview no transaccional.
       Reutiliza /api/cotizacion/aptitud-credito (CotizacionApiController), que a su
       vez llama a IClienteAptitudService.EvaluarAptitudSinGuardarAsync — MISMA fuente
       de verdad que ya usa Venta (ValidacionVentaService) y Cliente/Details. Esta
       pantalla no recalcula aptitud ni inventa un criterio propio, sólo consulta y
       muestra el resultado real.
    --------------------------------------------------------------------- */
    function renderAptitudCredito(view) {
        if (!els.aptitudCredito) return;
        if (!view) { hide(els.aptitudCredito); els.aptitudCredito.innerHTML = ''; return; }

        show(els.aptitudCredito);

        // §6: el eyebrow nombra el alcance real del estado. Sin él, "No apto"/"Requiere
        // autorización" a secas se lee como un veredicto sobre TODA la cotización,
        // cuando sólo condiciona una de las seis alternativas de pago.
        const eyebrow = '<div class="aptitud-card__eyebrow">Crédito personal</div>';

        if (view.tone === 'evaluando') {
            els.aptitudCredito.innerHTML = `
                <div class="aptitud-card aptitud-card--evaluando">
                    <div class="aptitud-card__top">${eyebrow}<span class="aptitud-card__head"><span class="material-symbols-outlined" style="font-size:15px">progress_activity</span> Evaluando…</span></div>
                </div>`;
            return;
        }

        if (view.tone === 'error') {
            // Un fallo técnico de la consulta NO es una falta de aptitud: el copy lo
            // dice explícitamente y ofrece reintentar, en vez de dejar al operador
            // creyendo que el cliente fue rechazado.
            els.aptitudCredito.innerHTML = `
                <div class="aptitud-card aptitud-card--error">
                    <div class="aptitud-card__top">${eyebrow}<span class="aptitud-card__head"><span class="material-symbols-outlined" style="font-size:15px">error</span> No se pudo evaluar</span></div>
                    <p class="aptitud-card__resumen">Error al consultar la evaluación — no es un rechazo del cliente.</p>
                    <button type="button" class="btn btn-soft btn-xs" data-cotizacion-reintentar-aptitud>
                        <span class="material-symbols-outlined" style="font-size:14px">refresh</span> Reintentar
                    </button>
                </div>`;
            return;
        }

        const data = view.data;
        const tone = aptitudTone(data);
        const motivos = Array.isArray(data.motivos) ? data.motivos.filter(Boolean) : [];
        const motivosHtml = motivos.length
            ? `<ul>${motivos.map(m => `<li>${esc(m)}</li>`).join('')}</ul>`
            : '';

        if (tone === 'apto') {
            els.aptitudCredito.innerHTML = `
                <div class="aptitud-card aptitud-card--apto">
                    <div class="aptitud-card__top">${eyebrow}<span class="aptitud-card__head"><span class="material-symbols-outlined" style="font-size:15px">check_circle</span> Apto</span></div>
                    <p class="aptitud-card__resumen">Cupo disponible: ${formatCurrency(data.cupoDisponible)}</p>
                    <details class="aptitud-card__detalle">
                        <summary><span class="material-symbols-outlined chev" style="font-size:13px">expand_more</span> Ver situación</summary>
                        <dl>
                            <dt>Cupo disponible</dt><dd>${formatCurrency(data.cupoDisponible)}</dd>
                            <dt>Monto solicitado</dt><dd>${formatCurrency(data.montoSolicitado)}</dd>
                            <dt>Disponible restante</dt><dd>${formatCurrency(Math.max(0, Number(data.cupoDisponible) - Number(data.montoSolicitado)))}</dd>
                        </dl>
                    </details>
                </div>`;
            return;
        }

        // VENTA-COTIZACION-REWORK-02 (§6, "elegibilidad compacta"): la línea de
        // resumen reproduce el patrón de la referencia visual ("Mora 106 días · 3
        // documentos faltantes") a partir de los mismos campos reales que ya trae
        // el endpoint (mora/documentacion) — sin inventar un criterio nuevo, sólo
        // formatea lo que EvaluarAptitudSinGuardarAsync ya devuelve. El detalle
        // completo (dl + motivos) pasa a un <details> plegado — antes quedaba
        // siempre expandido y era lo que más empujaba Resultados fuera del fold.
        const resumenPartes = [];
        if (data.mora?.tiene && Number(data.mora.dias) > 0) resumenPartes.push(`Mora ${data.mora.dias} ${Number(data.mora.dias) === 1 ? 'día' : 'días'}`);
        const faltantesCount = Array.isArray(data.documentacion?.faltantes) ? data.documentacion.faltantes.length : 0;
        if (faltantesCount > 0) resumenPartes.push(`${faltantesCount} documento${faltantesCount === 1 ? '' : 's'} faltante${faltantesCount === 1 ? '' : 's'}`);
        const resumenLinea = resumenPartes.length ? resumenPartes.join(' · ') : (motivos[0] || '');

        // Prioridad 1: RequiereAutorizacion NO es un bloqueo — Venta va a pedir autorización
        // de supervisor y puede continuar (a diferencia de NoApto, que Venta sí rechaza sin
        // excepción). Copy e ícono honestos con esa diferencia real, no "No apto" genérico.
        if (tone === 'requiere-autorizacion') {
            els.aptitudCredito.innerHTML = `
                <div class="aptitud-card aptitud-card--requiere-autorizacion">
                    <div class="aptitud-card__top">${eyebrow}<span class="aptitud-card__head"><span class="material-symbols-outlined" style="font-size:15px">gpp_maybe</span> Requiere autorización</span></div>
                    ${resumenLinea ? `<p class="aptitud-card__resumen">${esc(resumenLinea)}</p>` : ''}
                    <details class="aptitud-card__detalle">
                        <summary><span class="material-symbols-outlined chev" style="font-size:13px">expand_more</span> Ver situación</summary>
                        <p class="aptitud-card__resumen">Podés continuar: la venta va a pedir autorización de un supervisor.</p>
                        <dl>
                            <dt>Cupo disponible</dt><dd>${formatCurrency(data.cupoDisponible)}</dd>
                            <dt>Monto solicitado</dt><dd>${formatCurrency(data.montoSolicitado)}</dd>
                        </dl>
                        ${motivosHtml}
                    </details>
                </div>`;
            return;
        }

        els.aptitudCredito.innerHTML = `
            <div class="aptitud-card aptitud-card--no-apto">
                <div class="aptitud-card__top">${eyebrow}<span class="aptitud-card__head"><span class="material-symbols-outlined" style="font-size:15px">cancel</span> No apto</span></div>
                ${resumenLinea ? `<p class="aptitud-card__resumen">${esc(resumenLinea)}</p>` : ''}
                <details class="aptitud-card__detalle">
                    <summary><span class="material-symbols-outlined chev" style="font-size:13px">expand_more</span> Ver situación</summary>
                    <dl>
                        <dt>Cupo disponible</dt><dd>${formatCurrency(data.cupoDisponible)}</dd>
                        <dt>Monto solicitado</dt><dd>${formatCurrency(data.montoSolicitado)}</dd>
                        <dt>Faltante</dt><dd>${formatCurrency(data.faltante)}</dd>
                    </dl>
                    ${motivosHtml}
                </details>
            </div>`;
    }

    // Sólo evalúa con cliente + monto cotizado real (nunca al tipear, nunca sin
    // cliente): dispara al seleccionar/quitar cliente y después de cada simulación
    // exitosa con ese cliente. Cachea por clienteId+monto para no repetir la misma
    // consulta en renders sucesivos que no cambiaron ninguno de los dos.
    async function evaluarAptitudCredito() {
        if (!els.aptitudCredito) return;
        const cliente = state.clienteSeleccionado;
        if (!cliente?.id) {
            // §12 (pedido del usuario, 2026-09-17): bumpear el token también acá — sin esto,
            // una consulta en vuelo para un cliente que se QUITA (no se reemplaza por otro)
            // seguía teniendo token === state.aptitudToken al resolver y pisaba este estado
            // "sin cliente" con datos de un cliente que ya no está seleccionado.
            state.aptitudToken++;
            state.aptitud = null;
            state.aptitudClienteId = null;
            state.aptitudMonto = null;
            renderAptitudCredito(null);
            return;
        }

        const monto = Number(state.ultimaSimulacion?.totalBase ?? previewBase()) || 0;
        if (monto <= 0) {
            // Mismo motivo: si el monto cae a 0 (p.ej. se vació el carrito) mientras una
            // consulta anterior está en vuelo, esa respuesta tardía no debe aplicarse acá.
            state.aptitudToken++;
            renderAptitudCredito(null);
            return;
        }

        if (state.aptitud && state.aptitudClienteId === cliente.id && state.aptitudMonto === monto) {
            renderAptitudCredito({ tone: 'ok', data: state.aptitud });
            return;
        }

        const token = ++state.aptitudToken;
        renderAptitudCredito({ tone: 'evaluando' });
        try {
            const data = await fetchJson(`${urls.aptitudCredito}?clienteId=${cliente.id}&monto=${monto}`);
            if (token !== state.aptitudToken) return; // superado por una consulta más nueva
            state.aptitud = data;
            state.aptitudClienteId = cliente.id;
            state.aptitudMonto = monto;
            renderAptitudCredito({ tone: 'ok', data });
            refrescarTablaPorAptitud();
        } catch {
            if (token !== state.aptitudToken) return;
            state.aptitud = null;
            renderAptitudCredito({ tone: 'error' });
        }
    }

    // Bug crítico (reporte del usuario, 2026-09-17): simular() pinta la tabla de
    // comparación ANTES de que esta consulta resuelva (dispara en paralelo, después
    // del render) — con cliente elegido antes de agregar productos, state.aptitud
    // todavía es null en ese primer pintado (evaluarAptitudCredito no llega a
    // consultar sin monto > 0). El resultado: la card de Cliente ya muestra "No
    // apto" con la evaluación fresca, pero la tabla quedó pintada con la aptitud
    // vieja/nula y sigue mostrando Crédito personal "Disponible" con "Elegir" — la
    // misma fila se contradice con la card, a metros de distancia. Repintar la
    // tabla acá, en cuanto esta consulta trae un resultado nuevo, es la única forma
    // de que ambas superficies queden de acuerdo (renderResultado ya preserva la
    // selección vigente si sigue siendo válida, ver optionKey en su tail).
    function refrescarTablaPorAptitud() {
        if (state.ultimaSimulacion?.exitoso) renderResultado(state.ultimaSimulacion);
    }

    // % a mostrar en la columna "Recargo" de la comparativa. Ojo: costoFinancieroTotal es un
    // IMPORTE en pesos (informativo, se usa aparte en el desglose de Credito personal), no un
    // porcentaje — mezclarlo acá en el Math.max inflaba el recargo mostrado a miles de "%".
    // Ajuste firmado: un plan con ajuste negativo configurado (ej. descuento por pago en
    // Efectivo) llega del servidor como recargoPorcentaje = 0 + descuentoPorcentaje > 0
    // (ver CotizacionPagoCalculator.CrearPlanResultado). Se devuelve como negativo para que
    // la comparativa muestre el descuento real en vez de un "0%" que oculta que el total
    // ya viene rebajado. Recargo y descuento son excluyentes en un mismo plan.
    function recargoValor(plan) {
        if (!plan) return 0;
        const descuento = Number(plan.descuentoPorcentaje || 0);
        if (descuento > 0) return -descuento;
        return Math.max(Number(plan.recargoPorcentaje || 0), Number(plan.interesPorcentaje || 0));
    }

    function recargoTexto(r) {
        return `${r > 0 ? '+' : ''}${pct(r)}`;
    }

    function pct(n) {
        return `${new Intl.NumberFormat('es-AR', { maximumFractionDigits: 2 }).format(n)}%`;
    }

    // Mockup: > 0 es ámbar (recargo); 0% y < 0 (descuento real del plan, ej. Efectivo) son
    // verdes — "sin recargo" es la buena noticia de la comparación.
    function recargoClass(r) {
        if (r > 0) return 'text-amber-300';
        return 'text-emerald-400';
    }

    function groupByMedioPago(rows) {
        const groups = [];
        const seen = new Map();
        rows.forEach(row => {
            const key = row.opcion.medioPago;
            if (!seen.has(key)) {
                const group = { medioPago: key, label: medioLabel(key, row.opcion.nombreMedioPago), opcion: row.opcion, rows: [] };
                seen.set(key, group);
                groups.push(group);
            }
            seen.get(key).rows.push(row);
        });
        return groups;
    }

    function estadoPill(estadoStr) {
        if (estadoStr === 'RequiereCliente') {
            return { cls: 'pill-amber', label: 'Req. cliente' };
        }
        if (estadoStr === 'RequiereEvaluacion') {
            return { cls: 'pill-amber', label: 'Sin planes' };
        }
        if (estadoStr === 'AnticipoInvalido') {
            return { cls: 'pill-red', label: 'Anticipo inválido' };
        }
        return { cls: 'pill-red', label: 'Sin planes' };
    }

    function renderResultado(data) {
        if (!data || !els.resultadosTbody) return;

        if (els.subtotal) els.subtotal.textContent = formatCurrency(data.subtotal);
        if (els.descuento) {
            els.descuento.textContent = formatCurrency(data.descuentoTotal);
            // $0,00 en verde no comunica nada (mismo criterio que recargoClass() con
            // 0%): sólo se destaca cuando hay un descuento real aplicado.
            const hayDescuento = Number(data.descuentoTotal) > 0;
            els.descuento.classList.toggle('text-emerald-400', hayDescuento);
            els.descuento.classList.toggle('text-white', !hayDescuento);
        }
        if (els.totalBase) els.totalBase.textContent = formatCurrency(data.totalBase);
        els.totalesBar?.classList.remove('is-pendiente');
        renderTotalesEnvio();
        updateHeaderCounts();

        els.resultadosTbody.replaceChildren();
        const rows = flattenOpciones(data.opcionesPago || []);

        if (!rows.length) {
            const tr = document.createElement('tr');
            tr.className = 'off';
            tr.innerHTML = `<td colspan="7" class="text-center text-sm text-slate-500" style="padding:1.5rem">No hay medios disponibles para los filtros seleccionados.</td>`;
            els.resultadosTbody.appendChild(tr);
        } else {
            // mejor global: menor total con plan disponible. Un plan de Crédito
            // personal No apto queda afuera de la comparación — coronarlo "Mejor
            // precio" o auto-seleccionarlo sería ofrecer como ganadora una alternativa
            // que Venta va a rechazar en el paso Crédito (mismo criterio que
            // accionCellHtml/openPlanDrawer más abajo).
            const bloqueada = r => esCreditoPersonalMedio(r.opcion.medioPago) && aptitudTone(state.aptitud) === 'no-apto';
            let bestKey = null, bestTotal = Infinity;
            rows.forEach(r => {
                if (r.plan && !bloqueada(r) && Number(r.plan.total) < bestTotal) { bestTotal = Number(r.plan.total); bestKey = optionKey(r); }
            });
            state.bestKey = bestKey;

            // COTIZACION-SIMULAR-ORDEN-01 (reapertura, reporte directo del usuario: "está
            // ordenado de forma horrible"): antes el orden de las filas era el orden fijo en
            // que el backend devuelve los medios (enum), no un orden pensado para comparar —
            // un medio "Sin planes"/"Req. cliente" en el medio de la lista interrumpía la
            // comparación de precios entre los medios sí disponibles. Ahora los grupos con
            // planes disponibles van primero, ordenados por su plan más barato (coincide con
            // el criterio que ya usa la pill "Mejor precio"); los grupos sin planes quedan al
            // final, en su orden relativo original (sort estable). Sin cambiar bestKey,
            // opcionSeleccionada ni ningún dato: sólo el orden de pintado.
            const groups = groupByMedioPago(rows)
                .map(group => {
                    const totales = group.rows.filter(r => r.plan).map(r => Number(r.plan.total));
                    return { group, tienePlanes: totales.length > 0, minTotal: totales.length ? Math.min(...totales) : Infinity };
                })
                .sort((a, b) => (a.tienePlanes === b.tienePlanes) ? (a.minTotal - b.minTotal) : (a.tienePlanes ? -1 : 1))
                .map(x => x.group);
            const frag = document.createDocumentFragment();
            groups.forEach(group => appendGroup(frag, group, bestKey));
            els.resultadosTbody.appendChild(frag);

            // auto-seleccionar recomendado (o el mejor) para habilitar guardar — nunca
            // una alternativa bloqueada (ver `bloqueada` arriba). Si esta es una
            // repintada por aptitud llegada tarde (ver refrescarTablaPorAptitud) y la
            // selección vigente sigue siendo válida, se preserva en vez de saltar al
            // "mejor precio": sólo se descarta cuando la fila elegida quedó bloqueada
            // por la aptitud recién resuelta.
            const seleccionPrevia = state.seleccionRow
                ? rows.find(r => r.plan && optionKey(r) === optionKey(state.seleccionRow) && !bloqueada(r))
                : null;
            const recomendado = seleccionPrevia
                || rows.find(r => r.plan?.recomendado && !bloqueada(r))
                || rows.find(r => r.plan && optionKey(r) === bestKey && !bloqueada(r))
                || rows.find(r => r.plan && !bloqueada(r));
            if (recomendado) {
                seleccionarRow(recomendado, { abrirDrawer: false });
            } else {
                state.seleccionRow = null;
                renderSeleccionBar();
            }
        }

        hide(els.resultadosVacio);
        show(els.resultados);

        // §9: con una simulación vigente el CTA pasa a "Actualizar cotización" —
        // volver a apretarlo re-simula con los mismos datos, no arranca de cero.
        setSimularLabel('Actualizar cotización');

        const mensajes = [...(data.errores || []), ...(data.advertencias || [])];
        if (mensajes.length) {
            showFeedback(mensajes.join(' '), data.errores?.length ? 'error' : 'warning');
        }

        if (els.guardar) els.guardar.disabled = data.exitoso === false;
        renderSeleccionBar();
    }

    // Punto único de selección: la usan el botón "Elegir" de la fila, el click sobre
    // la fila (que además abre el detalle) y el pie del drawer. Mantiene sincronizados
    // state.opcionSeleccionada (lo que viaja al backend al guardar), state.seleccionRow
    // (lo que muestra la barra de cierre) y el resaltado de la tabla.
    function seleccionarRow(row, options) {
        if (!row?.plan) return;
        state.opcionSeleccionada = toSeleccion(row);
        state.seleccionRow = row;
        updateSelectedRowHighlight(optionKey(row));
        renderSeleccionBar();
        if (options?.abrirDrawer) openPlanDrawer(row);
    }

    // ---- Celdas Estado / Acción -------------------------------------------------
    // §11/§15: "Estado" dice si la alternativa está disponible (y con qué condición
    // para Crédito personal); "Acción" es la affordance explícita para elegirla.
    // Antes ambas cosas compartían una sola pill que alternaba entre "Mejor precio",
    // "Elegir" y "Seleccionado" — mezclaba una etiqueta de estado con un botón y no
    // había forma visible de saber que la fila era clickeable.
    // Prioridad absoluta (auditoría UX del usuario, 2026-09-16): "Disponible" repetido
    // en cada fila (Efectivo, Transferencia, Tarjeta, MercadoPago, Cheque...) nunca
    // cambia de valor — todo lo que llega a esta tabla YA está disponible, así que la
    // pill sólo competía visualmente con Total/Cuota/Recargo sin aportar nada. Ahora la
    // columna queda vacía en el caso trivial y sólo habla cuando hay algo real que
    // advertir (Crédito personal con autorización u observación pendiente).
    const PILL_NO_APTO = '<span class="pill pill-red"><span class="material-symbols-outlined">cancel</span> No apto</span>';

    function estadoCellHtml(row) {
        if (esCreditoPersonalMedio(row.opcion.medioPago)) {
            const tone = aptitudTone(state.aptitud);
            if (tone === 'no-apto') return PILL_NO_APTO;
            if (tone === 'requiere-autorizacion') return '<span class="pill pill-amber">Requiere autorización</span>';
        }
        return '';
    }

    // Prioridad absoluta (auditoría UX del usuario, 2026-09-16): la Acción tiene que
    // ser coherente con la Estado de la misma fila/grupo. Antes esta función ignoraba
    // la aptitud e imprimía "Elegir" para cualquier plan, incluso uno de Crédito
    // personal ya marcado "No apto" en la columna de al lado — la fila entera se
    // contradecía a sí misma. Ahora la acción sigue el mismo tri-estado que ya usan
    // estadoCellHtml/pillOpciones: No apto no ofrece ninguna acción de selección,
    // Requiere autorización lo dice en el propio botón en vez de sonar igual que un
    // medio sin condiciones.
    function accionCellHtml(row, selectedKey) {
        const key = optionKey(row);
        const selected = Boolean(selectedKey && key === selectedKey);
        if (esCreditoPersonalMedio(row.opcion.medioPago)) {
            const tone = aptitudTone(state.aptitud);
            if (tone === 'no-apto') {
                // VENTA-COTIZACION-EXCEPCION-01: "No apto" sigue siendo "No apto" (§5 del
                // pedido) — la excepción es una vía extraordinaria separada, nunca lo
                // reemplaza. Un plan con la excepción ya solicitada (state.excepcion, ver
                // openPlanDrawer/confirmarExcepcionEnDrawer) lo dice explícitamente en vez de
                // volver a ofrecer "Solicitar excepción"; nunca se ofrece la acción cuando no
                // corresponde según la misma regla que ya usa Venta/Create (esExcepcionDisponible)
                // o cuando el usuario actual no tiene el permiso (puedeExcepcionDocumental, mismo
                // gate server-side que @if (User.TienePermiso("ventas","authorize")) en el wizard).
                const excepcion = excepcionParaRow(row);
                if (excepcion) {
                    return `<button type="button" class="rt-btn rt-btn--warn" data-cotizacion-ver-excepcion="${esc(key)}" aria-pressed="true"><span class="material-symbols-outlined" style="font-size:13px">warning</span> Excepción solicitada</button>`;
                }
                if (puedeExcepcionDocumental && esExcepcionDisponible(state.aptitud)) {
                    return `<button type="button" class="rt-btn rt-btn--warn" data-cotizacion-excepcion="${esc(key)}" aria-pressed="false"><span class="material-symbols-outlined" style="font-size:13px">warning</span> Solicitar excepción</button>`;
                }
                return '<span class="rt-accion-bloqueada" aria-disabled="true">No disponible</span>';
            }
            if (tone === 'requiere-autorizacion') {
                if (selected) {
                    return `<button type="button" class="rt-btn rt-btn--on" data-cotizacion-elegir="${esc(key)}" aria-pressed="true"><span class="material-symbols-outlined" style="font-size:13px">check</span> Seleccionado</button>`;
                }
                return `<button type="button" class="rt-btn rt-btn--warn" data-cotizacion-elegir="${esc(key)}" aria-pressed="false">Solicitar autorización</button>`;
            }
        }
        if (selected) {
            return `<button type="button" class="rt-btn rt-btn--on" data-cotizacion-elegir="${esc(key)}" aria-pressed="true"><span class="material-symbols-outlined" style="font-size:13px">check</span> Seleccionado</button>`;
        }
        return `<button type="button" class="rt-btn" data-cotizacion-elegir="${esc(key)}" aria-pressed="false">Elegir</button>`;
    }

    function appendGroup(frag, group, bestKey) {
        const meta = medioMeta(group.medioPago);
        const planRows = group.rows.filter(r => r.plan);
        const estadoStr = estadoLabel(group.opcion.estado);

        // Sin planes disponibles -> fila off
        if (!planRows.length) {
            const tr = document.createElement('tr');
            tr.className = 'off';

            // Crédito personal + aptitud ya evaluada (mismo dato de la ficha de Cliente,
            // sin segunda consulta): reemplaza el motivo genérico por la razón real de
            // negocio (cupo/mora/documentación) en vez de "No hay planes activos" —
            // item 18/19 del pedido de rework.
            if (esCreditoPersonalMedio(group.medioPago) && state.aptitud && state.aptitudClienteId === state.clienteSeleccionado?.id) {
                // Prioridad 1: 3 estados reales, no 2 — ver aptitudTone().
                const tone = aptitudTone(state.aptitud);
                const pillCls = tone === 'apto' ? 'pill-green' : (tone === 'requiere-autorizacion' ? 'pill-amber' : 'pill-red');
                const pillLabel = tone === 'apto' ? 'Apto' : (tone === 'requiere-autorizacion' ? 'Requiere autorización' : 'No apto');
                const resumen = tone === 'apto'
                    ? 'Cliente apto — sin planes configurados para este monto.'
                    : tone === 'requiere-autorizacion'
                        ? (state.aptitud.motivo || 'Requiere autorización de un supervisor — sin planes configurados para este monto.')
                        : `Cupo ${formatCurrency(state.aptitud.cupoDisponible)} · solicitado ${formatCurrency(state.aptitud.montoSolicitado)} · faltante ${formatCurrency(state.aptitud.faltante)}`;
                const rmedioCls = tone === 'apto' ? 'rmedio-aptitud--apto' : (tone === 'requiere-autorizacion' ? 'rmedio-aptitud--requiere-autorizacion' : 'rmedio-aptitud--no-apto');
                tr.innerHTML = `
                    <td><span class="rmedio"><span class="pay-ico pay-ico--slate"><span class="material-symbols-outlined" style="font-size:16px">${meta.icon}</span></span><span class="rmedio-nombre font-medium text-slate-300">${esc(group.label)}</span></span></td>
                    <td class="r text-slate-500">—</td>
                    <td colspan="3" class="rmedio-aptitud ${rmedioCls}">${esc(resumen)}</td>
                    <td><span class="pill ${pillCls}">${esc(pillLabel)}</span></td>
                    <td class="r rt-accion"><button type="button" class="rt-btn rt-btn--ghost" data-cotizacion-ver-situacion>Ver situación</button></td>`;
                frag.appendChild(tr);
                return;
            }

            const pill = estadoPill(estadoStr);
            const motivo = group.opcion.motivoNoDisponible
                || (pill.label === 'Req. cliente' ? 'Seleccioná un cliente para evaluar el crédito.' : 'No hay planes activos para el medio solicitado.');
            const motivoIcon = pill.label === 'Req. cliente' ? 'person_alert' : 'warning';
            tr.innerHTML = `
                <td><span class="rmedio"><span class="pay-ico pay-ico--slate"><span class="material-symbols-outlined" style="font-size:16px">${meta.icon}</span></span><span class="rmedio-nombre font-medium text-slate-300">${esc(group.label)}</span></span></td>
                <td class="r text-slate-500">—</td>
                <td colspan="3" class="rt-motivo"><span class="material-symbols-outlined text-amber-300" style="font-size:14px">${motivoIcon}</span> ${esc(motivo)}</td>
                <td><span class="pill ${pill.cls}">${esc(pill.label)}</span></td>
                <td class="r rt-accion text-slate-600">—</td>`;
            frag.appendChild(tr);
            return;
        }

        // Un solo plan -> fila simple
        if (planRows.length === 1) {
            frag.appendChild(buildSingleRow(planRows[0], meta, bestKey));
            return;
        }

        // Varios planes -> parent + detail
        const gkey = `g${typeof group.medioPago === 'string' ? group.medioPago : group.medioPago}`;
        const totals = planRows.map(r => Number(r.plan.total));
        const minTotal = Math.min(...totals);
        const recargos = planRows.map(r => recargoValor(r.plan));
        const minR = Math.min(...recargos), maxR = Math.max(...recargos);
        const recargoTxt = minR === maxR ? recargoTexto(minR) : `${recargoTexto(minR)} a ${recargoTexto(maxR)}`;

        const fuenteTxt = group.opcion.fuenteTasaDescripcion
            ? `<div class="rmedio-sub">${esc(group.opcion.fuenteTasaDescripcion)}</div>`
            : '';
        // La pill de la fila-padre distingue NoApto (bloqueo real, Venta rechaza) de
        // RequiereAutorizacion (Venta sigue, pide autorización de supervisor).
        const aptitudNota = esCreditoPersonalMedio(group.medioPago) ? aptitudNotaHtml() : '';
        const toneGrupo = esCreditoPersonalMedio(group.medioPago) ? aptitudTone(state.aptitud) : null;
        const pillOpciones = toneGrupo === 'no-apto'
            ? PILL_NO_APTO
            : toneGrupo === 'requiere-autorizacion'
                ? '<span class="pill pill-amber">Requiere autorización</span>'
                : '<span class="pill pill-green">Disponible</span>';

        // §13: la fila padre resume el medio (identidad + rango de precio + estado) y
        // su acción es EXPANDIR, no elegir — elegir es una decisión por plan, que vive
        // en las filas hijas. Por eso no lleva data-cotizacion-opcion-key.
        // Banda angosta (mobile): los medios con planes arrancan colapsados para poder
        // comparar medios de un vistazo (eran ~1400px de filas de plan) y se abren al
        // tocar. Nunca se colapsa el grupo que contiene la opción elegida o la de mejor
        // precio: la selección tiene que seguir a la vista. Ancho/desktop: sin cambios.
        const seleccionKey = state.seleccionRow ? optionKey(state.seleccionRow) : null;
        const colapsarGrupo = esBandaAngosta()
            && !planRows.some(r => { const k = optionKey(r); return k === bestKey || k === seleccionKey; });

        const parent = document.createElement('tr');
        parent.className = 'parent';
        parent.setAttribute('aria-expanded', colapsarGrupo ? 'false' : 'true');
        parent.dataset.group = gkey;
        parent.innerHTML = `
            <td><span class="rmedio"><span class="pay-ico pay-ico--${meta.tone}"><span class="material-symbols-outlined" style="font-size:16px">${meta.icon}</span></span><span><span class="rmedio-nombre">${esc(group.label)}</span>${fuenteTxt}</span><span class="material-symbols-outlined twist">expand_more</span></span></td>
            <td class="r rt-desde"><span class="text-slate-300">desde </span><span class="total-display font-semibold text-white">${formatCurrency(minTotal)}</span></td>
            <td class="text-slate-400">${planRows.length} planes</td>
            <td class="r text-slate-500">—</td>
            <td class="r font-semibold ${recargoClass(maxR)}">${recargoTxt}</td>
            <td>${pillOpciones}</td>
            <td class="r rt-accion"><span class="rt-btn rt-btn--ghost" aria-hidden="true">Ver planes</span></td>${aptitudNota ? `<td class="rt-nota">${aptitudNota}</td>` : ''}`;
        frag.appendChild(parent);

        // detalle más barato
        let cheapKey = null, cheapTotal = Infinity;
        planRows.forEach(r => { if (Number(r.plan.total) < cheapTotal) { cheapTotal = Number(r.plan.total); cheapKey = optionKey(r); } });

        planRows.forEach(row => {
            const detalle = buildDetailRow(row, gkey, bestKey, cheapKey);
            if (colapsarGrupo) detalle.hidden = true;
            frag.appendChild(detalle);
        });
    }

    // Misma banda que los @container (max-width: 37.9375rem) de cotizacion-simulador.css:
    // se mide el ancho REAL del host (standalone o panel embebido en Venta/Create), no el
    // viewport — a 700px de viewport el cotizador ya está en 2 columnas.
    function esBandaAngosta() {
        const app = document.querySelector('[data-cotizacion-simulador]');
        const host = app && (app.closest('.venta-cotizar-panel, .cotz-standalone') || app.parentElement);
        return !!host && host.clientWidth > 0 && host.clientWidth < 38 * 16;
    }

    function planLabelCuotas(plan) {
        return Number(plan.cantidadCuotas) > 1 ? `${plan.cantidadCuotas} cuotas` : '1 pago';
    }

    // "Mejor precio" es una propiedad del PRECIO, así que vive junto al medio/plan en
    // la primera columna (§12), no mezclada con el estado ni con la acción.
    function mejorPrecioBadge(key, bestKey) {
        return key === bestKey ? ' <span class="pill pill-green">Mejor precio</span>' : '';
    }

    function buildSingleRow(row, meta, bestKey) {
        const plan = row.plan;
        const key = optionKey(row);
        const r = recargoValor(plan);
        const tr = document.createElement('tr');
        tr.dataset.cotizacionRowKey = key;
        tr.dataset.cotizacionOpcionKey = key;
        if (key === bestKey) tr.className = 'best';
        const cuotasTxt = Number(plan.cantidadCuotas) > 1 ? formatCurrency(plan.valorCuota) : '—';
        const fuenteTxt = row.opcion.fuenteTasaDescripcion
            ? `<div class="rmedio-sub">${esc(row.opcion.fuenteTasaDescripcion)}</div>`
            : '';
        const aptitudNota = esCreditoPersonalMedio(row.opcion.medioPago) ? aptitudNotaHtml() : '';
        tr.innerHTML = `
            <td><span class="rmedio"><span class="pay-ico pay-ico--${meta.tone}"><span class="material-symbols-outlined" style="font-size:16px">${meta.icon}</span></span><span><span class="rmedio-nombre">${esc(medioLabel(row.opcion.medioPago, row.opcion.nombreMedioPago))}</span>${mejorPrecioBadge(key, bestKey)}${fuenteTxt}</span></span></td>
            <td class="r"><span class="total-display font-semibold text-white">${formatCurrency(plan.total)}</span></td>
            <td class="text-slate-400">${planLabelCuotas(plan)}</td>
            <td class="rt-cuota r ${Number(plan.cantidadCuotas) > 1 ? 'font-semibold text-slate-200 total-display' : 'text-slate-500 is-empty'}">${cuotasTxt}</td>
            <td class="r font-semibold ${recargoClass(r)}">${r > 0 ? '+' : ''}${pct(r)}</td>
            <td>${estadoCellHtml(row)}</td>
            <td class="r rt-accion">${accionCellHtml(row, null)}</td>${aptitudNota ? `<td class="rt-nota">${aptitudNota}</td>` : ''}`;
        return tr;
    }

    function buildDetailRow(row, gkey, bestKey, cheapKey) {
        const plan = row.plan;
        const key = optionKey(row);
        const r = recargoValor(plan);
        const tr = document.createElement('tr');
        tr.className = 'detail' + (key === cheapKey ? ' cheap' : '');
        tr.dataset.g = gkey;
        tr.dataset.cotizacionRowKey = key;
        tr.dataset.cotizacionOpcionKey = key;
        // §13: la fila hija no repite el ícono del medio (ya lo trae el padre) ni su
        // peso tipográfico — sólo el plan y sus números. Tampoco repite el ESTADO: es
        // el mismo para todos los planes del medio y ya lo declara la fila padre (con
        // Crédito personal no apto, antes "No apto" aparecía una vez por plan además
        // de en el padre — 3 copias del mismo veredicto en un solo grupo).
        const planName = plan.plan || medioLabel(row.opcion.medioPago, row.opcion.nombreMedioPago);
        tr.innerHTML = `
            <td><span class="plan-medio"><span class="plan-nombre text-slate-200">${esc(planName)}</span>${mejorPrecioBadge(key, bestKey)}</span></td>
            <td class="r"><span class="total-display font-semibold text-white">${formatCurrency(plan.total)}</span></td>
            <td class="rt-plan text-slate-400">${planLabelCuotas(plan)}</td>
            <td class="rt-cuota r font-semibold total-display text-slate-200${Number(plan.cantidadCuotas) > 1 ? '' : ' is-empty'}">${Number(plan.cantidadCuotas) > 1 ? formatCurrency(plan.valorCuota) : '—'}</td>
            <td class="r font-semibold ${recargoClass(r)}">${r > 0 ? '+' : ''}${pct(r)}</td>
            <td></td>
            <td class="r rt-accion">${accionCellHtml(row, null)}</td>`;
        return tr;
    }

    // Actualiza el resaltado de fila Y el botón de la columna Acción: "Seleccionado"
    // tiene que seguir a la selección real y no depender sólo del color de fondo
    // (§15). state.bestKey lo fija renderResultado().
    function updateSelectedRowHighlight(selectedKey) {
        $$('#cotizacion-resultados-tbody tr[data-cotizacion-row-key]').forEach(tr => {
            const key = tr.dataset.cotizacionRowKey;
            tr.classList.toggle('selected', key === selectedKey);
            const row = findRowByKey(key);
            const accionCell = tr.querySelector('td.rt-accion');
            if (row && accionCell) accionCell.innerHTML = accionCellHtml(row, selectedKey);
        });
    }

    function findRowByKey(key) {
        const rows = flattenOpciones(state.ultimaSimulacion?.opcionesPago || []);
        return rows.find(r => optionKey(r) === key) || null;
    }

    // Resumen corto del vector de cuotas tal cual lo devuelve el servidor
    // (FinancialCalculationService via CreditoSimulacionVentaService): "N cuotas de $X" cuando
    // son todas iguales; con importes distintos (residuo de redondeo Y/O cuotas sin recargo,
    // CSR-ML6) → "Ver detalle por cuota" — la tabla completa (#plan-credito-cuotas-tabla) es la
    // fuente visual final, nunca se resume comparando sólo primera vs última. Nunca se recalcula acá.
    function formatearVectorCuotas(cuotas) {
        if (!Array.isArray(cuotas) || cuotas.length === 0) return '';
        if (cuotas.length === 1) {
            return `1 cuota de ${formatCurrency(cuotas[0].total)}`;
        }
        const primera = cuotas[0].total;
        const todasIguales = cuotas.every(c => Math.abs(Number(c.total) - Number(primera)) < 0.005);
        return todasIguales
            ? `${cuotas.length} cuotas de ${formatCurrency(primera)}`
            : 'Ver detalle por cuota';
    }

    // CSR-ML6: "N°, N°, …" a partir de la metadata del plan (plan.cuotasSinRecargo), nunca inferido
    // de interes === 0 (con un plan 0% todas las cuotas tendrían interés 0 sin estar necesariamente
    // marcadas como "sin recargo").
    function formatearCuotasSinRecargo(lista) {
        if (!Array.isArray(lista) || lista.length === 0) return 'Ninguna';
        return lista.slice().sort((a, b) => a - b).join(', ');
    }

    // Pinta la tabla completa por cuota tal cual el vector del servidor: no recalcula capital,
    // recargo ni total. El badge "Sin recargo" se pinta por el propio interes de la fila (0), así
    // que con un plan 0% aparece en todas las filas sin sugerir que las demás sí cobran recargo.
    function renderTablaCuotasCredito(cuotas) {
        if (!els.planCreditoCuotasTablaBody) return;
        els.planCreditoCuotasTablaBody.innerHTML = '';
        if (!Array.isArray(cuotas)) return;

        cuotas.forEach(c => {
            const sinRecargo = Number(c.interes) === 0;
            const badge = sinRecargo
                ? '<span class="pill pill-slate ml-1.5">Sin recargo</span>'
                : '';
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td class="px-2 py-1 border-t border-slate-800/70 text-slate-300">${esc(c.numeroCuota)}</td>
                <td class="px-2 py-1 border-t border-slate-800/70 text-right text-slate-300 font-mono">${formatCurrency(c.capital)}</td>
                <td class="px-2 py-1 border-t border-slate-800/70 text-right text-slate-300 font-mono">${formatCurrency(c.interes)}${badge}</td>
                <td class="px-2 py-1 border-t border-slate-800/70 text-right text-white font-mono font-semibold">${formatCurrency(c.total)}</td>`;
            els.planCreditoCuotasTablaBody.appendChild(tr);
        });
    }

    function openPlanDrawer(row) {
        if (!row?.plan) return;
        // El pie del drawer ("Elegir esta opción") necesita saber qué plan está
        // abierto: se llega ahí explorando el detalle, no necesariamente desde la
        // fila ya seleccionada.
        state.planAbiertoKey = optionKey(row);
        // El detalle cuota por cuota no debe competir con Total/cuotas/valor de
        // cuota/recargo al ABRIR el drawer — si quedó expandido de una consulta
        // anterior (mismo <details>, el drawer no se recrea), se repliega acá.
        els.planCreditoCuotasDetalle?.removeAttribute('open');
        const plan = row.plan;
        const medio = medioLabel(row.opcion.medioPago, row.opcion.nombreMedioPago);
        const planName = plan.plan && plan.plan !== medio ? `${medio} · ${plan.plan}` : medio;
        const cuotasTxt = Number(plan.cantidadCuotas) > 1 ? `${plan.cantidadCuotas} cuotas` : 'Pago único';
        const r = recargoValor(plan);
        if (els.planMedio) els.planMedio.textContent = planName;
        if (els.planCuotas) els.planCuotas.textContent = cuotasTxt;
        if (els.planTotal) els.planTotal.textContent = formatCurrency(plan.total);
        if (els.planDetalleCuotas) els.planDetalleCuotas.textContent = cuotasTxt;
        if (els.planValorCuota) els.planValorCuota.textContent = Number(plan.cantidadCuotas) > 1 ? formatCurrency(plan.valorCuota) : '—';
        if (els.planRecargo) {
            els.planRecargo.textContent = recargoTexto(r);
            els.planRecargo.className = recargoClass(r) + ' font-mono';
        }
        if (els.planRecargoLabel) els.planRecargoLabel.textContent = r < 0 ? 'Descuento por plan' : 'Recargo total';

        // Item 24 del rework: estado de elegibilidad arriba del resto del drawer,
        // sólo para Crédito personal y sólo si ya hay una evaluación (misma que la
        // ficha de Cliente — no se dispara una segunda consulta acá).
        if (els.planElegibilidad) {
            const mostrarElegibilidad = esCreditoPersonalMedio(row.opcion.medioPago)
                && state.aptitud && state.aptitudClienteId === state.clienteSeleccionado?.id;
            if (mostrarElegibilidad) {
                // Prioridad 1: mismo tri-estado que el resto de la pantalla — un plan de
                // Crédito personal con RequiereAutorizacion no es "No apto" (Venta lo deja
                // continuar pidiendo autorización de supervisor).
                const tone = aptitudTone(state.aptitud);
                const cardCls = tone === 'apto' ? 'aptitud-card--apto' : (tone === 'requiere-autorizacion' ? 'aptitud-card--requiere-autorizacion' : 'aptitud-card--no-apto');
                const icon = tone === 'apto' ? 'check_circle' : (tone === 'requiere-autorizacion' ? 'gpp_maybe' : 'cancel');
                const titulo = tone === 'apto' ? 'Apto para crédito' : (tone === 'requiere-autorizacion' ? 'Requiere autorización' : 'No apto para crédito');
                els.planElegibilidad.innerHTML = `
                    <div class="aptitud-card ${cardCls}">
                        <div class="aptitud-card__head">
                            <span class="material-symbols-outlined" style="font-size:16px">${icon}</span>
                            ${esc(titulo)}
                        </div>
                        ${tone === 'requiere-autorizacion' ? `<p class="aptitud-card__resumen">Podés continuar: la venta va a pedir autorización de un supervisor.</p>` : ''}
                        ${tone === 'no-apto' ? `<dl><dt>Faltante</dt><dd>${formatCurrency(state.aptitud.faltante)}</dd></dl>` : ''}
                        ${Array.isArray(state.aptitud.motivos) && state.aptitud.motivos.length
                            ? `<ul>${state.aptitud.motivos.map(m => `<li>${esc(m)}</li>`).join('')}</ul>` : ''}
                    </div>`;
                show(els.planElegibilidad);

                // El pie del drawer tiene que ofrecer la misma acción que la fila
                // (accionCellHtml): un plan No apto no se puede "elegir" desde acá
                // tampoco — sería la misma contradicción con otra puerta de entrada.
                // VENTA-COTIZACION-EXCEPCION-01: cuando SÍ corresponde ofrecer la excepción
                // (mismo criterio que accionCellHtml), este botón abre el formulario en vez de
                // quedar deshabilitado — nunca selecciona la fila (ver el listener de
                // [data-cotizacion-elegir-drawer], que branchea por state.planDrawerMode).
                if (els.planElegirDrawer) {
                    if (tone === 'no-apto') {
                        const excepcionVigente = excepcionParaRow(row);
                        const puedeExceptuar = puedeExcepcionDocumental && esExcepcionDisponible(state.aptitud);
                        if (excepcionVigente) {
                            state.planDrawerMode = 'excepcion-vigente';
                            els.planElegirDrawer.disabled = true;
                            els.planElegirDrawer.innerHTML = '<span class="material-symbols-outlined" style="font-size:18px">shield_question</span> Excepción solicitada';
                        } else if (puedeExceptuar) {
                            state.planDrawerMode = 'excepcion';
                            els.planElegirDrawer.disabled = false;
                            els.planElegirDrawer.innerHTML = '<span class="material-symbols-outlined" style="font-size:18px">shield_question</span> Solicitar excepción';
                        } else {
                            state.planDrawerMode = 'bloqueado';
                            els.planElegirDrawer.disabled = true;
                            els.planElegirDrawer.innerHTML = '<span class="material-symbols-outlined" style="font-size:18px">block</span> No disponible';
                        }
                    } else if (tone === 'requiere-autorizacion') {
                        state.planDrawerMode = 'elegir';
                        els.planElegirDrawer.disabled = false;
                        els.planElegirDrawer.innerHTML = '<span class="material-symbols-outlined" style="font-size:18px">gpp_maybe</span> Solicitar autorización';
                    } else {
                        state.planDrawerMode = 'elegir';
                        els.planElegirDrawer.disabled = false;
                        els.planElegirDrawer.innerHTML = '<span class="material-symbols-outlined" style="font-size:18px">check</span> Elegir esta opción';
                    }
                }
            } else {
                hide(els.planElegibilidad);
                els.planElegibilidad.innerHTML = '';
                state.planDrawerMode = 'elegir';
                if (els.planElegirDrawer) {
                    els.planElegirDrawer.disabled = false;
                    els.planElegirDrawer.innerHTML = '<span class="material-symbols-outlined" style="font-size:18px">check</span> Elegir esta opción';
                }
            }
        }

        renderFormularioExcepcionEnDrawer(row);

        // Desglose de Credito personal: solo estos planes traen saldoAFinanciar/totalFinanciado
        // (server-authoritative, via CreditoSimulacionVentaService). El resto de los medios no
        // financian en cuotas con recargo total y no muestran este bloque.
        const esCreditoPersonal = plan.saldoAFinanciar !== undefined && plan.saldoAFinanciar !== null;
        if (esCreditoPersonal) {
            if (els.planCreditoFuente) els.planCreditoFuente.textContent = plan.fuentePorcentaje || '—';
            if (els.planCreditoPrecio) els.planCreditoPrecio.textContent = formatCurrency(Number(plan.saldoAFinanciar) + Number(plan.anticipo || 0));
            if (els.planCreditoAnticipo) els.planCreditoAnticipo.textContent = formatCurrency(plan.anticipo || 0);
            if (els.planCreditoSaldo) els.planCreditoSaldo.textContent = formatCurrency(plan.saldoAFinanciar);
            if (els.planCreditoImporteRecargo) els.planCreditoImporteRecargo.textContent = formatCurrency(plan.costoFinancieroTotal || 0);
            if (els.planCreditoTotalFinanciado) els.planCreditoTotalFinanciado.textContent = formatCurrency(plan.totalFinanciado);
            if (els.planCreditoVector) els.planCreditoVector.textContent = formatearVectorCuotas(plan.cuotas);
            // CSR-ML6: metadata del plan + tabla completa, pintadas tal cual las manda el servidor.
            if (els.planCreditoCuotasSinRecargo) els.planCreditoCuotasSinRecargo.textContent = formatearCuotasSinRecargo(plan.cuotasSinRecargo);
            renderTablaCuotasCredito(plan.cuotas);
            show(els.planCreditoDesglose);
        } else {
            hide(els.planCreditoDesglose);
        }

        window.openModal?.('modal-plan');
    }

    // VENTA-COTIZACION-EXCEPCION-01: formulario de excepción documental dentro del drawer.
    // Pinta según state.planDrawerMode, ya decidido por openPlanDrawer para esta fila:
    // 'excepcion-vigente' muestra el resumen de lo ya solicitado (nunca "aplicada" — eso
    // sólo lo confirma el backend al convertir, ver continuarConExcepcion); 'excepcion'
    // deja el formulario listo pero PLEGADO (se abre con abrirFormularioExcepcionEnDrawer,
    // desde el botón del pie o el "Solicitar excepción" de la fila); cualquier otro modo
    // oculta todo el panel.
    function renderFormularioExcepcionEnDrawer(row) {
        if (!els.planExcepcionPanel) return;

        if (state.planDrawerMode === 'excepcion-vigente') {
            const excepcion = excepcionParaRow(row);
            show(els.planExcepcionPanel);
            hide(els.planExcepcionFormulario);
            show(els.planExcepcionResumenWrap);
            if (els.planExcepcionResumen) els.planExcepcionResumen.textContent = excepcion?.motivo || '';
            return;
        }

        if (state.planDrawerMode !== 'excepcion') {
            hide(els.planExcepcionPanel);
            return;
        }

        hide(els.planExcepcionPanel);
        show(els.planExcepcionFormulario);
        hide(els.planExcepcionResumenWrap);
        if (els.planExcepcionMotivo) els.planExcepcionMotivo.value = '';
        els.planExcepcionMotivo?.classList.remove('border-red-500');
        hide(els.planExcepcionMotivoError);
    }

    function abrirFormularioExcepcionEnDrawer() {
        if (state.planDrawerMode !== 'excepcion') return;
        show(els.planExcepcionPanel);
        els.planExcepcionMotivo?.focus();
    }

    function cancelarFormularioExcepcionEnDrawer() {
        hide(els.planExcepcionPanel);
        if (els.planExcepcionMotivo) els.planExcepcionMotivo.value = '';
        els.planExcepcionMotivo?.classList.remove('border-red-500');
        hide(els.planExcepcionMotivoError);
    }

    // "Aplicar y continuar" del drawer: fija el PLAN OBJETIVO PARA EXCEPCIÓN (state.excepcion)
    // — deliberadamente no llama seleccionarRow (§6: nunca se vuelve una selección válida acá).
    // El backend recién decide de verdad al convertir (continuarConExcepcion/pasarAVenta), con
    // el mismo criterio (IVentaService.AplicarExcepcionDocumentalSiCorresponde).
    function confirmarExcepcionEnDrawer() {
        const row = findRowByKey(state.planAbiertoKey);
        if (!row) return;
        const motivo = (els.planExcepcionMotivo?.value || '').trim();
        if (!motivo) {
            show(els.planExcepcionMotivoError);
            els.planExcepcionMotivo?.classList.add('border-red-500');
            els.planExcepcionMotivo?.focus();
            return;
        }
        state.excepcion = {
            key: optionKey(row),
            medioPago: row.opcion.medioPago,
            plan: row.plan.plan,
            cantidadCuotas: row.plan.cantidadCuotas,
            motivo
        };
        window.closeModal?.('modal-plan');
        refrescarTablaPorAptitud();
        renderSeleccionBar();
    }

    // Item 25 del lote: Anticipo sólo importa mientras Crédito personal está
    // incluido en la comparativa. No oculta el input (el valor sigue viajando en
    // el payload igual, ver buildRequest) ni cambia el cálculo — sólo baja la
    // prioridad visual cuando ese medio está destildado.
    function actualizarPrioridadAnticipo() {
        if (!els.anticipoBloque) return;
        const activo = els.incluirCreditoPersonal ? !!els.incluirCreditoPersonal.checked : true;
        els.anticipoBloque.dataset.anticipoActivo = String(activo);
    }

    /* ---------------------------------------------------------------------
       Eventos
    --------------------------------------------------------------------- */
    function bindEvents() {
        els.productoBuscar?.addEventListener('input', debounce(buscarProductos, 220));
        els.clienteBuscar?.addEventListener('input', debounce(buscarClientes, 220));

        els.agregarProducto?.addEventListener('click', () => agregarProducto(state.productoSeleccionado, els.cantidad?.value));
        els.agregarManual?.addEventListener('click', agregarProductoManual);
        els.simular?.addEventListener('click', simular);
        els.guardar?.addEventListener('click', guardarSolo);
        // COTIZACION-MIVENTA-02: "Confirmar Mi Venta" corre primero el preflight
        // (§NUEVA REGLA FUNDAMENTAL); el modal de confirmación sólo se abre si puede
        // terminar — continuarConOpcion() recién crea/confirma si el operador acepta ahí.
        els.continuar?.addEventListener('click', iniciarConfirmarMiVenta);
        els.continuarWizard?.addEventListener('click', continuarConWizard);
        els.confirmarAceptar?.addEventListener('click', () => {
            window.closeModal?.('modal-confirmar-venta');
            continuarConOpcion();
        });
        els.confirmarCancelar?.addEventListener('click', () => window.closeModal?.('modal-confirmar-venta'));
        els.pasarVenta?.addEventListener('click', pasarAVenta);
        els.nuevaCotizacion?.addEventListener('click', () => window.location.reload());

        // Envío: el checkbox abre/cierra el modal real de datos (§CHECKBOX "CONSIDERAR
        // ENVÍO A DOMICILIO" del pedido) en vez de sólo declarar intención.
        els.tieneEnvio?.addEventListener('change', () => {
            if (els.tieneEnvio.checked) {
                abrirModalEnvio();
            } else {
                state.envio = null;
                renderResumenEnvio();
                renderSeleccionBar();
            }
        });
        els.envioEditar?.addEventListener('click', abrirModalEnvio);
        els.envioGuardar?.addEventListener('click', guardarModalEnvio);
        els.envioCancelar?.addEventListener('click', cancelarModalEnvio);

        // COTIZACION-MIVENTA-02: Facturar sólo visible con permiso ventas/invoice
        // (data-puede-facturar-bloque, gate server-side idéntico al de
        // VentaController.Facturar/ConfirmarYFacturar) — mismo patrón checkbox → modal →
        // resumen → Editar que ya usa Envío (§21 del pedido: alinear ambos visualmente).
        if (puedeFacturar) show(els.facturarBloque);
        els.facturarCheckbox?.addEventListener('change', () => {
            if (els.facturarCheckbox.checked) {
                abrirModalFacturar();
            } else {
                state.facturarConfig = null;
                renderResumenFacturar();
                renderSeleccionBar();
            }
        });
        els.facturarEditar?.addEventListener('click', abrirModalFacturar);
        els.facturarGuardar?.addEventListener('click', guardarModalFacturar);
        els.facturarCancelar?.addEventListener('click', cancelarModalFacturar);

        els.limpiarCliente?.addEventListener('click', () => {
            setCliente(null);
            if (els.clienteBuscar) els.clienteBuscar.value = '';
        });

        els.aptitudCredito?.addEventListener('click', event => {
            if (!event.target.closest('[data-cotizacion-reintentar-aptitud]')) return;
            state.aptitudClienteId = null; // invalida la cache para forzar una nueva consulta
            evaluarAptitudCredito();
        });

        // medios filter -> marca pendiente si ya había simulación
        $$('[data-cotizacion-medio]').forEach(input => {
            input.addEventListener('change', () => invalidarSimulacion());
        });
        els.incluirCreditoPersonal?.addEventListener('change', actualizarPrioridadAnticipo);
        actualizarPrioridadAnticipo();

        // Fecha "Válida hasta": vacía se ve como placeholder apagado (mockup); el input nativo de
        // fecha no tiene placeholder, así que CSS lo atenúa vía data-vacio.
        if (els.fechaVencimiento) {
            const marcarVacio = () => { els.fechaVencimiento.dataset.vacio = String(!els.fechaVencimiento.value); };
            els.fechaVencimiento.addEventListener('input', marcarVacio);
            els.fechaVencimiento.addEventListener('change', marcarVacio);
            marcarVacio();
        }

        // descuentos generales + anticipo -> pendiente
        [els.descuentoGralPct, els.descuentoGralImporte, els.anticipo].forEach(el => {
            el?.addEventListener('input', () => invalidarSimulacion());
        });

        // carrito: abrir confirmación de quitar
        els.productosTbody?.addEventListener('click', event => {
            // Selector % / $ del descuento por línea (COTIZACION-MOCKUP-01): sólo alterna cuál de
            // los dos <input> se ve; ningún valor cargado se modifica ni se descarta.
            const modoBtn = event.target.closest('[data-cotizacion-dto-modo]');
            if (modoBtn) {
                const index = Number(modoBtn.dataset.index);
                const modo = modoBtn.dataset.cotizacionDtoModo === 'importe' ? 'importe' : 'pct';
                if (state.productos[index]) state.productos[index].descModo = modo;
                const control = modoBtn.closest('.dto-control');
                control?.querySelectorAll('[data-cotizacion-dto-modo]').forEach(btn => {
                    btn.setAttribute('aria-pressed', String(btn === modoBtn));
                });
                const pctInput = control?.querySelector('[data-cotizacion-desc-pct-index]');
                const importeInput = control?.querySelector('[data-cotizacion-desc-importe-index]');
                pctInput?.classList.toggle('hidden', modo !== 'pct');
                importeInput?.classList.toggle('hidden', modo !== 'importe');
                (modo === 'pct' ? pctInput : importeInput)?.focus();
                return;
            }

            const deleteButton = event.target.closest('[data-cotizacion-eliminar-index]');
            if (!deleteButton) return;
            state.pendingDeleteIndex = Number(deleteButton.dataset.cotizacionEliminarIndex);
            window.openModal?.('modal-quitar-producto');
        });

        els.quitarConfirm?.addEventListener('click', () => {
            if (state.pendingDeleteIndex !== null && state.pendingDeleteIndex >= 0) {
                state.productos.splice(state.pendingDeleteIndex, 1);
                state.pendingDeleteIndex = null;
                invalidarSimulacion();
                renderProductos();
            }
            window.closeModal?.('modal-quitar-producto');
        });

        // carrito: cantidad / descuentos por producto
        els.productosTbody?.addEventListener('input', event => {
            const cantInput = event.target.closest('[data-cotizacion-cantidad-index]');
            if (cantInput) {
                const index = Number(cantInput.dataset.cotizacionCantidadIndex);
                const qty = parsePositiveInt(cantInput.value) || 1;
                state.productos[index].cantidad = qty;
                cantInput.value = String(qty);
                invalidarSimulacion();
                renderProductos();
                return;
            }

            const descPctInput = event.target.closest('[data-cotizacion-desc-pct-index]');
            if (descPctInput) {
                const index = Number(descPctInput.dataset.cotizacionDescPctIndex);
                state.productos[index].descuentoPorcentaje = parseNonNegativeDecimal(descPctInput.value);
                marcarDescuentoCargado(descPctInput, 'pct', state.productos[index].descuentoPorcentaje);
                invalidarSimulacion();
                return;
            }

            const descImporteInput = event.target.closest('[data-cotizacion-desc-importe-index]');
            if (descImporteInput) {
                const index = Number(descImporteInput.dataset.cotizacionDescImporteIndex);
                state.productos[index].descuentoImporte = parseNonNegativeDecimal(descImporteInput.value);
                marcarDescuentoCargado(descImporteInput, 'importe', state.productos[index].descuentoImporte);
                invalidarSimulacion();
                return;
            }
        });

        // resultados: expandir grupos / elegir alternativa / ver detalle
        els.resultadosTbody?.addEventListener('click', event => {
            // "Ver situación" (fila de Crédito personal sin planes): lleva la atención
            // a la evaluación ya hecha en Cliente, sin abrir el drawer de plan (no hay
            // plan que mostrar) ni repetir la consulta.
            if (event.target.closest('[data-cotizacion-ver-situacion]')) {
                els.aptitudCredito?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                els.aptitudCredito?.querySelector('.aptitud-card__detalle')?.setAttribute('open', '');
                return;
            }

            // §15: "Elegir" es la acción explícita de selección y NO abre el drawer —
            // elegir y explorar el detalle son dos intenciones distintas. Antes ambas
            // estaban pegadas al mismo click sobre la fila, así que elegir siempre
            // forzaba un drawer que había que cerrar a mano.
            const elegirBtn = event.target.closest('[data-cotizacion-elegir]');
            if (elegirBtn && els.resultadosTbody.contains(elegirBtn)) {
                event.stopPropagation();
                const row = findRowByKey(elegirBtn.dataset.cotizacionElegir);
                if (row) seleccionarRow(row, { abrirDrawer: false });
                return;
            }

            // VENTA-COTIZACION-EXCEPCION-01: "Solicitar excepción"/"Excepción solicitada" abren
            // el mismo drawer que ya usa esta fila para su detalle — el formulario de motivo
            // vive ahí (§4 del pedido), nunca seleccionan la fila (openPlanDrawer, no
            // seleccionarRow).
            const excepcionBtn = event.target.closest('[data-cotizacion-excepcion]');
            if (excepcionBtn && els.resultadosTbody.contains(excepcionBtn)) {
                event.stopPropagation();
                const row = findRowByKey(excepcionBtn.dataset.cotizacionExcepcion);
                if (row) { openPlanDrawer(row); abrirFormularioExcepcionEnDrawer(); }
                return;
            }
            const verExcepcionBtn = event.target.closest('[data-cotizacion-ver-excepcion]');
            if (verExcepcionBtn && els.resultadosTbody.contains(verExcepcionBtn)) {
                event.stopPropagation();
                const row = findRowByKey(verExcepcionBtn.dataset.cotizacionVerExcepcion);
                if (row) openPlanDrawer(row);
                return;
            }

            const parent = event.target.closest('tr.parent');
            if (parent && els.resultadosTbody.contains(parent)) {
                const open = parent.getAttribute('aria-expanded') === 'true';
                parent.setAttribute('aria-expanded', open ? 'false' : 'true');
                $$(`#cotizacion-resultados-tbody tr.detail[data-g="${parent.dataset.group}"]`).forEach(r => { r.hidden = open; });
                return;
            }

            // Click en el resto de la fila: abre el detalle del plan. Sigue marcándola
            // como seleccionada (mismo comportamiento que antes, y la barra de cierre
            // necesita una opción vigente para habilitar Continuar) — salvo un plan de
            // Crédito personal No apto: ahí el click sólo abre el detalle (para ver por
            // qué), nunca lo selecciona, porque el botón Elegir de esa fila tampoco
            // existe (accionCellHtml).
            const selectable = event.target.closest('tr[data-cotizacion-opcion-key]');
            if (selectable && els.resultadosTbody.contains(selectable)) {
                const row = findRowByKey(selectable.dataset.cotizacionOpcionKey);
                if (row) {
                    const bloqueada = esCreditoPersonalMedio(row.opcion.medioPago) && aptitudTone(state.aptitud) === 'no-apto';
                    if (bloqueada) openPlanDrawer(row);
                    else seleccionarRow(row, { abrirDrawer: true });
                }
            }
        });

        // Pie del drawer: elegir la alternativa que se estaba mirando y cerrar — sin
        // esto, llegar al detalle explorando dejaba al operador sin salida hacia la
        // decisión (tenía que cerrar y buscar la fila de nuevo).
        root.querySelector('[data-cotizacion-elegir-drawer]')?.addEventListener('click', () => {
            // VENTA-COTIZACION-EXCEPCION-01: en modo 'excepcion' este botón abre el formulario
            // de motivo en vez de seleccionar la fila (§6 — ver confirmarExcepcionEnDrawer).
            // En 'excepcion-vigente'/'bloqueado' el botón está disabled, así que nunca dispara
            // este click.
            if (state.planDrawerMode === 'excepcion') {
                abrirFormularioExcepcionEnDrawer();
                return;
            }
            const row = findRowByKey(state.planAbiertoKey);
            if (row) seleccionarRow(row, { abrirDrawer: false });
            window.closeModal?.('modal-plan');
        });

        els.planExcepcionConfirmar?.addEventListener('click', confirmarExcepcionEnDrawer);
        els.planExcepcionCancelar?.addEventListener('click', cancelarFormularioExcepcionEnDrawer);

        document.addEventListener('click', event => {
            if (!event.target.closest('#cotizacion-producto-buscar') && !event.target.closest('#cotizacion-productos-dropdown')) {
                hide(els.productosDropdown);
                els.productoBuscar?.setAttribute('aria-expanded', 'false');
            }
            if (!event.target.closest('#cotizacion-cliente-buscar') && !event.target.closest('#cotizacion-clientes-dropdown')) {
                hide(els.clientesDropdown);
                els.clienteBuscar?.setAttribute('aria-expanded', 'false');
            }
        });
    }

    bindEvents();
    renderProductos();
    // Estado inicial del cierre: sin simulación no hay opción elegida, así que
    // Continuar arranca deshabilitado con el resumen explicando qué falta (§16/§26).
    renderSeleccionBar();
})();

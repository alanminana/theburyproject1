/**
 * venta-create.js
 * Lógica de la vista Create_tw de Venta:
 *  - Búsqueda de clientes (autocomplete)
 *  - Búsqueda de productos con filtros avanzados
 *  - Gestión de detalles (agregar/eliminar filas)
 *  - Cálculo de totales (backend)
 *  - Paneles dinámicos según tipo de pago (Tarjeta / Cheque / Crédito Personal)
 *  - Verificación crediticia
 *  - Cálculo de cuotas de tarjeta
 */
(function () {
    'use strict';

    const theBury = window.TheBury || {};
    const ventaModule = window.VentaModule || {};

    // ── State ──────────────────────────────────────────────────────────
    const detalles = [];         // { productoId, codigo, nombre, cantidad, precioUnitario, descuento, subtotal, stock }
    let clienteSeleccionado = null;  // { id, nombre, apellido, tipoDocumento, numeroDocumento }
    let tarjetaInfoCache = [];   // from /api/ventas/GetTarjetasActivas
    let creditoCupoDisponible = null;
    let debounceTimer = null;
    let detalleScrollAffordance = null;
    let reintentandoSubmitConDatosTarjeta = false;
    let recargoDebitoPreview = null;
    let diagnosticoCondicionesTimer = null;
    let diagnosticoCondicionesRequestSeq = 0;
    let diagnosticoCondicionesBloqueaContinuidad = false;
    let limiteCuotasExistente = null;
    let cuotasLimitadasPorReglaExistente = false;
    let limiteCuotasDiagnostico = null;
    let cuotasLimitadasPorDiagnostico = false;
    let planesDisponibles = [];
    let configuracionPagosGlobal = null;
    let configuracionPagosGlobalDisponible = false;
    const condicionesProductoCache = new Map();

    // TipoPago enum integer values (must match Models/Enums/TipoPago.cs)
    const TIPO_PAGO = {
        Efectivo: '0',
        Transferencia: '1',
        TarjetaDebito: '2',
        TarjetaCredito: '3',
        Cheque: '4',
        CreditoPersonal: '5',
        MercadoPago: '6',
        CuentaCorriente: '7',
        Tarjeta: '8'
    };

    const TIPO_CUOTA_TARJETA = {
        SinInteres: 0,
        ConInteres: 1
    };

    // TipoTarjeta enum integer values (must match Models/Enums/TipoTarjeta.cs)
    const TIPO_TARJETA = {
        Debito: '0',
        Credito: '1'
    };

    const TIPO_PAGO_LABELS = {
        '0': 'Efectivo', '1': 'Transferencia', '2': 'Débito',
        '3': 'Crédito', '4': 'Cheque', '5': 'Créd. Personal',
        '6': 'Mercado Pago', '7': 'Cta. Cte.', '8': 'Tarjeta'
    };

    // ── DOM refs ───────────────────────────────────────────────────────
    const $ = (sel) => document.querySelector(sel);
    const inputBuscarCliente = $('#input-buscar-cliente');
    const dropdownClientes = $('#dropdown-clientes');
    const hdnClienteId = $('#hdn-cliente-id');
    const infoCliente = $('#info-cliente');
    const infoClienteNombre = $('#info-cliente-nombre');
    const infoClienteDoc = $('#info-cliente-doc');
    const btnLimpiarCliente = $('#btn-limpiar-cliente');

    const inputBuscarProducto = $('#input-buscar-producto');
    const dropdownProductos = $('#dropdown-productos');
    const panelAgregarProducto = $('#panel-agregar-producto');
    const txtProductoSeleccionado = $('#txt-producto-seleccionado');
    const hdnProductoId = $('#hdn-producto-id');
    const hdnProductoCodigo = $('#hdn-producto-codigo');
    const hdnProductoPrecio = $('#hdn-producto-precio');
    const hdnProductoStock = $('#hdn-producto-stock');
    const txtCantidad = $('#txt-cantidad');
    const txtDescuentoItem = $('#txt-descuento-item');
    const stockError = $('#stock-error');
    const btnAgregarProducto = $('#btn-agregar-producto');
    const tbodyDetalles = $('#tbody-detalles');
    const detallesVacio = $('#detalles-vacio');
    const detallesHiddenInputs = $('#detalles-hidden-inputs');

    const selectTipoPago = $('#select-tipo-pago');
    const panelTarjeta = $('#panel-tarjeta');
    const panelCheque = $('#panel-cheque');
    const panelCreditoPersonal = $('#panel-credito-personal');
    const panelVerificacionCrediticia = $('#panel-verificacion-crediticia');
    const selectTarjeta = $('#select-tarjeta');
    const selectCuotasTarjeta = $('#select-cuotas-tarjeta');
    const panelTarjetaResumen = $('#panel-tarjeta-resumen');
    const panelAvisoCuotasSinInteres = $('#panel-aviso-cuotas-sin-interes');
    const hdnTarjetaNombre = $('#hdn-tarjeta-nombre');
    const hdnTarjetaTipo = $('#hdn-tarjeta-tipo');
    const hdnConfiguracionPagoPlanId = $('#hdn-configuracion-pago-plan-id');
    const estadoConfiguracionPagosGlobal = $('#configuracion-pagos-global-estado');

    const panelPlanesPago = $('#panel-planes-pago');
    const listaPlanesPago = $('#lista-planes-pago');

    const panelAvisoCredito = $('#panel-aviso-credito');
    const panelDiagnosticoCondicionesPago = $('#panel-diagnostico-condiciones-pago');
    const diagnosticoCondicionesPagoIcon = $('#diagnostico-condiciones-pago-icon');
    const diagnosticoCondicionesPagoEstado = $('#diagnostico-condiciones-pago-estado');
    const diagnosticoCondicionesPagoResumen = $('#diagnostico-condiciones-pago-resumen');
    const diagnosticoCondicionesPagoBloqueo = $('#diagnostico-condiciones-pago-bloqueo');
    const diagnosticoCondicionesPagoDetalle = $('#diagnostico-condiciones-pago-detalle');
    const btnConfirmarVenta = $('#btn-confirmar');

    const filtroCategoria = $('#filtro-categoria');
    const filtroMarca = $('#filtro-marca');
    const filtroPrecioMin = $('#filtro-precio-min');
    const filtroPrecioMax = $('#filtro-precio-max');
    const filtroSoloStock = $('#filtro-solo-stock');
    const feedbackSlot = $('#venta-create-feedback-slot');
    const btnCerrarBannerErrores = $('#btn-cerrar-banner-errores');
    const btnImprimirBorrador = $('#btn-imprimir-borrador');
    const btnIrDocumentacion = $('#btn-ir-documentacion');
    const heroCliente = $('#hero-cliente');
    const heroClienteDetalle = $('#hero-cliente-detalle');
    const heroDetallesCount = $('#hero-detalles-count');
    const heroTotal = $('#hero-total');
    const heroTipoPago = $('#hero-tipo-pago');
    const detalleItemsBadge = $('#detalle-items-badge');

    // Totals
    const totalSubtotal = $('#total-subtotal');
    const totalDescuento = $('#total-descuento');
    const totalIva = $('#total-iva');
    const totalFinal = $('#total-final');
    const hdnSubtotal = $('#hdn-subtotal');
    const hdnDescuento = $('#hdn-descuento');
    const hdnIva = $('#hdn-iva');
    const hdnTotal = $('#hdn-total');

    // ── Helpers ────────────────────────────────────────────────────────
    const formatCurrency = theBury.formatCurrency;

    function show(el) { el?.classList.remove('hidden'); }
    function hide(el) { el?.classList.add('hidden'); }

    function formatPercent(value) {
        return new Intl.NumberFormat('es-AR', {
            maximumFractionDigits: 2
        }).format(Math.abs(Number(value) || 0));
    }

    function formatRecargoDebitoPreview(recargo) {
        return `${formatCurrency(recargo.monto)} (${formatPercent(recargo.porcentaje)}%)`;
    }

    function clearFeedback() {
        if (!feedbackSlot) return;
        feedbackSlot.hidden = true;
        feedbackSlot.className = 'hidden';
        feedbackSlot.replaceChildren();
    }

    function showFeedback(message, tone) {
        if (!feedbackSlot || !message) return;

        const palette = {
            info: {
                wrapper: 'border-primary/20 bg-primary/10 text-primary',
                icon: 'info'
            },
            warning: {
                wrapper: 'border-amber-500/20 bg-amber-500/10 text-amber-600 dark:text-amber-400',
                icon: 'warning'
            },
            error: {
                wrapper: 'border-red-500/20 bg-red-500/10 text-red-500',
                icon: 'error'
            }
        };

        const variant = palette[tone] || palette.info;
        const toast = document.createElement('div');
        toast.className = `toast-msg flex items-center gap-3 rounded-xl border p-4 text-sm font-semibold ${variant.wrapper}`;
        toast.setAttribute('role', tone === 'error' ? 'alert' : 'status');
        const icon = document.createElement('span');
        icon.className = 'material-symbols-outlined text-lg';
        icon.textContent = variant.icon;

        const text = document.createElement('p');
        text.textContent = message;

        toast.append(icon, text);

        feedbackSlot.hidden = false;
        feedbackSlot.className = '';
        feedbackSlot.replaceChildren(toast);
        if (typeof ventaModule.initSharedUi === 'function') {
            ventaModule.initSharedUi(4500);
        } else if (typeof theBury.autoDismissToasts === 'function') {
            theBury.autoDismissToasts(4500);
        }
    }

    function updateDetallesScrollAffordance() {
        if (!detalleScrollAffordance || typeof detalleScrollAffordance.update !== 'function') return;
        if (typeof ventaModule.refreshScrollAffordance === 'function') {
            ventaModule.refreshScrollAffordance(detalleScrollAffordance);
            return;
        }
        requestAnimationFrame(() => detalleScrollAffordance.update());
    }

    function actualizarResumenOperacion(total) {
        const cantidadItems = detalles.reduce((acc, d) => acc + d.cantidad, 0);
        const detalleTexto = cantidadItems === 1 ? '1 producto' : `${cantidadItems} productos`;
        const tipoPagoTexto = selectTipoPago?.selectedOptions?.[0]?.textContent?.trim() || 'Sin definir';

        if (heroDetallesCount) {
            heroDetallesCount.textContent = detalleTexto;
        }

        if (detalleItemsBadge) {
            detalleItemsBadge.innerHTML = `<span class="material-symbols-outlined text-sm">shopping_bag</span>${detalleTexto}`;
        }

        if (heroTipoPago) {
            heroTipoPago.textContent = tipoPagoTexto;
        }

        if (heroTotal) {
            heroTotal.textContent = formatCurrency(total || 0);
        }

        if (heroCliente && heroClienteDetalle) {
            if (clienteSeleccionado) {
                heroCliente.textContent = `${clienteSeleccionado.nombre} ${clienteSeleccionado.apellido}`;
                heroClienteDetalle.textContent = `${clienteSeleccionado.tipoDocumento}: ${clienteSeleccionado.numeroDocumento}`;
            } else {
                heroCliente.textContent = 'Sin seleccionar';
                heroClienteDetalle.textContent = 'Buscá un cliente para iniciar la operación.';
            }
        }
    }

    function debounce(fn, ms) {
        return function (...args) {
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(() => fn.apply(this, args), ms);
        };
    }

    function programarDiagnosticoCondicionesPago() {
        clearTimeout(diagnosticoCondicionesTimer);
    }

    async function fetchJson(url) {
        const resp = await fetch(url);
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
        return resp.json();
    }

    async function postJson(url, body) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const resp = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token || ''
            },
            body: JSON.stringify(body)
        });
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
        return resp.json();
    }

    function setEstadoConfiguracionPagosGlobal(message, tone) {
        if (!estadoConfiguracionPagosGlobal) return;

        const tones = {
            ok: 'text-emerald-400',
            warning: 'text-amber-400',
            error: 'text-red-400',
            muted: 'text-slate-500'
        };

        estadoConfiguracionPagosGlobal.className = `mt-1 text-xs ${tones[tone] || tones.muted}`;
        estadoConfiguracionPagosGlobal.textContent = message;
    }

    function normalizarMedioGlobal(medio) {
        return {
            id: Number(getProp(medio, 'id', 'Id')),
            tipoPago: String(getProp(medio, 'tipoPago', 'TipoPago')),
            nombre: getProp(medio, 'nombre', 'Nombre') || 'Medio de pago',
            activo: getProp(medio, 'activo', 'Activo') !== false,
            observaciones: getProp(medio, 'observaciones', 'Observaciones') || '',
            ajuste: getProp(medio, 'ajuste', 'Ajuste') || {},
            tarjetas: (getProp(medio, 'tarjetas', 'Tarjetas') || []).map(t => ({
                id: Number(getProp(t, 'id', 'Id')),
                nombre: getProp(t, 'nombre', 'Nombre') || 'Tarjeta',
                tipoTarjeta: getProp(t, 'tipoTarjeta', 'TipoTarjeta'),
                planesEspecificos: getProp(t, 'planesEspecificos', 'PlanesEspecificos') || []
            })),
            planesGenerales: getProp(medio, 'planesGenerales', 'PlanesGenerales') || []
        };
    }

    function getMediosGlobalesActivos() {
        return configuracionPagosGlobal?.medios || [];
    }

    function getMedioGlobalSeleccionado() {
        const tipoPago = selectTipoPago?.value;
        if (!tipoPago || !configuracionPagosGlobalDisponible) return null;
        return getMediosGlobalesActivos().find(m => m.tipoPago === String(tipoPago)) || null;
    }

    async function cargarConfiguracionPagosGlobal() {
        if (!selectTipoPago) return;

        try {
            const data = await fetchJson('/api/ventas/configuracion-pagos-global');
            const medios = (getProp(data, 'medios', 'Medios') || [])
                .map(normalizarMedioGlobal)
                .filter(m => m.activo);

            configuracionPagosGlobal = { medios };
            configuracionPagosGlobalDisponible = true;
            aplicarMediosGlobalesAlSelector(medios);

            if (medios.length === 0) {
                setEstadoConfiguracionPagosGlobal('No hay medios activos en la configuracion global.', 'warning');
            } else {
                setEstadoConfiguracionPagosGlobal('Configuracion global activa cargada.', 'ok');
            }
        } catch {
            configuracionPagosGlobal = null;
            configuracionPagosGlobalDisponible = false;
            setEstadoConfiguracionPagosGlobal('No se pudo cargar la configuracion global. Se conserva el selector actual.', 'warning');
        }

        onTipoPagoChange();
    }

    function aplicarMediosGlobalesAlSelector(medios) {
        if (!selectTipoPago || !Array.isArray(medios)) return;

        const selectedBefore = selectTipoPago.value;
        selectTipoPago.replaceChildren();

        medios.forEach(medio => {
            const opt = document.createElement('option');
            opt.value = medio.tipoPago;
            opt.textContent = medio.nombre;
            opt.dataset.configuracionPagoId = String(medio.id);
            selectTipoPago.appendChild(opt);
        });

        const mantenerSeleccion = medios.some(m => m.tipoPago === selectedBefore);
        if (mantenerSeleccion) {
            selectTipoPago.value = selectedBefore;
        } else if (selectTipoPago.options.length > 0) {
            selectTipoPago.selectedIndex = 0;
        }
    }

    function setDiagnosticoCondicionesPagoEstado(tone, estado, resumen, iconName) {
        if (!panelDiagnosticoCondicionesPago) return;

        const tones = {
            empty: {
                panel: 'border-slate-700 bg-slate-800/45',
                badge: 'border-slate-600 text-slate-300',
                icon: 'text-primary'
            },
            ok: {
                panel: 'border-emerald-500/20 bg-emerald-500/10',
                badge: 'border-emerald-500/30 text-emerald-300',
                icon: 'text-emerald-400'
            },
            warning: {
                panel: 'border-amber-500/25 bg-amber-500/10',
                badge: 'border-amber-500/30 text-amber-300',
                icon: 'text-amber-400'
            },
            blocked: {
                panel: 'border-red-500/30 bg-red-500/10',
                badge: 'border-red-500/35 text-red-200',
                icon: 'text-red-300'
            },
            error: {
                panel: 'border-slate-700 bg-slate-800/45',
                badge: 'border-slate-600 text-slate-300',
                icon: 'text-slate-400'
            }
        };
        const selected = tones[tone] || tones.empty;

        panelDiagnosticoCondicionesPago.className = `mt-6 rounded-xl border p-4 ${selected.panel}`;
        if (diagnosticoCondicionesPagoEstado) {
            diagnosticoCondicionesPagoEstado.className = `rounded-full border px-2 py-0.5 text-[10px] font-black uppercase tracking-widest ${selected.badge}`;
            diagnosticoCondicionesPagoEstado.textContent = estado;
        }
        if (diagnosticoCondicionesPagoResumen) {
            diagnosticoCondicionesPagoResumen.textContent = resumen;
        }
        if (diagnosticoCondicionesPagoIcon) {
            diagnosticoCondicionesPagoIcon.className = `material-symbols-outlined mt-0.5 ${selected.icon}`;
            diagnosticoCondicionesPagoIcon.textContent = iconName || 'rule';
        }
    }

    function actualizarBloqueoContinuidadCondicionesPago(bloqueado) {
        diagnosticoCondicionesBloqueaContinuidad = Boolean(bloqueado);

        if (diagnosticoCondicionesPagoBloqueo) {
            if (diagnosticoCondicionesBloqueaContinuidad) {
                show(diagnosticoCondicionesPagoBloqueo);
            } else {
                diagnosticoCondicionesPagoBloqueo.textContent = '';
                hide(diagnosticoCondicionesPagoBloqueo);
            }
        }

        if (!btnConfirmarVenta) return;

        btnConfirmarVenta.disabled = diagnosticoCondicionesBloqueaContinuidad;
        btnConfirmarVenta.setAttribute('aria-disabled', diagnosticoCondicionesBloqueaContinuidad ? 'true' : 'false');

        if (diagnosticoCondicionesBloqueaContinuidad) {
            btnConfirmarVenta.classList.add('cursor-not-allowed', 'opacity-60');
            btnConfirmarVenta.classList.remove('hover:bg-slate-100');
            if (diagnosticoCondicionesPagoBloqueo?.id) {
                btnConfirmarVenta.setAttribute('aria-describedby', diagnosticoCondicionesPagoBloqueo.id);
            }
            return;
        }

        btnConfirmarVenta.classList.remove('cursor-not-allowed', 'opacity-60');
        btnConfirmarVenta.classList.add('hover:bg-slate-100');
        btnConfirmarVenta.removeAttribute('aria-describedby');
    }

    function obtenerMensajeBloqueoCondicionesPago(resultado) {
        const bloqueos = getProp(resultado, 'bloqueos', 'Bloqueos') || [];
        const primero = bloqueos[0];
        const motivo = primero ? (getProp(primero, 'motivo', 'Motivo') || '').trim() : '';
        const productoId = primero ? getProp(primero, 'productoId', 'ProductoId') : null;
        const producto = productoId ? textoProductoDiagnostico(productoId) : null;

        if (motivo && producto) {
            return `No se puede confirmar con el medio seleccionado. ${producto}: ${motivo}`;
        }

        if (motivo) {
            return `No se puede confirmar con el medio seleccionado. ${motivo}`;
        }

        return 'No se puede confirmar con el medio de pago seleccionado. Cambia el medio de pago para continuar.';
    }

    function normalizarLimiteCuotas(value) {
        const numeric = Number(value);
        if (!Number.isFinite(numeric) || numeric < 1) return null;
        return Math.floor(numeric);
    }

    function getTarjetaInfoSeleccionada() {
        const tarjetaId = parseInt(selectTarjeta?.value);
        if (!tarjetaId) return null;
        return tarjetaInfoCache.find(t => Number(t.id ?? t.Id) === tarjetaId) || null;
    }

    function getTipoCuotaTarjeta(info) {
        const tipoCuota = getProp(info, 'tipoCuota', 'TipoCuota');
        const numeric = Number(tipoCuota);
        return Number.isFinite(numeric) ? numeric : null;
    }

    function getCantidadMaximaCuotasTarjeta(info) {
        return normalizarLimiteCuotas(getProp(info, 'cantidadMaximaCuotas', 'CantidadMaximaCuotas')) || 12;
    }

    function normalizarPlanGlobal(plan) {
        return {
            id: Number(getProp(plan, 'id', 'Id')),
            cantidadCuotas: Number(getProp(plan, 'cantidadCuotas', 'CantidadCuotas')) || 1,
            tipoAjuste: getProp(plan, 'tipoAjuste', 'TipoAjuste'),
            ajustePorcentaje: Number(getProp(plan, 'ajustePorcentaje', 'AjustePorcentaje')) || 0,
            etiqueta: getProp(plan, 'etiqueta', 'Etiqueta') || '',
            orden: Number(getProp(plan, 'orden', 'Orden')) || 0,
            observaciones: getProp(plan, 'observaciones', 'Observaciones') || ''
        };
    }

    function getPlanesGlobalesDisponibles() {
        const medio = getMedioGlobalSeleccionado();
        if (!medio) return [];

        const planes = [...(medio.planesGenerales || [])].map(normalizarPlanGlobal);
        const tarjetaId = parseInt(selectTarjeta?.value);
        if (tarjetaId) {
            const tarjeta = medio.tarjetas.find(t => Number(t.id) === tarjetaId);
            if (tarjeta) {
                planes.push(...(tarjeta.planesEspecificos || []).map(normalizarPlanGlobal));
            }
        }

        return planes
            .filter(p => p.cantidadCuotas >= 1)
            .sort((a, b) => a.orden - b.orden || a.cantidadCuotas - b.cantidadCuotas || a.id - b.id);
    }

    function repoblarCuotasDesdePlanesGlobales(planes) {
        if (!selectCuotasTarjeta) return;

        while (selectCuotasTarjeta.options.length) selectCuotasTarjeta.remove(0);

        const cuotas = [...new Set((planes || []).map(p => p.cantidadCuotas).filter(c => c >= 1))]
            .sort((a, b) => a - b);

        if (cuotas.length === 0) {
            const opt = document.createElement('option');
            opt.value = '1';
            opt.textContent = '1 Pago';
            selectCuotasTarjeta.appendChild(opt);
            return;
        }

        cuotas.forEach(cantidad => {
            const opt = document.createElement('option');
            opt.value = String(cantidad);
            opt.textContent = cantidad === 1 ? '1 Pago' : `${cantidad} Cuotas`;
            selectCuotasTarjeta.appendChild(opt);
        });
    }

    function renderPlanesGlobalesSeleccionados() {
        if (!configuracionPagosGlobalDisponible) {
            limpiarSelectorPlanesPago();
            return;
        }

        const planes = getPlanesGlobalesDisponibles();
        repoblarCuotasDesdePlanesGlobales(planes);
        renderSelectorPlanesPago(planes, { mostrarVacio: true });
    }

    function obtenerLimiteDiagnosticoCuotas(resultado) {
        const maxSinInteres = normalizarLimiteCuotas(getProp(resultado, 'maxCuotasSinInteres', 'MaxCuotasSinInteres'));
        const maxConInteres = normalizarLimiteCuotas(getProp(resultado, 'maxCuotasConInteres', 'MaxCuotasConInteres'));
        const maxCredito = normalizarLimiteCuotas(getProp(resultado, 'maxCuotasCredito', 'MaxCuotasCredito'));

        if (selectTipoPago?.value === TIPO_PAGO.CreditoPersonal) {
            return maxCredito;
        }

        if (!esTipoPagoTarjeta(selectTipoPago?.value)) {
            return null;
        }

        const info = getTarjetaInfoSeleccionada();
        const tipoCuota = getTipoCuotaTarjeta(info);

        if (tipoCuota === TIPO_CUOTA_TARJETA.SinInteres) {
            return maxSinInteres;
        }

        if (tipoCuota === TIPO_CUOTA_TARJETA.ConInteres) {
            return maxConInteres;
        }

        return maxConInteres ?? maxSinInteres;
    }

    function repoblarCuotasTarjeta(info) {
        if (!selectCuotasTarjeta) return;

        while (selectCuotasTarjeta.options.length) selectCuotasTarjeta.remove(0);

        if (info && getProp(info, 'permiteCuotas', 'PermiteCuotas')) {
            const maxCuotas = getCantidadMaximaCuotasTarjeta(info);
            for (let i = 1; i <= maxCuotas; i++) {
                const opt = document.createElement('option');
                opt.value = i;
                opt.textContent = i === 1 ? '1 Pago' : `${i} Cuotas`;
                selectCuotasTarjeta.appendChild(opt);
            }
            return;
        }

        const opt = document.createElement('option');
        opt.value = '1';
        opt.textContent = '1 Pago';
        selectCuotasTarjeta.appendChild(opt);
    }

    function aplicarLimiteCuotasTarjeta() {
        if (!selectCuotasTarjeta || !esTipoPagoTarjeta(selectTipoPago?.value)) {
            if (panelAvisoCuotasSinInteres) hide(panelAvisoCuotasSinInteres);
            return;
        }

        const info = getTarjetaInfoSeleccionada();
        if (!info) {
            if (panelAvisoCuotasSinInteres) hide(panelAvisoCuotasSinInteres);
            return;
        }

        const selectedBefore = parseInt(selectCuotasTarjeta.value) || 1;
        if (configuracionPagosGlobalDisponible) {
            repoblarCuotasDesdePlanesGlobales(getPlanesGlobalesDisponibles());
        } else {
            repoblarCuotasTarjeta(info);
        }

        const limites = [limiteCuotasExistente, limiteCuotasDiagnostico]
            .map(normalizarLimiteCuotas)
            .filter(v => v != null);
        const limiteEfectivo = limites.length > 0 ? Math.min(...limites) : null;

        if (limiteEfectivo != null) {
            for (let i = selectCuotasTarjeta.options.length - 1; i >= 0; i--) {
                if (parseInt(selectCuotasTarjeta.options[i].value) > limiteEfectivo) {
                    selectCuotasTarjeta.remove(i);
                }
            }
        }

        const maxDisponible = Array.from(selectCuotasTarjeta.options)
            .map(opt => parseInt(opt.value))
            .filter(Number.isFinite)
            .reduce((max, value) => Math.max(max, value), 1);
        selectCuotasTarjeta.value = String(Math.min(selectedBefore, maxDisponible));

        const mostrarAviso = cuotasLimitadasPorReglaExistente || cuotasLimitadasPorDiagnostico;
        if (panelAvisoCuotasSinInteres) {
            mostrarAviso ? show(panelAvisoCuotasSinInteres) : hide(panelAvisoCuotasSinInteres);
        }
    }

    function limpiarDetalleDiagnosticoCondicionesPago() {
        if (!diagnosticoCondicionesPagoDetalle) return;
        diagnosticoCondicionesPagoDetalle.replaceChildren();
        hide(diagnosticoCondicionesPagoDetalle);
    }

    function textoProductoDiagnostico(productoId) {
        const detalle = detalles.find(d => Number(d.productoId) === Number(productoId));
        return detalle ? `${detalle.codigo} - ${detalle.nombre}` : `Producto #${productoId}`;
    }

    function getProp(obj, camel, pascal) {
        return obj?.[camel] ?? obj?.[pascal];
    }

    function crearBloqueDiagnostico(titulo, items, mapper) {
        if (!items || items.length === 0) return null;

        const wrapper = document.createElement('div');
        wrapper.className = 'rounded-lg border border-slate-700/70 bg-slate-900/40 p-3';
        const heading = document.createElement('p');
        heading.className = 'mb-2 text-[10px] font-black uppercase tracking-widest text-slate-400';
        heading.textContent = titulo;
        const list = document.createElement('ul');
        list.className = 'space-y-1 text-slate-300';

        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'flex items-start gap-2';
            const bullet = document.createElement('span');
            bullet.className = 'mt-1.5 size-1.5 shrink-0 rounded-full bg-current opacity-70';
            const text = document.createElement('span');
            text.textContent = mapper(item);
            li.append(bullet, text);
            list.appendChild(li);
        });

        wrapper.append(heading, list);
        return wrapper;
    }

    function renderDiagnosticoCondicionesPago(resultado) {
        const permitido = Boolean(getProp(resultado, 'permitido', 'Permitido'));
        const productoIdsBloqueantes = getProp(resultado, 'productoIdsBloqueantes', 'ProductoIdsBloqueantes') || [];
        const productoIdsRestrictivos = getProp(resultado, 'productoIdsRestrictivos', 'ProductoIdsRestrictivos') || [];
        const bloqueos = getProp(resultado, 'bloqueos', 'Bloqueos') || [];
        const restricciones = getProp(resultado, 'restricciones', 'Restricciones') || [];
        const ajustes = getProp(resultado, 'ajustesInformativos', 'AjustesInformativos') || [];
        const maxSinInteres = getProp(resultado, 'maxCuotasSinInteres', 'MaxCuotasSinInteres');
        const maxConInteres = getProp(resultado, 'maxCuotasConInteres', 'MaxCuotasConInteres');
        const maxCredito = getProp(resultado, 'maxCuotasCredito', 'MaxCuotasCredito');
        const planesRaw = getProp(resultado, 'planesDisponibles', 'PlanesDisponibles') || [];
        const mensajeBloqueo = obtenerMensajeBloqueoCondicionesPago(resultado);

        renderSelectorPlanesPago(planesRaw);

        setDiagnosticoCondicionesPagoEstado(
            permitido ? 'ok' : 'blocked',
            permitido ? 'Permitido' : 'Bloqueado',
            permitido
                ? 'El carrito no informa bloqueos para el medio de pago seleccionado. La venta sigue sin cambios productivos.'
                : mensajeBloqueo,
            permitido ? 'check_circle' : 'block'
        );
        if (!permitido && diagnosticoCondicionesPagoBloqueo) {
            diagnosticoCondicionesPagoBloqueo.textContent = mensajeBloqueo;
        }
        actualizarBloqueoContinuidadCondicionesPago(!permitido);
        limiteCuotasDiagnostico = obtenerLimiteDiagnosticoCuotas(resultado);
        cuotasLimitadasPorDiagnostico = limiteCuotasDiagnostico != null;
        aplicarLimiteCuotasTarjeta();

        if (!diagnosticoCondicionesPagoDetalle) return;
        diagnosticoCondicionesPagoDetalle.replaceChildren();

        const maximos = [];
        if (maxSinInteres != null) maximos.push(`Cuotas sin interes: hasta ${maxSinInteres}`);
        if (maxConInteres != null) maximos.push(`Cuotas con interes: hasta ${maxConInteres}`);
        if (maxCredito != null) maximos.push(`Credito personal: hasta ${maxCredito}`);

        const bloques = [
            crearBloqueDiagnostico('Productos bloqueantes', productoIdsBloqueantes, textoProductoDiagnostico),
            crearBloqueDiagnostico('Productos restrictivos', productoIdsRestrictivos, textoProductoDiagnostico),
            crearBloqueDiagnostico('Bloqueos detallados', bloqueos, b => {
                const productoId = getProp(b, 'productoId', 'ProductoId');
                const motivo = getProp(b, 'motivo', 'Motivo') || 'Condicion bloqueante configurada.';
                return `${textoProductoDiagnostico(productoId)}: ${motivo}`;
            }),
            crearBloqueDiagnostico('Maximos efectivos informativos', maximos, x => x),
            crearBloqueDiagnostico('Restricciones detalladas', restricciones, r => {
                const productoId = getProp(r, 'productoId', 'ProductoId');
                const valor = getProp(r, 'valor', 'Valor');
                const tipo = getProp(r, 'tipoRestriccion', 'TipoRestriccion');
                return `${textoProductoDiagnostico(productoId)}: ${tipo} = ${valor}`;
            }),
            crearBloqueDiagnostico('Ajustes configurados informativos', ajustes, a => {
                const productoId = getProp(a, 'productoId', 'ProductoId');
                const recargo = getProp(a, 'porcentajeRecargo', 'PorcentajeRecargo');
                const descuento = getProp(a, 'porcentajeDescuentoMaximo', 'PorcentajeDescuentoMaximo');
                const partes = [];
                if (recargo != null) partes.push(`recargo ${formatPercent(recargo)}%`);
                if (descuento != null) partes.push(`descuento maximo ${formatPercent(descuento)}%`);
                return `${textoProductoDiagnostico(productoId)}: ${partes.join(' / ') || 'ajuste configurado'} (no aplicado al total)`;
            })
        ].filter(Boolean);

        if (bloques.length === 0) {
            limpiarDetalleDiagnosticoCondicionesPago();
            return;
        }

        bloques.forEach(b => diagnosticoCondicionesPagoDetalle.appendChild(b));
        show(diagnosticoCondicionesPagoDetalle);
    }

    function obtenerTipoTarjetaDiagnostico() {
        const value = hdnTarjetaTipo?.value;
        if (value === '' || value == null) return null;
        const numeric = Number(value);
        return Number.isFinite(numeric) ? numeric : null;
    }

    btnCerrarBannerErrores?.addEventListener('click', function () {
        $('#banner-errores')?.remove();
    });

    btnImprimirBorrador?.addEventListener('click', function () {
        if (typeof globalThis.print === 'function') {
            globalThis.print();
        }
    });

    btnIrDocumentacion?.addEventListener('click', function () {
        const href = this.dataset.href;
        if (!href) return;

        if (typeof globalThis.open === 'function') {
            globalThis.open(href, '_blank', 'noopener');
            return;
        }

        globalThis.location.href = href;
    });

    // ── 1. Client Search ──────────────────────────────────────────────
    inputBuscarCliente?.addEventListener('input', debounce(async function () {
        const term = this.value.trim();
        if (term.length < 2) { hide(dropdownClientes); return; }

        try {
            const data = await fetchJson(`/api/ventas/BuscarClientes?term=${encodeURIComponent(term)}&take=10`);
            if (!data.length) { hide(dropdownClientes); return; }

            dropdownClientes.innerHTML = data.map(c => `
                <div class="px-4 py-3 hover:bg-slate-50 dark:hover:bg-slate-700 cursor-pointer border-b border-slate-100 dark:border-slate-700 last:border-0"
                     data-id="${c.id}" data-nombre="${c.nombre}" data-apellido="${c.apellido}" data-tipo-doc="${c.tipoDocumento}" data-num-doc="${c.numeroDocumento}">
                    <p class="text-sm font-medium text-slate-900 dark:text-white">${c.display}</p>
                    <p class="text-xs text-slate-500">${c.tipoDocumento}: ${c.numeroDocumento} ${c.telefono ? '· ' + c.telefono : ''}</p>
                </div>
            `).join('');
            show(dropdownClientes);
        } catch { hide(dropdownClientes); }
    }, 300));

    dropdownClientes?.addEventListener('click', function (e) {
        const item = e.target.closest('[data-id]');
        if (!item) return;

        clienteSeleccionado = {
            id: item.dataset.id,
            nombre: item.dataset.nombre,
            apellido: item.dataset.apellido,
            tipoDocumento: item.dataset.tipoDoc,
            numeroDocumento: item.dataset.numDoc
        };

        hdnClienteId.value = clienteSeleccionado.id;
        infoClienteNombre.textContent = `${clienteSeleccionado.nombre} ${clienteSeleccionado.apellido}`;
        infoClienteDoc.textContent = `${clienteSeleccionado.tipoDocumento}: ${clienteSeleccionado.numeroDocumento}`;

        show(infoCliente);
        hide(dropdownClientes);
        inputBuscarCliente.value = '';
        hide(inputBuscarCliente.parentElement);
        actualizarResumenOperacion(parseFloat(hdnTotal?.value) || 0);

        invalidarVerificacionCrediticia();
        onTipoPagoChange();
    });

    btnLimpiarCliente?.addEventListener('click', function () {
        clienteSeleccionado = null;
        hdnClienteId.value = '';
        infoClienteNombre.textContent = '';
        infoClienteDoc.textContent = '';
        hide(infoCliente);
        show(inputBuscarCliente.parentElement);
        inputBuscarCliente.value = '';
        invalidarVerificacionCrediticia();
        inputBuscarCliente.focus();
        actualizarResumenOperacion(parseFloat(hdnTotal?.value) || 0);
    });

    // Close dropdowns on outside click
    document.addEventListener('click', function (e) {
        if (!e.target.closest('#input-buscar-cliente') && !e.target.closest('#dropdown-clientes')) {
            hide(dropdownClientes);
        }
        if (!e.target.closest('#input-buscar-producto') && !e.target.closest('#dropdown-productos')) {
            hide(dropdownProductos);
        }
    });

    // ── 2. Product Search ─────────────────────────────────────────────
    inputBuscarProducto?.addEventListener('input', debounce(async function () {
        const term = this.value.trim();
        if (term.length < 2) { hide(dropdownProductos); return; }

        const params = new URLSearchParams({ term, take: 20 });
        const catId = filtroCategoria?.value;
        const marId = filtroMarca?.value;
        const pMin = filtroPrecioMin?.value;
        const pMax = filtroPrecioMax?.value;
        const soloStock = filtroSoloStock?.checked;

        if (catId) params.set('categoriaId', catId);
        if (marId) params.set('marcaId', marId);
        if (pMin) params.set('precioMin', pMin);
        if (pMax) params.set('precioMax', pMax);
        if (soloStock !== undefined) params.set('soloConStock', soloStock);

        try {
            const data = await fetchJson(`/api/ventas/BuscarProductos?${params}`);
            if (!data.length) {
                dropdownProductos.innerHTML = '<div class="px-4 py-3 text-sm text-slate-400">Sin resultados</div>';
                show(dropdownProductos);
                return;
            }

            dropdownProductos.innerHTML = data.map(p => `
                <div class="px-4 py-3 hover:bg-slate-50 dark:hover:bg-slate-700 cursor-pointer border-b border-slate-100 dark:border-slate-700 last:border-0"
                     data-id="${p.id}" data-codigo="${p.codigo}" data-nombre="${p.nombre}" data-precio="${p.precioVenta}" data-stock="${p.stockActual}">
                    <div class="flex items-center justify-between">
                        <p class="text-sm font-medium text-slate-900 dark:text-white">${p.nombre}</p>
                        <span class="text-xs font-bold text-primary">${formatCurrency(p.precioVenta)}</span>
                    </div>
                    <p class="text-xs text-slate-500">${p.codigo} ${p.marca ? '· ' + p.marca : ''} ${p.categoria ? '· ' + p.categoria : ''} · Stock: ${p.stockActual}</p>
                    ${p.caracteristicasResumen ? `<p class="text-[10px] text-slate-400 mt-0.5">${p.caracteristicasResumen}</p>` : ''}
                </div>
            `).join('');
            show(dropdownProductos);
        } catch { hide(dropdownProductos); }
    }, 300));

    dropdownProductos?.addEventListener('click', function (e) {
        const item = e.target.closest('[data-id]');
        if (!item) return;

        hdnProductoId.value = item.dataset.id;
        hdnProductoCodigo.value = item.dataset.codigo;
        hdnProductoPrecio.value = item.dataset.precio;
        hdnProductoStock.value = item.dataset.stock;
        txtProductoSeleccionado.value = `${item.dataset.codigo} - ${item.dataset.nombre}`;
        txtCantidad.value = 1;
        txtDescuentoItem.value = 0;
        hide(stockError);

        show(panelAgregarProducto);
        hide(dropdownProductos);
        inputBuscarProducto.value = '';
    });

    // Stock validation on quantity change
    txtCantidad?.addEventListener('input', function () {
        const qty = parseInt(this.value) || 0;
        const stock = parseInt(hdnProductoStock.value) || 0;
        if (qty > stock) {
            this.classList.add('border-red-500', 'ring-red-500', 'text-red-500');
            stockError.textContent = `Stock insuficiente (Máx: ${stock})`;
            show(stockError);
        } else {
            this.classList.remove('border-red-500', 'ring-red-500', 'text-red-500');
            hide(stockError);
        }
    });

    // ── 3. Add Product ────────────────────────────────────────────────
    btnAgregarProducto?.addEventListener('click', function () {
        const productoId = parseInt(hdnProductoId.value);
        const codigo = hdnProductoCodigo.value;
        const nombre = txtProductoSeleccionado.value.replace(`${codigo} - `, '');
        const precioUnitario = parseFloat(hdnProductoPrecio.value) || 0;
        const stock = parseInt(hdnProductoStock.value) || 0;
        const cantidad = parseInt(txtCantidad.value) || 0;
        const descuentoPct = parseFloat(txtDescuentoItem.value) || 0;

        if (!productoId || cantidad <= 0) return;

        if (cantidad > stock) {
            stockError.textContent = `Stock insuficiente (Máx: ${stock})`;
            show(stockError);
            return;
        }

        // Check if product already exists
        const existing = detalles.find(d => d.productoId === productoId);
        if (existing) {
            const newQty = existing.cantidad + cantidad;
            if (newQty > stock) {
                stockError.textContent = `Stock insuficiente. Ya tiene ${existing.cantidad} unid. (Máx: ${stock})`;
                show(stockError);
                return;
            }
            existing.cantidad = newQty;
            existing.descuento = descuentoPct;
            existing.subtotal = calcularSubtotalLinea(existing.precioUnitario, newQty, descuentoPct);
        } else {
            const subtotal = calcularSubtotalLinea(precioUnitario, cantidad, descuentoPct);
            detalles.push({ productoId, codigo, nombre, cantidad, precioUnitario, descuento: descuentoPct, subtotal, stock });
        }

        // Reset add panel
        hide(panelAgregarProducto);
        hdnProductoId.value = '';
        txtProductoSeleccionado.value = '';

        renderDetalles();
        invalidarVerificacionCrediticia();
        recalcularTotales();
    });

    function calcularSubtotalLinea(precio, cantidad, descPct) {
        const bruto = precio * cantidad;
        return bruto - (bruto * descPct / 100);
    }

    // ── 4. Render Details Table ───────────────────────────────────────
    function renderDetalles() {
        if (detalles.length === 0) {
            tbodyDetalles.innerHTML = '';
            show(detallesVacio);
            detallesHiddenInputs.innerHTML = '';
            actualizarResumenOperacion(parseFloat(hdnTotal?.value) || 0);
            programarDiagnosticoCondicionesPago();
            return;
        }

        hide(detallesVacio);

        tbodyDetalles.innerHTML = detalles.map((d, i) => `
            <tr>
                <td class="py-4 px-2 text-xs font-mono">${d.codigo}</td>
                <td class="py-4 px-2 text-sm font-medium">${d.nombre}</td>
                <td class="py-4 px-2 text-sm text-center">${d.cantidad}</td>
                <td class="py-4 px-2 text-sm text-right">${formatCurrency(d.precioUnitario)}</td>
                <td class="py-4 px-2 text-sm text-right">${d.descuento}%</td>
                <td class="py-4 px-2 text-sm font-bold text-right">${formatCurrency(d.subtotal)}</td>
                <td class="py-4 px-2 text-right">
                    <button type="button" class="btn-eliminar-detalle text-slate-400 hover:text-red-500 transition-colors" data-index="${i}">
                        <span class="material-symbols-outlined text-lg">delete</span>
                    </button>
                </td>
            </tr>
        `).join('');

        // Hidden inputs for form posting
        detallesHiddenInputs.innerHTML = '';
        detalles.forEach((d, i) => {
            const mkHidden = (name, val) => {
                const el = document.createElement('input');
                el.type = 'hidden';
                el.name = name;
                el.value = val;
                return el;
            };
            detallesHiddenInputs.appendChild(mkHidden(`Detalles[${i}].ProductoId`, d.productoId));
            detallesHiddenInputs.appendChild(mkHidden(`Detalles[${i}].Cantidad`, d.cantidad));
            detallesHiddenInputs.appendChild(mkHidden(`Detalles[${i}].PrecioUnitario`, d.precioUnitario));
            detallesHiddenInputs.appendChild(mkHidden(`Detalles[${i}].Descuento`, d.descuento));
            detallesHiddenInputs.appendChild(mkHidden(`Detalles[${i}].Subtotal`, d.subtotal));
        });

        actualizarResumenOperacion(parseFloat(hdnTotal?.value) || 0);
        updateDetallesScrollAffordance();
        programarDiagnosticoCondicionesPago();
    }

    // Delete detail row
    tbodyDetalles?.addEventListener('click', function (e) {
        const btn = e.target.closest('.btn-eliminar-detalle');
        if (!btn) return;
        const idx = parseInt(btn.dataset.index);
        detalles.splice(idx, 1);
        renderDetalles();
        invalidarVerificacionCrediticia();
        recalcularTotales();
    });

    async function recalcularTotales() {
        if (detalles.length === 0) {
            actualizarTotalesUI(0, 0, 0, 0);
            programarDiagnosticoCondicionesPago();
            return;
        }

        try {
            const tarjetaId = parseInt(selectTarjeta?.value) || null;
            const planId = parseInt(hdnConfiguracionPagoPlanId?.value) || null;
            const body = {
                detalles: detalles.map(d => ({
                    productoId: d.productoId,
                    cantidad: d.cantidad,
                    precioUnitario: d.precioUnitario,
                    descuento: d.descuento
                })),
                descuentoGeneral: 0,
                descuentoEsPorcentaje: true,
                tipoPago: parseInt(selectTipoPago?.value) || 0,
                tarjetaId: tarjetaId,
                configuracionPagoPlanId: planId
            };

            const result = await postJson('/api/ventas/CalcularTotalesVenta', body);
            actualizarTotalesUI(result.subtotal, result.descuentoGeneralAplicado, result.iva, result.total, result);
            aplicarLimiteCuotasSinInteres(result.maxCuotasSinInteresEfectivo ?? null, result.cuotasSinInteresLimitadasPorProducto ?? false);
        } catch {
            // Fallback visual only: do not infer IVA in the UI.
            const total = detalles.reduce((acc, d) => acc + d.subtotal, 0);
            actualizarTotalesUI(total, 0, 0, total);
        }

        // Update credit availability notice
        actualizarAvisoCredito();
        programarDiagnosticoCondicionesPago();
    }

    function aplicarLimiteCuotasSinInteres(maxEfectivo, limitadoPorProducto) {
        limiteCuotasExistente = normalizarLimiteCuotas(maxEfectivo);
        cuotasLimitadasPorReglaExistente = Boolean(limitadoPorProducto && limiteCuotasExistente != null);
        aplicarLimiteCuotasTarjeta();
    }

    function actualizarTotalesUI(subtotal, descuento, iva, total, backendResult) {
        const recargoDebito = Number(backendResult?.recargoDebitoAplicado) || 0;
        const porcentajeRecargoDebito = Number(backendResult?.porcentajeRecargoDebitoAplicado) || 0;
        const totalConAjustePagoGlobal = backendResult?.totalConAjustePagoGlobal;
        const totalConAjusteItems = backendResult?.totalConAjusteItems;
        const totalBase = totalConAjustePagoGlobal ?? totalConAjusteItems ?? total;
        const totalDisplay = recargoDebito > 0
            ? totalBase + recargoDebito
            : (backendResult?.totalConRecargoDebito ?? totalBase);

        if (totalSubtotal) totalSubtotal.textContent = formatCurrency(subtotal);
        if (totalDescuento) totalDescuento.textContent = `-${formatCurrency(descuento)}`;
        if (totalIva) totalIva.textContent = formatCurrency(iva);
        if (totalFinal) totalFinal.textContent = formatCurrency(totalDisplay);

        if (hdnSubtotal) hdnSubtotal.value = subtotal.toFixed(2);
        if (hdnDescuento) hdnDescuento.value = descuento.toFixed(2);
        if (hdnIva) hdnIva.value = iva.toFixed(2);
        if (hdnTotal) hdnTotal.value = totalBase.toFixed(2);
        actualizarResumenOperacion(totalDisplay);

        const tarjetaRecargo = $('#tarjeta-recargo');
        if (tarjetaRecargo && recargoDebito > 0) {
            recargoDebitoPreview = {
                monto: recargoDebito,
                porcentaje: porcentajeRecargoDebito
            };
            tarjetaRecargo.textContent = formatRecargoDebitoPreview(recargoDebitoPreview);
        } else {
            recargoDebitoPreview = null;
        }

        // Actualizar aviso de crédito si corresponde
        if (selectTipoPago?.value === TIPO_PAGO.CreditoPersonal) {
            actualizarAvisoCredito(creditoCupoDisponible);
        }
    }

    // ── 6. Payment Type Switch ────────────────────────────────────────
    selectTipoPago?.addEventListener('change', onTipoPagoChange);

    function esTipoPagoTarjeta(tipoPago) {
        return tipoPago === TIPO_PAGO.TarjetaCredito || tipoPago === TIPO_PAGO.TarjetaDebito;
    }

    function esTipoPagoConPlanes(tipoPago) {
        return tipoPago === TIPO_PAGO.TarjetaCredito ||
               tipoPago === TIPO_PAGO.TarjetaDebito ||
               tipoPago === TIPO_PAGO.MercadoPago;
    }

    function onTipoPagoChange() {
        invalidarVerificacionCrediticia();

        const val = selectTipoPago.value;

        const isTarjeta = esTipoPagoTarjeta(val);
        const isCheque = val === TIPO_PAGO.Cheque;
        const isCredito = val === TIPO_PAGO.CreditoPersonal;

        // Individual sub-panels toggle
        isTarjeta ? show(panelTarjeta) : hide(panelTarjeta);
        isCheque ? show(panelCheque) : hide(panelCheque);
        isCredito ? show(panelCreditoPersonal) : hide(panelCreditoPersonal);
        isCredito ? show(panelVerificacionCrediticia) : hide(panelVerificacionCrediticia);

        // Plan selector reset: always clear on payment type change, re-populated by diagnostico
        limpiarSelectorPlanesPago();
        tarjetaInfoCache = [];

        // Fetch card info if needed
        if (isTarjeta && configuracionPagosGlobalDisponible) {
            cargarTarjetasActivas();
        } else if (isTarjeta && tarjetaInfoCache.length === 0) {
            cargarTarjetasActivas();
        } else if (isTarjeta) {
            poblarDatosTarjetaSeleccionada();
        } else if (val === TIPO_PAGO.MercadoPago) {
            if (hdnTarjetaNombre) hdnTarjetaNombre.value = 'Mercado Pago';
            if (hdnTarjetaTipo) hdnTarjetaTipo.value = TIPO_TARJETA.Debito;
        } else {
            limpiarDatosTarjetaSeleccionada();
        }

        if (!isTarjeta && configuracionPagosGlobalDisponible) {
            renderPlanesGlobalesSeleccionados();
        }

        limiteCuotasDiagnostico = null;
        cuotasLimitadasPorDiagnostico = false;
        aplicarLimiteCuotasTarjeta();
        actualizarResumenOperacion(parseFloat(hdnTotal?.value) || 0);
        renderDetalles();
        programarDiagnosticoCondicionesPago();
    }

    // ── 7. Card Payment ───────────────────────────────────────────────
    async function cargarTarjetasActivas() {
        if (configuracionPagosGlobalDisponible) {
            const medio = getMedioGlobalSeleccionado();
            tarjetaInfoCache = (medio?.tarjetas || []).map(t => ({
                id: Number(t.id),
                nombre: t.nombre,
                tipo: t.tipoTarjeta,
                tipoTarjeta: t.tipoTarjeta,
                permiteCuotas: (t.planesEspecificos || []).length > 0 || (medio?.planesGenerales || []).length > 0,
                cantidadMaximaCuotas: Math.max(
                    1,
                    ...(medio?.planesGenerales || []).map(p => Number(getProp(p, 'cantidadCuotas', 'CantidadCuotas')) || 1),
                    ...(t.planesEspecificos || []).map(p => Number(getProp(p, 'cantidadCuotas', 'CantidadCuotas')) || 1))
            }));
            poblarSelectTarjetasGlobales(tarjetaInfoCache);
            poblarDatosTarjetaSeleccionada();
            return tarjetaInfoCache;
        }

        if (tarjetaInfoCache.length > 0) {
            return tarjetaInfoCache;
        }

        try {
            tarjetaInfoCache = await fetchJson('/api/ventas/GetTarjetasActivas');
            poblarDatosTarjetaSeleccionada();
        } catch { /* tarjetas already loaded from ViewBag */ }

        return tarjetaInfoCache;
    }

    function poblarSelectTarjetasGlobales(tarjetas) {
        if (!selectTarjeta) return;

        const selectedBefore = selectTarjeta.value;
        selectTarjeta.replaceChildren();

        const empty = document.createElement('option');
        empty.value = '';
        empty.textContent = tarjetas.length === 0 ? 'Sin tarjetas activas' : 'Seleccione tarjeta...';
        selectTarjeta.appendChild(empty);

        tarjetas.forEach(tarjeta => {
            const opt = document.createElement('option');
            opt.value = String(tarjeta.id);
            opt.textContent = tarjeta.nombre;
            selectTarjeta.appendChild(opt);
        });

        if (tarjetas.some(t => String(t.id) === selectedBefore)) {
            selectTarjeta.value = selectedBefore;
        }
    }

    function limpiarDatosTarjetaSeleccionada() {
        if (hdnTarjetaNombre) hdnTarjetaNombre.value = '';
        if (hdnTarjetaTipo) hdnTarjetaTipo.value = '';
        if (hdnConfiguracionPagoPlanId) hdnConfiguracionPagoPlanId.value = '';
        limpiarSelectorPlanesPago();
    }

    function limpiarSelectorPlanesPago() {
        planesDisponibles = [];
        if (hdnConfiguracionPagoPlanId) hdnConfiguracionPagoPlanId.value = '';
        if (listaPlanesPago) listaPlanesPago.replaceChildren();
        hide(panelPlanesPago);
    }

    function formatearEtiquetaPlan(plan) {
        const cuotas = plan.cantidadCuotas ?? getProp(plan, 'cantidadCuotas', 'CantidadCuotas');
        const ajuste = parseFloat(getProp(plan, 'ajustePorcentaje', 'AjustePorcentaje') ?? 0);
        const label = cuotas === 1 ? '1 pago' : `${cuotas} cuotas`;
        let ajusteLabel;
        if (ajuste === 0) {
            ajusteLabel = 'sin ajuste';
        } else if (ajuste > 0) {
            ajusteLabel = `+${formatPercent(ajuste)}% informativo`;
        } else {
            ajusteLabel = `${formatPercent(ajuste)}% informativo`;
        }
        return `${label} · ${ajusteLabel}`;
    }

    function renderSelectorPlanesPago(planes, options = {}) {
        const tipoPago = selectTipoPago?.value;
        if (!esTipoPagoConPlanes(tipoPago)) {
            limpiarSelectorPlanesPago();
            return;
        }

        if (!listaPlanesPago) return;

        planesDisponibles = planes || [];
        listaPlanesPago.replaceChildren();

        if (planesDisponibles.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'rounded-lg border border-slate-700 bg-slate-900/60 px-3 py-2 text-xs text-slate-400';
            empty.textContent = 'No hay planes activos configurados para el medio seleccionado.';
            listaPlanesPago.appendChild(empty);
            options.mostrarVacio ? show(panelPlanesPago) : hide(panelPlanesPago);
            return;
        }

        planesDisponibles.forEach(plan => {
            const planId = getProp(plan, 'id', 'Id');
            const etiqueta = formatearEtiquetaPlan(plan);
            const obs = getProp(plan, 'observaciones', 'Observaciones');

            const btn = document.createElement('button');
            btn.type = 'button';
            btn.role = 'radio';
            btn.setAttribute('aria-checked', 'false');
            btn.dataset.planId = planId;
            btn.className = 'plan-pago-btn px-3 py-2 rounded-lg border border-slate-600 bg-slate-800 text-xs text-slate-300 hover:border-primary hover:text-white transition-colors text-left';
            btn.setAttribute('aria-label', etiqueta + (obs ? '. ' + obs : ''));

            const spanLabel = document.createElement('span');
            spanLabel.className = 'block font-semibold';
            spanLabel.textContent = etiqueta;
            btn.appendChild(spanLabel);

            if (obs) {
                const spanObs = document.createElement('span');
                spanObs.className = 'block text-slate-500 text-[10px] mt-0.5';
                spanObs.textContent = obs;
                btn.appendChild(spanObs);
            }

            btn.addEventListener('click', () => seleccionarPlan(planId, btn));
            listaPlanesPago.appendChild(btn);
        });

        const planSeleccionado = hdnConfiguracionPagoPlanId?.value;
        const btnSeleccionado = planSeleccionado
            ? listaPlanesPago.querySelector(`.plan-pago-btn[data-plan-id="${planSeleccionado}"]`)
            : listaPlanesPago.querySelector('.plan-pago-btn');
        if (btnSeleccionado) {
            seleccionarPlan(btnSeleccionado.dataset.planId, btnSeleccionado);
        }

        show(panelPlanesPago);
    }

    function seleccionarPlan(planId, btnActivo) {
        if (hdnConfiguracionPagoPlanId) hdnConfiguracionPagoPlanId.value = planId ? String(planId) : '';

        const plan = planesDisponibles.find(p => Number(p.id ?? getProp(p, 'id', 'Id')) === Number(planId));
        const cuotas = Number(plan?.cantidadCuotas ?? getProp(plan, 'cantidadCuotas', 'CantidadCuotas')) || 1;
        if (selectCuotasTarjeta) {
            selectCuotasTarjeta.value = String(cuotas);
        }

        listaPlanesPago?.querySelectorAll('.plan-pago-btn').forEach(btn => {
            const activo = btn === btnActivo;
            btn.setAttribute('aria-checked', activo ? 'true' : 'false');
            btn.classList.toggle('border-primary', activo);
            btn.classList.toggle('text-white', activo);
            btn.classList.toggle('bg-primary/10', activo);
            btn.classList.toggle('border-slate-600', !activo);
            btn.classList.toggle('text-slate-300', !activo);
            btn.classList.toggle('bg-slate-800', !activo);
        });

        recalcularTotales();
    }

    function poblarDatosTarjetaSeleccionada() {
        const tarjetaId = parseInt(selectTarjeta?.value);
        if (!tarjetaId) {
            limpiarDatosTarjetaSeleccionada();
            return null;
        }

        const info = tarjetaInfoCache.find(t => Number(t.id) === tarjetaId);
        if (!info) {
            limpiarDatosTarjetaSeleccionada();
            return null;
        }

        if (hdnTarjetaNombre) hdnTarjetaNombre.value = info.nombre || '';
        if (hdnTarjetaTipo) hdnTarjetaTipo.value = info.tipo ?? info.tipoTarjeta ?? '';
        renderPlanesGlobalesSeleccionados();
        return info;
    }

    selectTarjeta?.addEventListener('change', async function () {
        condicionesProductoCache.clear();
        const tarjetaId = parseInt(this.value);
        if (!tarjetaId) {
            limpiarDatosTarjetaSeleccionada();
            renderPlanesGlobalesSeleccionados();
            hide(panelTarjetaResumen);
            limiteCuotasExistente = null;
            limiteCuotasDiagnostico = null;
            cuotasLimitadasPorReglaExistente = false;
            cuotasLimitadasPorDiagnostico = false;
            if (panelAvisoCuotasSinInteres) hide(panelAvisoCuotasSinInteres);
            programarDiagnosticoCondicionesPago();
            return;
        }

        let info = poblarDatosTarjetaSeleccionada();
        if (!info) {
            await cargarTarjetasActivas();
            info = poblarDatosTarjetaSeleccionada();
        }

        limiteCuotasDiagnostico = null;
        cuotasLimitadasPorDiagnostico = false;
        if (configuracionPagosGlobalDisponible) {
            renderPlanesGlobalesSeleccionados();
        } else {
            repoblarCuotasTarjeta(info);
        }

        // Refresh totals with the new tarjetaId so informational cuotas data stays current.
        await recalcularTotales();
        calcularCuotasTarjeta();
        programarDiagnosticoCondicionesPago();
    });

    selectCuotasTarjeta?.addEventListener('change', calcularCuotasTarjeta);

    async function calcularCuotasTarjeta() {
        const tarjetaId = parseInt(selectTarjeta?.value);
        const cuotas = parseInt(selectCuotasTarjeta?.value) || 1;
        const total = parseFloat(hdnTotal?.value) || 0;

        if (!tarjetaId || total <= 0) { hide(panelTarjetaResumen); return; }

        try {
            const data = await fetchJson(`/api/ventas/CalcularCuotasTarjeta?tarjetaId=${tarjetaId}&monto=${total}&cuotas=${cuotas}`);

            $('#tarjeta-monto-cuota').textContent = formatCurrency(data.montoCuota);
            $('#tarjeta-total-interes').textContent = formatCurrency(data.montoTotal);

            if (recargoDebitoPreview?.monto > 0) {
                $('#tarjeta-recargo').textContent = formatRecargoDebitoPreview(recargoDebitoPreview);
            } else {
                const recargo = total > 0 ? ((data.montoTotal - total) / total * 100).toFixed(1) : 0;
                $('#tarjeta-recargo').textContent = `${recargo}%`;
            }

            show(panelTarjetaResumen);
        } catch {
            hide(panelTarjetaResumen);
        }
    }

    // ── 8. Credit Personal ────────────────────────────────────────────
    // El crédito se genera automáticamente por el sistema según el cupo del cliente.
    // No hay selección de crédito existente. El aviso muestra cupo vs. total de la venta.

    function actualizarAvisoCredito(cupoDisponible) {
        const tipoPago = selectTipoPago?.value;
        if (tipoPago !== TIPO_PAGO.CreditoPersonal) { hide(panelAvisoCredito); return; }
        if (cupoDisponible === undefined || cupoDisponible === null) { hide(panelAvisoCredito); return; }

        const total = parseFloat(hdnTotal?.value) || 0;
        const margen = cupoDisponible - total;

        $('#credito-disponible').textContent = formatCurrency(cupoDisponible);
        $('#credito-solicitado').textContent = formatCurrency(total);

        const margenEl = $('#credito-margen');
        margenEl.textContent = formatCurrency(margen);
        if (margen < 0) {
            margenEl.classList.add('text-red-500');
            margenEl.classList.remove('text-green-500', 'text-slate-900', 'dark:text-white');
            panelAvisoCredito.classList.remove('bg-primary/5', 'border-primary/20');
            panelAvisoCredito.classList.add('bg-orange-500/10', 'border-orange-500/20');
        } else {
            margenEl.classList.remove('text-red-500');
            margenEl.classList.add('text-green-500');
            panelAvisoCredito.classList.remove('bg-orange-500/10', 'border-orange-500/20');
            panelAvisoCredito.classList.add('bg-primary/5', 'border-primary/20');
        }
        show(panelAvisoCredito);
    }

    // ── 9. Credit Verification (Crédito Personal Workflow) ───────────
    // State for credit verification
    let ultimaPrevalidacion = null;
    let excepcionActiva = false;

    const RESULTADO_PREVAL = { Aprobable: 0, RequiereAutorizacion: 1, NoViable: 2 };
    const TIPO_DOC_CLIENTE = { DNI: 1, ReciboSueldo: 2, Servicio: 3, ConstanciaCUIL: 6, Veraz: 8, Otro: 99 };

    function resetVerificacion() {
        hide($('#panel-resultado-verificacion'));
        hide($('#panel-motivos'));
        hide($('#panel-alerta-mora'));
        hide($('#panel-documentacion-faltante'));
        hide($('#panel-cupo-suficiente'));
        hide($('#panel-cupo-insuficiente'));
        ultimaPrevalidacion = null;
    }

    function esPrevalidacionExceptuable(data) {
        if (!data) return false;
        return data.resultado !== RESULTADO_PREVAL.Aprobable;
    }

    function actualizarDisponibilidadExcepcion(data) {
        resetExcepcionCrediticia();

        if (esPrevalidacionExceptuable(data)) {
            show($('#panel-excepcion-crediticia'));
            show($('#panel-excepcion-inactiva'));
        }
    }

    function invalidarVerificacionCrediticia() {
        creditoCupoDisponible = null;
        resetVerificacion();
        resetExcepcionCrediticia();
        hide($('#panel-credito-cupo'));
        hide(panelAvisoCredito);
        clearFeedback();
    }

    document.addEventListener('venta-crear-modal:open', invalidarVerificacionCrediticia);
    document.addEventListener('venta-crear-modal:close', invalidarVerificacionCrediticia);

    function resetExcepcionCrediticia() {
        excepcionActiva = false;

        const hdnExcepcion = $('#hdn-aplicar-excepcion');
        if (hdnExcepcion) hdnExcepcion.value = 'false';

        const txtMotivo = $('#txt-excepcion-documental');
        if (txtMotivo) {
            txtMotivo.value = '';
            txtMotivo.readOnly = false;
            txtMotivo.classList.remove('border-red-500', 'opacity-60', 'cursor-not-allowed');
        }

        const errEl = document.getElementById('excepcion-motivo-error');
        if (errEl) errEl.remove();

        const btnConfirmar = $('#btn-confirmar-excepcion');
        if (btnConfirmar) btnConfirmar.classList.remove('hidden');

        const btnCancelar = $('#btn-cancelar-excepcion');
        if (btnCancelar) btnCancelar.classList.remove('hidden');

        const badge = document.getElementById('excepcion-aplicada-badge');
        if (badge) badge.remove();

        hide($('#panel-excepcion-crediticia'));
        hide($('#panel-excepcion-inactiva'));
        hide($('#panel-excepcion-activa'));
    }

    function mostrarResultadoVerificacion(data) {
        const panelResultado = $('#panel-resultado-verificacion');
        const badge = $('#verificacion-badge');
        const estado = $('#verificacion-estado');

        // Badge color based on resultado
        const colorMap = {
            success: { bg: 'bg-green-500/10 border border-green-500/20', badge: 'bg-green-500', text: 'APROBADO' },
            warning: { bg: 'bg-amber-500/10 border border-amber-500/20', badge: 'bg-amber-500', text: 'REQUIERE AUTORIZACIÓN' },
            danger:  { bg: 'bg-red-500/10 border border-red-500/20', badge: 'bg-red-500', text: 'NO VIABLE' }
        };
        const color = colorMap[data.colorBadge] || colorMap.danger;

        badge.className = `flex items-center justify-between p-3 rounded-lg ${color.bg}`;
        estado.textContent = data.textoEstado || color.text;
        estado.className = `px-2 py-1 rounded-md ${color.badge} text-white text-[10px] font-black uppercase tracking-widest`;

        // Balance info
        $('#verificacion-limite').textContent = formatCurrency(data.limiteCredito || 0);
        $('#verificacion-utilizado').textContent = formatCurrency(data.creditoUtilizado || 0);
        $('#verificacion-saldo').textContent = formatCurrency(data.cupoDisponible || 0);

        // Saldo color
        const saldoEl = $('#verificacion-saldo');
        const total = parseFloat(hdnTotal?.value) || 0;
        if (data.cupoDisponible >= total && total > 0) {
            saldoEl.className = 'font-bold text-green-500';
        } else if (data.cupoDisponible > 0) {
            saldoEl.className = 'font-bold text-amber-500';
        } else {
            saldoEl.className = 'font-bold text-red-500';
        }

        // Progress bar (credit utilization)
        if (data.limiteCredito > 0) {
            const pct = Math.min((data.creditoUtilizado / data.limiteCredito) * 100, 100);
            const barra = $('#verificacion-barra');
            barra.style.width = `${pct}%`;
            if (pct > 80) barra.className = 'h-full bg-red-500 w-0 transition-all duration-500';
            else if (pct > 60) barra.className = 'h-full bg-amber-500 w-0 transition-all duration-500';
            else barra.className = 'h-full bg-primary w-0 transition-all duration-500';
            barra.style.width = `${pct}%`;
        }

        // Sufficiency check
        const panelSuficiente = $('#panel-cupo-suficiente');
        const panelInsuficiente = $('#panel-cupo-insuficiente');
        hide(panelSuficiente);
        hide(panelInsuficiente);

        if (total > 0 && data.cupoDisponible !== undefined) {
            if (data.cupoDisponible >= total) {
                show(panelSuficiente);
            } else {
                const faltante = total - data.cupoDisponible;
                $('#cupo-insuficiente-detalle').textContent =
                    `Monto solicitado: ${formatCurrency(total)} — Cupo disponible: ${formatCurrency(data.cupoDisponible)} — Faltante: ${formatCurrency(faltante)}`;
                show(panelInsuficiente);
            }
        }

        show(panelResultado);

        // Actualizar aviso de crédito y panel de cupo en el panel "Crédito Personal"
        creditoCupoDisponible = data.cupoDisponible ?? null;
        actualizarAvisoCredito(creditoCupoDisponible);

        const panelCupo = $('#panel-credito-cupo');
        if (panelCupo && data.cupoDisponible !== undefined) {
            $('#credito-cupo-valor').textContent = formatCurrency(data.cupoDisponible);
            $('#credito-cupo-estado').textContent = data.textoEstado || '—';
            show(panelCupo);
        }
    }

    function mostrarMotivos(data) {
        const panel = $('#panel-motivos');
        const lista = $('#lista-motivos');
        lista.innerHTML = '';

        if (data.motivos && data.motivos.length > 0) {
            data.motivos.forEach(m => {
                const iconMap = { 1: 'description', 2: 'account_balance', 3: 'schedule', 4: 'person_off', 5: 'settings' };
                const icon = iconMap[m.categoria] || 'info';
                const colorCls = m.esBloqueante ? 'text-red-500 bg-red-500/10 border-red-500/20' : 'text-amber-600 bg-amber-500/10 border-amber-500/20';
                const div = document.createElement('div');
                div.className = `p-2.5 rounded-lg border ${colorCls} flex items-start gap-2`;
                div.innerHTML = `
                    <span class="material-symbols-outlined text-sm mt-0.5">${icon}</span>
                    <div>
                        <p class="text-[11px] font-bold">${m.titulo}</p>
                        <p class="text-[10px] opacity-80">${m.descripcion}</p>
                    </div>`;
                lista.appendChild(div);
            });
            show(panel);
        } else if (data.mensajeResumen) {
            const div = document.createElement('div');
            div.className = 'p-2.5 rounded-lg border bg-slate-50 dark:bg-slate-800 border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-400';
            div.innerHTML = `<p class="text-[11px]">${data.mensajeResumen}</p>`;
            lista.appendChild(div);
            show(panel);
        } else {
            hide(panel);
        }
    }

    function mostrarAlertaMora(data) {
        const panel = $('#panel-alerta-mora');
        if (data.tieneMora) {
            const textos = [];
            if (data.diasMora) textos.push(`${data.diasMora} días de mora`);
            if (data.montoMora) textos.push(`Monto adeudado: ${formatCurrency(data.montoMora)}`);
            $('#alerta-mora-texto').textContent = textos.join(' — ') || 'El cliente presenta mora activa.';
            show(panel);
        } else {
            hide(panel);
        }
    }

    function mostrarDocumentacionFaltante(data) {
        const panel = $('#panel-documentacion-faltante');
        if (data.documentacionCompleta) {
            hide(panel);
            return;
        }

        const lista = $('#lista-docs-faltantes');
        lista.innerHTML = '';

        const agregarDoc = (nombre, tipo) => {
            const div = document.createElement('div');
            div.className = 'flex items-center gap-2 text-[11px]';
            const iconCls = tipo === 'faltante' ? 'text-amber-500' : 'text-red-500';
            const label = tipo === 'faltante' ? 'Faltante' : 'Vencido';
            div.innerHTML = `
                <span class="material-symbols-outlined text-xs ${iconCls}">
                    ${tipo === 'faltante' ? 'remove_circle_outline' : 'event_busy'}
                </span>
                <span class="text-slate-600 dark:text-slate-400">${nombre}</span>
                <span class="ml-auto text-[10px] font-bold ${iconCls} uppercase">${label}</span>`;
            lista.appendChild(div);
        };

        (data.documentosFaltantes || []).forEach(d => agregarDoc(d, 'faltante'));
        (data.documentosVencidos || []).forEach(d => agregarDoc(d, 'vencido'));

        if (lista.childElementCount > 0) {
            show(panel);
        } else {
            // No documentation info but flag says incomplete
            const div = document.createElement('div');
            div.className = 'text-[11px] text-slate-500';
            div.textContent = 'El cliente no tiene calificación crediticia completa.';
            lista.appendChild(div);
            show(panel);
        }
    }

    $('#btn-verificar-elegibilidad')?.addEventListener('click', async function () {
        if (selectTipoPago?.value !== TIPO_PAGO.CreditoPersonal) {
            return;
        }

        const clienteId = parseInt(hdnClienteId?.value);
        const total = parseFloat(hdnTotal?.value) || 0;
        clearFeedback();

        if (!clienteId) {
            showFeedback('Seleccione un cliente primero.', 'warning');
            return;
        }
        if (total <= 0) {
            showFeedback('Agregue productos para verificar elegibilidad.', 'warning');
            return;
        }

        this.disabled = true;
        this.innerHTML = '<span class="material-symbols-outlined animate-spin">progress_activity</span> Verificando...';
        resetVerificacion();
        resetExcepcionCrediticia();

        try {
            const data = await fetchJson(`/api/ventas/PrevalidarCredito?clienteId=${clienteId}&monto=${total}`);
            ultimaPrevalidacion = data;

            mostrarResultadoVerificacion(data);
            mostrarMotivos(data);
            mostrarAlertaMora(data);
            mostrarDocumentacionFaltante(data);
            actualizarDisponibilidadExcepcion(data);

        } catch (err) {
            showFeedback('Error al verificar elegibilidad: ' + err.message, 'error');
        } finally {
            this.disabled = false;
            this.innerHTML = '<span class="material-symbols-outlined">analytics</span> Verificar Elegibilidad';
        }
    });

    // ── 10. Exception Workflow ────────────────────────────────────────
    // excepcionActiva = true means the panel is open AND the user confirmed with motivo.
    // The hidden field is only set to 'true' at submit time, never at panel-open time.

    function mostrarPanelExcepcion() {
        if (!esPrevalidacionExceptuable(ultimaPrevalidacion)) {
            resetExcepcionCrediticia();
            showFeedback('Verificá elegibilidad antes de aplicar una excepción.', 'warning');
            return;
        }

        hide($('#panel-excepcion-inactiva'));
        show($('#panel-excepcion-activa'));
        hide($('#panel-cupo-insuficiente'));
        hide($('#panel-documentacion-faltante'));
        const txt = $('#txt-excepcion-documental');
        if (txt) txt.focus();
    }

    function ocultarPanelExcepcion() {
        excepcionActiva = false;
        const hdnExcepcion = $('#hdn-aplicar-excepcion');
        if (hdnExcepcion) hdnExcepcion.value = 'false';

        const txtMotivo = $('#txt-excepcion-documental');
        if (txtMotivo) {
            txtMotivo.value = '';
            txtMotivo.readOnly = false;
            txtMotivo.classList.remove('border-red-500', 'opacity-60', 'cursor-not-allowed');
        }
        // Clear any inline error
        const errEl = document.getElementById('excepcion-motivo-error');
        if (errEl) errEl.remove();

        // Restore buttons and remove confirmed badge
        const btnConfirmar = $('#btn-confirmar-excepcion');
        if (btnConfirmar) btnConfirmar.classList.remove('hidden');
        const btnCancelar = $('#btn-cancelar-excepcion');
        if (btnCancelar) btnCancelar.classList.remove('hidden');
        const badge = document.getElementById('excepcion-aplicada-badge');
        if (badge) badge.remove();

        if (esPrevalidacionExceptuable(ultimaPrevalidacion)) {
            show($('#panel-excepcion-crediticia'));
            show($('#panel-excepcion-inactiva'));
        } else {
            hide($('#panel-excepcion-crediticia'));
            hide($('#panel-excepcion-inactiva'));
        }
        hide($('#panel-excepcion-activa'));

        if (ultimaPrevalidacion) {
            mostrarResultadoVerificacion(ultimaPrevalidacion);
            mostrarDocumentacionFaltante(ultimaPrevalidacion);
        }
    }

    $('#btn-aplicar-excepcion')?.addEventListener('click', mostrarPanelExcepcion);
    $('#btn-cancelar-excepcion')?.addEventListener('click', ocultarPanelExcepcion);

    // "Aplicar y continuar" dentro del panel: valida motivo, activa la excepción y bloquea el panel para edición
    $('#btn-confirmar-excepcion')?.addEventListener('click', function () {
        const txtMotivo = $('#txt-excepcion-documental');
        const motivo = txtMotivo ? txtMotivo.value.trim() : '';

        if (!motivo) {
            let errEl = document.getElementById('excepcion-motivo-error');
            if (!errEl) {
                errEl = document.createElement('p');
                errEl.id = 'excepcion-motivo-error';
                errEl.className = 'text-[11px] text-red-500 font-bold mt-1';
                errEl.textContent = 'El motivo es obligatorio para aplicar la excepción.';
                txtMotivo?.insertAdjacentElement('afterend', errEl);
            }
            txtMotivo?.classList.add('border-red-500');
            txtMotivo?.focus();
            return;
        }

        // Motivo válido — activar excepción sin submitear; el usuario continúa con "Confirmar Transacción"
        excepcionActiva = true;
        const hdnExcepcion = $('#hdn-aplicar-excepcion');
        if (hdnExcepcion) hdnExcepcion.value = 'true';

        // Bloquear edición del panel y mostrar estado confirmado
        if (txtMotivo) {
            txtMotivo.readOnly = true;
            txtMotivo.classList.add('opacity-60', 'cursor-not-allowed');
        }
        const btnConfirmar = $('#btn-confirmar-excepcion');
        if (btnConfirmar) btnConfirmar.classList.add('hidden');
        const btnCancelar = $('#btn-cancelar-excepcion');
        if (btnCancelar) btnCancelar.classList.add('hidden');

        // Mostrar badge de excepción aplicada
        const panelActivo = $('#panel-excepcion-activa');
        if (panelActivo) {
            let badge = document.getElementById('excepcion-aplicada-badge');
            if (!badge) {
                badge = document.createElement('div');
                badge.id = 'excepcion-aplicada-badge';
                badge.className = 'flex items-center gap-2 mt-3 text-green-400 text-sm font-semibold';
                badge.innerHTML = '<span class="material-symbols-outlined text-base">check_circle</span> Excepción aplicada. Podés continuar con "Confirmar Transacción".';
                panelActivo.appendChild(badge);
            }
        }
    });

    // Clear inline error when typing in motivo
    $('#txt-excepcion-documental')?.addEventListener('input', function () {
        this.classList.remove('border-red-500');
        const errEl = document.getElementById('excepcion-motivo-error');
        if (errEl) errEl.remove();
    });

    // Guard on native form submit: if panel open but user bypasses via top submit button
    const ventaForm = document.getElementById('venta-form');
    if (ventaForm) {
        ventaForm.addEventListener('submit', async function (e) {
            if (diagnosticoCondicionesBloqueaContinuidad) {
                e.preventDefault();
                showFeedback('No se puede confirmar con el medio de pago seleccionado. Cambia el medio de pago para continuar.', 'error');
                panelDiagnosticoCondicionesPago?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                return;
            }

            if (!reintentandoSubmitConDatosTarjeta &&
                esTipoPagoTarjeta(selectTipoPago?.value) &&
                parseInt(selectTarjeta?.value) &&
                (!hdnTarjetaNombre?.value || !hdnTarjetaTipo?.value)) {
                e.preventDefault();
                await cargarTarjetasActivas();
                const info = poblarDatosTarjetaSeleccionada();

                if (!info) {
                    showFeedback('No se pudo cargar la informaciÃ³n de la tarjeta seleccionada.', 'error');
                    return;
                }

                reintentandoSubmitConDatosTarjeta = true;
                ventaForm.requestSubmit();
                return;
            }

            reintentandoSubmitConDatosTarjeta = false;

            const panelActivo = $('#panel-excepcion-activa');
            const panelVisible = panelActivo && !panelActivo.classList.contains('hidden');
            if (!panelVisible) return;

            // Panel open but hdn not yet set (user didn't click "Aplicar y continuar")
            const hdnExcepcion = $('#hdn-aplicar-excepcion');
            if (!hdnExcepcion || hdnExcepcion.value !== 'true') {
                e.preventDefault();
                const txtMotivo = $('#txt-excepcion-documental');
                const motivo = txtMotivo ? txtMotivo.value.trim() : '';
                if (!motivo) {
                    txtMotivo?.classList.add('border-red-500');
                    let errEl = document.getElementById('excepcion-motivo-error');
                    if (!errEl) {
                        errEl = document.createElement('p');
                        errEl.id = 'excepcion-motivo-error';
                        errEl.className = 'text-[11px] text-red-500 font-bold mt-1';
                        errEl.textContent = 'Usá el botón "Aplicar y continuar" para confirmar la excepción.';
                        txtMotivo?.insertAdjacentElement('afterend', errEl);
                    }
                    txtMotivo?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                }
            }
        });
    }

    // ── 11. Documentation Upload Modal ────────────────────────────────
    const modalDoc = document.querySelector('[data-venta-modal="documentacion"]');

    function prepararModalDocumentacion() {
        if (!modalDoc) return;
        const clienteId = parseInt(hdnClienteId?.value);
        if (!clienteId) return;

        // Populate missing/expired docs from last verification
        const listaModal = $('#modal-lista-docs');
        listaModal.innerHTML = '';

        if (ultimaPrevalidacion) {
            (ultimaPrevalidacion.documentosFaltantes || []).forEach(d => {
                const div = document.createElement('div');
                div.className = 'flex items-center gap-2 p-2 rounded-lg bg-amber-500/5 border border-amber-500/10';
                div.innerHTML = `
                    <span class="material-symbols-outlined text-sm text-amber-500">remove_circle_outline</span>
                    <span class="text-xs text-slate-600 dark:text-slate-400">${d}</span>
                    <span class="ml-auto text-[10px] font-bold text-amber-500 uppercase">Faltante</span>`;
                listaModal.appendChild(div);
            });
            (ultimaPrevalidacion.documentosVencidos || []).forEach(d => {
                const div = document.createElement('div');
                div.className = 'flex items-center gap-2 p-2 rounded-lg bg-red-500/5 border border-red-500/10';
                div.innerHTML = `
                    <span class="material-symbols-outlined text-sm text-red-500">event_busy</span>
                    <span class="text-xs text-slate-600 dark:text-slate-400">${d}</span>
                    <span class="ml-auto text-[10px] font-bold text-red-500 uppercase">Vencido</span>`;
                listaModal.appendChild(div);
            });
        }

        if (listaModal.childElementCount === 0) {
            listaModal.innerHTML = '<p class="text-xs text-slate-500">Sin información de documentos pendientes. Suba la documentación requerida.</p>';
        }

        // Populate document type select
        const selectTipo = $('#select-tipo-documento');
        selectTipo.innerHTML = '<option value="">Seleccione tipo...</option>';
        Object.entries(TIPO_DOC_CLIENTE).forEach(([name, val]) => {
            const opt = document.createElement('option');
            opt.value = val;
            opt.textContent = name.replace(/([A-Z])/g, ' $1').trim();
            selectTipo.appendChild(opt);
        });

        // Set link to full documentation page
        if (btnIrDocumentacion) {
            btnIrDocumentacion.dataset.href = `/DocumentoCliente/Upload?clienteId=${clienteId}`;
        }

        // Reset upload state
        const inputFile = $('#input-doc-archivo');
        if (inputFile) inputFile.value = '';
        hide($('#doc-archivo-nombre'));
        hide($('#doc-upload-feedback'));
        $('#btn-subir-documento').disabled = true;

    }

    if (typeof ventaModule.bindModal === 'function') {
        ventaModule.bindModal('documentacion', {
            beforeOpen: prepararModalDocumentacion
        });
    }

    // File selection
    $('#input-doc-archivo')?.addEventListener('change', function () {
        const nombreEl = $('#doc-archivo-nombre');
        if (this.files.length > 0) {
            nombreEl.textContent = this.files[0].name;
            show(nombreEl);
        } else {
            hide(nombreEl);
        }
        // Enable upload button when file + type selected
        const tipoDoc = $('#select-tipo-documento')?.value;
        $('#btn-subir-documento').disabled = !(this.files.length > 0 && tipoDoc);
    });

    $('#select-tipo-documento')?.addEventListener('change', function () {
        const file = $('#input-doc-archivo')?.files?.length > 0;
        $('#btn-subir-documento').disabled = !(file && this.value);
    });

    // Upload document via AJAX
    $('#btn-subir-documento')?.addEventListener('click', async function () {
        const clienteId = parseInt(hdnClienteId?.value);
        const file = $('#input-doc-archivo')?.files?.[0];
        const tipoDoc = $('#select-tipo-documento')?.value;
        const feedback = $('#doc-upload-feedback');

        if (!clienteId || !file || !tipoDoc) {
            feedback.className = 'p-3 rounded-lg text-xs font-bold bg-red-500/10 text-red-500 border border-red-500/20';
            feedback.textContent = !file
                ? 'Debe seleccionar un archivo'
                : 'Debe seleccionar el tipo de documento.';
            show(feedback);
            return;
        }

        this.disabled = true;
        this.innerHTML = '<span class="material-symbols-outlined animate-spin text-sm">progress_activity</span> Subiendo...';

        try {
            const formData = new FormData();
            formData.append('ClienteId', clienteId);
            formData.append('TipoDocumento', tipoDoc);
            formData.append('Archivo', file);

            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const resp = await fetch('/DocumentoCliente/Upload', {
                method: 'POST',
                headers: {
                    ...(token ? { 'RequestVerificationToken': token } : {}),
                    'X-Requested-With': 'XMLHttpRequest',
                    'Accept': 'application/json'
                },
                body: formData
            });

            const contentType = resp.headers.get('content-type') || '';
            const payload = contentType.includes('application/json')
                ? await resp.json()
                : null;

            if (resp.ok && (!payload || payload.success !== false)) {
                feedback.className = 'p-3 rounded-lg text-xs font-bold bg-green-500/10 text-green-600 border border-green-500/20';
                feedback.textContent = payload?.message || 'Documento subido correctamente.';
                show(feedback);
                // Reset file input
                $('#input-doc-archivo').value = '';
                hide($('#doc-archivo-nombre'));
            } else {
                feedback.className = 'p-3 rounded-lg text-xs font-bold bg-red-500/10 text-red-500 border border-red-500/20';
                feedback.textContent = payload?.message || 'Error al subir el documento. Intente nuevamente.';
                show(feedback);
            }
        } catch (err) {
            feedback.className = 'p-3 rounded-lg text-xs font-bold bg-red-500/10 text-red-500 border border-red-500/20';
            feedback.textContent = 'Error de conexión al subir el documento.';
            show(feedback);
        } finally {
            this.disabled = false;
            this.innerHTML = '<span class="material-symbols-outlined text-sm">upload</span> Subir Documento';
        }
    });

    // ── 11. Toast auto-dismiss ────────────────────────────────────────
    if (typeof ventaModule.initSharedUi === 'function') {
        ventaModule.initSharedUi();
    } else if (typeof theBury.autoDismissToasts === 'function') {
        theBury.autoDismissToasts();
    }

    if (typeof ventaModule.initScrollAffordance === 'function') {
        detalleScrollAffordance = ventaModule.initScrollAffordance('#venta-detalles-scroll');
        updateDetallesScrollAffordance();
    } else if (typeof theBury.initHorizontalScrollAffordance === 'function') {
        detalleScrollAffordance = theBury.initHorizontalScrollAffordance('#venta-detalles-scroll');
        updateDetallesScrollAffordance();
    }

    // ── Init ──────────────────────────────────────────────────────────
    cargarConfiguracionPagosGlobal();
    onTipoPagoChange();
    renderDetalles();
    recalcularTotales();
    actualizarResumenOperacion(parseFloat(hdnTotal?.value) || 0);

})();

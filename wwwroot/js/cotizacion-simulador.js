(function () {
    'use strict';

    const root = document.querySelector('[data-cotizacion-simulador]');
    if (!root) return;

    const theBury = window.TheBury || {};
    const formatCurrency = theBury.formatCurrency || function (value) {
        return new Intl.NumberFormat('es-AR', {
            style: 'currency',
            currency: 'ARS',
            minimumFractionDigits: 2
        }).format(value || 0);
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

    const urls = {
        simular: root.dataset.simularUrl || '/api/cotizacion/simular',
        guardar: root.dataset.guardarUrl || '/api/cotizacion/guardar',
        productos: root.dataset.productosUrl || '/Cotizacion/BuscarProductos',
        productoResumen: root.dataset.productoResumenUrl || '/Cotizacion/ProductoResumen',
        clientes: root.dataset.clientesUrl || '/Cotizacion/BuscarClientes',
        convertirBase: root.dataset.convertirBaseUrl || '/api/cotizacion',
        ventaEdit: root.dataset.ventaEditUrl || '/Venta/Edit/',
        aptitudCredito: root.dataset.aptitudCreditoUrl || '/api/cotizacion/aptitud-credito'
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
        simular: $('#cotizacion-simular'),
        simularLabel: $('[data-simular-label]'),
        guardar: $('#cotizacion-guardar'),
        // COTIZACION-WORKSTATION-01 (§16/§17): "Continuar con esta opción" es la
        // acción primaria del cierre; Guardar queda como secundaria y sólo persiste.
        continuar: $('#cotizacion-continuar'),
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
        resultadosTbody: $('#cotizacion-resultados-tbody'),
        // plan drawer
        planMedio: $('#plan-medio'),
        planCuotas: $('#plan-cuotas'),
        planTotal: $('#plan-total'),
        planDetalleCuotas: $('#plan-detalle-cuotas'),
        planValorCuota: $('#plan-valor-cuota'),
        planRecargo: $('#plan-recargo'),
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
        planElegibilidad: $('#plan-elegibilidad')
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
            if (ico) ico.textContent = value ? 'progress_activity' : 'calculate';
        }
        if (els.guardar) {
            els.guardar.disabled = value || !state.ultimaSimulacion?.exitoso;
            const ico = els.guardar.querySelector('.material-symbols-outlined');
            if (ico) ico.textContent = value ? 'progress_activity' : 'save';
        }
        // Continuar exige además una alternativa elegida (§16): sin selección no hay
        // "esta opción" con la que seguir.
        if (els.continuar) {
            els.continuar.disabled = value || !state.ultimaSimulacion?.exitoso || !state.seleccionRow?.plan;
            const ico = els.continuar.querySelector('.material-symbols-outlined');
            if (ico) ico.textContent = value ? 'progress_activity' : 'arrow_forward';
        }
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
            // COTIZACION-SIMULAR-REDESIGN-VISUAL-POLISH-01: precio vigente sube a la
            // fila del nombre (dato, no "widget" propio) y Subtotal pasa a su propia
            // fila a ancho completo — antes compartía una grilla de 3 columnas con
            // Dto.%/Dto.$ y se recortaba (overflow-x) en el rail angosto de Productos.
            article.innerHTML = `
                <div class="flex items-start justify-between gap-2">
                    <div class="cart-row__nombre truncate-1 min-w-0 flex-1">${esc(producto.nombre || `Producto ${producto.productoId}`)}</div>
                    <button type="button" data-cotizacion-eliminar-index="${index}" class="cart-row__quitar shrink-0" aria-label="Quitar">
                        <span class="material-symbols-outlined" style="font-size:16px">close</span>
                    </button>
                </div>
                <div class="flex items-center justify-between gap-2">
                    <div class="cart-row__meta truncate-1 min-w-0">ID ${producto.productoId}${producto.codigo ? ' · ' + esc(producto.codigo) : ''}</div>
                    <div class="cart-row__precio text-slate-300 total-display shrink-0">${formatCurrency(producto.precioUnitario)}</div>
                </div>
                <div class="cart-row-inputs">
                    <div class="qty-step">
                        <button type="button" aria-label="Restar" onclick="stepRow(this,-1)">−</button>
                        <input type="number" min="1" value="${producto.cantidad}" data-cotizacion-cantidad-index="${index}" aria-label="Cantidad">
                        <button type="button" aria-label="Sumar" onclick="stepRow(this,1)">+</button>
                    </div>
                    <label class="block"><span class="text-[10px] text-slate-500">Dto. %</span>
                        <input type="number" value="${producto.descuentoPorcentaje ?? ''}" min="0" max="100" step="0.01" placeholder="0" data-cotizacion-desc-pct-index="${index}" aria-label="Descuento porcentaje producto" class="mini w-full mt-0.5"></label>
                    <label class="block"><span class="text-[10px] text-slate-500">Dto. $</span>
                        <input type="number" value="${producto.descuentoImporte ?? ''}" min="0" step="0.01" placeholder="0" data-cotizacion-desc-importe-index="${index}" aria-label="Descuento importe producto" class="mini w-full mt-0.5"></label>
                </div>
                <div class="cart-row-subtotal">
                    <span class="text-[10px] uppercase tracking-wide text-slate-500">Subtotal</span>
                    <span class="text-sm font-semibold text-white total-display">${formatCurrency(subtotal)}</span>
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
                els.productoSeleccionado.innerHTML = `<span class="material-symbols-outlined text-blue-400" style="font-size:14px">check_circle</span> ${esc(producto.nombre)}${marcaSel ? ' · ' + esc(marcaSel) : ''} · ${esc(formatCurrency(producto.precioVenta))} · Stock ${esc(producto.stockActual ?? '-')}`;
            } else {
                els.productoSeleccionado.classList.add('italic');
                els.productoSeleccionado.innerHTML = `<span class="material-symbols-outlined text-slate-600" style="font-size:14px">inventory_2</span> Sin producto seleccionado.`;
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
        if (els.guardar) els.guardar.disabled = true;
        resetGuardado();
        resetTotalesBar();
        // §9: una única acción primaria contextual. "Actualizar cotización" sólo
        // cuando hubo un resultado que quedó desactualizado por un cambio; en frío
        // (nunca se simuló) sigue siendo "Simular cotización".
        setSimularLabel(hadResults ? 'Actualizar cotización' : 'Simular cotización');
        renderSeleccionBar();
        if (hadResults) setState('pending');
    }

    function setSimularLabel(texto) {
        if (els.simularLabel) els.simularLabel.textContent = texto;
    }

    // §16: el cierre del comparador dice qué se eligió y habilita las dos acciones
    // que siguen. Sin selección válida, Continuar queda deshabilitado — la regla real
    // no cambia (guardarYPasarAVenta ya exigía simulación válida): sólo se hace
    // visible en el botón en vez de fallar recién al clickear.
    function renderSeleccionBar() {
        const row = state.seleccionRow;
        const hayOpcion = !!(row && row.plan);
        if (els.continuar) els.continuar.disabled = !hayOpcion;
        if (!els.seleccionResumen) return;

        if (!hayOpcion) {
            els.seleccionResumen.innerHTML = `
                <span class="seleccion-resumen__label">Sin opción elegida</span>
                <span class="seleccion-resumen__valor">Simulá y elegí una alternativa para continuar.</span>`;
            return;
        }

        const medio = medioLabel(row.opcion.medioPago, row.opcion.nombreMedioPago);
        const plan = planLabelCuotas(row.plan);
        // Crédito personal con autorización pendiente: el resumen lo dice acá también
        // (§16) — el botón sigue habilitado porque Venta SÍ deja continuar pidiendo
        // autorización de supervisor; NoApto es el único caso que Venta rechaza.
        const tone = esCreditoPersonalMedio(row.opcion.medioPago) ? aptitudTone(state.aptitud) : null;
        const nota = tone === 'requiere-autorizacion'
            ? ` <span class="rmedio-aptitud rmedio-aptitud--requiere-autorizacion">· Requiere autorización</span>`
            : tone === 'no-apto'
                ? ` <span class="rmedio-aptitud rmedio-aptitud--no-apto">· Cliente no apto</span>`
                : '';
        els.seleccionResumen.innerHTML = `
            <span class="seleccion-resumen__label">Opción seleccionada</span>
            <span class="seleccion-resumen__valor"><strong>${esc(medio)}</strong> · ${esc(plan)} · <span class="total-display">${formatCurrency(row.plan.total)}</span>${nota}</span>`;
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
    async function guardarCotizacion() {
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
                tieneEnvio: els.tieneEnvio?.checked || false
            };

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
        setState('saved');
        mostrarAccionesPostGuardado(data);
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

    // Acción primaria "Continuar con esta opción" (§16): guarda y encadena a la
    // conversión en venta con la alternativa elegida. Si el guardado falla no se
    // intenta nada más; si guarda bien pero la conversión no puede seguir (sin cliente
    // de sistema, o esa segunda llamada falla), la cotización queda guardada y visible
    // con "Pasar a venta" para reintentar a mano, nunca en un estado ambiguo.
    async function continuarConOpcion() {
        const data = await guardarCotizacion();
        if (!data) return;

        if (state.cotizacionGuardadaClienteId) {
            showFeedback(`Cotización ${data.numero} guardada. Pasando a venta…`, 'ok');
            await pasarAVenta();
        } else {
            showFeedback(`Cotización ${data.numero} guardada. Seleccioná un cliente del sistema para pasarla a venta.`, 'warning');
        }
    }

    // Conversión directa: la cotización recién guardada usa precios vigentes, así
    // que se convierte con precio cotizado y auto-confirma avisos informativos
    // (p. ej. unidades trazables se asignan luego en Venta/Edit). La llama
    // guardarYPasarAVenta() automáticamente cuando hay un cliente de sistema; el
    // botón "Pasar a venta" del estado post-guardado la reusa igual para el caso en
    // que el guardado no pudo continuar solo (sin cliente de sistema en ese
    // momento, o esta llamada automática falló).
    async function pasarAVenta() {
        if (!state.cotizacionGuardadaId) return;

        if (!state.cotizacionGuardadaClienteId) {
            showFeedback('Seleccioná un cliente del sistema para pasar a venta.', 'warning');
            return;
        }

        const btn = els.pasarVenta;
        if (btn) {
            btn.disabled = true;
            const ico = btn.querySelector('.material-symbols-outlined');
            if (ico) ico.textContent = 'progress_activity';
        }

        try {
            const resp = await fetch(`${urls.convertirBase}/${state.cotizacionGuardadaId}/conversion/convertir`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                },
                body: JSON.stringify({
                    usarPrecioCotizado: true,
                    confirmarAdvertencias: true,
                    clienteIdOverride: null,
                    observacionesAdicionales: null
                })
            });

            const data = await resp.json().catch(() => ({}));

            if (resp.ok && data.exitoso && data.ventaId) {
                showFeedback(`Venta ${data.numeroVenta || ''} creada. Abriendo…`, 'ok');
                window.location.assign(`${urls.ventaEdit}${data.ventaId}`);
                return;
            }

            const mensaje = (data.errores && data.errores.length)
                ? data.errores.join(' ')
                : (data.error || 'No se pudo pasar la cotización a venta.');
            showFeedback(mensaje, 'error');
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

    function medioLabel(medio, nombre) {
        if (nombre) return nombre;
        if (typeof medio === 'string') return medio;
        return {
            0: 'Efectivo',
            1: 'Transferencia',
            2: 'Tarjeta crédito',
            3: 'Tarjeta débito',
            4: 'MercadoPago',
            5: 'Crédito personal'
        }[medio] || 'Medio';
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
            'MercadoPago': { icon: 'qr_code_2', tone: 'cyan' },
            'Crédito personal': { icon: 'handshake', tone: 'amber' }
        };
        return map[key] || { icon: 'payments', tone: 'slate' };
    }

    function esCreditoPersonalMedio(medio) {
        return medioLabel(medio, null) === 'Crédito personal';
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
        return `<div class="rmedio-aptitud ${clsTone}">Cupo ${formatCurrency(state.aptitud.cupoDisponible)} · solicitado ${formatCurrency(state.aptitud.montoSolicitado)}</div>`;
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
                    ${eyebrow}
                    <div class="aptitud-card__head" style="color:#93a2b8">
                        <span class="material-symbols-outlined" style="font-size:15px">progress_activity</span> Evaluando…
                    </div>
                </div>`;
            return;
        }

        if (view.tone === 'error') {
            // Un fallo técnico de la consulta NO es una falta de aptitud: el copy lo
            // dice explícitamente y ofrece reintentar, en vez de dejar al operador
            // creyendo que el cliente fue rechazado.
            els.aptitudCredito.innerHTML = `
                <div class="aptitud-card aptitud-card--error">
                    ${eyebrow}
                    <div class="aptitud-card__head"><span class="material-symbols-outlined" style="font-size:15px">error</span> No se pudo evaluar</div>
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
                    ${eyebrow}
                    <div class="aptitud-card__head"><span class="material-symbols-outlined" style="font-size:15px">check_circle</span> Apto</div>
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
        if (data.mora?.tiene && Number(data.mora.dias) > 0) resumenPartes.push(`Mora ${data.mora.dias} días`);
        const faltantesCount = Array.isArray(data.documentacion?.faltantes) ? data.documentacion.faltantes.length : 0;
        if (faltantesCount > 0) resumenPartes.push(`${faltantesCount} documento${faltantesCount === 1 ? '' : 's'} faltante${faltantesCount === 1 ? '' : 's'}`);
        const resumenLinea = resumenPartes.length ? resumenPartes.join(' · ') : (motivos[0] || '');

        // Prioridad 1: RequiereAutorizacion NO es un bloqueo — Venta va a pedir autorización
        // de supervisor y puede continuar (a diferencia de NoApto, que Venta sí rechaza sin
        // excepción). Copy e ícono honestos con esa diferencia real, no "No apto" genérico.
        if (tone === 'requiere-autorizacion') {
            els.aptitudCredito.innerHTML = `
                <div class="aptitud-card aptitud-card--requiere-autorizacion">
                    ${eyebrow}
                    <div class="aptitud-card__head"><span class="material-symbols-outlined" style="font-size:15px">gpp_maybe</span> Requiere autorización</div>
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
                ${eyebrow}
                <div class="aptitud-card__head"><span class="material-symbols-outlined" style="font-size:15px">cancel</span> No apto</div>
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
            state.aptitud = null;
            state.aptitudClienteId = null;
            state.aptitudMonto = null;
            renderAptitudCredito(null);
            return;
        }

        const monto = Number(state.ultimaSimulacion?.totalBase ?? previewBase()) || 0;
        if (monto <= 0) {
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
        } catch {
            if (token !== state.aptitudToken) return;
            state.aptitud = null;
            renderAptitudCredito({ tone: 'error' });
        }
    }

    // % a mostrar en la columna "Recargo" de la comparativa. Ojo: costoFinancieroTotal es un
    // IMPORTE en pesos (informativo, se usa aparte en el desglose de Credito personal), no un
    // porcentaje — mezclarlo acá en el Math.max inflaba el recargo mostrado a miles de "%".
    function recargoValor(plan) {
        if (!plan) return 0;
        return Math.max(Number(plan.recargoPorcentaje || 0), Number(plan.interesPorcentaje || 0));
    }

    function pct(n) {
        return `${new Intl.NumberFormat('es-AR', { maximumFractionDigits: 2 }).format(n)}%`;
    }

    // Recargo 0% es un valor neutral (no un éxito ni un descuento): sólo > 0 es
    // ámbar. < 0 quedaría verde (descuento real), pero recargoValor() nunca
    // devuelve negativo hoy — no se inventa ese caso acá (item 13/14 del lote).
    function recargoClass(r) {
        if (r > 0) return 'text-amber-300';
        if (r < 0) return 'text-emerald-400';
        return 'text-slate-400';
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
        updateHeaderCounts();

        els.resultadosTbody.replaceChildren();
        const rows = flattenOpciones(data.opcionesPago || []);

        if (!rows.length) {
            const tr = document.createElement('tr');
            tr.className = 'off';
            tr.innerHTML = `<td colspan="7" class="text-center text-sm text-slate-500" style="padding:1.5rem">No hay medios disponibles para los filtros seleccionados.</td>`;
            els.resultadosTbody.appendChild(tr);
        } else {
            // mejor global: menor total con plan disponible
            let bestKey = null, bestTotal = Infinity;
            rows.forEach(r => {
                if (r.plan && Number(r.plan.total) < bestTotal) { bestTotal = Number(r.plan.total); bestKey = optionKey(r); }
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

            // auto-seleccionar recomendado (o el mejor) para habilitar guardar
            const recomendado = rows.find(r => r.plan?.recomendado) || rows.find(r => r.plan && optionKey(r) === bestKey) || rows.find(r => r.plan);
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
    function estadoCellHtml(row) {
        if (esCreditoPersonalMedio(row.opcion.medioPago)) {
            const tone = aptitudTone(state.aptitud);
            if (tone === 'no-apto') return '<span class="pill pill-red">No apto</span>';
            if (tone === 'requiere-autorizacion') return '<span class="pill pill-amber">Requiere autorización</span>';
        }
        return '<span class="pill pill-green">Disponible</span>';
    }

    function accionCellHtml(row, selectedKey) {
        const key = optionKey(row);
        if (selectedKey && key === selectedKey) {
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
                <td colspan="3" class="text-xs text-amber-200/80"><span class="material-symbols-outlined text-amber-300" style="font-size:14px">${motivoIcon}</span> ${esc(motivo)}</td>
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
        const recargoTxt = minR === maxR ? (minR > 0 ? `+${pct(minR)}` : pct(minR)) : `+${pct(minR)} a +${pct(maxR)}`;

        const fuenteTxt = group.opcion.fuenteTasaDescripcion
            ? `<div class="text-[10px] text-slate-500">${esc(group.opcion.fuenteTasaDescripcion)}</div>`
            : '';
        // La pill de la fila-padre distingue NoApto (bloqueo real, Venta rechaza) de
        // RequiereAutorizacion (Venta sigue, pide autorización de supervisor).
        const aptitudNota = esCreditoPersonalMedio(group.medioPago) ? aptitudNotaHtml() : '';
        const toneGrupo = esCreditoPersonalMedio(group.medioPago) ? aptitudTone(state.aptitud) : null;
        const pillOpciones = toneGrupo === 'no-apto'
            ? '<span class="pill pill-red">No apto</span>'
            : toneGrupo === 'requiere-autorizacion'
                ? '<span class="pill pill-amber">Requiere autorización</span>'
                : '<span class="pill pill-green">Disponible</span>';

        // §13: la fila padre resume el medio (identidad + rango de precio + estado) y
        // su acción es EXPANDIR, no elegir — elegir es una decisión por plan, que vive
        // en las filas hijas. Por eso no lleva data-cotizacion-opcion-key.
        const parent = document.createElement('tr');
        parent.className = 'parent';
        parent.setAttribute('aria-expanded', 'true');
        parent.dataset.group = gkey;
        parent.innerHTML = `
            <td><span class="rmedio"><span class="pay-ico pay-ico--${meta.tone}"><span class="material-symbols-outlined" style="font-size:16px">${meta.icon}</span></span><span><span class="rmedio-nombre font-semibold text-white">${esc(group.label)}</span>${fuenteTxt}${aptitudNota}</span><span class="material-symbols-outlined twist">expand_more</span></span></td>
            <td class="r"><span class="text-[10px] text-slate-500">desde </span><span class="total-display font-semibold text-white">${formatCurrency(minTotal)}</span></td>
            <td class="text-slate-400">${planRows.length} planes</td>
            <td class="r text-slate-500">—</td>
            <td class="r ${recargoClass(maxR)}">${recargoTxt}</td>
            <td>${pillOpciones}</td>
            <td class="r rt-accion"><span class="rt-btn rt-btn--ghost" aria-hidden="true">Ver planes</span></td>`;
        frag.appendChild(parent);

        // detalle más barato
        let cheapKey = null, cheapTotal = Infinity;
        planRows.forEach(r => { if (Number(r.plan.total) < cheapTotal) { cheapTotal = Number(r.plan.total); cheapKey = optionKey(r); } });

        planRows.forEach(row => {
            frag.appendChild(buildDetailRow(row, gkey, bestKey, cheapKey));
        });
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
            ? `<div class="text-[10px] text-slate-500">${esc(row.opcion.fuenteTasaDescripcion)}</div>`
            : '';
        const aptitudNota = esCreditoPersonalMedio(row.opcion.medioPago) ? aptitudNotaHtml() : '';
        tr.innerHTML = `
            <td><span class="rmedio"><span class="pay-ico pay-ico--${meta.tone}"><span class="material-symbols-outlined" style="font-size:16px">${meta.icon}</span></span><span><span class="rmedio-nombre font-semibold text-white">${esc(medioLabel(row.opcion.medioPago, row.opcion.nombreMedioPago))}</span>${mejorPrecioBadge(key, bestKey)}${fuenteTxt}${aptitudNota}</span></span></td>
            <td class="r"><span class="total-display font-semibold text-white">${formatCurrency(plan.total)}</span></td>
            <td class="text-slate-400">${planLabelCuotas(plan)}</td>
            <td class="r ${Number(plan.cantidadCuotas) > 1 ? 'text-slate-300 total-display' : 'text-slate-500'}">${cuotasTxt}</td>
            <td class="r ${recargoClass(r)}">${r > 0 ? '+' : ''}${pct(r)}</td>
            <td>${estadoCellHtml(row)}</td>
            <td class="r rt-accion">${accionCellHtml(row, null)}</td>`;
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
            <td class="r"><span class="total-display text-white">${formatCurrency(plan.total)}</span></td>
            <td class="text-slate-400">${planLabelCuotas(plan)}</td>
            <td class="r total-display text-slate-300">${Number(plan.cantidadCuotas) > 1 ? formatCurrency(plan.valorCuota) : '—'}</td>
            <td class="r ${recargoClass(r)}">${r > 0 ? '+' : ''}${pct(r)}</td>
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
            els.planRecargo.textContent = `${r > 0 ? '+' : ''}${pct(r)}`;
            els.planRecargo.className = recargoClass(r) + ' font-mono';
        }

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
            } else {
                hide(els.planElegibilidad);
                els.planElegibilidad.innerHTML = '';
            }
        }

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
        els.continuar?.addEventListener('click', continuarConOpcion);
        els.pasarVenta?.addEventListener('click', pasarAVenta);
        els.nuevaCotizacion?.addEventListener('click', () => window.location.reload());

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

        // descuentos generales + anticipo -> pendiente
        [els.descuentoGralPct, els.descuentoGralImporte, els.anticipo].forEach(el => {
            el?.addEventListener('input', () => invalidarSimulacion());
        });

        // carrito: abrir confirmación de quitar
        els.productosTbody?.addEventListener('click', event => {
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
                invalidarSimulacion();
                return;
            }

            const descImporteInput = event.target.closest('[data-cotizacion-desc-importe-index]');
            if (descImporteInput) {
                const index = Number(descImporteInput.dataset.cotizacionDescImporteIndex);
                state.productos[index].descuentoImporte = parseNonNegativeDecimal(descImporteInput.value);
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

            const parent = event.target.closest('tr.parent');
            if (parent && els.resultadosTbody.contains(parent)) {
                const open = parent.getAttribute('aria-expanded') === 'true';
                parent.setAttribute('aria-expanded', open ? 'false' : 'true');
                $$(`#cotizacion-resultados-tbody tr.detail[data-g="${parent.dataset.group}"]`).forEach(r => { r.hidden = open; });
                return;
            }

            // Click en el resto de la fila: abre el detalle del plan. Sigue marcándola
            // como seleccionada (mismo comportamiento que antes, y la barra de cierre
            // necesita una opción vigente para habilitar Continuar).
            const selectable = event.target.closest('tr[data-cotizacion-opcion-key]');
            if (selectable && els.resultadosTbody.contains(selectable)) {
                const row = findRowByKey(selectable.dataset.cotizacionOpcionKey);
                if (row) seleccionarRow(row, { abrirDrawer: true });
            }
        });

        // Pie del drawer: elegir la alternativa que se estaba mirando y cerrar — sin
        // esto, llegar al detalle explorando dejaba al operador sin salida hacia la
        // decisión (tenía que cerrar y buscar la fila de nuevo).
        root.querySelector('[data-cotizacion-elegir-drawer]')?.addEventListener('click', () => {
            const row = findRowByKey(state.planAbiertoKey);
            if (row) seleccionarRow(row, { abrirDrawer: false });
            window.closeModal?.('modal-plan');
        });

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

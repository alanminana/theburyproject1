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
        pendingDeleteIndex: null,
        cotizacionGuardadaId: null,
        cotizacionGuardadaClienteId: null,
        busy: false
    };

    const urls = {
        simular: root.dataset.simularUrl || '/api/cotizacion/simular',
        guardar: root.dataset.guardarUrl || '/api/cotizacion/guardar',
        productos: root.dataset.productosUrl || '/Cotizacion/BuscarProductos',
        productoResumen: root.dataset.productoResumenUrl || '/Cotizacion/ProductoResumen',
        clientes: root.dataset.clientesUrl || '/Cotizacion/BuscarClientes',
        convertirBase: root.dataset.convertirBaseUrl || '/api/cotizacion',
        ventaEdit: root.dataset.ventaEditUrl || '/Venta/Edit/'
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
        headerUnitCount: $('#header-unit-count'),
        sideTotal: $('[data-side-total]'),
        clienteBuscar: $('#cotizacion-cliente-buscar'),
        clientesDropdown: $('#cotizacion-clientes-dropdown'),
        clienteId: $('#cotizacion-cliente-id'),
        clienteSeleccionado: $('#cotizacion-cliente-seleccionado'),
        clienteNombre: $('#cotizacion-cliente-nombre'),
        clienteDoc: $('#cotizacion-cliente-doc'),
        limpiarCliente: $('#cotizacion-limpiar-cliente'),
        nombreLibre: $('#cotizacion-nombre-libre'),
        telefonoLibre: $('#cotizacion-telefono-libre'),
        descuentoGralPct: $('#cotizacion-descuento-gral-pct'),
        descuentoGralImporte: $('#cotizacion-descuento-gral-importe'),
        fechaVencimiento: $('#cotizacion-fecha-vencimiento'),
        observaciones: $('#cotizacion-observaciones'),
        simular: $('#cotizacion-simular'),
        guardar: $('#cotizacion-guardar'),
        guardarConfirm: $('#cotizacion-guardar-confirm'),
        accionesPre: $('#cotizacion-acciones-pre'),
        accionesPost: $('#cotizacion-acciones-post'),
        pasarVenta: $('#cotizacion-pasar-venta'),
        verGuardada: $('#cotizacion-ver-guardada'),
        nuevaCotizacion: $('#cotizacion-nueva'),
        quitarConfirm: $('#cotizacion-quitar-confirm'),
        simularEstado: $('#cotizacion-simular-estado'),
        resultadosVacio: $('#cotizacion-resultados-vacio'),
        resultados: $('#cotizacion-resultados'),
        subtotal: $('#cotizacion-subtotal'),
        descuento: $('#cotizacion-descuento'),
        totalBase: $('#cotizacion-total-base'),
        resultadosTbody: $('#cotizacion-resultados-tbody'),
        // modal guardar summary
        mgCliente: $('#modal-guardar-cliente'),
        mgProductos: $('#modal-guardar-productos'),
        mgTotal: $('#modal-guardar-total'),
        mgMejor: $('#modal-guardar-mejor'),
        // plan drawer
        planMedio: $('#plan-medio'),
        planCuotas: $('#plan-cuotas'),
        planTotal: $('#plan-total'),
        planDetalleCuotas: $('#plan-detalle-cuotas'),
        planValorCuota: $('#plan-valor-cuota'),
        planRecargo: $('#plan-recargo')
    };

    function show(el) { el?.classList.remove('hidden'); }
    function hide(el) { el?.classList.add('hidden'); }
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
        els.feedback.className = `cotz-feedback ${variant} fixed top-3 right-3 z-[60] max-w-sm`;
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
            updateGuardarModal();
            return;
        }

        hide(els.productosVacio);
        state.productos.forEach((producto, index) => {
            const subtotal = Number(producto.precioUnitario) * Number(producto.cantidad);
            const article = document.createElement('article');
            article.className = 'cart-row';
            article.innerHTML = `
                <div class="flex items-start gap-2">
                    <div class="min-w-0 flex-1">
                        <div class="text-sm font-medium text-white truncate-1">${esc(producto.nombre || `Producto ${producto.productoId}`)}</div>
                        <div class="text-[11px] text-slate-500 font-mono">ID ${producto.productoId}${producto.codigo ? ' · ' + esc(producto.codigo) : ''}</div>
                    </div>
                    <button type="button" data-cotizacion-eliminar-index="${index}" class="icon-btn btn btn-ghost text-slate-500 hover:text-red-300" aria-label="Quitar">
                        <span class="material-symbols-outlined" style="font-size:16px">close</span>
                    </button>
                </div>
                <div class="flex items-center justify-between gap-2">
                    <div class="qty-step">
                        <button type="button" aria-label="Restar" onclick="stepRow(this,-1)">−</button>
                        <input type="number" min="1" value="${producto.cantidad}" data-cotizacion-cantidad-index="${index}" aria-label="Cantidad">
                        <button type="button" aria-label="Sumar" onclick="stepRow(this,1)">+</button>
                    </div>
                    <div class="text-right">
                        <div class="text-[10px] uppercase tracking-wide text-slate-500">Precio vig.</div>
                        <div class="text-sm text-white total-display">${formatCurrency(producto.precioUnitario)}</div>
                    </div>
                </div>
                <div class="grid grid-cols-3 gap-1.5 items-end">
                    <label class="block"><span class="text-[10px] text-slate-500">Dto. %</span>
                        <input type="number" value="${producto.descuentoPorcentaje ?? ''}" min="0" max="100" step="0.01" placeholder="0" data-cotizacion-desc-pct-index="${index}" aria-label="Descuento porcentaje producto" class="mini w-full mt-0.5"></label>
                    <label class="block"><span class="text-[10px] text-slate-500">Dto. $</span>
                        <input type="number" value="${producto.descuentoImporte ?? ''}" min="0" step="0.01" placeholder="0" data-cotizacion-desc-importe-index="${index}" aria-label="Descuento importe producto" class="mini w-full mt-0.5"></label>
                    <div class="text-right">
                        <div class="text-[10px] text-slate-500">Subtotal</div>
                        <div class="text-sm font-semibold text-white total-display mt-0.5">${formatCurrency(subtotal)}</div>
                    </div>
                </div>`;
            els.productosTbody.appendChild(article);
        });
        updateGuardarModal();
    }

    function previewBase() {
        return state.productos.reduce((acc, p) => acc + Number(p.precioUnitario) * Number(p.cantidad), 0);
    }

    function updateHeaderCounts() {
        const items = state.productos.length;
        const unidades = state.productos.reduce((acc, p) => acc + Number(p.cantidad), 0);
        if (els.headerProdCount) els.headerProdCount.textContent = String(items);
        if (els.headerUnitCount) els.headerUnitCount.textContent = String(unidades);
        // total lateral: si hay simulación usamos la base real, si no, preview bruto
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
                els.productoSeleccionado.innerHTML = `<span class="material-symbols-outlined text-blue-400" style="font-size:14px">check_circle</span> ${esc(producto.nombre)} · ${esc(formatCurrency(producto.precioVenta))} · Stock ${esc(producto.stockActual ?? '-')}`;
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
        if (els.guardar) els.guardar.disabled = true;
        resetGuardado();
        if (hadResults) setState('pending');
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
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'dropdown-item flex w-full items-start justify-between gap-3 text-left';
                button.innerHTML = `
                    <span class="min-w-0">
                        <span class="block text-sm font-medium text-white truncate-1">${esc(producto.nombre)}</span>
                        <span class="block text-[11px] text-slate-500">${esc(producto.codigo || `ID ${producto.id}`)} · ${esc(producto.categoria || 'Sin categoría')}</span>
                    </span>
                    <span class="shrink-0 text-right text-xs font-semibold text-slate-300 total-display">${formatCurrency(producto.precioVenta)}</span>`;
                button.addEventListener('click', () => setProductoSeleccionado(producto));
                els.productosDropdown.appendChild(button);
            });

        show(els.productosDropdown);
        els.productoBuscar?.setAttribute('aria-expanded', 'true');
    }

    function setCliente(cliente) {
        state.clienteSeleccionado = cliente;
        if (els.clienteId) els.clienteId.value = cliente?.id || '';
        if (els.clienteBuscar) {
            els.clienteBuscar.value = cliente?.display || '';
            els.clienteBuscar.setAttribute('aria-expanded', 'false');
        }
        hide(els.clientesDropdown);

        if (!cliente) {
            hide(els.clienteSeleccionado);
            updateGuardarModal();
            return;
        }

        if (els.clienteNombre) els.clienteNombre.textContent = cliente.display || `${cliente.nombre} ${cliente.apellido}`;
        if (els.clienteDoc) els.clienteDoc.textContent = `${cliente.tipoDocumento || 'Doc'}: ${cliente.numeroDocumento || '-'}`;
        show(els.clienteSeleccionado);
        updateGuardarModal();
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
            button.innerHTML = `
                <span class="block text-sm font-medium text-white truncate-1">${esc(cliente.display || `${cliente.nombre} ${cliente.apellido}`)}</span>
                <span class="block text-[11px] text-slate-500 font-mono">${esc(cliente.tipoDocumento || 'Doc')}: ${esc(cliente.numeroDocumento || '-')}</span>`;
            button.addEventListener('click', () => setCliente(cliente));
            els.clientesDropdown.appendChild(button);
        });

        show(els.clientesDropdown);
        els.clienteBuscar?.setAttribute('aria-expanded', 'true');
    }

    function buildRequest() {
        const descPct = parseNonNegativeDecimal(els.descuentoGralPct?.value);
        const descImporte = parseNonNegativeDecimal(els.descuentoGralImporte?.value);

        const request = {
            clienteId: state.clienteSeleccionado?.id || null,
            nombreClienteLibre: els.nombreLibre?.value?.trim() || null,
            descuentoGeneralPorcentaje: descPct !== null && descPct > 0 ? descPct : null,
            descuentoGeneralImporte: descImporte !== null && descImporte > 0 ? descImporte : null,
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

    async function guardar() {
        clearFeedback();
        if (!state.ultimaSimulacion?.exitoso) {
            showFeedback('Primero simula una cotizacion valida.', 'warning');
            return;
        }

        setBusy(true);
        try {
            const payload = {
                simulacion: buildRequest(),
                opcionSeleccionada: state.opcionSeleccionada,
                observaciones: els.observaciones?.value?.trim() || null,
                nombreClienteLibre: els.nombreLibre?.value?.trim() || null,
                telefonoClienteLibre: els.telefonoLibre?.value?.trim() || null,
                fechaVencimiento: els.fechaVencimiento?.value || null
            };

            const data = await fetchJson(urls.guardar, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                },
                body: JSON.stringify(payload)
            });

            window.closeModal?.('modal-guardar');
            state.cotizacionGuardadaId = data.id;
            state.cotizacionGuardadaClienteId = state.clienteSeleccionado?.id || null;
            setState('saved');
            showFeedback(`Cotizacion ${data.numero} guardada. Ya podés pasarla a venta.`, 'ok');
            mostrarAccionesPostGuardado(data);
        } catch (error) {
            showFeedback(error.message || 'No se pudo guardar la cotizacion.', 'error');
        } finally {
            setBusy(false);
        }
    }

    // Conversión directa (1 clic): la cotización recién guardada usa precios
    // vigentes, así que se convierte con precio cotizado y auto-confirma avisos
    // informativos (p. ej. unidades trazables se asignan luego en Venta/Edit).
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
            6: 'CuotaInactiva'
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

    function recargoValor(plan) {
        if (!plan) return 0;
        return Math.max(Number(plan.recargoPorcentaje || 0), Number(plan.interesPorcentaje || 0), Number(plan.costoFinancieroTotal || 0));
    }

    function pct(n) {
        return `${new Intl.NumberFormat('es-AR', { maximumFractionDigits: 2 }).format(n)}%`;
    }

    const ENT_COLORS = ['#3b82f6', '#22d3ee', '#f97316', '#a855f7', '#10b981', '#eab308', '#ec4899'];
    function entColor(name) {
        let h = 0;
        const s = String(name || '');
        for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
        return ENT_COLORS[h % ENT_COLORS.length];
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
        if (estadoStr === 'RequiereCliente' || estadoStr === 'RequiereEvaluacion') {
            return { cls: 'pill-amber', label: 'Req. cliente' };
        }
        return { cls: 'pill-red', label: 'Sin planes' };
    }

    function renderResultado(data) {
        if (!data || !els.resultadosTbody) return;

        if (els.subtotal) els.subtotal.textContent = formatCurrency(data.subtotal);
        if (els.descuento) els.descuento.textContent = formatCurrency(data.descuentoTotal);
        if (els.totalBase) els.totalBase.textContent = formatCurrency(data.totalBase);
        updateHeaderCounts();

        els.resultadosTbody.replaceChildren();
        const rows = flattenOpciones(data.opcionesPago || []);

        if (!rows.length) {
            const tr = document.createElement('tr');
            tr.className = 'off';
            tr.innerHTML = `<td colspan="6" class="text-center text-sm text-slate-500" style="padding:1.5rem">No hay medios disponibles para los filtros seleccionados.</td>`;
            els.resultadosTbody.appendChild(tr);
        } else {
            // mejor global: menor total con plan disponible
            let bestKey = null, bestTotal = Infinity;
            rows.forEach(r => {
                if (r.plan && Number(r.plan.total) < bestTotal) { bestTotal = Number(r.plan.total); bestKey = optionKey(r); }
            });

            const groups = groupByMedioPago(rows);
            const frag = document.createDocumentFragment();
            groups.forEach(group => appendGroup(frag, group, bestKey));
            els.resultadosTbody.appendChild(frag);

            // auto-seleccionar recomendado (o el mejor) para habilitar guardar
            const recomendado = rows.find(r => r.plan?.recomendado) || rows.find(r => r.plan && optionKey(r) === bestKey) || rows.find(r => r.plan);
            if (recomendado) {
                state.opcionSeleccionada = toSeleccion(recomendado);
                updateSelectedRowHighlight(optionKey(recomendado));
            }
        }

        hide(els.resultadosVacio);
        show(els.resultados);
        updateGuardarModal();

        const mensajes = [...(data.errores || []), ...(data.advertencias || [])];
        if (mensajes.length) {
            showFeedback(mensajes.join(' '), data.errores?.length ? 'error' : 'warning');
        }

        if (els.guardar) els.guardar.disabled = data.exitoso === false;
    }

    function appendGroup(frag, group, bestKey) {
        const meta = medioMeta(group.medioPago);
        const planRows = group.rows.filter(r => r.plan);
        const estadoStr = estadoLabel(group.opcion.estado);

        // Sin planes disponibles -> fila off
        if (!planRows.length) {
            const tr = document.createElement('tr');
            tr.className = 'off';
            const pill = estadoPill(estadoStr);
            const motivo = group.opcion.motivoNoDisponible
                || (pill.label === 'Req. cliente' ? 'Seleccioná un cliente para evaluar el crédito.' : 'No hay planes activos para el medio solicitado.');
            const motivoIcon = pill.label === 'Req. cliente' ? 'person_alert' : 'warning';
            tr.innerHTML = `
                <td><span class="rmedio"><span class="pay-ico pay-ico--slate"><span class="material-symbols-outlined" style="font-size:16px">${meta.icon}</span></span><span class="font-medium text-slate-300">${esc(group.label)}</span></span></td>
                <td class="r text-slate-500">—</td>
                <td colspan="3" class="text-xs text-amber-200/80"><span class="material-symbols-outlined text-amber-300" style="font-size:14px">${motivoIcon}</span> ${esc(motivo)}</td>
                <td><span class="pill ${pill.cls}">${esc(pill.label)}</span></td>`;
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

        const parent = document.createElement('tr');
        parent.className = 'parent';
        parent.setAttribute('aria-expanded', 'true');
        parent.dataset.group = gkey;
        parent.innerHTML = `
            <td><span class="rmedio"><span class="pay-ico pay-ico--${meta.tone}"><span class="material-symbols-outlined" style="font-size:16px">${meta.icon}</span></span><span class="font-medium text-white">${esc(group.label)}</span><span class="material-symbols-outlined twist">expand_more</span></span></td>
            <td class="r"><span class="text-[10px] text-slate-500">desde </span><span class="total-display font-semibold text-white">${formatCurrency(minTotal)}</span></td>
            <td class="text-slate-300">${planRows.length} planes</td>
            <td class="r text-slate-500">—</td>
            <td class="r ${maxR > 0 ? 'text-amber-300' : 'text-emerald-400'}">${recargoTxt}</td>
            <td><span class="pill pill-slate">${planRows.length} opciones</span></td>`;
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

    function pillForRow(row, bestKey) {
        const key = optionKey(row);
        if (key === bestKey) return '<span class="pill pill-green"><span class="material-symbols-outlined" style="font-size:12px">star</span> Mejor</span>';
        if (row.plan?.recomendado) return '<span class="pill pill-blue">Recomendado</span>';
        return '<span class="pill pill-slate">Elegir</span>';
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
        tr.innerHTML = `
            <td><span class="rmedio"><span class="pay-ico pay-ico--${meta.tone}"><span class="material-symbols-outlined" style="font-size:16px">${meta.icon}</span></span><span class="font-medium text-white">${esc(medioLabel(row.opcion.medioPago, row.opcion.nombreMedioPago))}</span></span></td>
            <td class="r"><span class="total-display font-semibold text-white">${formatCurrency(plan.total)}</span></td>
            <td class="text-slate-300">${planLabelCuotas(plan)}</td>
            <td class="r ${Number(plan.cantidadCuotas) > 1 ? 'text-slate-300 total-display' : 'text-slate-400'}">${cuotasTxt}</td>
            <td class="r ${r > 0 ? 'text-amber-300' : 'text-emerald-400'}">${r > 0 ? '+' : ''}${pct(r)}</td>
            <td>${pillForRow(row, bestKey)}</td>`;
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
        const planName = plan.plan || medioLabel(row.opcion.medioPago, row.opcion.nombreMedioPago);
        tr.innerHTML = `
            <td><span class="plan-medio"><span class="ent-dot" style="background:${entColor(planName)}"></span><span class="text-slate-200 font-medium">${esc(planName)}</span></span></td>
            <td class="r"><span class="total-display text-white">${formatCurrency(plan.total)}</span></td>
            <td class="text-slate-300">${planLabelCuotas(plan)}</td>
            <td class="r total-display text-slate-300">${Number(plan.cantidadCuotas) > 1 ? formatCurrency(plan.valorCuota) : '—'}</td>
            <td class="r ${r > 0 ? 'text-amber-300' : 'text-emerald-400'}">${r > 0 ? '+' : ''}${pct(r)}</td>
            <td>${pillForRow(row, bestKey)}</td>`;
        return tr;
    }

    function updateSelectedRowHighlight(selectedKey) {
        $$('#cotizacion-resultados-tbody tr[data-cotizacion-row-key]').forEach(tr => {
            tr.classList.toggle('selected', tr.dataset.cotizacionRowKey === selectedKey);
        });
    }

    function findRowByKey(key) {
        const rows = flattenOpciones(state.ultimaSimulacion?.opcionesPago || []);
        return rows.find(r => optionKey(r) === key) || null;
    }

    function openPlanDrawer(row) {
        if (!row?.plan) return;
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
            els.planRecargo.className = (r > 0 ? 'text-amber-300' : 'text-emerald-400') + ' font-mono';
        }
        window.openModal?.('modal-plan');
    }

    function updateGuardarModal() {
        if (els.mgCliente) {
            els.mgCliente.textContent = state.clienteSeleccionado?.display
                || (els.nombreLibre?.value?.trim() || '—');
        }
        if (els.mgProductos) {
            const items = state.productos.length;
            const unidades = state.productos.reduce((acc, p) => acc + Number(p.cantidad), 0);
            els.mgProductos.textContent = items
                ? `${items} ${items === 1 ? 'ítem' : 'ítems'} · ${unidades} ${unidades === 1 ? 'unidad' : 'unidades'}`
                : '—';
        }
        const base = state.ultimaSimulacion?.totalBase ?? previewBase();
        if (els.mgTotal) els.mgTotal.textContent = formatCurrency(base);
        if (els.mgMejor) {
            const sel = state.opcionSeleccionada;
            if (sel && state.ultimaSimulacion) {
                const row = findRowByKey(`${sel.medioPago}|${sel.plan || ''}|${sel.cantidadCuotas || ''}`);
                if (row?.plan) {
                    els.mgMejor.textContent = `${medioLabel(row.opcion.medioPago, row.opcion.nombreMedioPago)} · ${formatCurrency(row.plan.total)}`;
                } else {
                    els.mgMejor.textContent = '—';
                }
            } else {
                els.mgMejor.textContent = '—';
            }
        }
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
        els.guardarConfirm?.addEventListener('click', guardar);
        els.pasarVenta?.addEventListener('click', pasarAVenta);
        els.nuevaCotizacion?.addEventListener('click', () => window.location.reload());

        els.limpiarCliente?.addEventListener('click', () => {
            setCliente(null);
            if (els.clienteBuscar) els.clienteBuscar.value = '';
        });

        // medios filter -> marca pendiente si ya había simulación
        $$('[data-cotizacion-medio]').forEach(input => {
            input.addEventListener('change', () => invalidarSimulacion());
        });

        // descuentos generales -> pendiente
        [els.descuentoGralPct, els.descuentoGralImporte].forEach(el => {
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

        // resultados: expandir grupos / seleccionar plan
        els.resultadosTbody?.addEventListener('click', event => {
            const parent = event.target.closest('tr.parent');
            if (parent && els.resultadosTbody.contains(parent)) {
                const open = parent.getAttribute('aria-expanded') === 'true';
                parent.setAttribute('aria-expanded', open ? 'false' : 'true');
                $$(`#cotizacion-resultados-tbody tr.detail[data-g="${parent.dataset.group}"]`).forEach(r => { r.hidden = open; });
                return;
            }

            const selectable = event.target.closest('tr[data-cotizacion-opcion-key]');
            if (selectable && els.resultadosTbody.contains(selectable)) {
                const key = selectable.dataset.cotizacionOpcionKey;
                const row = findRowByKey(key);
                state.opcionSeleccionada = row ? toSeleccion(row) : null;
                updateSelectedRowHighlight(key);
                updateGuardarModal();
                if (row) openPlanDrawer(row);
            }
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
})();

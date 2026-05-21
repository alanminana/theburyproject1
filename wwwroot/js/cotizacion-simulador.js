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
        busy: false
    };

    const urls = {
        simular: root.dataset.simularUrl || '/api/cotizacion/simular',
        guardar: root.dataset.guardarUrl || '/api/cotizacion/guardar',
        productos: root.dataset.productosUrl || '/Cotizacion/BuscarProductos',
        productoResumen: root.dataset.productoResumenUrl || '/Cotizacion/ProductoResumen',
        clientes: root.dataset.clientesUrl || '/Cotizacion/BuscarClientes'
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
        simularEstado: $('#cotizacion-simular-estado'),
        resultadosVacio: $('#cotizacion-resultados-vacio'),
        resultados: $('#cotizacion-resultados'),
        subtotal: $('#cotizacion-subtotal'),
        descuento: $('#cotizacion-descuento'),
        totalBase: $('#cotizacion-total-base'),
        resultadosTbody: $('#cotizacion-resultados-tbody')
    };

    function show(el) { el?.classList.remove('hidden'); }
    function hide(el) { el?.classList.add('hidden'); }

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
            els.simular.querySelector('.material-symbols-outlined').textContent = value ? 'progress_activity' : 'calculate';
        }
        if (els.guardar) {
            els.guardar.disabled = value || !state.ultimaSimulacion?.exitoso;
            els.guardar.querySelector('.material-symbols-outlined').textContent = value ? 'progress_activity' : 'save';
        }
        if (els.simularEstado) {
            els.simularEstado.textContent = value ? 'Simulando...' : 'Listo para simular.';
        }
    }

    function showFeedback(message, tone) {
        if (!els.feedback || !message) return;
        const palette = {
            error: 'border-red-500/20 bg-red-500/10 text-red-400',
            warning: 'border-amber-500/20 bg-amber-500/10 text-amber-400',
            ok: 'border-emerald-500/20 bg-emerald-500/10 text-emerald-400',
            info: 'border-primary/20 bg-primary/10 text-primary'
        };
        const icon = tone === 'error' ? 'error' : tone === 'warning' ? 'warning' : tone === 'ok' ? 'check_circle' : 'info';
        els.feedback.className = `flex items-center gap-3 rounded-lg border p-4 text-sm font-semibold ${palette[tone] || palette.info}`;
        els.feedback.innerHTML = `<span class="material-symbols-outlined text-lg">${icon}</span><p>${esc(message)}</p>`;
    }

    function clearFeedback() {
        if (!els.feedback) return;
        els.feedback.className = 'hidden';
        els.feedback.replaceChildren();
    }

    function renderProductos() {
        if (!els.productosTbody) return;
        els.productosTbody.replaceChildren();

        if (state.productos.length === 0) {
            show(els.productosVacio);
            return;
        }

        hide(els.productosVacio);
        state.productos.forEach((producto, index) => {
            const tr = document.createElement('tr');
            tr.className = 'hover:bg-white/5';
            tr.innerHTML = `
                <td class="px-4 py-3">
                    <p class="text-sm font-bold text-white">${esc(producto.nombre || `Producto ${producto.productoId}`)}</p>
                    <p class="text-xs text-slate-500">ID ${producto.productoId}</p>
                </td>
                <td class="px-4 py-3 text-sm font-mono text-slate-400">${esc(producto.codigo || '-')}</td>
                <td class="px-4 py-3 text-right">
                    <input type="number" min="1" step="1" value="${producto.cantidad}"
                           data-cotizacion-cantidad-index="${index}"
                           class="w-20 rounded border border-slate-700 bg-slate-800 px-2 py-1.5 text-right text-sm text-white focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/30" />
                </td>
                <td class="px-4 py-3 text-right text-sm font-semibold text-slate-200">${formatCurrency(producto.precioUnitario)}</td>
                <td class="px-4 py-3 text-right">
                    <input type="number" min="0" max="100" step="0.01"
                           value="${producto.descuentoPorcentaje ?? ''}"
                           data-cotizacion-desc-pct-index="${index}"
                           aria-label="Descuento porcentaje producto"
                           class="w-16 rounded border border-slate-700 bg-slate-800 px-1.5 py-1.5 text-right text-sm text-white focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/30"
                           placeholder="0" />
                </td>
                <td class="px-4 py-3 text-right">
                    <input type="number" min="0" step="0.01"
                           value="${producto.descuentoImporte ?? ''}"
                           data-cotizacion-desc-importe-index="${index}"
                           aria-label="Descuento importe producto"
                           class="w-16 rounded border border-slate-700 bg-slate-800 px-1.5 py-1.5 text-right text-sm text-white focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/30"
                           placeholder="0" />
                </td>
                <td class="px-4 py-3 text-right text-sm font-bold text-white">${formatCurrency(producto.precioUnitario * producto.cantidad)}</td>
                <td class="px-4 py-3 text-right">
                    <button type="button" data-cotizacion-eliminar-index="${index}"
                            class="inline-flex size-8 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-red-500/10 hover:text-red-400"
                            title="Eliminar producto" aria-label="Eliminar producto">
                        <span class="material-symbols-outlined text-lg">delete</span>
                    </button>
                </td>`;
            els.productosTbody.appendChild(tr);
        });
    }

    function setProductoSeleccionado(producto) {
        state.productoSeleccionado = producto;
        if (els.productoBuscar) {
            els.productoBuscar.value = producto ? `${producto.codigo || producto.id} - ${producto.nombre}` : '';
            els.productoBuscar.setAttribute('aria-expanded', 'false');
        }
        if (els.productoSeleccionado) {
            els.productoSeleccionado.textContent = producto
                ? `${producto.nombre} - ${formatCurrency(producto.precioVenta)} - Stock ${producto.stockActual ?? '-'}`
                : 'Sin producto seleccionado.';
        }
        hide(els.productosDropdown);
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

        state.ultimaSimulacion = null;
        state.opcionSeleccionada = null;
        if (els.guardar) els.guardar.disabled = true;
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
            item.className = 'px-4 py-3 text-sm text-slate-500';
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
                button.className = 'flex w-full items-start justify-between gap-3 px-4 py-3 text-left transition-colors hover:bg-slate-700';
                button.innerHTML = `
                    <span>
                        <span class="block text-sm font-bold text-white">${esc(producto.nombre)}</span>
                        <span class="block text-xs text-slate-500">${esc(producto.codigo || `ID ${producto.id}`)} · ${esc(producto.categoria || 'Sin categoria')}</span>
                    </span>
                    <span class="shrink-0 text-right text-xs font-bold text-slate-300">${formatCurrency(producto.precioVenta)}</span>`;
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
            return;
        }

        if (els.clienteNombre) els.clienteNombre.textContent = cliente.display || `${cliente.nombre} ${cliente.apellido}`;
        if (els.clienteDoc) els.clienteDoc.textContent = `${cliente.tipoDocumento || 'Doc'}: ${cliente.numeroDocumento || '-'}`;
        show(els.clienteSeleccionado);
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
            item.className = 'px-4 py-3 text-sm text-slate-500';
            item.textContent = emptyMessage || 'Sin resultados.';
            els.clientesDropdown.appendChild(item);
            show(els.clientesDropdown);
            els.clienteBuscar?.setAttribute('aria-expanded', 'true');
            return;
        }

        clientes.forEach(cliente => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'block w-full px-4 py-3 text-left transition-colors hover:bg-slate-700';
            button.innerHTML = `
                <span class="block text-sm font-bold text-white">${esc(cliente.display || `${cliente.nombre} ${cliente.apellido}`)}</span>
                <span class="block text-xs text-slate-500">${esc(cliente.tipoDocumento || 'Doc')}: ${esc(cliente.numeroDocumento || '-')}</span>`;
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
                showFeedback('La simulacion devolvio observaciones que requieren revision.', 'warning');
            } else {
                showFeedback('Cotizacion simulada correctamente.', 'ok');
            }
        } catch (error) {
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

            showFeedback(`Cotizacion ${data.numero} guardada correctamente.`, 'ok');
            if (data.detalleUrl) {
                window.location.assign(data.detalleUrl);
            }
        } catch (error) {
            showFeedback(error.message || 'No se pudo guardar la cotizacion.', 'error');
        } finally {
            setBusy(false);
        }
    }

    function renderResultado(data) {
        if (!data || !els.resultadosTbody) return;

        els.subtotal.textContent = formatCurrency(data.subtotal);
        els.descuento.textContent = formatCurrency(data.descuentoTotal);
        els.totalBase.textContent = formatCurrency(data.totalBase);

        els.resultadosTbody.replaceChildren();
        const rows = flattenOpciones(data.opcionesPago || []);

        if (!rows.length) {
            const empty = document.createElement('div');
            empty.className = 'col-span-full px-4 py-8 text-center text-sm text-slate-500';
            empty.textContent = 'No hay medios disponibles para los filtros seleccionados.';
            els.resultadosTbody.appendChild(empty);
        } else {
            const groups = groupByMedioPago(rows);
            groups.forEach(group => els.resultadosTbody.appendChild(renderResultadoGroup(group)));
            const recomendado = rows.find(row => row.plan?.recomendado) || rows.find(row => row.plan);
            if (recomendado) {
                state.opcionSeleccionada = toSeleccion(recomendado);
                const radio = Array.from(els.resultadosTbody.querySelectorAll('[data-cotizacion-opcion-key]'))
                    .find(input => input.dataset.cotizacionOpcionKey === optionKey(recomendado));
                if (radio) radio.checked = true;
                updateSelectedRowHighlight(optionKey(recomendado));
            }
        }

        hide(els.resultadosVacio);
        show(els.resultados);

        const mensajes = [...(data.errores || []), ...(data.advertencias || [])];
        if (mensajes.length) {
            showFeedback(mensajes.join(' '), data.errores?.length ? 'error' : 'warning');
        }

        if (els.guardar) {
            els.guardar.disabled = data.exitoso === false;
        }
    }

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

    function estadoBadge(estado) {
        const label = estadoLabel(estado);
        const modifier = {
            Disponible: 'payment-status-chip--available',
            RequiereCliente: 'payment-status-chip--requires-client',
            RequiereEvaluacion: 'payment-status-chip--requires-client',
            BloqueadoPorProducto: 'payment-status-chip--blocked',
            PlanInactivo: 'payment-status-chip--blocked',
            CuotaInactiva: 'payment-status-chip--blocked',
            NoDisponible: 'payment-status-chip--blocked'
        }[label] || 'payment-status-chip--blocked';
        return `<span class="payment-status-chip ${modifier}">${esc(label)}</span>`;
    }

    function updateSelectedRowHighlight(selectedKey) {
        Array.from(els.resultadosTbody?.querySelectorAll('[data-cotizacion-row-key]') || []).forEach(el => {
            el.classList.toggle('payment-option-card--selected', el.dataset.cotizacionRowKey === selectedKey);
        });
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
            2: 'Tarjeta credito',
            3: 'Tarjeta debito',
            4: 'MercadoPago',
            5: 'Credito personal'
        }[medio] || 'Medio';
    }

    function groupByMedioPago(rows) {
        const groups = [];
        const seen = new Map();
        rows.forEach(row => {
            const key = row.opcion.medioPago;
            if (!seen.has(key)) {
                const group = { medioPago: key, label: medioLabel(key, row.opcion.nombreMedioPago), rows: [] };
                seen.set(key, group);
                groups.push(group);
            }
            seen.get(key).rows.push(row);
        });
        return groups;
    }

    function renderResultadoCard(row) {
        const opcion = row.opcion;
        const plan = row.plan;
        const key = optionKey(row);
        const estadoStr = estadoLabel(opcion.estado);

        const advertencias = [
            opcion.motivoNoDisponible,
            ...(plan?.advertencias || [])
        ].filter(Boolean);

        const recargo = plan
            ? Math.max(Number(plan.recargoPorcentaje || 0), Number(plan.interesPorcentaje || 0), Number(plan.costoFinancieroTotal || 0))
            : 0;
        const recargoTexto = plan?.costoFinancieroTotal
            ? formatCurrency(plan.costoFinancieroTotal)
            : `${new Intl.NumberFormat('es-AR', { maximumFractionDigits: 2 }).format(recargo)}%`;

        let cardModifier = '';
        if (!plan) {
            cardModifier = 'payment-option-card--blocked';
        } else if (['RequiereCliente', 'RequiereEvaluacion'].includes(estadoStr) || advertencias.length) {
            cardModifier = 'payment-option-card--warning';
        }

        const div = document.createElement('div');
        div.className = `payment-option-card ${cardModifier}`.trim();
        div.dataset.cotizacionRowKey = key;

        // Header: radio+medio a la izquierda, total a la derecha
        const header = document.createElement('div');
        Object.assign(header.style, { display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: '0.75rem', width: '100%' });

        const leftCol = document.createElement('div');
        Object.assign(leftCol.style, { display: 'flex', flexDirection: 'column', gap: '0.375rem', minWidth: '0' });

        const radioLabel = document.createElement('label');
        Object.assign(radioLabel.style, { display: 'flex', alignItems: 'center', gap: '0.5rem', cursor: !plan ? 'not-allowed' : 'pointer' });

        const radioInput = document.createElement('input');
        radioInput.type = 'radio';
        radioInput.name = 'cotizacion-opcion-pago';
        radioInput.dataset.cotizacionOpcionKey = key;
        radioInput.className = 'border-slate-600 bg-slate-800 text-primary focus:ring-primary';
        if (!plan) radioInput.disabled = true;

        const medioSpan = document.createElement('span');
        Object.assign(medioSpan.style, { fontSize: '0.9375rem', fontWeight: '700' });
        medioSpan.textContent = medioLabel(opcion.medioPago, opcion.nombreMedioPago);

        radioLabel.appendChild(radioInput);
        radioLabel.appendChild(medioSpan);

        const chipModifier = {
            Disponible: 'payment-status-chip--available',
            RequiereCliente: 'payment-status-chip--requires-client',
            RequiereEvaluacion: 'payment-status-chip--requires-client',
            BloqueadoPorProducto: 'payment-status-chip--blocked',
            PlanInactivo: 'payment-status-chip--blocked',
            CuotaInactiva: 'payment-status-chip--blocked',
            NoDisponible: 'payment-status-chip--blocked'
        }[estadoStr] || 'payment-status-chip--blocked';

        const estadoChip = document.createElement('span');
        estadoChip.className = `payment-status-chip ${chipModifier}`;
        estadoChip.textContent = estadoStr;

        leftCol.appendChild(radioLabel);
        leftCol.appendChild(estadoChip);

        const rightCol = document.createElement('div');
        Object.assign(rightCol.style, { textAlign: 'right', flexShrink: '0' });

        if (plan?.recomendado) {
            const recBadge = document.createElement('span');
            recBadge.className = 'payment-status-chip payment-status-chip--selected';
            recBadge.style.marginBottom = '0.25rem';
            recBadge.textContent = 'Recomendado';
            rightCol.appendChild(recBadge);
        }

        const totalEl = document.createElement('div');
        Object.assign(totalEl.style, {
            fontSize: plan ? '1.125rem' : '0.875rem',
            fontWeight: plan ? '900' : '400',
            marginTop: '0.25rem',
            color: plan ? '' : '#475569'
        });
        totalEl.textContent = plan ? formatCurrency(plan.total) : 'Sin planes';
        rightCol.appendChild(totalEl);

        header.appendChild(leftCol);
        header.appendChild(rightCol);
        div.appendChild(header);

        if (plan) {
            const divider = document.createElement('div');
            Object.assign(divider.style, { width: '100%', height: '1px', background: '#1e293b', marginTop: '0.5rem' });
            div.appendChild(divider);

            const grid = document.createElement('div');
            Object.assign(grid.style, { width: '100%', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.375rem 1rem', marginTop: '0.125rem' });

            const addField = (labelText, valueText) => {
                const item = document.createElement('div');
                const lbl = document.createElement('div');
                Object.assign(lbl.style, { fontSize: '0.6875rem', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.05em', color: '#64748b' });
                lbl.textContent = labelText;
                const val = document.createElement('div');
                Object.assign(val.style, { fontSize: '0.8125rem', marginTop: '0.125rem' });
                val.textContent = valueText;
                item.appendChild(lbl);
                item.appendChild(val);
                grid.appendChild(item);
            };

            addField('Plan', plan.plan || '-');
            const cuotasText = Number(plan.cantidadCuotas) > 1
                ? `${plan.cantidadCuotas} x ${formatCurrency(plan.valorCuota)}`
                : 'Pago único';
            addField('Cuotas', cuotasText);
            addField('Recargo / Interés', recargoTexto);

            div.appendChild(grid);
        }

        if (advertencias.length) {
            const advRow = document.createElement('div');
            Object.assign(advRow.style, { display: 'flex', alignItems: 'flex-start', gap: '0.375rem', marginTop: '0.5rem', padding: '0.5rem 0.625rem', borderRadius: '0.375rem', background: 'rgba(245,158,11,0.08)', border: '1px solid rgba(245,158,11,0.2)', width: '100%', boxSizing: 'border-box' });

            const warnIcon = document.createElement('span');
            warnIcon.className = 'material-symbols-outlined';
            Object.assign(warnIcon.style, { fontSize: '14px', color: '#fbbf24', flexShrink: '0', marginTop: '1px' });
            warnIcon.textContent = 'warning';

            const advMsg = document.createElement('span');
            Object.assign(advMsg.style, { fontSize: '0.75rem', color: '#fbbf24', lineHeight: '1.4' });
            advMsg.textContent = advertencias.join(' · ');

            advRow.appendChild(warnIcon);
            advRow.appendChild(advMsg);
            div.appendChild(advRow);
        }

        return div;
    }

    function renderResultadoGroup(group) {
        const section = document.createElement('div');
        section.className = 'payment-option-group';
        Object.assign(section.style, { gridColumn: '1 / -1', display: 'flex', flexDirection: 'column', gap: '0.625rem' });

        const hdr = document.createElement('div');
        Object.assign(hdr.style, { display: 'flex', alignItems: 'center', gap: '0.625rem', paddingBottom: '0.375rem', borderBottom: '1px solid #1e293b' });

        const title = document.createElement('span');
        Object.assign(title.style, { fontSize: '0.8125rem', fontWeight: '800', textTransform: 'uppercase', letterSpacing: '0.06em', color: '#94a3b8' });
        title.textContent = group.label || 'Sin medio informado';

        const countBadge = document.createElement('span');
        const n = group.rows.length;
        Object.assign(countBadge.style, { fontSize: '0.75rem', fontWeight: '600', color: '#475569', background: '#0f172a', border: '1px solid #1e293b', borderRadius: '9999px', padding: '0.125rem 0.5rem' });
        countBadge.textContent = `${n} ${n === 1 ? 'opción' : 'opciones'}`;

        hdr.appendChild(title);
        hdr.appendChild(countBadge);
        section.appendChild(hdr);

        const grid = document.createElement('div');
        Object.assign(grid.style, { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: '0.75rem' });
        group.rows.forEach(row => grid.appendChild(renderResultadoCard(row)));
        section.appendChild(grid);

        return section;
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

    function bindEvents() {
        els.productoBuscar?.addEventListener('input', debounce(buscarProductos, 220));
        els.clienteBuscar?.addEventListener('input', debounce(buscarClientes, 220));

        els.agregarProducto?.addEventListener('click', () => agregarProducto(state.productoSeleccionado, els.cantidad?.value));
        els.agregarManual?.addEventListener('click', agregarProductoManual);
        els.simular?.addEventListener('click', simular);
        els.guardar?.addEventListener('click', guardar);

        els.limpiarCliente?.addEventListener('click', () => {
            setCliente(null);
            if (els.clienteBuscar) els.clienteBuscar.value = '';
        });

        els.productosTbody?.addEventListener('click', event => {
            const deleteButton = event.target.closest('[data-cotizacion-eliminar-index]');
            if (!deleteButton) return;
            const index = Number(deleteButton.dataset.cotizacionEliminarIndex);
            state.productos.splice(index, 1);
            state.ultimaSimulacion = null;
            state.opcionSeleccionada = null;
            if (els.guardar) els.guardar.disabled = true;
            renderProductos();
        });

        els.productosTbody?.addEventListener('input', event => {
            const cantInput = event.target.closest('[data-cotizacion-cantidad-index]');
            if (cantInput) {
                const index = Number(cantInput.dataset.cotizacionCantidadIndex);
                const qty = parsePositiveInt(cantInput.value) || 1;
                state.productos[index].cantidad = qty;
                cantInput.value = String(qty);
                state.ultimaSimulacion = null;
                state.opcionSeleccionada = null;
                if (els.guardar) els.guardar.disabled = true;
                renderProductos();
                return;
            }

            const descPctInput = event.target.closest('[data-cotizacion-desc-pct-index]');
            if (descPctInput) {
                const index = Number(descPctInput.dataset.cotizacionDescPctIndex);
                state.productos[index].descuentoPorcentaje = parseNonNegativeDecimal(descPctInput.value);
                state.ultimaSimulacion = null;
                state.opcionSeleccionada = null;
                if (els.guardar) els.guardar.disabled = true;
                return;
            }

            const descImporteInput = event.target.closest('[data-cotizacion-desc-importe-index]');
            if (descImporteInput) {
                const index = Number(descImporteInput.dataset.cotizacionDescImporteIndex);
                state.productos[index].descuentoImporte = parseNonNegativeDecimal(descImporteInput.value);
                state.ultimaSimulacion = null;
                state.opcionSeleccionada = null;
                if (els.guardar) els.guardar.disabled = true;
                return;
            }
        });

        els.resultadosTbody?.addEventListener('change', event => {
            const input = event.target.closest('[data-cotizacion-opcion-key]');
            if (!input) return;
            const rows = flattenOpciones(state.ultimaSimulacion?.opcionesPago || []);
            const row = rows.find(item => optionKey(item) === input.dataset.cotizacionOpcionKey);
            state.opcionSeleccionada = row ? toSeleccion(row) : null;
            updateSelectedRowHighlight(input.dataset.cotizacionOpcionKey || null);
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

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
        busy: false
    };

    const urls = {
        simular: root.dataset.simularUrl || '/api/cotizacion/simular',
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
        simular: $('#cotizacion-simular'),
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
                precioUnitario: Number(producto.precioVenta) || 0
            });
        }

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
        const request = {
            clienteId: state.clienteSeleccionado?.id || null,
            productos: state.productos.map(p => ({
                productoId: p.productoId,
                cantidad: p.cantidad
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

    function renderResultado(data) {
        if (!data || !els.resultadosTbody) return;

        els.subtotal.textContent = formatCurrency(data.subtotal);
        els.descuento.textContent = formatCurrency(data.descuentoTotal);
        els.totalBase.textContent = formatCurrency(data.totalBase);

        els.resultadosTbody.replaceChildren();
        const rows = flattenOpciones(data.opcionesPago || []);

        if (!rows.length) {
            const tr = document.createElement('tr');
            tr.innerHTML = '<td colspan="8" class="px-4 py-8 text-center text-sm text-slate-500">No hay medios disponibles para los filtros seleccionados.</td>';
            els.resultadosTbody.appendChild(tr);
        } else {
            rows.forEach(row => els.resultadosTbody.appendChild(renderResultadoRow(row)));
        }

        hide(els.resultadosVacio);
        show(els.resultados);

        const mensajes = [...(data.errores || []), ...(data.advertencias || [])];
        if (mensajes.length) {
            showFeedback(mensajes.join(' '), data.errores?.length ? 'error' : 'warning');
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
        const tone = {
            Disponible: 'border-emerald-500/20 bg-emerald-500/10 text-emerald-400',
            RequiereCliente: 'border-amber-500/20 bg-amber-500/10 text-amber-400',
            RequiereEvaluacion: 'border-amber-500/20 bg-amber-500/10 text-amber-400',
            BloqueadoPorProducto: 'border-red-500/20 bg-red-500/10 text-red-400',
            NoDisponible: 'border-slate-700 bg-slate-800 text-slate-400'
        }[label] || 'border-slate-700 bg-slate-800 text-slate-400';

        return `<span class="inline-flex rounded-full border px-2 py-0.5 text-xs font-bold ${tone}">${esc(label)}</span>`;
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

    function renderResultadoRow(row) {
        const opcion = row.opcion;
        const plan = row.plan;
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

        const tr = document.createElement('tr');
        tr.className = 'hover:bg-white/5';
        tr.innerHTML = `
            <td class="px-4 py-3 text-sm font-bold text-white">${esc(medioLabel(opcion.medioPago, opcion.nombreMedioPago))}</td>
            <td class="px-4 py-3">${estadoBadge(opcion.estado)}</td>
            <td class="px-4 py-3 text-sm text-slate-300">${esc(plan?.plan || '-')}</td>
            <td class="px-4 py-3 text-right text-sm text-slate-300">${plan?.cantidadCuotas ?? '-'}</td>
            <td class="px-4 py-3 text-right text-sm font-bold text-white">${plan ? formatCurrency(plan.total) : '-'}</td>
            <td class="px-4 py-3 text-right text-sm text-slate-300">${plan?.valorCuota ? formatCurrency(plan.valorCuota) : '-'}</td>
            <td class="px-4 py-3 text-right text-sm text-slate-300">${plan ? recargoTexto : '-'}</td>
            <td class="px-4 py-3 text-xs text-slate-400">${esc(advertencias.join(' · ') || '-')}</td>`;
        return tr;
    }

    function bindEvents() {
        els.productoBuscar?.addEventListener('input', debounce(buscarProductos, 220));
        els.clienteBuscar?.addEventListener('input', debounce(buscarClientes, 220));

        els.agregarProducto?.addEventListener('click', () => agregarProducto(state.productoSeleccionado, els.cantidad?.value));
        els.agregarManual?.addEventListener('click', agregarProductoManual);
        els.simular?.addEventListener('click', simular);

        els.limpiarCliente?.addEventListener('click', () => {
            setCliente(null);
            if (els.clienteBuscar) els.clienteBuscar.value = '';
        });

        els.productosTbody?.addEventListener('click', event => {
            const deleteButton = event.target.closest('[data-cotizacion-eliminar-index]');
            if (!deleteButton) return;
            const index = Number(deleteButton.dataset.cotizacionEliminarIndex);
            state.productos.splice(index, 1);
            renderProductos();
        });

        els.productosTbody?.addEventListener('input', event => {
            const input = event.target.closest('[data-cotizacion-cantidad-index]');
            if (!input) return;
            const index = Number(input.dataset.cotizacionCantidadIndex);
            const qty = parsePositiveInt(input.value) || 1;
            state.productos[index].cantidad = qty;
            input.value = String(qty);
            renderProductos();
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

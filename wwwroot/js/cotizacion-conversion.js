(function () {
    'use strict';

    const root = document.querySelector('[data-cotizacion-conversion]');
    if (!root) return;

    const formatCurrency = (window.TheBury && window.TheBury.formatCurrency)
        ? window.TheBury.formatCurrency
        : function (v) {
            return new Intl.NumberFormat('es-AR', {
                style: 'currency',
                currency: 'ARS',
                minimumFractionDigits: 2
            }).format(v || 0);
        };

    const urls = {
        preview: root.dataset.previewUrl,
        convertir: root.dataset.convertirUrl,
        clientes: root.dataset.clientesUrl || '/Cotizacion/BuscarClientes',
        ventaEdit: root.dataset.ventaEditUrl || '/Venta/Edit/'
    };

    const modal = document.getElementById('cotizacion-conversion-modal');
    const loading = document.getElementById('cotizacion-conversion-loading');
    const contenido = document.getElementById('cotizacion-conversion-contenido');
    const footer = document.getElementById('cotizacion-conversion-footer');
    const erroresPanel = document.getElementById('cotizacion-conversion-errores');
    const erroresLista = document.getElementById('cotizacion-conversion-errores-lista');
    const advertenciasPanel = document.getElementById('cotizacion-conversion-advertencias');
    const advertenciasLista = document.getElementById('cotizacion-conversion-advertencias-lista');
    const resumen = document.getElementById('cotizacion-conversion-resumen');
    const totalEl = document.getElementById('cotizacion-total-cotizado');
    const clienteOverridePanel = document.getElementById('cotizacion-cliente-override-panel');
    const clienteBuscarInput = document.getElementById('cotizacion-override-cliente-buscar');
    const clienteDropdown = document.getElementById('cotizacion-override-clientes-dropdown');
    const clienteIdInput = document.getElementById('cotizacion-override-cliente-id');
    const clienteNombreEl = document.getElementById('cotizacion-override-cliente-nombre');
    const detallesPreviewPanel = document.getElementById('cotizacion-detalles-preview-panel');
    const precioCotizadoRadio = document.getElementById('cotizacion-precio-cotizado');
    const precioActualRadio = document.getElementById('cotizacion-precio-actual');
    const confirmarAdvertenciasPanel = document.getElementById('cotizacion-confirmar-advertencias-panel');
    const checkAdvertencias = document.getElementById('cotizacion-check-advertencias');
    const btnConfirmar = document.getElementById('cotizacion-btn-confirmar-conversion');
    const errorGeneralPanel = document.getElementById('cotizacion-conversion-error-general');
    const errorGeneralTexto = document.getElementById('cotizacion-conversion-error-general-texto');

    let previewData = null;
    let clienteSearchTimer = null;
    let clienteSearchController = null;

    function show(el) { el.classList.remove('hidden'); }
    function hide(el) { el.classList.add('hidden'); }

    function clearChildren(el) {
        while (el.firstChild) el.removeChild(el.firstChild);
    }

    function resetBotonConfirmar() {
        clearChildren(btnConfirmar);
        const icon = document.createElement('span');
        icon.className = 'material-symbols-outlined text-lg';
        icon.textContent = 'check';
        btnConfirmar.appendChild(icon);
        btnConfirmar.appendChild(document.createTextNode(' Confirmar Conversion'));
        btnConfirmar.disabled = true;
    }

    function abrirModal() {
        show(modal);
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
    }

    function cerrarModal() {
        hide(modal);
        modal.classList.remove('flex');
        document.body.style.overflow = '';
        resetModal();
    }

    function resetModal() {
        previewData = null;
        hide(loading);
        hide(contenido);
        hide(footer);
        hide(erroresPanel);
        hide(advertenciasPanel);
        hide(resumen);
        hide(clienteOverridePanel);
        hide(confirmarAdvertenciasPanel);
        hide(errorGeneralPanel);
        if (detallesPreviewPanel) { clearChildren(detallesPreviewPanel); hide(detallesPreviewPanel); }
        clearChildren(erroresLista);
        clearChildren(advertenciasLista);
        clienteIdInput.value = '';
        clienteBuscarInput.value = '';
        hide(clienteNombreEl);
        checkAdvertencias.checked = false;
        precioCotizadoRadio.checked = true;
        resetBotonConfirmar();
    }

    function actualizarBotonConfirmar() {
        if (!previewData || !previewData.convertible) {
            btnConfirmar.disabled = true;
            return;
        }
        const clienteOk = !previewData.clienteFaltante || clienteIdInput.value !== '';
        const advertenciasOk = previewData.advertencias.length === 0 || checkAdvertencias.checked;
        btnConfirmar.disabled = !(clienteOk && advertenciasOk);
    }

    async function cargarPreview() {
        abrirModal();
        show(loading);
        hide(contenido);
        hide(footer);
        hide(errorGeneralPanel);

        try {
            const resp = await fetch(urls.preview, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });

            if (!resp.ok) {
                throw new Error('HTTP ' + resp.status);
            }

            const data = await resp.json();
            previewData = data;
            renderPreview(data);
        } catch (_) {
            hide(loading);
            show(errorGeneralPanel);
            errorGeneralTexto.textContent = 'No se pudo obtener el preview de conversion. Intente nuevamente.';
        }
    }

    function renderTablaDetalles(detalles) {
        if (!detallesPreviewPanel || !detalles || detalles.length === 0) return;

        clearChildren(detallesPreviewPanel);

        const titulo = document.createElement('p');
        titulo.className = 'text-xs font-bold uppercase tracking-wider text-slate-500';
        titulo.textContent = 'Detalle de productos';
        detallesPreviewPanel.appendChild(titulo);

        const hayCambios = detalles.some(function (d) { return d.precioCambio; });

        if (!hayCambios) {
            const msg = document.createElement('p');
            msg.className = 'text-sm text-slate-400';
            msg.textContent = 'No se detectaron cambios de precio.';
            detallesPreviewPanel.appendChild(msg);
            show(detallesPreviewPanel);
            return;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'overflow-x-auto';

        const table = document.createElement('table');
        table.className = 'w-full text-sm border-collapse';

        const thead = document.createElement('thead');
        const headerRow = document.createElement('tr');
        ['Producto', 'Cant.', 'Precio cotizado', 'Precio actual', 'Diferencia', 'Total dif.', 'Estado'].forEach(function (col) {
            const th = document.createElement('th');
            th.className = 'pb-2 pr-3 text-left text-xs font-bold uppercase tracking-wider text-slate-500 whitespace-nowrap';
            th.textContent = col;
            headerRow.appendChild(th);
        });
        thead.appendChild(headerRow);
        table.appendChild(thead);

        const tbody = document.createElement('tbody');
        detalles.forEach(function (d) {
            const tr = document.createElement('tr');
            tr.className = 'border-t border-slate-700/50';

            const tdNombre = document.createElement('td');
            tdNombre.className = 'py-2 pr-3 font-medium ' + (d.precioCambio ? 'text-amber-200' : 'text-slate-300');
            tdNombre.textContent = d.nombreProducto;
            tr.appendChild(tdNombre);

            const tdCant = document.createElement('td');
            tdCant.className = 'py-2 pr-3 tabular-nums text-slate-300';
            tdCant.textContent = d.cantidad;
            tr.appendChild(tdCant);

            const tdCotiz = document.createElement('td');
            tdCotiz.className = 'py-2 pr-3 tabular-nums text-slate-300';
            tdCotiz.textContent = formatCurrency(d.precioCotizado);
            tr.appendChild(tdCotiz);

            const tdActual = document.createElement('td');
            tdActual.className = 'py-2 pr-3 tabular-nums text-slate-300';
            tdActual.textContent = d.precioActual != null ? formatCurrency(d.precioActual) : 'No disponible';
            tr.appendChild(tdActual);

            const tdDifUnit = document.createElement('td');
            tdDifUnit.className = 'py-2 pr-3 tabular-nums';
            if (d.diferenciaUnitaria != null) {
                tdDifUnit.textContent = formatCurrency(d.diferenciaUnitaria);
                tdDifUnit.classList.add(d.diferenciaUnitaria > 0 ? 'text-red-400' : d.diferenciaUnitaria < 0 ? 'text-emerald-400' : 'text-slate-400');
            } else {
                tdDifUnit.textContent = '—';
                tdDifUnit.classList.add('text-slate-500');
            }
            tr.appendChild(tdDifUnit);

            const tdDifTotal = document.createElement('td');
            tdDifTotal.className = 'py-2 pr-3 tabular-nums';
            if (d.diferenciaTotal != null) {
                tdDifTotal.textContent = formatCurrency(d.diferenciaTotal);
                tdDifTotal.classList.add(d.diferenciaTotal > 0 ? 'text-red-400' : d.diferenciaTotal < 0 ? 'text-emerald-400' : 'text-slate-400');
            } else {
                tdDifTotal.textContent = '—';
                tdDifTotal.classList.add('text-slate-500');
            }
            tr.appendChild(tdDifTotal);

            const estados = [];
            if (!d.productoActivo) estados.push('Inactivo');
            if (d.precioCambio) estados.push('Precio cambió');
            if (d.requiereUnidadFisica) estados.push('Req. unidad');
            const tdEstado = document.createElement('td');
            tdEstado.className = 'py-2 text-xs ' + (estados.length > 0 ? 'text-amber-400' : 'text-slate-500');
            tdEstado.textContent = estados.length > 0 ? estados.join(', ') : '—';
            tr.appendChild(tdEstado);

            tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        wrapper.appendChild(table);
        detallesPreviewPanel.appendChild(wrapper);
        show(detallesPreviewPanel);
    }

    function appendItems(lista, items) {
        clearChildren(lista);
        items.forEach(function (texto) {
            const li = document.createElement('li');
            li.textContent = texto;
            lista.appendChild(li);
        });
    }

    function renderPreview(data) {
        hide(loading);
        show(contenido);
        show(footer);

        if (data.errores && data.errores.length > 0) {
            appendItems(erroresLista, data.errores);
            show(erroresPanel);
        }

        if (data.advertencias && data.advertencias.length > 0) {
            appendItems(advertenciasLista, data.advertencias);
            show(advertenciasPanel);
            show(confirmarAdvertenciasPanel);
        }

        show(resumen);
        totalEl.textContent = formatCurrency(data.totalCotizado);
        renderTablaDetalles(data.detalles);

        if (data.clienteFaltante) {
            show(clienteOverridePanel);
        }

        actualizarBotonConfirmar();
    }

    async function confirmarConversion() {
        if (btnConfirmar.disabled) return;

        btnConfirmar.disabled = true;
        clearChildren(btnConfirmar);
        btnConfirmar.textContent = 'Convirtiendo...';
        hide(errorGeneralPanel);
        hide(erroresPanel);

        const clienteOverride = clienteIdInput.value ? parseInt(clienteIdInput.value, 10) : null;
        const request = {
            usarPrecioCotizado: precioCotizadoRadio.checked,
            confirmarAdvertencias: checkAdvertencias.checked,
            clienteIdOverride: clienteOverride,
            observacionesAdicionales: null
        };

        let redirigiendo = false;
        try {
            const resp = await fetch(urls.convertir, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(request)
            });

            const data = await resp.json();

            if (resp.ok && data.exitoso) {
                redirigiendo = true;
                window.location.href = urls.ventaEdit + data.ventaId;
                return;
            }

            const errores = data.errores && data.errores.length > 0
                ? data.errores
                : [data.error || 'Error desconocido al convertir.'];

            appendItems(erroresLista, errores);
            show(erroresPanel);
        } catch (_) {
            show(errorGeneralPanel);
            errorGeneralTexto.textContent = 'Error al conectar con el servidor. Intente nuevamente.';
        } finally {
            if (!redirigiendo) {
                resetBotonConfirmar();
                actualizarBotonConfirmar();
            }
        }
    }

    // Busqueda de clientes para ClienteIdOverride
    clienteBuscarInput.addEventListener('input', function () {
        clearTimeout(clienteSearchTimer);
        const term = this.value.trim();
        if (term.length < 2) {
            hide(clienteDropdown);
            return;
        }
        clienteSearchTimer = setTimeout(function () {
            buscarClientes(term);
        }, 280);
    });

    async function buscarClientes(term) {
        if (clienteSearchController) clienteSearchController.abort();
        clienteSearchController = new AbortController();

        try {
            const resp = await fetch(
                urls.clientes + '?term=' + encodeURIComponent(term) + '&take=10',
                { signal: clienteSearchController.signal }
            );
            const items = await resp.json();
            renderClientesDropdown(items);
        } catch (err) {
            if (err.name !== 'AbortError') hide(clienteDropdown);
        }
    }

    function renderClientesDropdown(items) {
        clearChildren(clienteDropdown);
        if (!items || items.length === 0) {
            hide(clienteDropdown);
            return;
        }
        items.forEach(function (c) {
            const li = document.createElement('li');
            li.className = 'cursor-pointer px-3 py-2 text-sm text-white hover:bg-slate-700';
            li.textContent = c.display || (c.nombre + ' ' + (c.apellido || '')).trim();
            li.addEventListener('click', function () { seleccionarCliente(c); });
            clienteDropdown.appendChild(li);
        });
        show(clienteDropdown);
    }

    function seleccionarCliente(c) {
        const nombre = c.display || (c.nombre + ' ' + (c.apellido || '')).trim();
        clienteIdInput.value = c.id;
        clienteBuscarInput.value = nombre;
        clienteNombreEl.textContent = 'Cliente seleccionado: ' + nombre;
        show(clienteNombreEl);
        hide(clienteDropdown);
        actualizarBotonConfirmar();
    }

    document.addEventListener('click', function (e) {
        if (!clienteBuscarInput.contains(e.target) && !clienteDropdown.contains(e.target)) {
            hide(clienteDropdown);
        }
    });

    document.getElementById('cotizacion-btn-convertir').addEventListener('click', cargarPreview);
    document.getElementById('cotizacion-modal-cerrar').addEventListener('click', cerrarModal);
    document.getElementById('cotizacion-modal-cancelar').addEventListener('click', cerrarModal);
    btnConfirmar.addEventListener('click', confirmarConversion);
    checkAdvertencias.addEventListener('change', actualizarBotonConfirmar);
    precioCotizadoRadio.addEventListener('change', actualizarBotonConfirmar);
    precioActualRadio.addEventListener('change', actualizarBotonConfirmar);

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !modal.classList.contains('hidden')) cerrarModal();
    });
})();

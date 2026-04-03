/**
 * ordencompra-form.js - Create / Edit Orden de Compra
 *
 * - Product search dropdown from embedded JSON data
 * - Add / remove product rows
 * - Live totals calculation (subtotal, descuento, iva, total)
 * - Generates hidden inputs for model binding: Detalles[i].PropertyName
 */
(() => {
    const form = document.getElementById('form-orden');
    if (!form) return;

    const tbody = document.getElementById('tabla-productos');
    const inpBuscar = document.getElementById('inp-buscar-producto');
    const dropdown = document.getElementById('dropdown-productos');
    const inpCantidad = document.getElementById('inp-cantidad');
    const inpPrecio = document.getElementById('inp-precio');
    const btnAgregar = document.getElementById('btn-agregar');
    const inpDescuento = document.getElementById('inp-descuento');

    const lblSubtotal = document.getElementById('lbl-subtotal');
    const lblDescuento = document.getElementById('lbl-descuento');
    const lblIva = document.getElementById('lbl-iva');
    const lblTotal = document.getElementById('lbl-total');

    const hdnSubtotal = document.getElementById('hdn-subtotal');
    const hdnDescuento = document.getElementById('hdn-descuento');
    const hdnIva = document.getElementById('hdn-iva');
    const hdnTotal = document.getElementById('hdn-total');

    const feedback = document.getElementById('ordencompra-feedback');
    const feedbackTitle = document.querySelector('[data-feedback-title]');
    const feedbackMessage = document.querySelector('[data-feedback-message]');
    const feedbackIcon = document.querySelector('[data-feedback-icon]');
    const feedbackClose = document.querySelector('[data-feedback-close]');
    const scrollAffordance = (window.TheBury && typeof window.TheBury.initHorizontalScrollAffordance === 'function')
        ? window.TheBury.initHorizontalScrollAffordance(document.querySelector('[data-oc-scroll]'))
        : null;

    const normalizeText = (window.TheBury && typeof window.TheBury.normalizeText === 'function')
        ? window.TheBury.normalizeText
        : (value) => (value || '').toString().toLowerCase();

    const productos = readJsonData('ordencompra-productos-data', window.__productosDisponibles || [])
        .map(mapProducto)
        .filter(p => p.id > 0);

    let filas = readJsonData('ordencompra-detalles-data', [])
        .map(mapDetalle)
        .filter(f => f.productoId > 0 && f.cantidad > 0);

    let productoSeleccionado = null;
    let feedbackTimer = null;

    function readJsonData(id, fallback) {
        const node = document.getElementById(id);
        if (!node || !node.textContent) return fallback;

        try {
            return JSON.parse(node.textContent);
        } catch (error) {
            console.warn(`No se pudo parsear ${id}`, error);
            return fallback;
        }
    }

    function mapProducto(producto) {
        return {
            id: parseInt(producto?.id ?? producto?.Id ?? 0, 10) || 0,
            nombre: `${producto?.nombre ?? producto?.Nombre ?? ''}`,
            codigo: `${producto?.codigo ?? producto?.Codigo ?? ''}`,
            precioCompra: parseFloat(producto?.precioCompra ?? producto?.PrecioCompra ?? 0) || 0
        };
    }

    function mapDetalle(detalle) {
        const cantidad = parseInt(detalle?.cantidad ?? detalle?.Cantidad ?? 0, 10) || 0;
        const precio = parseFloat(detalle?.precioUnitario ?? detalle?.PrecioUnitario ?? 0) || 0;
        const subtotal = parseFloat(detalle?.subtotal ?? detalle?.Subtotal ?? 0) || (cantidad * precio);

        return {
            productoId: parseInt(detalle?.productoId ?? detalle?.ProductoId ?? 0, 10) || 0,
            nombre: `${detalle?.productoNombre ?? detalle?.ProductoNombre ?? ''}`,
            codigo: `${detalle?.productoCodigo ?? detalle?.ProductoCodigo ?? ''}`,
            cantidad,
            precio,
            subtotal
        };
    }

    function esc(value) {
        const div = document.createElement('div');
        div.textContent = value || '';
        return div.innerHTML;
    }

    function num(value) {
        return parseFloat(value || 0).toFixed(2);
    }

    function numForPost(value) {
        return num(value).replace('.', ',');
    }

    function setDropdownVisible(visible) {
        if (!dropdown) return;
        dropdown.classList.toggle('hidden', !visible);
        if (inpBuscar) {
            inpBuscar.setAttribute('aria-expanded', visible ? 'true' : 'false');
        }
    }

    function renderDropdown(results) {
        if (!dropdown) return;

        if (!results.length) {
            dropdown.innerHTML = '';
            setDropdownVisible(false);
            return;
        }

        dropdown.innerHTML = results.map((producto) => `
            <button type="button"
                    role="option"
                    class="w-full px-4 py-3 text-left hover:bg-slate-100 dark:hover:bg-slate-700 flex justify-between items-center gap-4 transition-colors"
                    data-id="${producto.id}"
                    data-nombre="${esc(producto.nombre)}"
                    data-codigo="${esc(producto.codigo)}"
                    data-precio="${producto.precioCompra}">
                <span class="min-w-0">
                    <span class="block text-sm font-bold text-slate-900 dark:text-white truncate">${esc(producto.nombre)}</span>
                    <span class="block text-xs text-slate-500 truncate">${esc(producto.codigo)}</span>
                </span>
                <span class="text-xs font-medium text-primary shrink-0">$${num(producto.precioCompra)}</span>
            </button>
        `).join('');

        setDropdownVisible(true);
    }

    function syncTableOverflow() {
        scrollAffordance?.update();
    }

    function hideFeedback() {
        if (!feedback) return;
        feedback.hidden = true;
        feedback.dataset.variant = '';

        if (feedbackTimer) {
            clearTimeout(feedbackTimer);
            feedbackTimer = null;
        }
    }

    function showFeedback(message, options) {
        if (!feedback || !feedbackTitle || !feedbackMessage || !feedbackIcon) return;

        const variant = options?.variant || 'warning';
        const title = options?.title || 'Revisá este paso';
        const icons = {
            error: 'error',
            warning: 'warning',
            success: 'check_circle'
        };

        if (feedbackTimer) {
            clearTimeout(feedbackTimer);
        }

        feedback.dataset.variant = variant;
        feedbackTitle.textContent = title;
        feedbackMessage.textContent = message;
        feedbackIcon.textContent = icons[variant] || 'info';
        feedback.hidden = false;

        const rect = feedback.getBoundingClientRect();
        const outOfView = rect.top < 0 || rect.bottom > window.innerHeight;
        if (outOfView) {
            feedback.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }

        if (variant === 'success') {
            feedbackTimer = window.setTimeout(hideFeedback, 3200);
        } else {
            feedbackTimer = window.setTimeout(hideFeedback, 4500);
        }
    }

    function seleccionarProductoDesdeElemento(item) {
        productoSeleccionado = {
            id: parseInt(item.dataset.id, 10),
            nombre: item.dataset.nombre || '',
            codigo: item.dataset.codigo || '',
            precio: parseFloat(item.dataset.precio) || 0
        };

        if (inpBuscar) inpBuscar.value = productoSeleccionado.nombre;
        if (inpPrecio) inpPrecio.value = productoSeleccionado.precio.toFixed(2);

        hideFeedback();
        setDropdownVisible(false);
    }

    function agregarProducto() {
        if (!productoSeleccionado) {
            showFeedback('Seleccioná un producto desde el buscador antes de agregarlo.', {
                variant: 'warning',
                title: 'Falta elegir un producto'
            });
            inpBuscar?.focus();
            return;
        }

        const cantidad = parseInt(inpCantidad?.value || '0', 10) || 0;
        const precio = parseFloat(inpPrecio?.value || '0') || 0;

        if (cantidad <= 0) {
            showFeedback('La cantidad debe ser mayor a 0.', {
                variant: 'error',
                title: 'Cantidad inválida'
            });
            inpCantidad?.focus();
            return;
        }

        if (precio <= 0) {
            showFeedback('El precio debe ser mayor a 0.', {
                variant: 'error',
                title: 'Precio inválido'
            });
            inpPrecio?.focus();
            return;
        }

        const existente = filas.find(fila => fila.productoId === productoSeleccionado.id);

        if (existente) {
            existente.cantidad += cantidad;
            existente.precio = precio;
            existente.subtotal = existente.cantidad * existente.precio;

            showFeedback(`Se actualizó ${productoSeleccionado.nombre} en la orden existente.`, {
                variant: 'success',
                title: 'Producto actualizado'
            });
        } else {
            filas.push({
                productoId: productoSeleccionado.id,
                nombre: productoSeleccionado.nombre,
                codigo: productoSeleccionado.codigo,
                cantidad,
                precio,
                subtotal: cantidad * precio
            });

            hideFeedback();
        }

        productoSeleccionado = null;

        if (inpBuscar) {
            inpBuscar.value = '';
            inpBuscar.focus();
        }

        if (inpCantidad) inpCantidad.value = '1';
        if (inpPrecio) inpPrecio.value = '';

        renderTabla();
        calcularTotales();
    }

    function crearEmptyState() {
        const tr = document.createElement('tr');
        tr.id = 'empty-state';
        tr.innerHTML = `
            <td colspan="5" class="py-12 text-center">
                <div class="flex flex-col items-center gap-2 opacity-40">
                    <span class="material-symbols-outlined text-4xl">inventory</span>
                    <p class="text-slate-500 dark:text-slate-400 text-base">No hay productos agregados</p>
                </div>
            </td>
        `;
        return tr;
    }

    function renderTabla() {
        if (!tbody) return;

        tbody.innerHTML = '';

        if (!filas.length) {
            tbody.appendChild(crearEmptyState());
            requestAnimationFrame(syncTableOverflow);
            return;
        }

        filas.forEach((fila, index) => {
            const tr = document.createElement('tr');
            tr.className = 'hover:bg-slate-50/50 dark:hover:bg-slate-800/30 transition-colors';
            tr.innerHTML = `
                <td class="py-4 px-2">
                    <div class="flex flex-col gap-1">
                        <span class="text-sm font-bold text-slate-900 dark:text-white">${esc(fila.nombre)}</span>
                        <span class="text-xs text-slate-500">${esc(fila.codigo)}</span>
                    </div>
                    <input type="hidden" name="Detalles[${index}].ProductoId" value="${fila.productoId}" />
                    <input type="hidden" name="Detalles[${index}].ProductoNombre" value="${esc(fila.nombre)}" />
                    <input type="hidden" name="Detalles[${index}].ProductoCodigo" value="${esc(fila.codigo)}" />
                    <input type="hidden" name="Detalles[${index}].Cantidad" value="${fila.cantidad}" />
                    <input type="hidden" name="Detalles[${index}].PrecioUnitario" value="${numForPost(fila.precio)}" />
                    <input type="hidden" name="Detalles[${index}].Subtotal" value="${numForPost(fila.subtotal)}" />
                </td>
                <td class="py-4 px-2 text-center font-medium text-slate-900 dark:text-white">${fila.cantidad}</td>
                <td class="py-4 px-2 text-right font-medium text-slate-700 dark:text-slate-300">$${num(fila.precio)}</td>
                <td class="py-4 px-2 text-right font-bold text-slate-900 dark:text-white">$${num(fila.subtotal)}</td>
                <td class="py-4 px-2 text-center">
                    <button type="button"
                            data-remove="${index}"
                            aria-label="Quitar ${esc(fila.nombre)}"
                            class="inline-flex items-center gap-1 rounded-lg px-2 py-2 text-red-500 hover:bg-red-100 dark:hover:bg-red-900/30 transition-colors">
                        <span class="material-symbols-outlined text-[20px]">delete</span>
                        <span class="text-xs font-semibold">Quitar</span>
                    </button>
                </td>
            `;

            tbody.appendChild(tr);
        });

        requestAnimationFrame(syncTableOverflow);
    }

    function calcularTotales() {
        const subtotal = filas.reduce((sum, fila) => sum + fila.subtotal, 0);
        const descuento = Math.max(0, parseFloat(inpDescuento?.value || '0') || 0);
        const base = Math.max(0, subtotal - descuento);
        const iva = base * 0.21;
        const total = base + iva;

        if (lblSubtotal) lblSubtotal.textContent = `$${num(subtotal)}`;
        if (lblDescuento) lblDescuento.textContent = `-$${num(descuento)}`;
        if (lblIva) lblIva.textContent = `$${num(iva)}`;
        if (lblTotal) lblTotal.textContent = `$${num(total)}`;

        if (hdnSubtotal) hdnSubtotal.value = numForPost(subtotal);
        if (hdnDescuento) hdnDescuento.value = numForPost(descuento);
        if (hdnIva) hdnIva.value = numForPost(iva);
        if (hdnTotal) hdnTotal.value = numForPost(total);
    }

    document.querySelectorAll('[data-dismiss-target]').forEach((button) => {
        button.addEventListener('click', () => {
            const targetId = button.getAttribute('data-dismiss-target');
            if (!targetId) return;
            document.getElementById(targetId)?.remove();
        });
    });

    feedbackClose?.addEventListener('click', hideFeedback);

    if (inpBuscar) {
        inpBuscar.addEventListener('input', () => {
            hideFeedback();

            const query = normalizeText(inpBuscar.value.trim());
            if (!query.length) {
                productoSeleccionado = null;
                setDropdownVisible(false);
                return;
            }

            const results = productos
                .filter((producto) => normalizeText(producto.nombre).includes(query) || normalizeText(producto.codigo).includes(query))
                .slice(0, 8);

            renderDropdown(results);
        });

        inpBuscar.addEventListener('keydown', (event) => {
            if (event.key === 'Escape') {
                setDropdownVisible(false);
            }

            if (event.key === 'Enter') {
                const firstResult = dropdown?.querySelector('[data-id]');
                if (!firstResult) return;
                event.preventDefault();
                seleccionarProductoDesdeElemento(firstResult);
            }
        });
    }

    dropdown?.addEventListener('click', (event) => {
        const item = event.target.closest('[data-id]');
        if (!item) return;
        seleccionarProductoDesdeElemento(item);
    });

    document.addEventListener('click', (event) => {
        if (!event.target.closest('#inp-buscar-producto') && !event.target.closest('#dropdown-productos')) {
            setDropdownVisible(false);
        }
    });

    btnAgregar?.addEventListener('click', agregarProducto);

    [inpCantidad, inpPrecio].forEach((input) => {
        input?.addEventListener('keydown', (event) => {
            if (event.key !== 'Enter') return;
            event.preventDefault();
            agregarProducto();
        });
    });

    tbody?.addEventListener('click', (event) => {
        const button = event.target.closest('[data-remove]');
        if (!button) return;

        const index = parseInt(button.dataset.remove, 10);
        if (Number.isNaN(index)) return;

        const removed = filas[index];
        filas.splice(index, 1);
        renderTabla();
        calcularTotales();

        if (removed) {
            showFeedback(`${removed.nombre} se quitó de la orden.`, {
                variant: 'warning',
                title: 'Producto eliminado'
            });
        }
    });

    inpDescuento?.addEventListener('input', calcularTotales);
    renderTabla();
    calcularTotales();
    requestAnimationFrame(syncTableOverflow);
})();

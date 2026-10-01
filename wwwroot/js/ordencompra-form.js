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
    const selectProveedor = document.getElementById('ProveedorId');

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
    const feedbackLink = document.querySelector('[data-feedback-link]');
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

    const proveedores = readJsonData('ordencompra-proveedores-data', [])
        .map(mapProveedor)
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
            precioCompra: parseFloat(producto?.precioCompra ?? producto?.PrecioCompra ?? 0) || 0,
            porcentajeIva: parseFloat(producto?.porcentajeIva ?? producto?.PorcentajeIva ?? 0) || 0
        };
    }

    function porcentajeIvaDeProducto(productoId) {
        const producto = productos.find(p => p.id === productoId);
        return producto ? producto.porcentajeIva : 0;
    }

    function mapProveedor(proveedor) {
        const productoIds = proveedor?.productoIds ?? proveedor?.ProductoIds ?? [];

        return {
            id: parseInt(proveedor?.id ?? proveedor?.Id ?? 0, 10) || 0,
            nombre: `${proveedor?.nombre ?? proveedor?.Nombre ?? ''}`,
            productoIds: Array.isArray(productoIds)
                ? productoIds.map(id => parseInt(id, 10) || 0).filter(id => id > 0)
                : []
        };
    }

    function mapDetalle(detalle) {
        const cantidad = parseInt(detalle?.cantidad ?? detalle?.Cantidad ?? 0, 10) || 0;
        const precio = parseFloat(detalle?.precioUnitario ?? detalle?.PrecioUnitario ?? 0) || 0;
        const subtotal = parseFloat(detalle?.subtotal ?? detalle?.Subtotal ?? 0) || (cantidad * precio);
        const productoId = parseInt(detalle?.productoId ?? detalle?.ProductoId ?? 0, 10) || 0;

        return {
            id: parseInt(detalle?.id ?? detalle?.Id ?? 0, 10) || 0,
            productoId,
            nombre: `${detalle?.productoNombre ?? detalle?.ProductoNombre ?? ''}`,
            codigo: `${detalle?.productoCodigo ?? detalle?.ProductoCodigo ?? ''}`,
            cantidad,
            precio,
            subtotal,
            porcentajeIva: porcentajeIvaDeProducto(productoId)
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

    // Importes para mostrar (miles con punto, decimales con coma); los hidden siguen yendo con numForPost.
    function fmt(value) {
        return (parseFloat(value) || 0).toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function numForPost(value) {
        return num(value).replace('.', ',');
    }

    function getProveedorIdSeleccionado() {
        return parseInt(selectProveedor?.value || '0', 10) || 0;
    }

    function getProveedorSeleccionado() {
        const proveedorId = getProveedorIdSeleccionado();
        return proveedores.find(proveedor => proveedor.id === proveedorId) || null;
    }

    function proveedorPermiteProducto(productoId) {
        const proveedor = getProveedorSeleccionado();
        if (!proveedor || !proveedor.productoIds.length) return true;

        return proveedor.productoIds.includes(productoId);
    }

    function getProductosDisponibles() {
        const proveedor = getProveedorSeleccionado();
        if (!proveedor || !proveedor.productoIds.length) return productos;

        const productoIdsPermitidos = new Set(proveedor.productoIds);
        return productos.filter(producto => productoIdsPermitidos.has(producto.id));
    }

    function renderProveedorOptions(proveedoresPermitidos, selectedValue) {
        if (!selectProveedor || !proveedores.length) return;

        const selected = selectedValue != null ? `${selectedValue || ''}` : selectProveedor.value;
        const permitidos = proveedoresPermitidos instanceof Set ? proveedoresPermitidos : null;
        const options = ['<option value="">Seleccione un proveedor</option>'];

        proveedores.forEach((proveedor) => {
            if (permitidos && !permitidos.has(proveedor.id)) return;
            options.push(`<option value="${proveedor.id}">${esc(proveedor.nombre)}</option>`);
        });

        selectProveedor.innerHTML = options.join('');

        if (selected && Array.from(selectProveedor.options).some(option => option.value === selected)) {
            selectProveedor.value = selected;
        } else {
            selectProveedor.value = '';
        }
    }

    function getProductoIdsParaFiltrarProveedores() {
        if (productoSeleccionado?.id > 0) return [productoSeleccionado.id];

        return Array.from(new Set(
            filas
                .map(fila => fila.productoId)
                .filter(productoId => productoId > 0)
        ));
    }

    let sinProveedorParaProducto = false;

    function actualizarProveedoresPorProductoContexto() {
        sinProveedorParaProducto = false;
        btnAgregar?.removeAttribute('aria-disabled');
        if (!selectProveedor || !proveedores.length) return;

        const proveedorId = getProveedorIdSeleccionado();
        if (proveedorId > 0) {
            renderProveedorOptions(null, proveedorId);
            return;
        }

        const productoIds = getProductoIdsParaFiltrarProveedores();
        if (!productoIds.length) {
            renderProveedorOptions(null, '');
            return;
        }

        const proveedoresPermitidos = new Set(
            proveedores
                .filter(proveedor => productoIds.every(productoId => proveedor.productoIds.includes(productoId)))
                .map(proveedor => proveedor.id)
        );

        renderProveedorOptions(proveedoresPermitidos, '');
        btnAgregar?.removeAttribute('aria-disabled');

        if (!proveedoresPermitidos.size) {
            sinProveedorParaProducto = true;
            if (btnAgregar) btnAgregar.setAttribute('aria-disabled', 'true');
            showFeedback('Ningún proveedor tiene este producto asociado. Asocialo desde Proveedores → Editar, o elegí otro producto.', {
                variant: 'warning',
                title: 'Sin proveedores asociados',
                sticky: true,
                href: '/Proveedor',
                hrefLabel: 'Ir a Proveedores'
            });
        }
    }

    function limpiarProductoSeleccionado() {
        productoSeleccionado = null;
        if (inpBuscar) inpBuscar.value = '';
        if (inpPrecio) inpPrecio.value = '';
        setDropdownVisible(false);
    }

    function getProductosNoPermitidosPorProveedor() {
        const proveedor = getProveedorSeleccionado();
        if (!proveedor || !proveedor.productoIds.length) return [];

        return filas.filter(fila => !proveedor.productoIds.includes(fila.productoId));
    }

    function setDropdownVisible(visible) {
        if (!dropdown) return;
        dropdown.classList.toggle('hidden', !visible);
        if (inpBuscar) {
            inpBuscar.setAttribute('aria-expanded', visible ? 'true' : 'false');
        }
    }

    function renderDropdown(results, noAsociados) {
        if (!dropdown) return;

        const sinAsociar = noAsociados || [];

        if (!results.length && !sinAsociar.length) {
            dropdown.innerHTML = '';
            setDropdownVisible(false);
            return;
        }

        const items = results.map((producto) => `
            <button type="button"
                    role="option"
                    class="w-full px-4 py-3 text-left hover:bg-slate-100 dark:hover:bg-slate-700 flex justify-between items-center gap-4 transition-colors"
                    data-id="${producto.id}"
                    data-nombre="${esc(producto.nombre)}"
                    data-codigo="${esc(producto.codigo)}"
                    data-precio="${producto.precioCompra}"
                    data-porcentaje-iva="${producto.porcentajeIva}">
                <span class="min-w-0">
                    <span class="block text-sm font-bold text-slate-900 dark:text-white break-words" title="${esc(producto.nombre)}">${esc(producto.nombre)}</span>
                    <span class="block text-xs text-slate-500 truncate">${esc(producto.codigo)}</span>
                </span>
                <span class="text-xs font-medium text-primary shrink-0">$ ${fmt(producto.precioCompra)}</span>
            </button>
        `).join('');

        let aviso = '';
        if (sinAsociar.length) {
            const proveedor = getProveedorSeleccionado();
            const nombreProveedor = proveedor ? esc(proveedor.nombre) : 'el proveedor elegido';
            const lista = sinAsociar.slice(0, 3).map(p => esc(`${p.nombre} (${p.codigo})`)).join(', ');
            const resto = sinAsociar.length > 3 ? ` y ${sinAsociar.length - 3} más` : '';
            aviso = `
            <div class="px-4 py-3 text-xs text-amber-300 bg-amber-500/10 border-t border-slate-700" role="note">
                <p class="font-semibold">${results.length ? 'Otros productos coinciden' : 'Hay productos que coinciden'}, pero no están asociados a ${nombreProveedor}:</p>
                <p class="mt-1 text-amber-200/80">${lista}${resto}.</p>
                <a href="/Proveedor/Details/${getProveedorIdSeleccionado()}" class="mt-2 inline-flex items-center gap-1 font-semibold text-primary no-underline hover:underline">
                    Asociarlos desde el proveedor
                    <span class="material-symbols-outlined text-[14px]" aria-hidden="true">open_in_new</span>
                </a>
            </div>`;
        }

        dropdown.innerHTML = items + aviso;

        setDropdownVisible(true);

        // En pantallas chicas el buscador queda al borde: el aviso tiene que verse sin scrollear a mano.
        if (aviso) dropdown.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }

    function syncTableOverflow() {
        scrollAffordance?.update();
    }

    function hideFeedback() {
        if (!feedback) return;
        feedback.hidden = true;
        feedback.dataset.variant = '';
        if (feedbackLink) feedbackLink.hidden = true;

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
        if (feedbackLink) {
            // Acción opcional del aviso: lleva a resolver el bloqueo en vez de solo describirlo.
            feedbackLink.hidden = !options?.href;
            if (options?.href) {
                feedbackLink.href = options.href;
                feedbackLink.textContent = options.hrefLabel || 'Ver más';
            }
        }
        feedback.hidden = false;

        const rect = feedback.getBoundingClientRect();
        const outOfView = rect.top < 0 || rect.bottom > window.innerHeight;
        if (outOfView) {
            feedback.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }

        // Los avisos "sticky" explican un bloqueo que no se resuelve solo: se quedan hasta
        // que cambia el contexto (hideFeedback) o el usuario los cierra.
        if (options?.sticky) return;

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
            precio: parseFloat(item.dataset.precio) || 0,
            porcentajeIva: parseFloat(item.dataset.porcentajeIva) || 0
        };

        if (inpBuscar) inpBuscar.value = productoSeleccionado.nombre;
        if (inpPrecio) inpPrecio.value = productoSeleccionado.precio.toFixed(2);

        hideFeedback();
        setDropdownVisible(false);
        actualizarProveedoresPorProductoContexto();

        // Teclado: elegir el producto lleva directo a Cantidad.
        if (inpCantidad) {
            inpCantidad.focus();
            inpCantidad.select();
        }
    }

    function agregarProducto() {
        if (!productoSeleccionado) {
            const escrito = (inpBuscar?.value || '').trim();
            showFeedback(escrito
                ? `"${escrito}" todavía no es un producto elegido. Seleccionalo de la lista que aparece al escribir; si no aparece ninguno, no está asociado al proveedor.`
                : 'Seleccioná un producto desde el buscador antes de agregarlo.', {
                variant: 'warning',
                title: 'Falta elegir un producto'
            });
            inpBuscar?.focus();
            return;
        }

        if (sinProveedorParaProducto) {
            inpBuscar?.focus();
            return;
        }

        if (!proveedorPermiteProducto(productoSeleccionado.id)) {
            showFeedback('El producto seleccionado no esta asociado al proveedor elegido.', {
                variant: 'error',
                title: 'Producto no asociado'
            });
            limpiarProductoSeleccionado();
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
            existente.porcentajeIva = productoSeleccionado.porcentajeIva;

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
                subtotal: cantidad * precio,
                porcentajeIva: productoSeleccionado.porcentajeIva
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
        actualizarProveedoresPorProductoContexto();
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
                    <input type="hidden" name="Detalles[${index}].Id" value="${fila.id || 0}" />
                    <input type="hidden" name="Detalles[${index}].ProductoId" value="${fila.productoId}" />
                    <input type="hidden" name="Detalles[${index}].ProductoNombre" value="${esc(fila.nombre)}" />
                    <input type="hidden" name="Detalles[${index}].ProductoCodigo" value="${esc(fila.codigo)}" />
                    <input type="hidden" name="Detalles[${index}].Cantidad" value="${fila.cantidad}" />
                    <input type="hidden" name="Detalles[${index}].PrecioUnitario" value="${numForPost(fila.precio)}" />
                    <input type="hidden" name="Detalles[${index}].Subtotal" value="${numForPost(fila.subtotal)}" />
                </td>
                <td class="py-4 px-2 text-center font-medium text-slate-900 dark:text-white">${fila.cantidad}</td>
                <td class="py-4 px-2 text-right font-medium text-slate-700 dark:text-slate-300">$ ${fmt(fila.precio)}</td>
                <td class="py-4 px-2 text-right font-bold text-slate-900 dark:text-white">$ ${fmt(fila.subtotal)}</td>
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

    // Los precios unitarios ya incluyen IVA: el total es subtotal - descuento y el IVA
    // solo se desglosa por línea (alícuota del producto), nunca se suma al total.
    // Vista previa: el cálculo autoritativo lo hace el backend al guardar.
    function calcularTotales() {
        const subtotal = filas.reduce((sum, fila) => sum + fila.subtotal, 0);
        const descuento = Math.max(0, parseFloat(inpDescuento?.value || '0') || 0);
        const total = Math.max(0, subtotal - descuento);
        const factorDescuento = subtotal > 0 ? total / subtotal : 0;
        const iva = filas.reduce((sum, fila) => {
            const pct = fila.porcentajeIva || 0;
            if (pct <= 0) return sum;
            const base = fila.subtotal * factorDescuento;
            return sum + (base - base / (1 + pct / 100));
        }, 0);

        if (lblSubtotal) lblSubtotal.textContent = `$ ${fmt(subtotal)}`;
        if (lblDescuento) lblDescuento.textContent = `- $ ${fmt(descuento)}`;
        if (lblIva) lblIva.textContent = `$ ${fmt(iva)}`;
        if (lblTotal) lblTotal.textContent = `$ ${fmt(total)}`;

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

    selectProveedor?.addEventListener('change', () => {
        hideFeedback();

        if (productoSeleccionado && !proveedorPermiteProducto(productoSeleccionado.id)) {
            showFeedback('El producto seleccionado no esta asociado al proveedor elegido.', {
                variant: 'warning',
                title: 'Producto no asociado'
            });
            limpiarProductoSeleccionado();
        }

        actualizarProveedoresPorProductoContexto();
        setDropdownVisible(false);
    });

    if (inpBuscar) {
        inpBuscar.addEventListener('input', () => {
            hideFeedback();
            productoSeleccionado = null;
            actualizarProveedoresPorProductoContexto();

            const query = normalizeText(inpBuscar.value.trim());
            if (!query.length) {
                setDropdownVisible(false);
                return;
            }

            const coincide = (producto) => normalizeText(producto.nombre).includes(query) || normalizeText(producto.codigo).includes(query);
            const results = getProductosDisponibles().filter(coincide).slice(0, 8);
            // Con proveedor elegido, los productos que coinciden pero no están asociados no se
            // ofrecen: se avisa en vez de dejar el buscador vacío sin explicación.
            const noAsociados = getProveedorSeleccionado()
                ? productos.filter(p => coincide(p) && !proveedorPermiteProducto(p.id))
                : [];

            renderDropdown(results, noAsociados);
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
        actualizarProveedoresPorProductoContexto();

        if (removed) {
            showFeedback(`${removed.nombre} se quitó de la orden.`, {
                variant: 'warning',
                title: 'Producto eliminado'
            });
        }
    });

    form.addEventListener('submit', (event) => {
        const noPermitidos = getProductosNoPermitidosPorProveedor();
        if (!noPermitidos.length) return;

        event.preventDefault();
        const nombres = noPermitidos.map(fila => fila.nombre).join(', ');
        showFeedback(`Estos productos no estan asociados al proveedor elegido: ${nombres}.`, {
            variant: 'error',
            title: 'Revisa proveedor y productos'
        });
    });

    inpDescuento?.addEventListener('input', calcularTotales);
    renderTabla();
    calcularTotales();
    actualizarProveedoresPorProductoContexto();
    requestAnimationFrame(syncTableOverflow);
})();

/**
 * ordencompra-form.js  –  Create / Edit Orden de Compra
 *
 * - Product search dropdown from embedded JSON
 * - Add / remove product rows
 * - Live totals calculation (subtotal, descuento, iva, total)
 * - Generates hidden inputs for model binding: Detalles[i].PropertyName
 */
(() => {
    const productos = window.__productosDisponibles || [];
    const tbody = document.getElementById('tabla-productos');
    const emptyState = document.getElementById('empty-state');
    const inpBuscar = document.getElementById('inp-buscar-producto');
    const dropdown = document.getElementById('dropdown-productos');
    const inpCantidad = document.getElementById('inp-cantidad');
    const inpPrecio = document.getElementById('inp-precio');
    const btnAgregar = document.getElementById('btn-agregar');
    const inpDescuento = document.getElementById('inp-descuento');

    // Labels
    const lblSubtotal = document.getElementById('lbl-subtotal');
    const lblDescuento = document.getElementById('lbl-descuento');
    const lblIva = document.getElementById('lbl-iva');
    const lblTotal = document.getElementById('lbl-total');

    // Hidden fields
    const hdnSubtotal = document.getElementById('hdn-subtotal');
    const hdnDescuento = document.getElementById('hdn-descuento');
    const hdnIva = document.getElementById('hdn-iva');
    const hdnTotal = document.getElementById('hdn-total');

    let filas = []; // { productoId, nombre, codigo, cantidad, precio, subtotal }
    let productoSeleccionado = null;

    /* ── Product Search ── */
    function renderDropdown(results) {
        if (!results.length) {
            dropdown.classList.add('hidden');
            return;
        }
        dropdown.innerHTML = results.map(p =>
            `<div class="px-4 py-3 hover:bg-slate-100 dark:hover:bg-slate-700 cursor-pointer flex justify-between items-center"
                  data-id="${p.id}" data-nombre="${esc(p.nombre)}" data-codigo="${esc(p.codigo)}" data-precio="${p.precioCompra}">
                <div>
                    <p class="text-sm font-bold text-slate-900 dark:text-white">${esc(p.nombre)}</p>
                    <p class="text-xs text-slate-500">${esc(p.codigo)}</p>
                </div>
                <span class="text-xs font-medium text-primary">$${num(p.precioCompra)}</span>
            </div>`
        ).join('');
        dropdown.classList.remove('hidden');
    }

    function esc(s) { const d = document.createElement('div'); d.textContent = s || ''; return d.innerHTML; }
    function num(v) { return parseFloat(v || 0).toFixed(2); }

    if (inpBuscar) {
        inpBuscar.addEventListener('input', () => {
            const q = inpBuscar.value.trim().toLowerCase();
            if (q.length < 1) { dropdown.classList.add('hidden'); return; }
            const results = productos.filter(p =>
                p.nombre.toLowerCase().includes(q) || p.codigo.toLowerCase().includes(q)
            ).slice(0, 8);
            renderDropdown(results);
        });

        inpBuscar.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') dropdown.classList.add('hidden');
        });
    }

    if (dropdown) {
        dropdown.addEventListener('click', (e) => {
            const item = e.target.closest('[data-id]');
            if (!item) return;
            productoSeleccionado = {
                id: parseInt(item.dataset.id),
                nombre: item.dataset.nombre,
                codigo: item.dataset.codigo,
                precio: parseFloat(item.dataset.precio)
            };
            inpBuscar.value = productoSeleccionado.nombre;
            inpPrecio.value = productoSeleccionado.precio.toFixed(2);
            dropdown.classList.add('hidden');
        });
    }

    // Close dropdown on outside click
    document.addEventListener('click', (e) => {
        if (!e.target.closest('#inp-buscar-producto') && !e.target.closest('#dropdown-productos')) {
            dropdown.classList.add('hidden');
        }
    });

    /* ── Add Product ── */
    if (btnAgregar) {
        btnAgregar.addEventListener('click', agregarProducto);
    }

    function agregarProducto() {
        if (!productoSeleccionado) {
            alert('Seleccione un producto del buscador.');
            return;
        }
        const cantidad = parseInt(inpCantidad.value) || 0;
        const precio = parseFloat(inpPrecio.value) || 0;
        if (cantidad <= 0) { alert('La cantidad debe ser mayor a 0.'); return; }
        if (precio <= 0) { alert('El precio debe ser mayor a 0.'); return; }

        // Check if product already in list
        const existente = filas.find(f => f.productoId === productoSeleccionado.id);
        if (existente) {
            existente.cantidad += cantidad;
            existente.precio = precio;
            existente.subtotal = existente.cantidad * existente.precio;
        } else {
            filas.push({
                productoId: productoSeleccionado.id,
                nombre: productoSeleccionado.nombre,
                codigo: productoSeleccionado.codigo,
                cantidad: cantidad,
                precio: precio,
                subtotal: cantidad * precio
            });
        }

        // Reset inputs
        productoSeleccionado = null;
        inpBuscar.value = '';
        inpCantidad.value = '1';
        inpPrecio.value = '';
        inpBuscar.focus();

        renderTabla();
        calcularTotales();
    }

    /* ── Render Table ── */
    function renderTabla() {
        // Remove all current rows
        tbody.innerHTML = '';

        if (!filas.length) {
            tbody.appendChild(crearEmptyState());
            return;
        }

        filas.forEach((f, i) => {
            const tr = document.createElement('tr');
            tr.className = 'hover:bg-slate-50/50 dark:hover:bg-slate-800/30 transition-colors';
            tr.innerHTML = `
                <td class="py-4 px-2">
                    <div class="flex flex-col">
                        <span class="text-sm font-bold text-slate-900 dark:text-white">${esc(f.nombre)}</span>
                        <span class="text-xs text-slate-500">${esc(f.codigo)}</span>
                    </div>
                    <input type="hidden" name="Detalles[${i}].ProductoId" value="${f.productoId}" />
                    <input type="hidden" name="Detalles[${i}].ProductoNombre" value="${esc(f.nombre)}" />
                    <input type="hidden" name="Detalles[${i}].ProductoCodigo" value="${esc(f.codigo)}" />
                    <input type="hidden" name="Detalles[${i}].Cantidad" value="${f.cantidad}" />
                    <input type="hidden" name="Detalles[${i}].PrecioUnitario" value="${f.precio}" />
                    <input type="hidden" name="Detalles[${i}].Subtotal" value="${f.subtotal}" />
                </td>
                <td class="py-4 px-2 text-center font-medium text-slate-900 dark:text-white">${f.cantidad}</td>
                <td class="py-4 px-2 text-right font-medium text-slate-700 dark:text-slate-300">$${num(f.precio)}</td>
                <td class="py-4 px-2 text-right font-bold text-slate-900 dark:text-white">$${num(f.subtotal)}</td>
                <td class="py-4 px-2 text-center">
                    <button type="button" data-remove="${i}"
                            class="p-1 rounded hover:bg-red-100 dark:hover:bg-red-900/30 text-red-500 transition-colors">
                        <span class="material-symbols-outlined text-[20px]">delete</span>
                    </button>
                </td>
            `;
            tbody.appendChild(tr);
        });
    }

    function crearEmptyState() {
        const tr = document.createElement('tr');
        tr.id = 'empty-state';
        tr.innerHTML = `<td colspan="5" class="py-12 text-center">
            <div class="flex flex-col items-center gap-2 opacity-40">
                <span class="material-symbols-outlined text-4xl">inventory</span>
                <p class="text-slate-500 dark:text-slate-400 text-base">No hay productos agregados</p>
            </div>
        </td>`;
        return tr;
    }

    /* ── Remove Product ── */
    tbody.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-remove]');
        if (!btn) return;
        const idx = parseInt(btn.dataset.remove);
        filas.splice(idx, 1);
        renderTabla();
        calcularTotales();
    });

    /* ── Calculate Totals ── */
    function calcularTotales() {
        const subtotal = filas.reduce((sum, f) => sum + f.subtotal, 0);
        const descuento = Math.max(0, parseFloat(inpDescuento.value) || 0);
        const base = Math.max(0, subtotal - descuento);
        const iva = base * 0.21;
        const total = base + iva;

        lblSubtotal.textContent = `$${num(subtotal)}`;
        lblDescuento.textContent = `-$${num(descuento)}`;
        lblIva.textContent = `$${num(iva)}`;
        lblTotal.textContent = `$${num(total)}`;

        hdnSubtotal.value = subtotal.toFixed(2);
        hdnDescuento.value = descuento.toFixed(2);
        hdnIva.value = iva.toFixed(2);
        hdnTotal.value = total.toFixed(2);
    }

    if (inpDescuento) {
        inpDescuento.addEventListener('input', calcularTotales);
    }

    /* ── Init: load existing detalles if re-rendering after validation error ── */
    // (Products table starts empty on Create; Edit_tw will pre-populate filas)

    calcularTotales();
})();

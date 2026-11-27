/**
 * movimientos-inventario-modal.js
 * Modal de Movimientos de Inventario dentro de Catálogo Index_tw
 * - Open/close modal (global o por productoId desde fila de catálogo)
 * - Fetch movements from /MovimientoStock/ListJson
 * - Render table rows, stats, pagination
 * - Filter support
 */
(() => {
    const modal = document.getElementById('modal-movimientos');
    const btnOpen = document.getElementById('btn-movimientos-inventario');
    const btnClose = document.getElementById('btn-cerrar-movimientos');
    const tbody = document.getElementById('mov-tbody');
    const emptyState = document.getElementById('mov-empty-state');

    // Stats
    const statTotal = document.getElementById('mov-stat-total');
    const statEntradas = document.getElementById('mov-stat-entradas');
    const statSalidas = document.getElementById('mov-stat-salidas');
    const statAjustes = document.getElementById('mov-stat-ajustes');
    const countShowing = document.getElementById('mov-count-showing');
    const countTotal = document.getElementById('mov-count-total');

    // Filters
    const filtroDesde = document.getElementById('mov-fecha-desde');
    const filtroHasta = document.getElementById('mov-fecha-hasta');
    const filtroTipo = document.getElementById('mov-tipo');
    const filtroProducto = document.getElementById('mov-producto');
    const filtroFuente = document.getElementById('mov-fuente-costo');
    const filtrosBadge = document.getElementById('mov-filtros-badge');
    const movCountContext = document.getElementById('mov-count-context');
    const btnBuscar = document.getElementById('btn-mov-buscar');
    const btnLimpiar = document.getElementById('btn-mov-limpiar');
    const selectPageSize = document.getElementById('mov-page-size');
    const paginationDiv = document.getElementById('mov-pagination');

    // Contexto de producto (banner en modal)
    const productoContexto = document.getElementById('mov-producto-contexto');
    const productoNombreLabel = document.getElementById('mov-producto-nombre-label');
    const btnVerTodos = document.getElementById('btn-mov-ver-todos');

    let allItems = [];
    let displayItems = [];
    let currentPage = 1;
    let pageSize = 25;

    // Estado de filtro por producto (apertura desde fila de catálogo)
    let productoIdFiltro = null;
    let productoNombreFiltro = null;

    if (!modal) return;

    /* ── Open / Close ── */
    if (btnOpen) {
        btnOpen.addEventListener('click', () => abrirModal(null, null));
    }

    // Apertura desde fila de catálogo (delegación sobre el documento)
    document.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-movimientos-producto-id]');
        if (!btn) return;
        const id = parseInt(btn.dataset.movimientosProductoId, 10);
        const nombre = btn.dataset.movimientosProductoNombre || '';
        if (!isNaN(id)) {
            abrirModal(id, nombre);
        }
    });

    if (btnVerTodos) {
        btnVerTodos.addEventListener('click', () => abrirModal(null, null));
    }

    function abrirModal(productoId, productoNombre) {
        productoIdFiltro = productoId;
        productoNombreFiltro = productoNombre;
        actualizarBannerProducto();
        panel().abrir();
        fetchMovimientos();
        setTimeout(function () {
            var closeBtn = document.getElementById('btn-cerrar-movimientos');
            if (closeBtn) closeBtn.focus();
        }, 50);
    }

    function actualizarBannerProducto() {
        if (!productoContexto || !productoNombreLabel) return;
        if (productoIdFiltro !== null) {
            productoNombreLabel.textContent = productoNombreFiltro || `Producto #${productoIdFiltro}`;
            productoContexto.classList.remove('hidden');
        } else {
            productoContexto.classList.add('hidden');
        }
    }

    var _panel = null;
    function panel() {
        if (!_panel) {
            _panel = window.InvUI.panelFormulario(modal, {
                soloLectura: true,
                cierres: '#btn-cerrar-movimientos',
                alCerrar: limpiar
            });
        }
        return _panel;
    }

    function limpiar() {
        productoIdFiltro = null;
        productoNombreFiltro = null;
        if (productoContexto) {
            productoContexto.classList.add('hidden');
        }
    }

    /* ── Fetch ── */
    async function fetchMovimientos() {
        const params = new URLSearchParams();
        if (filtroDesde.value) params.set('fechaDesde', filtroDesde.value);
        if (filtroHasta.value) params.set('fechaHasta', filtroHasta.value);
        if (filtroTipo.value) params.set('tipo', filtroTipo.value);
        // Cuando se abre desde fila de catálogo, filtra por productoId exacto (más preciso que texto)
        if (productoIdFiltro !== null) {
            params.set('productoId', productoIdFiltro);
        } else if (filtroProducto.value.trim()) {
            params.set('busqueda', filtroProducto.value.trim());
        }

        try {
            const res = await fetch(`/MovimientoStock/ListJson?${params.toString()}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();

            allItems = data.items || [];

            // Stats
            statTotal.textContent = data.total;
            statEntradas.textContent = Math.round(data.entradas);
            statSalidas.textContent = Math.round(data.salidas);
            statAjustes.textContent = data.ajustes;
            countTotal.textContent = data.total;

            currentPage = 1;
            renderPage();
        } catch {
            // Un error de carga no es lo mismo que no tener movimientos.
            allItems = [];
            renderPage();
            tbody.innerHTML = filaAviso('No se pudieron cargar los movimientos. Probá de nuevo.', true);
        }
    }

    function filaAviso(texto, error) {
        return `<tr><td colspan="8" class="inv-empty${error ? ' inv-text-danger' : ''}">${esc(texto)}</td></tr>`;
    }

    /* ── Active filters ── */
    function getActiveFiltersCount() {
        let count = 0;
        if (filtroDesde && filtroDesde.value) count++;
        if (filtroHasta && filtroHasta.value) count++;
        if (filtroTipo && filtroTipo.value) count++;
        if (filtroProducto && filtroProducto.value.trim()) count++;
        if (filtroFuente && filtroFuente.value) count++;
        return count;
    }

    function updateFilterBadge() {
        if (!filtrosBadge) return;
        const count = getActiveFiltersCount();
        if (count > 0) {
            filtrosBadge.textContent = count === 1 ? '1 filtro activo' : `${count} filtros activos`;
            filtrosBadge.classList.remove('hidden');
        } else {
            filtrosBadge.classList.add('hidden');
        }
    }

    /* ── Render ── */
    function renderPage() {
        updateFilterBadge();
        const fuenteFilter = filtroFuente ? filtroFuente.value : '';
        displayItems = fuenteFilter
            ? allItems.filter(m => (m.fuenteCosto || 'NoInformado') === fuenteFilter)
            : allItems;

        const start = (currentPage - 1) * pageSize;
        const pageItems = displayItems.slice(start, start + pageSize);
        countShowing.textContent = pageItems.length;
        countTotal.textContent = displayItems.length;

        if (movCountContext) {
            const clientFiltered = (filtroFuente && filtroFuente.value) && displayItems.length < allItems.length;
            if (clientFiltered) {
                movCountContext.textContent = ` · ${allItems.length} en el período`;
                movCountContext.classList.remove('hidden');
            } else {
                movCountContext.classList.add('hidden');
            }
        }

        tbody.innerHTML = '';

        if (!pageItems.length) {
            tbody.innerHTML = filaAviso('No hay movimientos para mostrar');
            renderPagination();
            return;
        }

        pageItems.forEach(m => {
            const cv = cantidadVisual(m);
            const detalle = [m.referenciaLegible, m.motivo || 'Sin motivo registrado'].filter(Boolean).join(' · ');

            const tr = document.createElement('tr');
            tr.innerHTML = `
                <th scope="row"><span class="inv-nowrap">${esc(m.fecha)}</span><p class="inv-ayuda-texto">${esc(m.hora)}</p></th>
                <td data-label="Movimiento" class="inv-celda-detalle">
                    <strong>${esc(m.tipo)}</strong>
                    <p class="inv-ayuda-texto">${esc(detalle)}</p>
                    <p class="inv-ayuda-texto">${esc(m.usuario || '—')}</p>
                </td>
                <td data-label="Producto">${esc(m.productoNombre || '—')}<p class="inv-ayuda-texto">${esc(m.productoCodigo || '')}</p></td>
                <td data-label="Cantidad" class="inv-col-num"><span class="${cv.cls}">${cv.signo}${esc(String(Math.abs(m.cantidad)))}</span></td>
                <td data-label="Costo unitario" class="inv-col-num">
                    <span>${esc(formatMoney(m.costoUnitarioAlMomento))}</span>
                    <p class="inv-ayuda-texto" title="Origen usado para valuar el costo del movimiento." data-fuente-costo="${esc(m.fuenteCosto || 'NoInformado')}">${esc(fuenteCostoLabel(m.fuenteCosto))}</p>
                </td>
                <td data-label="Costo total" class="inv-col-num"><span class="inv-importe">${esc(formatMoney(m.costoTotalAlMomento))}</span></td>
                <td data-label="Saldo" class="inv-col-num">${esc(String(m.stockNuevo))} uds</td>
                <td class="inv-col-actions">
                    <a href="/Producto/Inventario/${Number(m.productoId)}?tab=movimientos" class="inv-btn inv-btn--ghost inv-btn--sm"
                       aria-label="Ver inventario de ${esc(m.productoNombre || 'este producto')}">Ver inventario</a>
                </td>`;
            tbody.appendChild(tr);
        });

        renderPagination();
    }

    const FUENTE_COSTO_LABELS = {
        'VentaDetalleSnapshot': 'Venta',
        'OrdenCompraDetalle': 'Orden de compra',
        'ProductoActual': 'Costo actual del producto',
        'AjusteManual': 'Ajuste manual',
        'Legacy': 'Histórico migrado',
        'NoInformado': 'No informado'
    };

    function fuenteCostoLabel(value) {
        return FUENTE_COSTO_LABELS[value] || value || 'No informado';
    }

    function cantidadVisual(m) {
        const tipo = (m.tipo || '').toLowerCase();
        if (tipo === 'entrada') {
            return { signo: '+', cls: 'inv-text-ok' };
        }
        if (tipo === 'salida') {
            return { signo: '-', cls: 'inv-text-danger' };
        }
        if (tipo === 'ajuste') {
            const ant = Number(m.stockAnterior ?? 0);
            const nuevo = Number(m.stockNuevo ?? 0);
            if (nuevo > ant) return { signo: '+', cls: 'inv-text-ok' };
            if (nuevo < ant) return { signo: '-', cls: 'inv-text-danger' };
            return { signo: '', cls: '' };
        }
        return { signo: '', cls: '' };
    }

    function formatMoney(value) {
        const amount = Number(value || 0);
        return new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS' }).format(amount);
    }

    // Sirve también dentro de atributos: escapa comillas además de los signos de HTML.
    function esc(s) {
        return String(s || '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
    }

    /* ── Pagination ── */
    function renderPagination() {
        const totalPages = Math.max(1, Math.ceil(displayItems.length / pageSize));
        paginationDiv.innerHTML = '';

        const prevBtn = pageBtn('chevron_left', currentPage > 1, () => { currentPage--; renderPage(); });
        paginationDiv.appendChild(prevBtn);

        const maxVisible = 5;
        let startPage = Math.max(1, currentPage - Math.floor(maxVisible / 2));
        let endPage = Math.min(totalPages, startPage + maxVisible - 1);
        if (endPage - startPage + 1 < maxVisible) startPage = Math.max(1, endPage - maxVisible + 1);

        if (startPage > 1) {
            paginationDiv.appendChild(numBtn(1));
            if (startPage > 2) paginationDiv.appendChild(ellipsis());
        }

        for (let i = startPage; i <= endPage; i++) {
            paginationDiv.appendChild(numBtn(i));
        }

        if (endPage < totalPages) {
            if (endPage < totalPages - 1) paginationDiv.appendChild(ellipsis());
            paginationDiv.appendChild(numBtn(totalPages));
        }

        const nextBtn = pageBtn('chevron_right', currentPage < totalPages, () => { currentPage++; renderPage(); });
        paginationDiv.appendChild(nextBtn);
    }

    function numBtn(n) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.textContent = n;
        btn.className = 'inv-btn inv-btn--sm ' + (n === currentPage ? 'inv-btn--primary' : 'inv-btn--ghost');
        btn.setAttribute('aria-label', 'Página ' + n);
        if (n === currentPage) btn.setAttribute('aria-current', 'page');
        btn.addEventListener('click', () => { currentPage = n; renderPage(); });
        return btn;
    }

    function pageBtn(icon, enabled, handler) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.innerHTML = `<span class="material-symbols-outlined" aria-hidden="true">${icon}</span>`;
        btn.disabled = !enabled;
        btn.className = 'inv-icon-btn';
        btn.setAttribute('aria-label', icon === 'chevron_left' ? 'Página anterior' : 'Página siguiente');
        if (enabled) btn.addEventListener('click', handler);
        return btn;
    }

    function ellipsis() {
        const span = document.createElement('span');
        span.textContent = '…';
        return span;
    }

    /* ── Events ── */
    btnBuscar.addEventListener('click', fetchMovimientos);

    btnLimpiar.addEventListener('click', () => {
        filtroDesde.value = '';
        filtroHasta.value = '';
        filtroTipo.value = '';
        filtroProducto.value = '';
        if (filtroFuente) filtroFuente.value = '';
        fetchMovimientos();
    });

    if (filtroFuente) {
        filtroFuente.addEventListener('change', () => {
            currentPage = 1;
            renderPage();
        });
    }

    selectPageSize.addEventListener('change', () => {
        pageSize = parseInt(selectPageSize.value) || 25;
        currentPage = 1;
        renderPage();
    });
})();

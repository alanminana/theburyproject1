/**
 * movimientos-inventario-modal.js
 * Modal de Movimientos de Inventario dentro de Catálogo Index_tw
 * - Open/close modal
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
    const btnBuscar = document.getElementById('btn-mov-buscar');
    const btnLimpiar = document.getElementById('btn-mov-limpiar');
    const selectPageSize = document.getElementById('mov-page-size');
    const paginationDiv = document.getElementById('mov-pagination');

    let allItems = [];
    let currentPage = 1;
    let pageSize = 25;

    if (!modal || !btnOpen) return;

    /* ── Open / Close ── */
    btnOpen.addEventListener('click', () => {
        modal.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
        fetchMovimientos();
    });

    btnClose.addEventListener('click', closeModal);

    modal.addEventListener('click', (e) => {
        if (e.target === modal) closeModal();
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && !modal.classList.contains('hidden')) closeModal();
    });

    function closeModal() {
        modal.classList.add('hidden');
        document.body.style.overflow = '';
    }

    /* ── Fetch ── */
    async function fetchMovimientos() {
        const params = new URLSearchParams();
        if (filtroDesde.value) params.set('fechaDesde', filtroDesde.value);
        if (filtroHasta.value) params.set('fechaHasta', filtroHasta.value);
        if (filtroTipo.value) params.set('tipo', filtroTipo.value);
        if (filtroProducto.value.trim()) params.set('busqueda', filtroProducto.value.trim());

        try {
            const res = await fetch(`/MovimientoStock/ListJson?${params.toString()}`);
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
            allItems = [];
            renderPage();
        }
    }

    /* ── Render ── */
    function renderPage() {
        const start = (currentPage - 1) * pageSize;
        const pageItems = allItems.slice(start, start + pageSize);
        countShowing.textContent = pageItems.length;

        tbody.innerHTML = '';

        if (!pageItems.length) {
            const tr = document.createElement('tr');
            tr.innerHTML = `<td colspan="8" class="py-16 text-center">
                <div class="flex flex-col items-center gap-2 opacity-40">
                    <span class="material-symbols-outlined text-4xl">swap_vert</span>
                    <p class="text-slate-500 dark:text-slate-400 text-base">No hay movimientos para mostrar</p>
                </div>
            </td>`;
            tbody.appendChild(tr);
            renderPagination();
            return;
        }

        pageItems.forEach(m => {
            const badge = tipoBadge(m.tipo);
            const signo = m.cantidad >= 0 ? '+' : '';
            const cantColor = m.cantidad >= 0
                ? 'text-emerald-600 dark:text-emerald-400'
                : 'text-rose-600 dark:text-rose-400';
            const initials = (m.usuario || '??').split(' ').map(w => w[0]).join('').toUpperCase().substring(0, 2);

            const tr = document.createElement('tr');
            tr.className = 'hover:bg-slate-50 dark:hover:bg-slate-800/20 transition-colors';
            tr.innerHTML = `
                <td class="px-4 py-4">
                    <div class="flex flex-col">
                        <span class="text-sm font-medium text-slate-900 dark:text-white">${esc(m.fecha)}</span>
                        <span class="text-xs text-slate-500">${esc(m.hora)}</span>
                    </div>
                </td>
                <td class="px-4 py-4">
                    <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-bold ${badge.cls}">
                        ${esc(badge.label)}
                    </span>
                </td>
                <td class="px-4 py-4">
                    <div class="flex flex-col">
                        <span class="text-sm font-medium text-slate-900 dark:text-white">${esc(m.productoNombre || '-')}</span>
                        <span class="text-xs text-slate-500 tracking-wider font-mono">${esc(m.productoCodigo || '')}</span>
                    </div>
                </td>
                <td class="px-4 py-4">
                    <span class="text-sm font-bold ${cantColor}">${signo}${m.cantidad}</span>
                </td>
                <td class="px-4 py-4">
                    <span class="text-sm text-slate-600 dark:text-slate-400">${esc(m.referencia || '-')}</span>
                </td>
                <td class="px-4 py-4">
                    <div class="flex items-center gap-2">
                        <div class="w-6 h-6 rounded-full bg-primary/20 flex items-center justify-center text-[10px] font-bold text-primary">${esc(initials)}</div>
                        <span class="text-sm text-slate-600 dark:text-slate-400">${esc(m.usuario || '-')}</span>
                    </div>
                </td>
                <td class="px-4 py-4">
                    <span class="text-sm font-bold text-slate-900 dark:text-white">${m.stockNuevo} uds</span>
                </td>
                <td class="px-4 py-4 text-right">
                    <button type="button" class="text-primary hover:text-primary/80 text-sm font-bold">Ver Detalle</button>
                </td>`;
            tbody.appendChild(tr);
        });

        renderPagination();
    }

    function tipoBadge(tipo) {
        switch (tipo) {
            case 'Entrada':
                return { cls: 'bg-emerald-100 dark:bg-emerald-500/20 text-emerald-700 dark:text-emerald-400', label: 'Entrada' };
            case 'Salida':
                return { cls: 'bg-rose-100 dark:bg-rose-500/20 text-rose-700 dark:text-rose-400', label: 'Salida' };
            case 'Ajuste':
                return { cls: 'bg-amber-100 dark:bg-amber-500/20 text-amber-700 dark:text-amber-400', label: 'Ajuste' };
            default:
                return { cls: 'bg-slate-100 dark:bg-slate-500/20 text-slate-700 dark:text-slate-400', label: tipo };
        }
    }

    function esc(s) { const d = document.createElement('div'); d.textContent = s || ''; return d.innerHTML; }

    /* ── Pagination ── */
    function renderPagination() {
        const totalPages = Math.max(1, Math.ceil(allItems.length / pageSize));
        paginationDiv.innerHTML = '';

        const prevBtn = pageBtn('chevron_left', currentPage > 1, () => { currentPage--; renderPage(); });
        prevBtn.classList.add('w-10', 'h-10', 'flex', 'items-center', 'justify-center', 'rounded-lg', 'border', 'border-slate-200', 'dark:border-slate-800');
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
        nextBtn.classList.add('w-10', 'h-10', 'flex', 'items-center', 'justify-center', 'rounded-lg', 'border', 'border-slate-200', 'dark:border-slate-800');
        paginationDiv.appendChild(nextBtn);
    }

    function numBtn(n) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.textContent = n;
        btn.className = n === currentPage
            ? 'w-10 h-10 flex items-center justify-center rounded-lg bg-primary text-white text-sm font-bold'
            : 'w-10 h-10 flex items-center justify-center rounded-lg text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 text-sm font-bold';
        btn.addEventListener('click', () => { currentPage = n; renderPage(); });
        return btn;
    }

    function pageBtn(icon, enabled, handler) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.innerHTML = `<span class="material-symbols-outlined">${icon}</span>`;
        btn.disabled = !enabled;
        btn.className = enabled
            ? 'text-slate-400 hover:text-slate-900 dark:hover:text-white'
            : 'text-slate-400 opacity-50 cursor-not-allowed';
        if (enabled) btn.addEventListener('click', handler);
        return btn;
    }

    function ellipsis() {
        const span = document.createElement('span');
        span.className = 'px-2 text-slate-400';
        span.textContent = '...';
        return span;
    }

    /* ── Events ── */
    btnBuscar.addEventListener('click', fetchMovimientos);

    btnLimpiar.addEventListener('click', () => {
        filtroDesde.value = '';
        filtroHasta.value = '';
        filtroTipo.value = '';
        filtroProducto.value = '';
        fetchMovimientos();
    });

    selectPageSize.addEventListener('change', () => {
        pageSize = parseInt(selectPageSize.value) || 25;
        currentPage = 1;
        renderPage();
    });
})();

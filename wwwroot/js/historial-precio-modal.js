/**
 * historial-precio-modal.js – Historial de cambios de precio por producto.
 * Mantiene la API existente del modal, pero mueve confirmaciones y acciones
 * al contrato estándar del proyecto.
 */
const HistorialPrecioModal = (() => {
    const catalogoModule = typeof CatalogoModule !== 'undefined' ? CatalogoModule : null;
    const modal = () => document.getElementById('modal-historial-precio');
    const tbody = () => document.getElementById('historial-precio-tbody');
    const loading = () => document.getElementById('historial-precio-loading');
    const empty = () => document.getElementById('historial-precio-empty');
    const nombre = () => document.getElementById('historial-producto-nombre');
    const codigo = () => document.getElementById('historial-producto-codigo');
    const feedback = () => document.getElementById('historial-precio-feedback');
    const count = () => document.getElementById('historial-precio-count');

    let productoIdActual = null;

    function dispatchScrollRefresh() {
        if (catalogoModule && typeof catalogoModule.requestScrollRefresh === 'function') {
            catalogoModule.requestScrollRefresh();
            return;
        }

        document.dispatchEvent(new CustomEvent('catalogo:refresh-scroll'));
    }

    function show(element) {
        if (element) {
            element.classList.remove('hidden');
        }
    }

    function hide(element) {
        if (element) {
            element.classList.add('hidden');
        }
    }

    function clearFeedback() {
        const box = feedback();
        if (!box) return;

        box.textContent = '';
        box.className = 'hidden mx-6 mt-5 rounded-xl border px-4 py-3 text-sm font-medium sm:mx-8';
    }

    function updateCount(total) {
        const badge = count();
        if (!badge) return;
        badge.textContent = total + ' evento' + (total === 1 ? '' : 's');
    }

    function showFeedback(message, type) {
        const box = feedback();
        if (!box) return;

        box.textContent = message;
        box.className = 'mx-6 mt-5 rounded-xl border px-4 py-3 text-sm font-medium sm:mx-8';

        if (type === 'success') {
            box.classList.add(
                'bg-emerald-500/10',
                'border-emerald-500/20',
                'text-emerald-600',
                'dark:text-emerald-400'
            );
        } else {
            box.classList.add(
                'bg-red-500/10',
                'border-red-500/20',
                'text-red-500',
                'dark:text-red-400'
            );
        }
    }

    function fmt(n) {
        return '$' + Number(n).toLocaleString('es-AR', {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    }

    function motivoBadge(motivo) {
        if (!motivo) return '';

        const lower = motivo.toLowerCase();
        let color = 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300';

        if (lower.includes('aumento') || lower.includes('proveedor')) {
            color = 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400';
        } else if (lower.includes('oferta') || lower.includes('descuento') || lower.includes('promoción')) {
            color = 'bg-green-50 text-green-700 dark:bg-green-900/30 dark:text-green-400';
        } else if (lower.includes('inflación') || lower.includes('ajuste')) {
            color = 'bg-amber-50 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400';
        } else if (lower.includes('revert') || lower.includes('reversión')) {
            color = 'bg-purple-50 text-purple-700 dark:bg-purple-900/30 dark:text-purple-400';
        }

        return '<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold ' + color + '">' + motivo + '</span>';
    }

    function variacionHtml(anterior, nuevo) {
        if (!anterior || anterior === 0) {
            return '<span class="text-slate-400">—</span>';
        }

        const diff = nuevo - anterior;
        const pct = ((diff / anterior) * 100).toFixed(1);
        const abs = fmt(Math.abs(diff));

        if (diff > 0) {
            return '<div class="flex flex-col items-center">' +
                '<span class="text-xs font-bold text-red-500">+' + pct + '%</span>' +
                '<span class="text-[11px] text-red-400">+' + abs + '</span>' +
                '</div>';
        }

        if (diff < 0) {
            return '<div class="flex flex-col items-center">' +
                '<span class="text-xs font-bold text-emerald-500">' + pct + '%</span>' +
                '<span class="text-[11px] text-emerald-400">' + abs + '</span>' +
                '</div>';
        }

        return '<span class="text-xs text-slate-400">0%</span>';
    }

    function getToken() {
        const element = document.querySelector('input[name="__RequestVerificationToken"]');
        return element ? element.value : '';
    }

    function renderRows(items) {
        const tableBody = tbody();
        tableBody.innerHTML = '';
        updateCount(items ? items.length : 0);

        if (!items || items.length === 0) {
            show(empty());
            dispatchScrollRefresh();
            return;
        }

        hide(empty());

        items.forEach((item) => {
            const partes = (item.fecha || '').split('·');
            const fechaStr = (partes[0] || '').trim();
            const horaStr = (partes[1] || '').trim();

            const revertBtn = item.puedeRevertir
                ? '<button type="button" data-catalogo-action="historial-revert" data-catalogo-evento-id="' + item.eventoId + '" class="inline-flex items-center gap-1.5 text-xs text-primary hover:text-primary/80 font-bold transition-colors">' +
                    '<span class="material-symbols-outlined text-sm">undo</span> Revertir' +
                  '</button>'
                : '<span class="text-xs text-slate-400 italic">—</span>';

            const row = document.createElement('tr');
            row.className = 'hover:bg-slate-50/50 dark:hover:bg-white/[.02] transition-colors';
            row.innerHTML =
                '<td class="px-8 py-4">' +
                    '<div class="text-sm font-medium dark:text-white">' + fechaStr + '</div>' +
                    '<div class="text-xs text-slate-400">' + horaStr + '</div>' +
                '</td>' +
                '<td class="px-6 py-4">' +
                    '<div class="flex items-center gap-2">' +
                        '<span class="flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-primary text-xs font-bold">' +
                            ((item.usuario || '?')[0] || '?').toUpperCase() +
                        '</span>' +
                        '<span class="text-sm dark:text-slate-300">' + (item.usuario || '—') + '</span>' +
                    '</div>' +
                '</td>' +
                '<td class="px-6 py-4">' + motivoBadge(item.motivo) + '</td>' +
                '<td class="px-6 py-4 text-right">' +
                    '<span class="text-sm font-mono text-slate-500 dark:text-slate-400">' + fmt(item.precioAnterior) + '</span>' +
                '</td>' +
                '<td class="px-6 py-4 text-right">' +
                    '<span class="text-sm font-mono font-bold dark:text-white">' + fmt(item.precioNuevo) + '</span>' +
                '</td>' +
                '<td class="px-6 py-4 text-center">' + variacionHtml(item.precioAnterior, item.precioNuevo) + '</td>' +
                '<td class="px-8 py-4 text-right">' + revertBtn + '</td>';
            tableBody.appendChild(row);
        });

        dispatchScrollRefresh();
    }

    async function fetchHistorial(productoId) {
        clearFeedback();
        hide(empty());
        tbody().innerHTML = '';
        show(loading());

        try {
            const response = await fetch('/Catalogo/HistorialPrecioProductoApi?productoId=' + encodeURIComponent(productoId));
            const data = await response.json();
            hide(loading());

            if (data.success === false) {
                show(empty());
                return;
            }

            nombre().textContent = data.producto?.nombre || '';
            codigo().textContent = data.producto?.codigo || '';
            renderRows(data.items);
        } catch {
            hide(loading());
            show(empty());
            showFeedback('No se pudo cargar el historial en este momento.', 'error');
        }
    }

    async function executeRevertir(eventoId) {
        clearFeedback();

        try {
            const formData = new URLSearchParams();
            formData.append('eventoId', eventoId);
            formData.append('__RequestVerificationToken', getToken());

            const response = await fetch('/Catalogo/RevertirCambioPrecioApi', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: formData.toString()
            });
            const data = await response.json();

            if (data.success) {
                if (productoIdActual) {
                    await fetchHistorial(productoIdActual);
                }
                showFeedback(data.mensaje || 'Cambio de precio revertido correctamente.', 'success');
            } else {
                showFeedback(data.mensaje || 'No se pudo revertir el cambio.', 'error');
            }
        } catch {
            showFeedback('Error de conexión al intentar revertir.', 'error');
        }
    }

    function revertir(eventoId) {
        if (window.TheBury && typeof window.TheBury.confirmAction === 'function') {
            window.TheBury.confirmAction(
                '¿Revertir este cambio de precio? Se restaurará el precio anterior.',
                function () {
                    executeRevertir(eventoId);
                }
            );
        }
    }

    function open(productoId) {
        productoIdActual = productoId;
        clearFeedback();
        nombre().textContent = '';
        codigo().textContent = '';
        updateCount(0);
        modal().classList.remove('hidden');
        dispatchScrollRefresh();
        fetchHistorial(productoId);
    }

    function close() {
        modal().classList.add('hidden');
        tbody().innerHTML = '';
        clearFeedback();
        productoIdActual = null;
        updateCount(0);
    }

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && modal() && !modal().classList.contains('hidden')) {
            close();
        }
    });

    document.addEventListener('click', function (event) {
        const revertButton = event.target.closest('[data-catalogo-action="historial-revert"]');
        if (!revertButton) return;

        event.preventDefault();

        const eventoId = parseInt(revertButton.getAttribute('data-catalogo-evento-id'), 10);
        if (!isNaN(eventoId)) {
            revertir(eventoId);
        }
    });

    return { open, close, revertir };
})();

if (typeof CatalogoModule !== 'undefined' && typeof CatalogoModule.registerModalApi === 'function') {
    CatalogoModule.registerModalApi('historial-precio', HistorialPrecioModal);
}

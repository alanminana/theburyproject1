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
    let _openTrigger = null;

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

    // Los datos del historial (motivo, usuario, fecha) son texto libre persistido: se renderizan siempre
    // como nodos DOM con textContent, nunca concatenados a HTML (COM-S-001).
    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = String(text);
        return node;
    }

    function cell(className, ...children) {
        const td = el('td', className);
        children.forEach((child) => child && td.appendChild(child));
        return td;
    }

    function motivoBadge(motivo) {
        if (!motivo) return null;

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

        return el('span', 'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold ' + color, motivo);
    }

    function variacionNode(anterior, nuevo) {
        if (!anterior || anterior === 0) {
            return el('span', 'text-slate-400', '—');
        }

        const diff = nuevo - anterior;
        const pct = ((diff / anterior) * 100).toFixed(1);
        const abs = fmt(Math.abs(diff));

        if (diff > 0 || diff < 0) {
            const up = diff > 0;
            const wrap = el('div', 'flex flex-col items-center');
            wrap.appendChild(el('span', 'text-xs font-bold ' + (up ? 'text-red-500' : 'text-emerald-500'), (up ? '+' : '') + pct + '%'));
            wrap.appendChild(el('span', up ? 'text-[11px] text-red-400' : 'text-[11px] text-emerald-400', (up ? '+' : '') + abs));
            return wrap;
        }

        return el('span', 'text-xs text-slate-400', '0%');
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

            let revertBtn;
            if (item.puedeRevertir) {
                revertBtn = el('button', 'inline-flex items-center gap-1.5 text-xs text-primary hover:text-primary/80 font-bold transition-colors');
                revertBtn.type = 'button';
                revertBtn.setAttribute('data-catalogo-action', 'historial-revert');
                revertBtn.setAttribute('data-catalogo-evento-id', String(parseInt(item.eventoId, 10)));
                revertBtn.appendChild(el('span', 'material-symbols-outlined text-sm', 'undo'));
                revertBtn.appendChild(document.createTextNode(' Revertir'));
            } else {
                revertBtn = el('span', 'text-xs text-slate-400 italic', '—');
            }

            const usuario = item.usuario ? String(item.usuario) : '';
            const userCell = el('div', 'flex items-center gap-2');
            userCell.appendChild(el('span', 'flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-primary text-xs font-bold',
                (Array.from(usuario)[0] || '?').toUpperCase()));
            userCell.appendChild(el('span', 'text-sm dark:text-slate-300', usuario || '—'));

            const row = el('tr', 'hover:bg-slate-50/50 dark:hover:bg-white/[.02] transition-colors');
            row.appendChild(cell('px-8 py-4',
                el('div', 'text-sm font-medium dark:text-white', fechaStr),
                el('div', 'text-xs text-slate-400', horaStr)));
            row.appendChild(cell('px-6 py-4', userCell));
            row.appendChild(cell('px-6 py-4', motivoBadge(item.motivo)));
            row.appendChild(cell('px-6 py-4 text-right',
                el('span', 'text-sm font-mono text-slate-500 dark:text-slate-400', fmt(item.precioAnterior))));
            row.appendChild(cell('px-6 py-4 text-right',
                el('span', 'text-sm font-mono font-bold dark:text-white', fmt(item.precioNuevo))));
            row.appendChild(cell('px-6 py-4 text-center', variacionNode(item.precioAnterior, item.precioNuevo)));
            row.appendChild(cell('px-8 py-4 text-right', revertBtn));
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

    function open(productoId, trigger) {
        _openTrigger = (trigger instanceof Element) ? trigger : null;
        productoIdActual = productoId;
        clearFeedback();
        nombre().textContent = '';
        codigo().textContent = '';
        updateCount(0);
        modal().classList.remove('hidden');
        dispatchScrollRefresh();
        fetchHistorial(productoId);
        setTimeout(function () {
            var closeBtn = document.querySelector('[data-catalogo-modal-close="historial-precio"]');
            if (closeBtn) closeBtn.focus();
        }, 50);
    }

    function close() {
        var trigger = _openTrigger;
        _openTrigger = null;
        modal().classList.add('hidden');
        tbody().innerHTML = '';
        clearFeedback();
        productoIdActual = null;
        updateCount(0);
        if (trigger) trigger.focus();
    }

    document.addEventListener('keydown', function (event) {
        var m = modal();
        if (!m || m.classList.contains('hidden')) return;
        if (event.key === 'Escape') { close(); return; }
        if (event.key === 'Tab' && window.CatalogoModule) window.CatalogoModule.trapFocus(m, event);
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

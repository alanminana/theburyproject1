/**
 * historial-precio-modal.js  –  Historial de Cambios de Precio (por producto)
 *
 * Expone:
 *   HistorialPrecioModal.open(productoId)
 *   HistorialPrecioModal.close()
 */
const HistorialPrecioModal = (() => {
    /* ─── DOM refs ─── */
    const modal    = () => document.getElementById('modal-historial-precio');
    const tbody    = () => document.getElementById('historial-precio-tbody');
    const loading  = () => document.getElementById('historial-precio-loading');
    const empty    = () => document.getElementById('historial-precio-empty');
    const nombre   = () => document.getElementById('historial-producto-nombre');
    const codigo   = () => document.getElementById('historial-producto-codigo');

    /* ─── State ─── */
    let _productoId = null;

    /* ─── Helpers ─── */
    function show(el) { el.classList.remove('hidden'); }
    function hide(el) { el.classList.add('hidden'); }

    function fmt(n) {
        return '$' + Number(n).toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function motivoBadge(motivo) {
        if (!motivo) return '';
        const lower = motivo.toLowerCase();
        let color = 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300';
        if (lower.includes('aumento') || lower.includes('proveedor'))
            color = 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400';
        else if (lower.includes('oferta') || lower.includes('descuento') || lower.includes('promoción'))
            color = 'bg-green-50 text-green-700 dark:bg-green-900/30 dark:text-green-400';
        else if (lower.includes('inflación') || lower.includes('ajuste'))
            color = 'bg-amber-50 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400';
        else if (lower.includes('revert') || lower.includes('reversión'))
            color = 'bg-purple-50 text-purple-700 dark:bg-purple-900/30 dark:text-purple-400';
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold ${color}">${motivo}</span>`;
    }

    function variacionHtml(anterior, nuevo) {
        if (!anterior || anterior === 0) return '<span class="text-slate-400">—</span>';
        const diff = nuevo - anterior;
        const pct  = ((diff / anterior) * 100).toFixed(1);
        const abs  = fmt(Math.abs(diff));
        if (diff > 0) {
            return `<div class="flex flex-col items-center">
                        <span class="text-xs font-bold text-red-500">+${pct}%</span>
                        <span class="text-[11px] text-red-400">+${abs}</span>
                    </div>`;
        } else if (diff < 0) {
            return `<div class="flex flex-col items-center">
                        <span class="text-xs font-bold text-emerald-500">${pct}%</span>
                        <span class="text-[11px] text-emerald-400">${abs}</span>
                    </div>`;
        }
        return '<span class="text-xs text-slate-400">0%</span>';
    }

    function getToken() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    /* ─── Render ─── */
    function renderRows(items) {
        const tb = tbody();
        tb.innerHTML = '';
        if (!items || items.length === 0) { show(empty()); return; }
        hide(empty());

        items.forEach(it => {
            const partes = (it.fecha || '').split('·');
            const fechaStr = (partes[0] || '').trim();
            const horaStr  = (partes[1] || '').trim();

            const revertBtn = it.puedeRevertir
                ? `<button onclick="HistorialPrecioModal.revertir(${it.eventoId})" class="inline-flex items-center gap-1.5 text-xs text-primary hover:text-primary/80 font-bold transition-colors">
                       <span class="material-symbols-outlined text-sm">undo</span> Revertir
                   </button>`
                : `<span class="text-xs text-slate-400 italic">—</span>`;

            const tr = document.createElement('tr');
            tr.className = 'hover:bg-slate-50/50 dark:hover:bg-white/[.02] transition-colors';
            tr.innerHTML = `
                <td class="px-8 py-4">
                    <div class="text-sm font-medium dark:text-white">${fechaStr}</div>
                    <div class="text-xs text-slate-400">${horaStr}</div>
                </td>
                <td class="px-6 py-4">
                    <div class="flex items-center gap-2">
                        <span class="flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-primary text-xs font-bold">
                            ${(it.usuario || '?')[0].toUpperCase()}
                        </span>
                        <span class="text-sm dark:text-slate-300">${it.usuario || '—'}</span>
                    </div>
                </td>
                <td class="px-6 py-4">${motivoBadge(it.motivo)}</td>
                <td class="px-6 py-4 text-right">
                    <span class="text-sm font-mono text-slate-500 dark:text-slate-400">${fmt(it.precioAnterior)}</span>
                </td>
                <td class="px-6 py-4 text-right">
                    <span class="text-sm font-mono font-bold dark:text-white">${fmt(it.precioNuevo)}</span>
                </td>
                <td class="px-6 py-4 text-center">${variacionHtml(it.precioAnterior, it.precioNuevo)}</td>
                <td class="px-8 py-4 text-right">${revertBtn}</td>`;
            tb.appendChild(tr);
        });
    }

    /* ─── API ─── */
    async function fetchHistorial(productoId) {
        hide(empty());
        tbody().innerHTML = '';
        show(loading());

        try {
            const res  = await fetch(`/Catalogo/HistorialPrecioProductoApi?productoId=${encodeURIComponent(productoId)}`);
            const data = await res.json();
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
        }
    }

    async function revertir(eventoId) {
        if (!confirm('¿Revertir este cambio de precio? Se restaurará el precio anterior.')) return;

        try {
            const formData = new URLSearchParams();
            formData.append('eventoId', eventoId);
            formData.append('__RequestVerificationToken', getToken());

            const res = await fetch('/Catalogo/RevertirCambioPrecioApi', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: formData.toString()
            });
            const data = await res.json();
            if (data.success) {
                // Refresh the history
                if (_productoId) fetchHistorial(_productoId);
            } else {
                alert(data.mensaje || 'No se pudo revertir el cambio.');
            }
        } catch {
            alert('Error de conexión al intentar revertir.');
        }
    }

    /* ─── Open / Close ─── */
    function open(productoId) {
        _productoId = productoId;
        nombre().textContent = '';
        codigo().textContent = '';
        const m = modal();
        m.classList.remove('hidden');
        fetchHistorial(productoId);
    }

    function close() {
        modal().classList.add('hidden');
        tbody().innerHTML = '';
        _productoId = null;
    }

    /* ESC to close */
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && !modal().classList.contains('hidden')) close();
    });

    return { open, close, revertir };
})();

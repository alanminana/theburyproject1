// Cotización · simulador — comportamiento de UI (steppers, modales, píldoras de estado).
// Extraído del <script> inline de Views/Cotizacion/Index_tw.cshtml al convertir el
// cotizador en el parcial reutilizable Views/Cotizacion/_CotizadorForm.cshtml, que hoy
// se embebe en la pestaña Cotizar de Venta/Create. Debe cargarse ANTES de
// cotizacion-simulador.js, que llama a window.setQuoteState.
(function () {
    'use strict';

    // Sin el cotizador en la página no hay nada que exponer: evita publicar globales
    // (openModal/closeModal/stepQty) en vistas que no los usan.
    if (!document.querySelector('[data-cotizacion-simulador]')) return;

    // ---- Steppers ----
    function stepQty(id, d) { const el = document.getElementById(id); if (!el) return; el.value = Math.max(1, (parseInt(el.value) || 1) + d); el.dispatchEvent(new Event('input', { bubbles: true })); }
    function stepRow(btn, d) { const inp = btn.parentElement.querySelector('input'); if (!inp) return; inp.value = Math.max(1, (parseInt(inp.value) || 1) + d); inp.dispatchEvent(new Event('input', { bubbles: true })); }
    window.stepQty = stepQty; window.stepRow = stepRow;

    // ---- Modals ----
    function openModal(id) { const el = document.getElementById(id); if (!el) return; el.classList.remove('hidden'); document.body.style.overflow = 'hidden'; }
    function closeModal(id) { const el = document.getElementById(id); if (!el) return; el.classList.add('hidden'); document.body.style.overflow = ''; }
    window.openModal = openModal; window.closeModal = closeModal;
    document.addEventListener('keydown', e => { if (e.key === 'Escape') { ['modal-guardar', 'modal-plan', 'modal-quitar-producto'].forEach(id => { const el = document.getElementById(id); if (el && !el.classList.contains('hidden')) closeModal(id); }); } });

    // ---- Quote state pills (banner + resultados + hint) ----
    function setQuoteState(state) {
        const cfg = {
            idle:      { pill: 'pill-slate', dot: 'bg-slate-500',   label: 'Sin simular',        hint: 'Listo para simular.',                  hintCls: 'text-slate-400',   icon: 'info',           banner: 'Sin simular' },
            simulated: { pill: 'pill-green', dot: 'bg-emerald-400', label: 'Simulada',           hint: 'Simulación lista.',                    hintCls: 'text-emerald-400', icon: 'check_circle',   banner: 'Simulada correctamente' },
            pending:   { pill: 'pill-amber', dot: 'bg-amber-400',   label: 'Cambios pendientes', hint: 'Cambios pendientes: volvé a simular.', hintCls: 'text-amber-300',   icon: 'warning',        banner: 'Cambios sin simular' },
            error:     { pill: 'pill-red',   dot: 'bg-red-400',     label: 'Error',              hint: 'No se pudo simular.',                  hintCls: 'text-red-300',     icon: 'error',          banner: 'Error de simulación' },
            saved:     { pill: 'pill-green', dot: 'bg-emerald-400', label: 'Guardada',           hint: 'Cotización guardada.',                 hintCls: 'text-emerald-400', icon: 'bookmark_added', banner: 'Cotización guardada' }
        };
        const s = cfg[state]; if (!s) return;
        const resPill = document.getElementById('resultados-status-pill');
        if (resPill) { resPill.className = 'pill ' + s.pill + ' ml-auto'; resPill.innerHTML = '<span class="w-1.5 h-1.5 rounded-full ' + s.dot + '"></span> ' + s.label; }
        const banner = document.getElementById('estado-banner');
        if (banner) { banner.className = 'pill ' + s.pill; banner.innerHTML = '<span class="material-symbols-outlined" style="font-size:13px">' + s.icon + '</span> ' + s.banner; }
        const hint = document.getElementById('cotizacion-simular-estado');
        if (hint) { hint.className = 'mt-1.5 text-[11px] text-center flex items-center justify-center gap-1 ' + s.hintCls; hint.innerHTML = '<span class="material-symbols-outlined" style="font-size:13px">' + s.icon + '</span> ' + s.hint; }
    }
    window.setQuoteState = setQuoteState;

    // ---- Aviso de doble descuento ----
    function watchDescuentos() {
        const pct = parseFloat(document.getElementById('cotizacion-descuento-gral-pct')?.value) || 0;
        const imp = parseFloat(document.getElementById('cotizacion-descuento-gral-importe')?.value) || 0;
        const aviso = document.querySelector('[data-descuentos-ambos]');
        if (aviso) aviso.classList.toggle('hidden', !(pct > 0 && imp > 0));
    }
    ['cotizacion-descuento-gral-pct', 'cotizacion-descuento-gral-importe'].forEach(id => {
        const el = document.getElementById(id); if (el) el.addEventListener('input', watchDescuentos);
    });

    setQuoteState('idle');
})();

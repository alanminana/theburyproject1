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

    // ---- Quote state signals ----
    // COTIZACION-SIMULAR-REDESIGN-VISUAL-IMPLEMENTACION-01: antes había 4 señales
    // anunciando el mismo estado (toast, banner del header, pill propia de
    // Resultados, hint fijo junto al CTA). Ahora quedan como máximo 2 visibles a
    // la vez: #estado-banner es el único indicador AMBIENTAL (siempre visible,
    // header) y el hint junto al CTA sólo aparece cuando hay algo que el usuario
    // deba hacer (pending/error) — CONTEXTUAL, no repite el mismo texto en idle/
    // simulated/saved. El toast (showFeedback en cotizacion-simulador.js) sigue
    // siendo el feedback transitorio de acciones puntuales (item 10).
    function setQuoteState(state) {
        const cfg = {
            idle:      { pill: 'pill-slate', hint: '',                                      hintCls: 'text-slate-400',   icon: 'info',           banner: 'Sin simular' },
            simulated: { pill: 'pill-green', hint: '',                                      hintCls: 'text-emerald-400', icon: 'check_circle',   banner: 'Simulada' },
            pending:   { pill: 'pill-amber', hint: 'Cambios pendientes: volvé a simular.', hintCls: 'text-amber-300',   icon: 'warning',        banner: 'Cambios pendientes' },
            error:     { pill: 'pill-red',   hint: 'No se pudo simular: revisá los datos.', hintCls: 'text-red-300',     icon: 'error',          banner: 'Error de simulación' },
            saved:     { pill: 'pill-green', hint: '',                                      hintCls: 'text-emerald-400', icon: 'bookmark_added', banner: 'Guardada' }
        };
        const s = cfg[state]; if (!s) return;
        const banner = document.getElementById('estado-banner');
        if (banner) { banner.className = 'pill ' + s.pill; banner.innerHTML = '<span class="material-symbols-outlined" style="font-size:13px">' + s.icon + '</span> ' + s.banner; }
        const hint = document.getElementById('cotizacion-simular-estado');
        if (hint) {
            hint.className = (s.hint ? '' : 'hidden ') + 'mt-1.5 text-[11px] text-center flex items-center justify-center gap-1 ' + s.hintCls;
            hint.innerHTML = s.hint ? '<span class="material-symbols-outlined" style="font-size:13px">' + s.icon + '</span> ' + s.hint : '';
        }

        // COTIZACION-SIMULAR-REDESIGN-VISUAL-POLISH-01: jerarquía de CTA por estado.
        // Con simulación vigente ("simulated") Guardar es la única acción primaria —
        // antes Simular (azul, btn-primary) y Guardar (verde, habilitado) competían
        // como dos CTAs de igual peso visual. En idle/pending/error, Simular sigue
        // siendo la única acción disponible y mantiene el estilo primario. Sólo clase
        // (no copy: "Simular de nuevo"/"Reintentar"/etc. no entran en el ancho fijo
        // de 2 columnas del CTA sin desbordar sobre Guardar) — ids, handlers y
        // disabled no se tocan acá.
        const simularBtn = document.getElementById('cotizacion-simular');
        if (simularBtn) {
            const esSecundaria = state === 'simulated';
            simularBtn.classList.toggle('btn-primary', !esSecundaria);
            simularBtn.classList.toggle('btn-soft', esSecundaria);
        }
    }
    window.setQuoteState = setQuoteState;

    // ---- Paneles colapsables (mobile real <40rem, COTIZACION-SIMULAR-REDESIGN-VISUAL-CIERRE-01) ----
    // El botón vive siempre en el DOM (oculto vía CSS fuera de esa banda, ver
    // cotizacion-simulador.css) — acá sólo se cablea el toggle genérico, sin
    // depender del ancho real: en bandas más anchas el botón es display:none y
    // nunca recibe click real, así que esto no tiene efecto visible ahí.
    document.querySelectorAll('.panel-toggle-btn, .config-toggle-head').forEach(btn => {
        btn.addEventListener('click', () => {
            const open = btn.getAttribute('aria-expanded') === 'true';
            btn.setAttribute('aria-expanded', String(!open));
            (btn.getAttribute('aria-controls') || '').split(/\s+/).filter(Boolean).forEach(id => {
                const el = document.getElementById(id);
                if (el) el.classList.toggle('is-collapsed', open);
            });
        });
    });

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

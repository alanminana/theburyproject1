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
    // COTIZACION-MIVENTA-02 (§31 del pedido — Escape): modal-envio y modal-confirmar-venta
    // (COTIZACION-MIVENTA-01) y modal-facturar-config (esta iteración) no estaban en esta
    // lista — Escape no los cerraba. Mismo criterio de "sin guardar nunca revierte el
    // checkbox" que ya usan cancelarModalEnvio/cancelarModalFacturar: Escape en esos dos
    // dispara el mismo botón "Cancelar" (no un simple ocultar) para no dejar el checkbox
    // tildado con un modal cerrado sin datos guardados.
    const CANCELAR_POR_ID = { 'modal-envio': 'cotizacion-envio-cancelar', 'modal-facturar-config': 'cotizacion-facturar-cancelar' };
    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape') return;
        ['modal-plan', 'modal-quitar-producto', 'modal-envio', 'modal-facturar-config', 'modal-confirmar-venta'].forEach(id => {
            const el = document.getElementById(id);
            if (!el || el.classList.contains('hidden')) return;
            const cancelarId = CANCELAR_POR_ID[id];
            const cancelarBtn = cancelarId && document.getElementById(cancelarId);
            if (cancelarBtn) cancelarBtn.click();
            else closeModal(id);
        });
    });

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

        // COTIZACION-MOCKUP-01: el CTA de la franja de totales conserva SIEMPRE el estilo
        // primario (mockup: "Actualizar cotización" y "Confirmar Mi Venta" son ambos de acento).
        // Antes (COTIZACION-SIMULAR-REDESIGN-VISUAL-POLISH-01) degradaba Simular a secundario con
        // una simulación vigente para que Guardar fuera la única acción primaria; el mockup del
        // usuario prevalece sobre esa jerarquía. Ids, handlers y disabled no se tocan acá.
        const simularBtn = document.getElementById('cotizacion-simular');
        if (simularBtn) {
            simularBtn.classList.add('btn-primary');
            simularBtn.classList.remove('btn-soft');
        }
    }
    window.setQuoteState = setQuoteState;

    // COTIZACION-WORKSTATION-01: se retiró el toggle de paneles colapsables de mobile
    // (.panel-toggle-btn / .config-toggle-head). Existían porque el apilado mobile
    // ponía Resultados primero y había que plegar Productos/Configuración para que no
    // lo empujaran fuera del primer viewport. El pedido actual del usuario fija el
    // orden natural (Productos → Cliente/Condiciones → Totales+Simular → Resultados,
    // §27), así que Productos vuelve a ser lo primero y no hay nada que plegar; los
    // botones y sus clases se eliminaron del parcial y del CSS.

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

/**
 * module-index.js — Shared logic for module index pages (filters, pagination, tables).
 */
(function () {
    'use strict';

    // ── Filter form helpers ──
    var filterForm = document.getElementById('filterForm');
    var btnLimpiar = document.getElementById('btnLimpiar');

    if (btnLimpiar && filterForm) {
        btnLimpiar.addEventListener('click', function () {
            var inputs = filterForm.querySelectorAll('input, select');
            for (var i = 0; i < inputs.length; i++) {
                var el = inputs[i];
                if (el.type === 'hidden') continue;
                if (el.tagName === 'SELECT') { el.selectedIndex = 0; }
                else { el.value = ''; }
            }
            filterForm.submit();
        });
    }

    // ── Collapsible filter panel (shared — works on any index with btn-toggle-filtros / filtros-body) ──
    // Uses data-expanded / data-hidden (not aria-expanded) to avoid layout.js closeAllDropdowns override.
    var btnToggle   = document.getElementById('btn-toggle-filtros');
    var filtrosBody = document.getElementById('filtros-body');
    // La animación la resuelve CSS (grid-template-rows en .filter-panel-erp__body): acá
    // sólo se alterna el estado.
    if (btnToggle && filtrosBody) {
        btnToggle.addEventListener('click', function (e) {
            e.stopPropagation();
            var expanded = btnToggle.getAttribute('data-expanded') === 'true';
            btnToggle.setAttribute('data-expanded', expanded ? 'false' : 'true');
            filtrosBody.setAttribute('data-hidden', expanded ? 'true' : 'false');
        });
    }

    // ── Delete confirmation ──
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-delete-url]');
        if (!btn) return;
        e.preventDefault();
        var nombre = btn.getAttribute('data-nombre') || 'este registro';
        var deleteUrl = btn.getAttribute('data-delete-url');
        if (!deleteUrl) return;

        if (window.TheBury && typeof window.TheBury.confirmAction === 'function') {
            window.TheBury.confirmAction(
                '¿Estás seguro de eliminar "' + nombre + '"? Esta acción no se puede deshacer.',
                function () { window.location.href = deleteUrl; }
            );
        }
    });
})();

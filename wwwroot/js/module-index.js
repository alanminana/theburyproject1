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

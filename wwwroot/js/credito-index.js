/* credito-index.js — Tabs y filtros para Créditos Index */

document.addEventListener('DOMContentLoaded', function () {
    // Tab switching
    var tabs = document.querySelectorAll('#credito-tabs button[data-tab]');
    tabs.forEach(function (tab) {
        tab.addEventListener('click', function () {
            var target = this.getAttribute('data-tab');

            // Update tab buttons
            tabs.forEach(function (t) {
                var isActive = t.getAttribute('data-tab') === target;
                t.classList.toggle('border-primary', isActive);
                t.classList.toggle('text-primary', isActive);
                t.classList.toggle('font-bold', isActive);
                t.classList.toggle('border-transparent', !isActive);
                t.classList.toggle('text-slate-500', !isActive);
                t.classList.toggle('font-medium', !isActive);
            });

            // Show/hide tab panels
            document.getElementById('tab-creditos').classList.toggle('hidden', target !== 'creditos');
            document.getElementById('tab-moras').classList.toggle('hidden', target !== 'moras');
        });
    });

    // Clear filters
    var btnLimpiar = document.getElementById('btnLimpiar');
    if (btnLimpiar) {
        btnLimpiar.addEventListener('click', function () {
            var form = document.getElementById('filterForm');
            if (!form) return;
            form.querySelectorAll('input, select').forEach(function (el) {
                if (el.type === 'checkbox') el.checked = false;
                else el.value = '';
            });
            form.submit();
        });
    }
});

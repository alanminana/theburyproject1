/**
 * proveedor-index.js  –  Proveedores Index page
 *
 * - Auto-dismiss toast notifications
 * - Sync select dropdown values from query params
 */
(() => {
    /* ── Toast auto-dismiss ── */
    ['toast-error', 'toast-success'].forEach(id => {
        const el = document.getElementById(id);
        if (el) setTimeout(() => el.remove(), 4000);
    });

    /* ── Sync <select> elements with query string ── */
    const params = new URLSearchParams(window.location.search);
    document.querySelectorAll('#form-filtros select[name]').forEach(sel => {
        const val = params.get(sel.name);
        if (val) sel.value = val;
    });

    /* ── Product search filter inside create modal ── */
    const searchInput = document.getElementById('proveedor-producto-search');
    if (searchInput) {
        searchInput.addEventListener('input', () => {
            const q = searchInput.value.toLowerCase().trim();
            document.querySelectorAll('.proveedor-producto-item').forEach(label => {
                label.style.display = !q || label.dataset.nombre.includes(q) ? '' : 'none';
            });
        });
    }
})();

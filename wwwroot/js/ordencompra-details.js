/**
 * ordencompra-details.js  –  Orden de Compra Details page
 *
 * - Auto-dismiss inline alerts
 * - Confirm estado change via dropdown
 * - Sync select with current value
 */
(() => {
    TheBury.autoDismissToasts();

    /* ── Confirm estado change ── */
    const form = document.getElementById('form-cambiar-estado');
    const sel = document.getElementById('select-estado');
    if (form && sel) {
        form.addEventListener('submit', (e) => {
            const label = sel.options[sel.selectedIndex]?.text || '';
            if (!confirm(`¿Está seguro que desea cambiar el estado a "${label}"?`)) {
                e.preventDefault();
            }
        });
    }
})();

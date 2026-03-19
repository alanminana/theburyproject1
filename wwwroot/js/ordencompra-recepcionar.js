/**
 * ordencompra-recepcionar.js  –  Recepcionar Mercadería page
 *
 * - Auto-dismiss alerts
 * - Live update summary counters from quantity inputs
 * - Validate max quantity per input
 * - Confirm submission
 */
(() => {
    /* ── Alert auto-dismiss ── */
    ['toast-error', 'toast-success'].forEach(id => {
        const el = document.getElementById(id);
        if (el) setTimeout(() => el.remove(), 5000);
    });

    /* ── Summary counters ── */
    const inputs = document.querySelectorAll('.input-recepcion');
    const lblIngresar = document.getElementById('lbl-ingresar');

    function updateSummary() {
        let total = 0;
        inputs.forEach(inp => {
            const val = parseInt(inp.value, 10) || 0;
            const max = parseInt(inp.getAttribute('max'), 10) || 0;
            // Clamp to valid range
            if (val < 0) inp.value = 0;
            if (val > max) inp.value = max;
            total += Math.min(Math.max(parseInt(inp.value, 10) || 0, 0), max);
        });
        if (lblIngresar) lblIngresar.textContent = total;
    }

    inputs.forEach(inp => {
        inp.addEventListener('input', updateSummary);
        inp.addEventListener('change', updateSummary);
    });

    /* ── Confirm submission ── */
    const form = document.getElementById('form-recepcionar');
    if (form) {
        form.addEventListener('submit', (e) => {
            let total = 0;
            inputs.forEach(inp => {
                total += parseInt(inp.value, 10) || 0;
            });
            if (total <= 0) {
                e.preventDefault();
                alert('Debe ingresar al menos una unidad para recepcionar.');
                return;
            }
            if (!confirm(`¿Confirmar la recepción de ${total} unidad(es)?`)) {
                e.preventDefault();
            }
        });
    }
})();

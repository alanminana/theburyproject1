(function () {
    'use strict';
    var panel = document.getElementById('cotizacion-cancelar-panel');
    if (!panel) return;
    var form = document.getElementById('cotizacion-cancelar-form');
    var motivo = document.getElementById('cotizacion-cancelar-motivo');
    var boton = document.getElementById('cotizacion-cancelar-btn');
    var error = document.getElementById('cotizacion-cancelar-error');

    function mostrarError(texto) {
        error.textContent = texto;
        error.classList.remove('hidden');
    }

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        error.classList.add('hidden');
        var texto = (motivo.value || '').trim();
        if (!texto) { mostrarError('Ingresá el motivo de la cancelación.'); motivo.focus(); return; }
        if (!window.confirm('¿Cancelar esta cotización? Ya no podrá convertirse en venta.')) return;

        boton.disabled = true;
        try {
            var token = form.querySelector('input[name="__RequestVerificationToken"]');
            var resp = await fetch(panel.dataset.cancelarUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token ? token.value : '' },
                body: JSON.stringify({ motivo: texto })
            });
            if (resp.ok) { window.location.reload(); return; }
            var data = null;
            try { data = await resp.json(); } catch (_) { }
            var errores = data && (data.errores || (data.error ? [data.error] : null));
            mostrarError(errores && errores.length ? errores.join(' ') : 'No se pudo cancelar la cotización.');
        } catch (_) {
            mostrarError('No se pudo cancelar la cotización.');
        } finally {
            boton.disabled = false;
        }
    });
})();

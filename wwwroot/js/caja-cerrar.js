document.addEventListener('DOMContentLoaded', () => {
    const TOLERANCIA = 0.01;

    const inputs = document.querySelectorAll('.arqueo-input');
    const montoEsperadoEl = document.getElementById('monto-esperado');
    const totalRealEl = document.getElementById('total-real');
    const diferenciaValorEl = document.getElementById('diferencia-valor');
    const diferenciaIconEl = document.getElementById('diferencia-icon');
    const justificacionEl = document.getElementById('justificacion');

    const montoEsperado = parseFloat(montoEsperadoEl?.dataset.value) || 0;

    function formatCurrency(value) {
        return '$' + value.toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function recalcular() {
        let totalReal = 0;
        inputs.forEach(input => {
            totalReal += parseFloat(input.value) || 0;
        });

        const diferencia = totalReal - montoEsperado;
        const tieneDiferencia = Math.abs(diferencia) > TOLERANCIA;

        totalRealEl.textContent = formatCurrency(totalReal);
        diferenciaValorEl.textContent = formatCurrency(diferencia);

        // Color + icon
        diferenciaValorEl.classList.remove('text-emerald-500', 'text-rose-500', 'text-amber-500');
        diferenciaIconEl.classList.remove('text-emerald-500', 'text-rose-500', 'text-amber-500');

        if (!tieneDiferencia) {
            diferenciaValorEl.classList.add('text-emerald-500');
            diferenciaIconEl.classList.add('text-emerald-500');
            diferenciaIconEl.textContent = 'check_circle';
        } else if (diferencia > 0) {
            diferenciaValorEl.classList.add('text-amber-500');
            diferenciaIconEl.classList.add('text-amber-500');
            diferenciaIconEl.textContent = 'trending_up';
        } else {
            diferenciaValorEl.classList.add('text-rose-500');
            diferenciaIconEl.classList.add('text-rose-500');
            diferenciaIconEl.textContent = 'trending_down';
        }

        // Enable/disable justificación
        justificacionEl.disabled = !tieneDiferencia;
        if (!tieneDiferencia) {
            justificacionEl.value = '';
        }
    }

    inputs.forEach(input => input.addEventListener('input', recalcular));

    // Initial calculation (e.g. on validation roundtrip)
    recalcular();

    // Toast auto-dismiss
    const toast = document.getElementById('toast-error');
    if (toast) {
        setTimeout(() => {
            toast.style.transition = 'opacity 0.5s';
            toast.style.opacity = '0';
            setTimeout(() => toast.remove(), 500);
        }, 5000);
    }
});

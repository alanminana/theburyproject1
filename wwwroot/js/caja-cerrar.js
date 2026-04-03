document.addEventListener('DOMContentLoaded', () => {
    const TOLERANCIA = 0.01;

    const inputs = document.querySelectorAll('.arqueo-input');
    const montoEsperadoEl = document.getElementById('monto-esperado');
    const totalRealEl = document.getElementById('total-real');
    const diferenciaValorEl = document.getElementById('diferencia-valor');
    const diferenciaIconEl = document.getElementById('diferencia-icon');
    const diferenciaStatusEl = document.getElementById('diferencia-status');
    const diferenciaHelpEl = document.getElementById('diferencia-help');
    const justificacionEl = document.getElementById('justificacion');
    const justificacionWrapEl = document.querySelector('[data-caja-justificacion]');

    const montoEsperado = parseFloat(montoEsperadoEl?.dataset.value) || 0;

    function setStatus({ label, help, tone, icon }) {
        if (diferenciaStatusEl) {
            diferenciaStatusEl.textContent = label;
            diferenciaStatusEl.className = `inline-flex items-center rounded-full border px-3 py-1 text-xs font-bold uppercase tracking-wide ${tone.badge}`;
        }

        if (diferenciaHelpEl) {
            diferenciaHelpEl.textContent = help;
        }

        if (diferenciaIconEl) {
            diferenciaIconEl.textContent = icon;
            diferenciaIconEl.classList.remove('text-emerald-500', 'text-rose-500', 'text-amber-500');
            diferenciaIconEl.classList.add(tone.icon);
        }

        if (diferenciaValorEl) {
            diferenciaValorEl.classList.remove('text-emerald-500', 'text-rose-500', 'text-amber-500');
            diferenciaValorEl.classList.add(tone.icon);
        }

        if (justificacionWrapEl) {
            justificacionWrapEl.className = `rounded-2xl border p-5 shadow-sm ${tone.panel}`;
        }
    }

    function recalcular() {
        let totalReal = 0;
        inputs.forEach(input => {
            totalReal += parseFloat(input.value) || 0;
        });

        const diferencia = totalReal - montoEsperado;
        const tieneDiferencia = Math.abs(diferencia) > TOLERANCIA;

        totalRealEl.textContent = TheBury.formatCurrency(totalReal);
        diferenciaValorEl.textContent = TheBury.formatCurrency(diferencia);

        if (!tieneDiferencia) {
            setStatus({
                label: 'Exacto',
                help: 'Cuando el total contado coincide con el sistema, no hace falta justificar diferencias.',
                icon: 'check_circle',
                tone: {
                    badge: 'border-emerald-500/20 bg-emerald-500/10 text-emerald-500',
                    icon: 'text-emerald-500',
                    panel: 'border-slate-200 bg-white dark:border-slate-700 dark:bg-slate-900/40'
                }
            });
        } else if (diferencia > 0) {
            setStatus({
                label: 'Sobrante',
                help: 'Hay un excedente respecto del sistema. Documente el motivo antes de cerrar la caja.',
                icon: 'trending_up',
                tone: {
                    badge: 'border-amber-500/20 bg-amber-500/10 text-amber-500',
                    icon: 'text-amber-500',
                    panel: 'border-amber-400/30 bg-amber-500/10 dark:border-amber-400/30 dark:bg-amber-500/10'
                }
            });
        } else {
            setStatus({
                label: 'Faltante',
                help: 'El monto contado quedó por debajo del esperado. La justificación es obligatoria para continuar.',
                icon: 'trending_down',
                tone: {
                    badge: 'border-rose-500/20 bg-rose-500/10 text-rose-500',
                    icon: 'text-rose-500',
                    panel: 'border-rose-500/20 bg-rose-500/10 dark:border-rose-500/20 dark:bg-rose-500/10'
                }
            });
        }

        justificacionEl.disabled = !tieneDiferencia;
        if (!tieneDiferencia) {
            justificacionEl.value = '';
        }
    }

    inputs.forEach(input => input.addEventListener('input', recalcular));

    // Initial calculation (e.g. on validation roundtrip)
    recalcular();

    TheBury.autoDismissToasts();
});

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
            diferenciaStatusEl.className = `chip ${tone.badge}`;
            diferenciaStatusEl.style.visibility = 'visible';
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

    let diferenciaActual = 0;
    let cierreConfirmado = false;

    function recalcular() {
        // Sin monto ingresado no hay diferencia que informar: estado neutro (no "faltante total").
        if ([...inputs].every(input => input.value.trim() === '')) {
            totalRealEl.textContent = TheBury.formatCurrency(0);
            diferenciaValorEl.textContent = '—';
            diferenciaValorEl.classList.remove('text-emerald-500', 'text-rose-500', 'text-amber-500');
            if (diferenciaStatusEl) {
                diferenciaStatusEl.style.visibility = 'hidden';
            }
            if (diferenciaHelpEl) {
                diferenciaHelpEl.textContent = 'Ingresá el efectivo contado para ver la diferencia.';
            }
            justificacionEl.disabled = true;
            justificacionEl.value = '';
            justificacionWrapEl?.classList.add('hidden');
            return;
        }

        let totalReal = 0;
        inputs.forEach(input => {
            totalReal += parseFloat(input.value) || 0;
        });

        const diferencia = totalReal - montoEsperado;
        diferenciaActual = diferencia;
        const tieneDiferencia = Math.abs(diferencia) > TOLERANCIA;

        totalRealEl.textContent = TheBury.formatCurrency(totalReal);
        diferenciaValorEl.textContent = TheBury.formatCurrency(diferencia);

        if (!tieneDiferencia) {
            setStatus({
                label: 'Exacto',
                help: 'Cuando el total contado coincide con el sistema, no hace falta justificar diferencias.',
                icon: 'check_circle',
                tone: {
                    badge: 'chip-ok',
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
                    badge: 'chip-warn',
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
                    badge: 'chip-bad',
                    icon: 'text-rose-500',
                    panel: 'border-rose-500/20 bg-rose-500/10 dark:border-rose-500/20 dark:bg-rose-500/10'
                }
            });
        }

        justificacionEl.disabled = !tieneDiferencia;
        justificacionWrapEl?.classList.toggle('hidden', !tieneDiferencia);
        if (!tieneDiferencia) {
            justificacionEl.value = '';
        }
    }

    inputs.forEach(input => input.addEventListener('input', recalcular));

    // La justificación es obligatoria cuando hay diferencia. El servidor sigue siendo la autoridad;
    // esto solo evita el viaje de ida y vuelta y deja el error pegado al campo.
    const form = document.getElementById('form-cerrar');
    // Span propio: jquery.validate.unobtrusive vacía el de asp-validation-for al validar el formulario.
    const justificacionErrorEl = document.querySelector('[data-caja-justificacion-error]');
    const justificacionErrorServidorEl = document.querySelector('[data-valmsg-for="JustificacionDiferencia"]');
    const MENSAJE_JUSTIFICACION = 'Debe proporcionar una justificación para la diferencia encontrada';

    function setErrorJustificacion(mensaje) {
        if (justificacionErrorEl) {
            justificacionErrorEl.textContent = mensaje;
        }
        if (mensaje === '' && justificacionErrorServidorEl) {
            justificacionErrorServidorEl.textContent = '';
        }
        justificacionEl.setAttribute('aria-invalid', mensaje ? 'true' : 'false');
    }

    form?.addEventListener('submit', event => {
        if (!justificacionEl.disabled && !justificacionEl.value.trim()) {
            event.preventDefault();
            setErrorJustificacion(MENSAJE_JUSTIFICACION);
            justificacionEl.focus();
            return;
        }

        // Cierre irreversible con diferencia: segunda confirmación explícita con el monto a la vista.
        if (!cierreConfirmado && Math.abs(diferenciaActual) > TOLERANCIA) {
            event.preventDefault();
            const tipo = diferenciaActual > 0 ? 'un sobrante' : 'un faltante';
            TheBury.confirmAction(
                `Vas a cerrar la caja con ${tipo} de ${TheBury.formatCurrency(Math.abs(diferenciaActual))}. El cierre es irreversible. ¿Confirmás?`,
                () => {
                    cierreConfirmado = true;
                    form.requestSubmit();
                });
        }
    });

    justificacionEl.addEventListener('input', () => {
        if (justificacionEl.value.trim()) {
            setErrorJustificacion('');
        }
    });

    // Initial calculation (e.g. on validation roundtrip)
    recalcular();

    if (justificacionErrorServidorEl?.textContent.trim()) {
        justificacionEl.setAttribute('aria-invalid', 'true');
        justificacionEl.focus();
    }

    TheBury.autoDismissToasts();
});

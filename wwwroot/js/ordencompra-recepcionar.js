/**
 * ordencompra-recepcionar.js - Recepcionar Mercaderia page
 *
 * - Auto-dismiss alerts
 * - Reuse horizontal scroll affordance
 * - Live update reception summary and progress
 * - Replace native alert/confirm with inline feedback + shared modal
 */
(() => {
    window.TheBury?.autoDismissToasts?.(4500);

    const scrollAffordance = window.TheBury?.initHorizontalScrollAffordance?.(
        document.querySelector('[data-oc-scroll]')
    );

    const form = document.getElementById('form-recepcionar');
    const btnConfirmar = document.getElementById('btn-confirmar');
    const inputs = Array.from(document.querySelectorAll('.input-recepcion'));
    const summary = document.getElementById('recepcionar-summary');
    const lblIngresar = document.getElementById('lbl-ingresar');
    const lblPendientes = document.getElementById('lbl-pendientes');
    const lblProductos = document.getElementById('lbl-productos-seleccionados');
    const progressBar = document.getElementById('recepcionar-progress-bar');
    const progressText = document.getElementById('recepcionar-progress-text');
    const feedback = document.getElementById('recepcionar-feedback');
    const feedbackIcon = document.getElementById('recepcionar-feedback-icon');
    const feedbackText = document.getElementById('recepcionar-feedback-text');
    const totalPendienteInicial = Number(summary?.dataset.totalPendiente || lblPendientes?.dataset.initial || 0);
    let confirmedSubmit = false;
    let feedbackTimer = null;

    function setFeedback(message, variant) {
        if (!feedback || !feedbackText || !feedbackIcon) {
            return;
        }

        if (feedbackTimer) {
            window.clearTimeout(feedbackTimer);
            feedbackTimer = null;
        }

        if (!message) {
            feedback.hidden = true;
            feedback.classList.add('hidden');
            feedback.removeAttribute('data-variant');
            feedbackText.textContent = '';
            return;
        }

        const safeVariant = variant === 'info' ? 'info' : 'error';
        feedback.hidden = false;
        feedback.classList.remove('hidden');
        feedback.dataset.variant = safeVariant;
        feedbackIcon.textContent = safeVariant === 'info' ? 'info' : 'error';
        feedbackText.textContent = message;

        if (safeVariant === 'info') {
            feedbackTimer = window.setTimeout(() => {
                setFeedback('', '');
            }, 3200);
        }
    }

    function updateSummary(options = {}) {
        let totalIngresar = 0;
        let productosSeleccionados = 0;
        let clamped = false;

        inputs.forEach((input) => {
            const max = Number(input.getAttribute('max') || 0);
            let value = Number.parseInt(input.value, 10);

            if (!Number.isFinite(value)) {
                value = 0;
            }

            if (value < 0) {
                value = 0;
                clamped = true;
            }

            if (value > max) {
                value = max;
                clamped = true;
            }

            if (Number.parseInt(input.value, 10) !== value) {
                input.value = String(value);
            }

            totalIngresar += value;
            if (value > 0) {
                productosSeleccionados += 1;
            }
        });

        const restante = Math.max(totalPendienteInicial - totalIngresar, 0);
        const porcentaje = totalPendienteInicial > 0
            ? Math.min(Math.round((totalIngresar / totalPendienteInicial) * 100), 100)
            : 0;

        if (lblIngresar) {
            lblIngresar.textContent = String(totalIngresar);
        }

        if (lblPendientes) {
            lblPendientes.textContent = String(restante);
        }

        if (lblProductos) {
            lblProductos.textContent = String(productosSeleccionados);
        }

        if (progressBar) {
            progressBar.style.width = `${porcentaje}%`;
        }

        if (progressText) {
            progressText.textContent = `${porcentaje}%`;
        }

        if (btnConfirmar) {
            const hasUnits = totalIngresar > 0;
            btnConfirmar.dataset.state = hasUnits ? 'ready' : 'idle';
            btnConfirmar.classList.toggle('opacity-60', !hasUnits);
            btnConfirmar.classList.toggle('shadow-none', !hasUnits);
        }

        if (totalIngresar > 0 && feedback?.dataset.variant === 'error') {
            setFeedback('', '');
        }

        if (clamped && options.showClampFeedback) {
            setFeedback('Se ajustó una cantidad al máximo pendiente permitido para ese producto.', 'info');
        } else if (!clamped && options.clearInfoFeedback && feedback?.dataset.variant === 'info') {
            setFeedback('', '');
        }

        return {
            totalIngresar,
            productosSeleccionados,
            restante
        };
    }

    inputs.forEach((input) => {
        input.addEventListener('input', () => updateSummary());
        input.addEventListener('change', () => updateSummary({ showClampFeedback: true, clearInfoFeedback: true }));
        input.addEventListener('blur', () => updateSummary({ showClampFeedback: true, clearInfoFeedback: true }));
    });

    if (form) {
        form.addEventListener('submit', (event) => {
            if (confirmedSubmit) {
                return;
            }

            const { totalIngresar, productosSeleccionados } = updateSummary();

            if (totalIngresar <= 0) {
                event.preventDefault();
                setFeedback('Debés ingresar al menos una unidad para recepcionar.', 'error');
                inputs[0]?.focus();
                return;
            }

            event.preventDefault();
            setFeedback('', '');

            if (typeof window.TheBury?.confirmAction === 'function') {
                window.TheBury.confirmAction(
                    `¿Confirmás la recepción de ${totalIngresar} unidad(es) distribuidas en ${productosSeleccionados} producto(s)?`,
                    () => {
                        confirmedSubmit = true;
                        if (typeof form.requestSubmit === 'function') {
                            form.requestSubmit();
                            return;
                        }

                        form.submit();
                    }
                );

                return;
            }

            confirmedSubmit = true;
            form.submit();
        });
    }

    updateSummary();
    window.addEventListener('load', () => scrollAffordance?.update());
})();

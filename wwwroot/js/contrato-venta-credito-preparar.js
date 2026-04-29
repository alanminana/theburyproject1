(function () {
    'use strict';

    const panel = document.querySelector('[data-contrato-panel]');
    if (!panel) return;

    const formGenerar = document.querySelector('[data-contrato-generar-form]');
    const formConfirmar = document.querySelector('[data-contrato-confirmar-form]');
    const btnGenerar = document.querySelector('[data-contrato-generar-btn]');
    const feedback = document.getElementById('contrato-feedback');

    const statePendiente = document.querySelector('[data-contrato-state-pendiente]');
    const stateGenerado = document.querySelector('[data-contrato-state-generado]');
    const titlePendiente = document.querySelector('[data-contrato-title-pendiente]');
    const titleGenerado = document.querySelector('[data-contrato-title-generado]');
    const verLink = document.querySelector('[data-contrato-ver-link]');

    function show(el) { el?.classList.remove('hidden'); }
    function hide(el) { el?.classList.add('hidden'); }

    function setFeedback(message) {
        if (!feedback) return;
        if (!message) {
            feedback.textContent = '';
            hide(feedback);
            return;
        }

        feedback.textContent = message;
        show(feedback);
    }

    function setGenerado(verUrl) {
        hide(statePendiente);
        show(stateGenerado);
        hide(titlePendiente);
        show(titleGenerado);
        hide(formGenerar);
        show(formConfirmar);
        setFeedback('');

        if (verUrl && verLink) {
            verLink.href = verUrl;
        }
    }

    formGenerar?.addEventListener('submit', async function (event) {
        event.preventDefault();

        const originalHtml = btnGenerar ? btnGenerar.innerHTML : '';
        if (btnGenerar) {
            btnGenerar.disabled = true;
            btnGenerar.innerHTML = '<span class="material-symbols-outlined animate-spin text-[20px]">progress_activity</span> Generando contrato...';
        }

        setFeedback('');

        try {
            const response = await fetch(formGenerar.action, {
                method: 'POST',
                body: new FormData(formGenerar),
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'same-origin'
            });

            const data = await response.json();
            if (!response.ok || !data.success) {
                throw new Error(data.message || 'No se pudo generar el contrato.');
            }

            setGenerado(data.verUrl);

            if (data.verUrl && typeof window.open === 'function') {
                window.open(data.verUrl, '_blank', 'noopener');
            }
        } catch (error) {
            setFeedback(error.message || 'No se pudo generar el contrato.');
        } finally {
            if (btnGenerar) {
                btnGenerar.disabled = false;
                btnGenerar.innerHTML = originalHtml;
            }
        }
    });
})();

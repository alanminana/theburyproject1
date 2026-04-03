/**
 * ordencompra-details.js - Orden de Compra Details page
 *
 * - Auto-dismiss inline alerts
 * - Confirm estado change via shared modal
 * - Bind print action
 * - Show horizontal scroll affordance for products table
 */
(() => {
    window.TheBury?.autoDismissToasts?.(4500);

    const scrollAffordance = window.TheBury?.initHorizontalScrollAffordance?.(
        document.querySelector('[data-oc-scroll]')
    );

    const printBtn = document.getElementById('btn-imprimir-orden');
    printBtn?.addEventListener('click', () => window.print());

    const form = document.getElementById('form-cambiar-estado');
    const sel = document.getElementById('select-estado');
    let confirmedSubmit = false;

    if (form && sel) {
        form.addEventListener('submit', (e) => {
            if (confirmedSubmit) {
                return;
            }

            e.preventDefault();

            const label = sel.options[sel.selectedIndex]?.text?.trim() || '';

            if (typeof window.TheBury?.confirmAction === 'function') {
                window.TheBury.confirmAction(
                    `¿Confirmás cambiar el estado de la orden a "${label}"?`,
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

    window.addEventListener('load', () => scrollAffordance?.update());
})();

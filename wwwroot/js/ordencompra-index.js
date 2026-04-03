/**
 * ordencompra-index.js  –  Órdenes de Compra Index page
 *
 * - Auto-dismiss toast notifications
 * - Initialize the module scroll affordance
 */
(() => {
    if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
        window.TheBury.autoDismissToasts(4000);
    }

    if (window.TheBury && typeof window.TheBury.initHorizontalScrollAffordance === 'function') {
        window.TheBury.initHorizontalScrollAffordance(document.querySelector('[data-oc-scroll]'));
    }
})();

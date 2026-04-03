/**
 * caja-historial.js — Historial de Cierres page
 */
(() => {
    'use strict';

    TheBury.autoDismissToasts();

    if (typeof TheBury.initHorizontalScrollAffordance === 'function') {
        document.querySelectorAll('[data-oc-scroll]').forEach((root) => {
            TheBury.initHorizontalScrollAffordance(root);
        });
    }
})();

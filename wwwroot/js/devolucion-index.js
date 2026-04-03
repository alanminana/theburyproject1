(function () {
    'use strict';

    const devolucion = window.TheBury?.DevolucionModule;
    if (!devolucion) {
        return;
    }

    const state = devolucion.createState();
    devolucion.initSharedUi();
    devolucion.initScrollAffordances(state);
    devolucion.refreshScrollAffordances(state);
})();

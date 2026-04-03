(() => {
    'use strict';

    const seguridad = TheBury.SeguridadModule;
    const state = seguridad.createState();

    seguridad.initSharedUi();
    seguridad.initScrollAffordance(state, { boundAttr: 'seguridadAuditoriaScrollBound' });
    seguridad.refreshScrollAffordance(state);
})();

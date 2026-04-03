(() => {
    'use strict';

    window.TheBury = window.TheBury || {};
    if (window.TheBury.DevolucionModule) {
        return;
    }

    const theBury = window.TheBury;
    const scrollSelector = '[data-oc-scroll]';
    const scrollBoundAttr = 'devolucionScrollBound';
    const scrollRefreshDelayMs = 150;

    function createState() {
        return {
            scrollControllers: [],
            bodyScrollLocked: false,
            previousBodyOverflow: ''
        };
    }

    function initSharedUi() {
        theBury.autoDismissToasts?.();
    }

    function initScrollAffordances(state, root = document) {
        if (!state || typeof theBury.initHorizontalScrollAffordance !== 'function') {
            return;
        }

        root.querySelectorAll(scrollSelector).forEach((element) => {
            if (element.dataset[scrollBoundAttr] === 'true') {
                return;
            }

            const controller = theBury.initHorizontalScrollAffordance(element);
            if (!controller) {
                return;
            }

            element.dataset[scrollBoundAttr] = 'true';
            state.scrollControllers.push(controller);
        });
    }

    function refreshScrollAffordances(state) {
        if (!state?.scrollControllers?.length) {
            return;
        }

        const refresh = () => state.scrollControllers.forEach((controller) => controller.update?.());
        refresh();
        window.requestAnimationFrame(refresh);
        window.setTimeout(refresh, scrollRefreshDelayMs);
    }

    function setBodyScrollLock(state, locked) {
        if (!state) {
            return;
        }

        if (locked) {
            if (!state.bodyScrollLocked) {
                state.previousBodyOverflow = document.body.style.overflow || '';
                state.bodyScrollLocked = true;
            }

            document.body.style.overflow = 'hidden';
            return;
        }

        document.body.style.overflow = state.previousBodyOverflow;
        state.previousBodyOverflow = '';
        state.bodyScrollLocked = false;
    }

    function getVentaId(element) {
        return element?.dataset?.devolucionVentaId || element?.dataset?.ventaId || '';
    }

    window.TheBury.DevolucionModule = {
        createState,
        initSharedUi,
        initScrollAffordances,
        refreshScrollAffordances,
        setBodyScrollLock,
        getVentaId
    };
})();

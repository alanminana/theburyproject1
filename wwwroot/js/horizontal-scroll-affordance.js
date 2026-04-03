/**
 * horizontal-scroll-affordance.js
 *
 * Shared helper for horizontal scroll affordances used by table-like regions.
 */
(() => {
    window.TheBury = window.TheBury || {};

    if (typeof window.TheBury.initHorizontalScrollAffordance === 'function') {
        return;
    }

    const initHorizontalScrollAffordance = function initHorizontalScrollAffordance(rootOrSelector) {
        const root = typeof rootOrSelector === 'string'
            ? document.querySelector(rootOrSelector)
            : (rootOrSelector || document.querySelector('[data-oc-scroll]'));

        if (!root) return null;
        if (root.dataset.scrollAffordanceInitialized === 'true') {
            return {
                update: () => {},
                destroy: () => {}
            };
        }

        const region = root.querySelector('[data-oc-scroll-region]');
        const hint = root.querySelector('[data-oc-scroll-hint]');
        const leftFade = root.querySelector('[data-oc-scroll-fade="left"]');
        const rightFade = root.querySelector('[data-oc-scroll-fade="right"]');

        if (!region) return null;

        root.dataset.scrollAffordanceInitialized = 'true';

        const update = () => {
            const maxScrollLeft = Math.max(0, region.scrollWidth - region.clientWidth);
            const scrollable = maxScrollLeft > 8;
            const atStart = region.scrollLeft <= 8;
            const atEnd = region.scrollLeft >= maxScrollLeft - 8;

            if (hint) hint.hidden = !scrollable;
            if (leftFade) leftFade.hidden = !scrollable || atStart;
            if (rightFade) rightFade.hidden = !scrollable || atEnd;
        };

        const handleResize = () => update();
        const handleLoad = () => update();

        region.addEventListener('scroll', update, { passive: true });
        window.addEventListener('resize', handleResize);
        window.addEventListener('load', handleLoad);

        update();
        requestAnimationFrame(() => requestAnimationFrame(update));
        window.setTimeout(update, 150);

        return {
            update,
            destroy() {
                region.removeEventListener('scroll', update);
                window.removeEventListener('resize', handleResize);
                window.removeEventListener('load', handleLoad);
                delete root.dataset.scrollAffordanceInitialized;
            }
        };
    };

    window.TheBury.initHorizontalScrollAffordance = initHorizontalScrollAffordance;
    window.TheBury.initOrdenCompraScrollAffordance = initHorizontalScrollAffordance;
})();

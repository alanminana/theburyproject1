(() => {
    window.TheBury = window.TheBury || {};

    const BASE_TAB_CLASSES = ['min-h-11', 'px-3', 'py-1.5', 'text-xs', 'font-semibold', 'rounded-xl'];
    const INACTIVE_TAB_CLASSES = ['bg-slate-100', 'dark:bg-slate-800', 'text-slate-600', 'dark:text-slate-300'];
    const ACTIVE_TAB_CLASSES = {
        danger: ['bg-red-500/10', 'text-red-500', 'border', 'border-red-500/20'],
        info: ['bg-blue-500/10', 'text-blue-500', 'border', 'border-blue-500/20']
    };

    const applyTabClasses = (button, isActive) => {
        const tone = button.dataset.dashboardTabTone === 'info' ? 'info' : 'danger';
        const activeClasses = ACTIVE_TAB_CLASSES[tone];
        const allVariantClasses = [...INACTIVE_TAB_CLASSES, ...ACTIVE_TAB_CLASSES.danger, ...ACTIVE_TAB_CLASSES.info];

        button.classList.add(...BASE_TAB_CLASSES);
        button.classList.remove(...allVariantClasses);
        button.classList.add(...(isActive ? activeClasses : INACTIVE_TAB_CLASSES));
        button.setAttribute('aria-selected', isActive ? 'true' : 'false');
        button.tabIndex = isActive ? 0 : -1;
    };

    const setActiveTab = (tabName) => {
        const buttons = Array.from(document.querySelectorAll('[data-dashboard-tab]'));
        const panels = Array.from(document.querySelectorAll('[data-dashboard-tab-panel]'));

        buttons.forEach((button) => {
            applyTabClasses(button, button.dataset.dashboardTab === tabName);
        });

        panels.forEach((panel) => {
            panel.classList.toggle('hidden', panel.dataset.dashboardTabPanel !== tabName);
        });

        // La nota "Mostrando las N…" corresponde a la pestaña activa.
        document.querySelectorAll('[data-dashboard-tab-note]').forEach((note) => {
            note.hidden = note.dataset.dashboardTabNote !== tabName;
        });

        // Sin filas no hay nada que deslizar: el CSS quita el ancho mínimo y la pista de scroll.
        const activePanel = panels.find((panel) => panel.dataset.dashboardTabPanel === tabName);
        const scrollRoot = document.querySelector('[data-dashboard-cuotas-scroll]');
        if (scrollRoot && activePanel) {
            scrollRoot.dataset.empty = activePanel.dataset.empty === 'true' ? 'true' : 'false';
        }
    };

    const initTabs = () => {
        const defaultButton = document.querySelector('[data-dashboard-tab][aria-selected="true"]') || document.querySelector('[data-dashboard-tab="vencidas"]');
        if (defaultButton) {
            setActiveTab(defaultButton.dataset.dashboardTab);
        }
    };

    // Navegación por teclado del patrón ARIA tabs (ERP-UI-STANDARD §5), mismo
    // enfoque que wwwroot/js/venta-index-rework.js: roving tabindex + flechas/Home/End
    // mueven foco y activan la pestaña a la vez.
    const moveTabFocus = (current, direction) => {
        const buttons = Array.from(document.querySelectorAll('[data-dashboard-tab]'));
        if (!buttons.length) return;

        const index = buttons.indexOf(current);
        if (index < 0) return;

        const nextIndex = (index + direction + buttons.length) % buttons.length;
        buttons[nextIndex].focus();
        setActiveTab(buttons[nextIndex].dataset.dashboardTab);
    };

    const handleTabKeydown = (event) => {
        const tabButton = event.target.closest('[data-dashboard-tab]');
        if (!tabButton) return;

        const buttons = Array.from(document.querySelectorAll('[data-dashboard-tab]'));
        if (!buttons.length) return;

        if (event.key === 'ArrowRight') {
            event.preventDefault();
            moveTabFocus(tabButton, 1);
        } else if (event.key === 'ArrowLeft') {
            event.preventDefault();
            moveTabFocus(tabButton, -1);
        } else if (event.key === 'Home') {
            event.preventDefault();
            buttons[0].focus();
            setActiveTab(buttons[0].dataset.dashboardTab);
        } else if (event.key === 'End') {
            event.preventDefault();
            buttons[buttons.length - 1].focus();
            setActiveTab(buttons[buttons.length - 1].dataset.dashboardTab);
        }
    };

    const initScrollAffordances = () => {
        const roots = Array.from(document.querySelectorAll('[data-oc-scroll]'));
        const affordances = roots
            .map((root) => window.TheBury.initHorizontalScrollAffordance?.(root))
            .filter(Boolean);

        const refresh = () => {
            affordances.forEach((affordance) => {
                affordance.update?.();
            });
        };

        refresh();
        window.requestAnimationFrame(() => window.requestAnimationFrame(refresh));
        window.setTimeout(refresh, 150);
    };

    const handleClick = (event) => {
        const tabButton = event.target.closest('[data-dashboard-tab]');
        if (tabButton) {
            event.preventDefault();
            setActiveTab(tabButton.dataset.dashboardTab);
            return;
        }
    };

    const init = () => {
        window.TheBury.autoDismissToasts?.();
        initTabs();
        initScrollAffordances();
        document.addEventListener('click', handleClick);
        document.addEventListener('keydown', handleTabKeydown);
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();

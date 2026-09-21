/**
 * producto-modal-tabs.js
 * Autoridad única de solapas para los modales de Producto (Crear y Editar):
 * mismo patrón ARIA completo (role=tab/tabpanel, aria-selected, aria-controls,
 * aria-labelledby, roving tabindex, flechas/Home/End) para ambos, sin duplicar
 * la lógica en cada modal. Cada modal solo parametriza sus propios data-attribute
 * y clases activas/inactivas (que ya traía).
 */
window.ProductoModalTabs = (function () {
    'use strict';

    function wire(opts) {
        var root = (typeof opts.root === 'string') ? document.querySelector(opts.root) : opts.root;
        if (!root) return null;

        var tabAttr = opts.tabAttr;
        var panelAttr = opts.panelAttr;
        var idPrefix = opts.idPrefix || 'prod-modal';
        var tabs = Array.prototype.slice.call(root.querySelectorAll('[' + tabAttr + ']'));
        var panels = Array.prototype.slice.call(root.querySelectorAll('[' + panelAttr + ']'));
        if (!tabs.length) return null;

        var tablist = tabs[0].closest('[role="tablist"]');

        tabs.forEach(function (tab, i) {
            var name = tab.getAttribute(tabAttr);
            var panel = panels.filter(function (p) { return p.getAttribute(panelAttr) === name; })[0];
            if (!tab.id) tab.id = idPrefix + '-tab-' + name;
            if (panel) {
                if (!panel.id) panel.id = idPrefix + '-panel-' + name;
                tab.setAttribute('aria-controls', panel.id);
                panel.setAttribute('aria-labelledby', tab.id);
            }
            tab.setAttribute('tabindex', i === 0 ? '0' : '-1');
            tab.addEventListener('click', function () { activate(name); });
        });

        function activate(name, activateOpts) {
            activateOpts = activateOpts || {};
            tabs.forEach(function (tab) {
                var active = tab.getAttribute(tabAttr) === name;
                tab.setAttribute('aria-selected', active ? 'true' : 'false');
                tab.setAttribute('tabindex', active ? '0' : '-1');
                tab.classList.toggle('border-primary', active);
                tab.classList.toggle('text-white', active);
                tab.classList.toggle('border-transparent', !active);
                tab.classList.toggle('text-slate-400', !active);
                if (active && activateOpts.focus) tab.focus();
            });
            panels.forEach(function (panel) {
                panel.classList.toggle('hidden', panel.getAttribute(panelAttr) !== name);
            });
            if (typeof opts.onActivate === 'function') opts.onActivate(name);
        }

        if (tablist) {
            tablist.addEventListener('keydown', function (e) {
                var idx = tabs.indexOf(document.activeElement);
                if (idx === -1) return;
                var next = null;
                if (e.key === 'ArrowRight') next = tabs[(idx + 1) % tabs.length];
                else if (e.key === 'ArrowLeft') next = tabs[(idx - 1 + tabs.length) % tabs.length];
                else if (e.key === 'Home') next = tabs[0];
                else if (e.key === 'End') next = tabs[tabs.length - 1];
                if (next) {
                    e.preventDefault();
                    activate(next.getAttribute(tabAttr), { focus: true });
                }
            });
        }

        // Dado un name de campo (name="..." o data-valmsg-for="..."), devuelve la
        // solapa que lo contiene, o null. Usado para saltar a la solapa con el
        // primer error de validación del servidor.
        function findTabForField(fieldName) {
            for (var i = 0; i < panels.length; i++) {
                var panel = panels[i];
                if (panel.querySelector('[name="' + fieldName + '"], [data-valmsg-for="' + fieldName + '"]')) {
                    return panel.getAttribute(panelAttr);
                }
            }
            return null;
        }

        return {
            activate: activate,
            findTabForField: findTabForField,
            first: function () { return tabs.length ? tabs[0].getAttribute(tabAttr) : null; }
        };
    }

    return { wire: wire };
})();

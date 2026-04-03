/* credito-index.js — Tabs, filtros y affordance para Credito/Index */

document.addEventListener('DOMContentLoaded', function () {
    var creditoModule = window.TheBury && window.TheBury.CreditoModule;
    var tabButtons = Array.from(document.querySelectorAll('[data-credito-tab]'));
    var tabTriggers = Array.from(document.querySelectorAll('[data-credito-tab-trigger]'));
    var filterForm = document.querySelector('[data-credito-filter-form]');
    var clearFiltersButton = document.querySelector('[data-credito-clear-filters]');
    var creditosPanel = document.getElementById('tab-creditos');
    var morasPanel = document.getElementById('tab-moras');

    function setTabButtonState(button, isActive) {
        button.setAttribute('aria-pressed', isActive ? 'true' : 'false');
        button.classList.toggle('bg-white', isActive);
        button.classList.toggle('text-slate-900', isActive);
        button.classList.toggle('font-semibold', isActive);
        button.classList.toggle('shadow-sm', isActive);
        button.classList.toggle('dark:bg-slate-900', isActive);
        button.classList.toggle('dark:text-white', isActive);

        button.classList.toggle('text-slate-500', !isActive);
        button.classList.toggle('font-medium', !isActive);
        button.classList.toggle('dark:text-slate-400', !isActive);
    }

    function activateTab(target) {
        tabButtons.forEach(function (button) {
            setTabButtonState(button, button.getAttribute('data-credito-tab') === target);
        });

        if (creditosPanel) {
            creditosPanel.classList.toggle('hidden', target !== 'creditos');
        }

        if (morasPanel) {
            morasPanel.classList.toggle('hidden', target !== 'moras');
        }

        if (creditoModule && typeof creditoModule.refreshScrollAffordance === 'function') {
            creditoModule.refreshScrollAffordance(document);
            window.setTimeout(function () {
                creditoModule.refreshScrollAffordance(document);
            }, 120);
        }
    }

    tabButtons.forEach(function (button) {
        button.addEventListener('click', function () {
            activateTab(button.getAttribute('data-credito-tab'));
        });
    });

    tabTriggers.forEach(function (button) {
        button.addEventListener('click', function () {
            activateTab(button.getAttribute('data-credito-tab-trigger'));
        });
    });

    if (clearFiltersButton && filterForm) {
        clearFiltersButton.addEventListener('click', function () {
            filterForm.querySelectorAll('input, select').forEach(function (element) {
                if (element.type === 'checkbox' || element.type === 'radio') {
                    element.checked = false;
                    return;
                }

                if (element.type !== 'hidden') {
                    element.value = '';
                }
            });

            filterForm.submit();
        });
    }

    if (creditoModule && typeof creditoModule.initSharedUi === 'function') {
        creditoModule.initSharedUi();
    }
    activateTab('creditos');
});

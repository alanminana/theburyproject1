(function () {
    window.TheBury = window.TheBury || {};

    var module = window.TheBury.DocumentoClienteModule || {};
    var deleteConfirmationBound = false;

    function requestSubmit(form) {
        if (typeof form.requestSubmit === 'function') {
            form.requestSubmit();
            return;
        }

        form.submit();
    }

    function confirmDelete(form) {
        var label = form.getAttribute('data-documento-label') || 'este documento';
        var message = '¿Está seguro de eliminar ' + label + '? Esta acción no se puede deshacer.';

        if (window.TheBury && typeof window.TheBury.confirmAction === 'function') {
            window.TheBury.confirmAction(message, function () {
                form.dataset.confirmed = 'true';
                requestSubmit(form);
            });
            return;
        }

        if (window.confirm(message)) {
            form.dataset.confirmed = 'true';
            requestSubmit(form);
        }
    }

    function initAutoDismissToasts() {
        if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
            window.TheBury.autoDismissToasts();
        }
    }

    function initDeleteConfirmation(root) {
        if (deleteConfirmationBound) return;

        (root || document).addEventListener('submit', function (event) {
            var form = event.target.closest('[data-documento-delete-form]');
            if (!form) return;

            if (form.dataset.confirmed === 'true') {
                delete form.dataset.confirmed;
                return;
            }

            event.preventDefault();
            confirmDelete(form);
        });

        deleteConfirmationBound = true;
    }

    function initScrollAffordance(root) {
        if (!window.TheBury || typeof window.TheBury.initHorizontalScrollAffordance !== 'function') return;

        (root || document).querySelectorAll('[data-oc-scroll]').forEach(function (element) {
            window.TheBury.initHorizontalScrollAffordance(element);
        });
    }

    function initClearFilters(root) {
        var scope = root || document;
        var form = scope.querySelector('[data-documento-filter-form]') || scope.getElementById('filterForm');
        var button = scope.querySelector('[data-documento-clear-filters]') || scope.getElementById('btnLimpiar');

        if (!form || !button || button.dataset.documentoBound === 'true') return;

        button.dataset.documentoBound = 'true';
        button.addEventListener('click', function () {
            form.querySelectorAll('select').forEach(function (select) {
                select.value = '';
            });

            form.querySelectorAll('input[type="checkbox"]').forEach(function (checkbox) {
                checkbox.checked = false;
            });

            form.submit();
        });
    }

    function initCommon(root) {
        initAutoDismissToasts();
        initDeleteConfirmation(root);
        initScrollAffordance(root);
    }

    module.initCommon = initCommon;
    module.initIndex = function (root) {
        initCommon(root);
        initClearFilters(root);
    };
    module.initDetails = initCommon;
    module.initUpload = initCommon;

    window.TheBury.DocumentoClienteModule = module;
})();

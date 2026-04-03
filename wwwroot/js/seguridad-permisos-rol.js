(() => {
    'use strict';

    const seguridad = TheBury.SeguridadModule;
    seguridad.initSharedUi();

    const $ = id => document.getElementById(id);
    const normalize = seguridad.normalizeText;
    const currentUrl = seguridad.getCurrentUrl();
    const state = seguridad.createState();

    const searchInput = $('searchPermissionModules');
    const groupFilter = $('permissionGroupFilter');
    const clearFiltersButton = $('clearPermissionFilters');
    const roleSelect = $('permissionRoleSelect');
    const rows = Array.from(document.querySelectorAll('.permission-row'));
    const visibleCount = $('permissionsVisibleCount');
    const selectedCount = $('permissionsSelectedCount');
    const visiblePill = $('permissionsVisiblePill');
    const selectedHeader = $('permissionsSelectedHeader');
    const selectedPill = $('permissionsSelectedPill');
    const emptyFilterState = $('permissionsEmptyFilterState');
    const copyPermissionsContainer = $('copyPermissionsContainer');

    function initPermissionsScrollAffordance() {
        seguridad.initScrollAffordance(state, { boundAttr: 'seguridadPermisosScrollBound' });
    }

    function refreshPermissionsScrollAffordance() {
        seguridad.refreshScrollAffordance(state);
    }

    seguridad.bindEscapeToState(state);

    function getVisibleRows() {
        const search = normalize(searchInput?.value);
        const group = normalize(groupFilter?.value);

        return rows.filter(row => {
            const matchSearch = !search || normalize(row.dataset.search).includes(search);
            const matchGroup = !group || normalize(row.dataset.group) === group;
            return matchSearch && matchGroup;
        });
    }

    function updateSelectedCount() {
        const value = document.querySelectorAll('.permission-checkbox:checked').length.toString();
        if (selectedCount) selectedCount.textContent = value;
        if (selectedHeader) selectedHeader.textContent = value;
        if (selectedPill) selectedPill.textContent = value;
    }

    function applyFilters() {
        if (!rows.length) return;

        const visibleRows = getVisibleRows();
        rows.forEach(row => { row.style.display = 'none'; });
        visibleRows.forEach(row => { row.style.display = ''; });

        const visibleRowsCount = visibleRows.length.toString();
        if (visibleCount) visibleCount.textContent = visibleRowsCount;
        if (visiblePill) visiblePill.textContent = visibleRowsCount;
        emptyFilterState?.classList.toggle('hidden', visibleRows.length > 0);
        refreshPermissionsScrollAffordance();
    }

    function clearFilters() {
        if (searchInput) searchInput.value = '';
        if (groupFilter) groupFilter.value = '';
        applyFilters();
    }

    function applyRoleActionLayout() {
        const wrapper = $('permissionsRoleActions');
        const buttons = Array.from(document.querySelectorAll('.permissions-role-action'));
        const isDesktop = window.matchMedia('(min-width: 640px)').matches;

        if (wrapper) {
            wrapper.style.width = isDesktop ? 'auto' : '100%';
            wrapper.style.flexDirection = isDesktop ? 'row' : 'column';
        }

        buttons.forEach(button => {
            button.style.width = isDesktop ? 'auto' : '100%';
            button.style.justifyContent = 'center';
        });
    }

    function enhanceCopyPermissionsModalLayout() {
        const card = $('copyPermissionsCard');
        const form = $('formCopyPermissions');
        const closeButton = $('closeCopyPermissions');
        const cancelButton = $('cancelCopyPermissions');
        const submitButton = $('submitCopyPermissions');
        const footer = submitButton?.closest('div');
        const body = form?.querySelector('.p-6.space-y-5');
        const isMobile = window.matchMedia('(max-width: 639px)').matches;

        if (card) {
            card.style.maxHeight = 'calc(100vh - 2rem)';
            card.style.display = 'flex';
            card.style.flexDirection = 'column';
        }

        if (form) {
            form.style.display = 'flex';
            form.style.flex = '1 1 auto';
            form.style.minHeight = '0';
            form.style.flexDirection = 'column';
        }

        if (body) {
            body.style.minHeight = '0';
            body.style.overflowY = 'auto';
        }

        closeButton?.classList.add('rounded-lg', 'p-2', 'hover:bg-slate-100', 'dark:hover:bg-slate-800');

        if (footer) {
            footer.style.display = 'flex';
            footer.style.gap = '0.75rem';
            footer.style.flexDirection = isMobile ? 'column-reverse' : 'row';
            footer.style.alignItems = isMobile ? 'stretch' : 'center';
            footer.style.justifyContent = isMobile ? 'flex-start' : 'flex-end';
        }

        [cancelButton, submitButton].filter(Boolean).forEach(button => {
            button.style.width = isMobile ? '100%' : 'auto';
            button.style.justifyContent = 'center';
        });
    }

    function bindCopyPermissionsModal(modal) {
        const form = $('formCopyPermissions');
        const errorBox = $('copyPermissionsErrors');
        const errorList = $('copyPermissionsErrorList');
        const submitButton = $('submitCopyPermissions');
        const defaultSubmitHtml = submitButton?.innerHTML || '';

        form?.addEventListener('submit', event => {
            event.preventDefault();

            if (errorBox && errorList) {
                errorBox.classList.add('hidden');
                errorList.innerHTML = '';
            }

            if (submitButton) {
                submitButton.disabled = true;
                submitButton.innerHTML = '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Copiando...';
            }

            fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            })
                .then(response => response.json())
                .then(result => {
                    if (result.success) {
                        modal.close();
                        seguridad.navigateTo(seguridad.resolveReturnUrl(form, result, currentUrl), currentUrl);
                        return;
                    }

                    if (errorBox && errorList) {
                        errorList.innerHTML = (result.errors || ['No se pudo copiar permisos.'])
                            .map(message => `<p>${message}</p>`)
                            .join('');
                        errorBox.classList.remove('hidden');
                    }

                    if (submitButton) {
                        submitButton.disabled = false;
                        submitButton.innerHTML = defaultSubmitHtml;
                    }
                })
                .catch(() => {
                    if (errorBox && errorList) {
                        errorList.innerHTML = '<p>Error de conexión. Intentá de nuevo.</p>';
                        errorBox.classList.remove('hidden');
                    }

                    if (submitButton) {
                        submitButton.disabled = false;
                        submitButton.innerHTML = defaultSubmitHtml;
                    }
                });
        });
    }

    function openCopyPermissionsModal(button) {
        if (!copyPermissionsContainer) return;

        const params = new URLSearchParams({ roleId: seguridad.getRoleId(button) || '' });
        const returnUrl = seguridad.getReturnUrl(button);
        if (returnUrl) params.set('returnUrl', returnUrl);

        seguridad.openInjectedModal(state, {
            url: `/Seguridad/CopyPermisosRol?${params.toString()}`,
            container: copyPermissionsContainer,
            backdropId: 'copyPermissionsBackdrop',
            cardId: 'copyPermissionsCard',
            closeButtonId: 'closeCopyPermissions',
            cancelButtonId: 'cancelCopyPermissions',
            onClosed: refreshPermissionsScrollAffordance,
            onOpen: modal => {
                enhanceCopyPermissionsModalLayout();
                bindCopyPermissionsModal(modal);
            }
        });
    }

    searchInput?.addEventListener('input', applyFilters);
    groupFilter?.addEventListener('change', applyFilters);
    clearFiltersButton?.addEventListener('click', clearFilters);

    roleSelect?.addEventListener('change', () => {
        roleSelect.form?.requestSubmit();
    });

    document.querySelectorAll('.permission-checkbox').forEach(checkbox => {
        checkbox.addEventListener('change', updateSelectedCount);
    });

    document.querySelectorAll('.btn-select-row').forEach(button => {
        button.addEventListener('click', () => {
            const rowId = button.dataset.rowId;
            document.querySelectorAll(`.permission-checkbox[data-row-id="${rowId}"]`).forEach(checkbox => {
                checkbox.checked = true;
            });
            updateSelectedCount();
        });
    });

    document.querySelectorAll('.btn-clear-row').forEach(button => {
        button.addEventListener('click', () => {
            const rowId = button.dataset.rowId;
            document.querySelectorAll(`.permission-checkbox[data-row-id="${rowId}"]`).forEach(checkbox => {
                checkbox.checked = false;
            });
            updateSelectedCount();
        });
    });

    document.querySelectorAll('.btn-select-column').forEach(button => {
        button.addEventListener('click', () => {
            const columnKey = button.dataset.columnKey;
            document.querySelectorAll(`.permission-checkbox[data-column-key="${columnKey}"]`).forEach(checkbox => {
                checkbox.checked = true;
            });
            updateSelectedCount();
        });
    });

    document.getElementById('btnCopyPermissions')?.addEventListener('click', event => {
        openCopyPermissionsModal(event.currentTarget);
    });

    applyRoleActionLayout();
    initPermissionsScrollAffordance();
    applyFilters();
    updateSelectedCount();
    refreshPermissionsScrollAffordance();
})();

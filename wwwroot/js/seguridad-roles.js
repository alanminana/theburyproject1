(() => {
    'use strict';

    const seguridad = TheBury.SeguridadModule;
    seguridad.initSharedUi();

    const $ = id => document.getElementById(id);
    const normalize = seguridad.normalizeText;
    const currentUrl = seguridad.getCurrentUrl();
    const state = seguridad.createState();

    const searchInput = $('searchRoles');
    const estadoSelect = $('filterEstadoRol');
    const clearFiltersButton = $('clearRolesFilters');
    const clearFiltersEmptyButton = $('clearRolesFiltersEmpty');
    const tableBody = $('rolesTableBody');
    const rows = Array.from(document.querySelectorAll('.rol-row'));
    const visibleCount = $('rolesVisibleCount');
    const emptyFilterState = $('rolesEmptyFilterState');
    const actionFeedback = $('rolesActionFeedback');

    const createContainer = $('createRoleContainer');
    const editContainer = $('editRoleContainer');
    const duplicateContainer = $('duplicateRoleContainer');

    function resolveReturnUrl(form, result) {
        return seguridad.resolveReturnUrl(form, result, currentUrl);
    }

    function navigateTo(url) {
        seguridad.navigateTo(url, currentUrl);
    }

    function showActionFeedback(message, kind = 'error') {
        if (!actionFeedback) return;
        const isError = kind === 'error';
        actionFeedback.textContent = message;
        actionFeedback.className = `rounded-xl border px-4 py-3 text-sm shadow-sm ${isError
            ? 'border-rose-500/20 bg-rose-500/10 text-rose-600 dark:text-rose-400'
            : 'border-emerald-500/20 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'}`;
        actionFeedback.classList.remove('hidden');
        actionFeedback.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function clearActionFeedback() {
        if (!actionFeedback) return;
        actionFeedback.textContent = '';
        actionFeedback.className = 'hidden rounded-xl border px-4 py-3 text-sm shadow-sm';
    }

    function initRolesScrollAffordance() {
        seguridad.initScrollAffordance(state, { boundAttr: 'seguridadRolesScrollBound' });
    }

    function refreshRolesScrollAffordance() {
        seguridad.refreshScrollAffordance(state);
    }

    function applyModalFooterLayout(footer, buttons = []) {
        seguridad.applyResponsiveFooterLayout(footer, buttons);
    }

    seguridad.bindEscapeToState(state);

    function bindAjaxModal(modal, config) {
        const form = $(config.formId);
        const errorBox = $(config.errorBoxId);
        const errorList = $(config.errorListId);
        const submitButton = $(config.submitButtonId);
        const cancelButton = $(config.cancelButtonId);
        const defaultSubmitHtml = submitButton?.innerHTML || '';
        const footer = submitButton?.closest('div');

        applyModalFooterLayout(footer, [cancelButton, submitButton]);

        form?.addEventListener('submit', event => {
            event.preventDefault();
            clearActionFeedback();

            if (errorBox && errorList) {
                errorBox.classList.add('hidden');
                errorList.innerHTML = '';
            }

            if (submitButton) {
                submitButton.disabled = true;
                submitButton.innerHTML = config.loadingHtml;
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
                        navigateTo(resolveReturnUrl(form, result));
                        return;
                    }

                    if (errorBox && errorList) {
                        errorList.innerHTML = (result.errors || ['No se pudo completar la operación.'])
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

    function openInjectedModal({ url, container, backdropId, cardId, closeButtonId, cancelButtonId, bindForm }) {
        if (!container) return;
        clearActionFeedback();

        seguridad.openInjectedModal(state, {
            url,
            container,
            backdropId,
            cardId,
            closeButtonId,
            cancelButtonId,
            onClosed: refreshRolesScrollAffordance,
            onOpen: bindForm,
            onError: () => {
                showActionFeedback('No se pudo abrir el modal solicitado. Intentá nuevamente.');
            }
        });
    }

    function applyFilters() {
        const search = normalize(searchInput?.value);
        const estado = normalize(estadoSelect?.value);
        let visible = 0;

        rows.forEach(row => {
            const matchSearch = !search ||
                normalize(row.dataset.name).includes(search) ||
                normalize(row.dataset.description).includes(search);
            const matchEstado = !estado || normalize(row.dataset.estado) === estado;
            const show = matchSearch && matchEstado;

            row.style.display = show ? '' : 'none';
            if (show) {
                visible++;
                tableBody?.appendChild(row);
            }
        });

        if (visibleCount) visibleCount.textContent = visible.toString();
        emptyFilterState?.classList.toggle('hidden', visible > 0 || rows.length === 0);
        refreshRolesScrollAffordance();
    }

    function clearFilters() {
        if (searchInput) searchInput.value = '';
        if (estadoSelect) estadoSelect.value = '';
        clearActionFeedback();
        applyFilters();
    }

    function openCreateRoleModal(button) {
        const params = new URLSearchParams();
        const returnUrl = seguridad.getReturnUrl(button);
        if (returnUrl) params.set('returnUrl', returnUrl);
        openInjectedModal({
            url: `/Seguridad/CreateRol?${params.toString()}`,
            container: createContainer,
            backdropId: 'createRoleBackdrop',
            cardId: 'createRoleCard',
            closeButtonId: 'closeCreateRole',
            cancelButtonId: 'cancelCreateRole',
            bindForm: modal => bindAjaxModal(modal, {
                formId: 'formCreateRole',
                errorBoxId: 'createRoleErrors',
                errorListId: 'createRoleErrorList',
                submitButtonId: 'submitCreateRole',
                cancelButtonId: 'cancelCreateRole',
                loadingHtml: '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Guardando...'
            })
        });
    }

    function openEditRoleModal(button) {
        const params = new URLSearchParams({ id: seguridad.getRoleId(button) || '' });
        const returnUrl = seguridad.getReturnUrl(button);
        if (returnUrl) params.set('returnUrl', returnUrl);
        openInjectedModal({
            url: `/Seguridad/EditRol?${params.toString()}`,
            container: editContainer,
            backdropId: 'editRoleBackdrop',
            cardId: 'editRoleCard',
            closeButtonId: 'closeEditRole',
            cancelButtonId: 'cancelEditRole',
            bindForm: modal => bindAjaxModal(modal, {
                formId: 'formEditRole',
                errorBoxId: 'editRoleErrors',
                errorListId: 'editRoleErrorList',
                submitButtonId: 'submitEditRole',
                cancelButtonId: 'cancelEditRole',
                loadingHtml: '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Guardando...'
            })
        });
    }

    function openDuplicateRoleModal(button) {
        const params = new URLSearchParams({ id: seguridad.getRoleId(button) || '' });
        const returnUrl = seguridad.getReturnUrl(button);
        if (returnUrl) params.set('returnUrl', returnUrl);
        openInjectedModal({
            url: `/Seguridad/DuplicateRol?${params.toString()}`,
            container: duplicateContainer,
            backdropId: 'duplicateRoleBackdrop',
            cardId: 'duplicateRoleCard',
            closeButtonId: 'closeDuplicateRole',
            cancelButtonId: 'cancelDuplicateRole',
            bindForm: modal => bindAjaxModal(modal, {
                formId: 'formDuplicateRole',
                errorBoxId: 'duplicateRoleErrors',
                errorListId: 'duplicateRoleErrorList',
                submitButtonId: 'submitDuplicateRole',
                cancelButtonId: 'cancelDuplicateRole',
                loadingHtml: '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Duplicando...'
            })
        });
    }

    searchInput?.addEventListener('input', applyFilters);
    estadoSelect?.addEventListener('change', applyFilters);
    clearFiltersButton?.addEventListener('click', clearFilters);
    clearFiltersEmptyButton?.addEventListener('click', clearFilters);

    document.addEventListener('click', event => {
        const createButton = event.target.closest('.btn-open-create-role');
        if (createButton) {
            openCreateRoleModal(createButton);
            return;
        }

        const editButton = event.target.closest('.btn-edit-role');
        if (editButton) {
            openEditRoleModal(editButton);
            return;
        }

        const duplicateButton = event.target.closest('.btn-duplicate-role');
        if (duplicateButton) {
            openDuplicateRoleModal(duplicateButton);
        }
    });

    document.addEventListener('submit', event => {
        const deleteForm = event.target.closest('.js-delete-role-form');
        if (!deleteForm) return;

        event.preventDefault();
        clearActionFeedback();

        const hasUsers = seguridad.getHasUsers(deleteForm);
        const roleName = seguridad.getRoleName(deleteForm) || 'este rol';

        if (hasUsers) {
            showActionFeedback(`No se puede eliminar ${roleName} porque todavía tiene usuarios asignados.`);
            return;
        }

        seguridad.confirmAction(`¿Eliminar el rol ${roleName}? Esta acción no se puede deshacer.`, () => deleteForm.submit());
    });

    initRolesScrollAffordance();
    applyFilters();
    refreshRolesScrollAffordance();
})();

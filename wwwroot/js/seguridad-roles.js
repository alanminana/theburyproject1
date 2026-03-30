(() => {
    'use strict';

    TheBury.autoDismissToasts();

    const searchInput = document.getElementById('searchRoles');
    const estadoSelect = document.getElementById('filterEstadoRol');
    const clearFiltersButton = document.getElementById('clearRolesFilters');
    const clearFiltersEmptyButton = document.getElementById('clearRolesFiltersEmpty');
    const tableBody = document.getElementById('rolesTableBody');
    const rows = Array.from(document.querySelectorAll('.rol-row'));
    const visibleCount = document.getElementById('rolesVisibleCount');
    const emptyFilterState = document.getElementById('rolesEmptyFilterState');

    const createContainer = document.getElementById('createRoleContainer');
    const editContainer = document.getElementById('editRoleContainer');
    const duplicateContainer = document.getElementById('duplicateRoleContainer');

    const normalize = TheBury.normalizeText;

    function applyFilters() {
        if (!rows.length) return;

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

        if (visibleCount) {
            visibleCount.textContent = visible.toString();
        }

        emptyFilterState?.classList.toggle('hidden', visible > 0 || rows.length === 0);
    }

    function clearFilters() {
        if (searchInput) searchInput.value = '';
        if (estadoSelect) estadoSelect.value = '';
        applyFilters();
    }

    searchInput?.addEventListener('input', applyFilters);
    estadoSelect?.addEventListener('change', applyFilters);
    clearFiltersButton?.addEventListener('click', clearFilters);
    clearFiltersEmptyButton?.addEventListener('click', clearFilters);

    function openInjectedModal(url, container, bindModal) {
        if (!container) return;

        fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(response => {
                if (!response.ok) throw new Error(response.status);
                return response.text();
            })
            .then(html => {
                container.innerHTML = html;
                bindModal(container);
            })
            .catch(() => { });
    }

    function animateOpen(backdrop, card) {
        requestAnimationFrame(() => {
            backdrop?.classList.replace('opacity-0', 'opacity-100');
            if (card) {
                card.classList.replace('scale-95', 'scale-100');
                card.classList.replace('opacity-0', 'opacity-100');
            }
        });
    }

    function animateClose(container, backdrop, card) {
        backdrop?.classList.replace('opacity-100', 'opacity-0');
        if (card) {
            card.classList.replace('scale-100', 'scale-95');
            card.classList.replace('opacity-100', 'opacity-0');
        }
        setTimeout(() => { container.innerHTML = ''; }, 300);
    }

    function bindAjaxModal(container, config) {
        const backdrop = document.getElementById(config.backdropId);
        const card = document.getElementById(config.cardId);
        const closeButton = document.getElementById(config.closeButtonId);
        const cancelButton = document.getElementById(config.cancelButtonId);
        const form = document.getElementById(config.formId);
        const errorBox = document.getElementById(config.errorBoxId);
        const errorList = document.getElementById(config.errorListId);
        const submitButton = document.getElementById(config.submitButtonId);
        const defaultSubmitHtml = submitButton?.innerHTML || '';

        const closeModal = () => animateClose(container, backdrop, card);

        closeButton?.addEventListener('click', closeModal);
        cancelButton?.addEventListener('click', closeModal);
        backdrop?.addEventListener('click', closeModal);
        card?.addEventListener('click', event => event.stopPropagation());

        animateOpen(backdrop, card);

        form?.addEventListener('submit', event => {
            event.preventDefault();

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
                        window.location.href = result.redirectUrl || window.location.href;
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

    document.addEventListener('click', event => {
        const createButton = event.target.closest('.btn-open-create-role');
        if (createButton) {
            const params = new URLSearchParams();
            if (createButton.dataset.returnUrl) {
                params.set('returnUrl', createButton.dataset.returnUrl);
            }

            openInjectedModal(`/Seguridad/CreateRol?${params.toString()}`, createContainer, container => {
                bindAjaxModal(container, {
                    backdropId: 'createRoleBackdrop',
                    cardId: 'createRoleCard',
                    closeButtonId: 'closeCreateRole',
                    cancelButtonId: 'cancelCreateRole',
                    formId: 'formCreateRole',
                    errorBoxId: 'createRoleErrors',
                    errorListId: 'createRoleErrorList',
                    submitButtonId: 'submitCreateRole',
                    loadingHtml: '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Guardando...'
                });
            });
            return;
        }

        const editButton = event.target.closest('.btn-edit-role');
        if (editButton) {
            const params = new URLSearchParams({ id: editButton.dataset.roleId || '' });
            if (editButton.dataset.returnUrl) {
                params.set('returnUrl', editButton.dataset.returnUrl);
            }

            openInjectedModal(`/Seguridad/EditRol?${params.toString()}`, editContainer, container => {
                bindAjaxModal(container, {
                    backdropId: 'editRoleBackdrop',
                    cardId: 'editRoleCard',
                    closeButtonId: 'closeEditRole',
                    cancelButtonId: 'cancelEditRole',
                    formId: 'formEditRole',
                    errorBoxId: 'editRoleErrors',
                    errorListId: 'editRoleErrorList',
                    submitButtonId: 'submitEditRole',
                    loadingHtml: '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Guardando...'
                });
            });
            return;
        }

        const duplicateButton = event.target.closest('.btn-duplicate-role');
        if (duplicateButton) {
            const params = new URLSearchParams({ id: duplicateButton.dataset.roleId || '' });
            if (duplicateButton.dataset.returnUrl) {
                params.set('returnUrl', duplicateButton.dataset.returnUrl);
            }

            openInjectedModal(`/Seguridad/DuplicateRol?${params.toString()}`, duplicateContainer, container => {
                bindAjaxModal(container, {
                    backdropId: 'duplicateRoleBackdrop',
                    cardId: 'duplicateRoleCard',
                    closeButtonId: 'closeDuplicateRole',
                    cancelButtonId: 'cancelDuplicateRole',
                    formId: 'formDuplicateRole',
                    errorBoxId: 'duplicateRoleErrors',
                    errorListId: 'duplicateRoleErrorList',
                    submitButtonId: 'submitDuplicateRole',
                    loadingHtml: '<span class="material-symbols-outlined text-sm animate-spin">progress_activity</span> Duplicando...'
                });
            });
        }
    });

    document.addEventListener('submit', event => {
        const deleteForm = event.target.closest('.js-delete-role-form');
        if (!deleteForm) return;

        const hasUsers = deleteForm.dataset.hasUsers === 'true';
        const roleName = deleteForm.dataset.roleName || 'este rol';

        if (hasUsers) {
            event.preventDefault();
            window.alert(`No se puede eliminar ${roleName} porque todavía tiene usuarios asignados.`);
            return;
        }

        if (!window.confirm(`¿Eliminar el rol ${roleName}? Esta acción no se puede deshacer.`)) {
            event.preventDefault();
        }
    });

    applyFilters();
})();

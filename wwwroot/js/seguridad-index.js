(() => {
    'use strict';

    const seguridad = TheBury.SeguridadModule;
    seguridad.initSharedUi();

    const $ = id => document.getElementById(id);
    const normalizeText = seguridad.normalizeText;
    const currentUrl = seguridad.getCurrentUrl();
    const state = seguridad.createState();

    const searchInput = $('searchUsuarios');
    const filterRol = $('filterRol');
    const filterSucursal = $('filterSucursal');
    const filterEstado = $('filterEstado');
    const orderBy = $('orderBy');
    const toggleOrderDir = $('toggleOrderDir');
    const orderDirIcon = $('orderDirIcon');
    const clearBtn = $('clearFilters');
    const clearFiltersEmpty = $('clearFiltersEmpty');
    const rows = Array.from(document.querySelectorAll('.usuario-row'));
    const visibleCount = $('visibleCount');
    const emptyFilterState = $('emptyFilterState');
    const tableBody = $('usuariosTableBody');

    const selectAll = $('selectAll');
    const bulkBar = $('bulkBar');
    const selectedCountEl = $('selectedCount');
    const bulkIds = $('bulkIds');
    const bulkForm = $('bulkForm');
    const bulkAccion = $('bulkAccion');
    const bulkRol = $('bulkRol');
    const bulkSucursalId = $('bulkSucursalId');
    const clearSelection = $('clearSelection');
    const bulkActionButtons = document.querySelectorAll('[data-seguridad-bulk-action]');
    const bulkModalButtons = document.querySelectorAll('[data-seguridad-bulk-modal]');
    const bulkConfigContainer = $('bulkConfigContainer');
    const bulkConfigBackdrop = $('bulkConfigBackdrop');
    const bulkConfigCard = $('bulkConfigCard');
    const bulkConfigTitle = $('bulkConfigTitle');
    const bulkConfigDescription = $('bulkConfigDescription');
    const bulkConfigError = $('bulkConfigError');
    const bulkRoleField = $('bulkRoleField');
    const bulkRoleSelect = $('bulkRoleSelect');
    const bulkSucursalField = $('bulkSucursalField');
    const bulkSucursalSelect = $('bulkSucursalSelect');
    const closeBulkConfig = $('closeBulkConfig');
    const cancelBulkConfig = $('cancelBulkConfig');
    const submitBulkConfig = $('submitBulkConfig');

    let orderDirection = 'asc';
    let currentBulkModalAction = null;

    function resolveReturnUrl(form, result) {
        return seguridad.resolveReturnUrl(form, result, currentUrl);
    }

    function navigateTo(url) {
        seguridad.navigateTo(url, currentUrl);
    }

    function initUsersScrollAffordance() {
        seguridad.initScrollAffordance(state, { boundAttr: 'seguridadScrollBound' });
    }

    function refreshUsersScrollAffordance() {
        seguridad.refreshScrollAffordance(state);
    }

    function applyModalFooterLayout(footer, buttons = []) {
        seguridad.applyResponsiveFooterLayout(footer, buttons);
    }

    function bindModalFrame({ container, backdrop, card, closeButtons = [], onClosed }) {
        return seguridad.bindModalFrame(state, { container, backdrop, card, closeButtons, onClosed });
    }

    seguridad.bindEscapeToState(state);

    function openInjectedModal({ url, container, backdropId, cardId, closeButtonId, cancelButtonId, onOpen }) {
        seguridad.openInjectedModal(state, {
            url,
            container,
            backdropId,
            cardId,
            closeButtonId,
            cancelButtonId,
            onOpen,
            onClosed: refreshUsersScrollAffordance
        });
    }

    function getVisibleRows() {
        const term = normalizeText(searchInput?.value || '');
        const rol = normalizeText(filterRol?.value || '');
        const sucursal = normalizeText(filterSucursal?.value || '');
        const estado = normalizeText(filterEstado?.value || '');
        return rows.filter(row => {
            const rowRoles = (row.dataset.roleValues || '').split('||').map(normalizeText).filter(Boolean);
            return (!term || normalizeText(row.dataset.search).includes(term))
                && (!rol || rowRoles.includes(rol))
                && (!sucursal || normalizeText(row.dataset.sucursalId || '') === sucursal)
                && (!estado || normalizeText(row.dataset.estado) === estado);
        });
    }

    function getSortValue(row, field) {
        switch (field) {
            case 'username': return row.dataset.name || '';
            case 'nombre': return row.dataset.fullname || '';
            case 'email': return row.dataset.email || '';
            case 'rol': return row.dataset.rolPrincipal || '';
            case 'sucursal': return row.dataset.sucursal || '';
            case 'estado': return row.dataset.estado || '';
            case 'ultimoAcceso': return row.dataset.ultimoAcceso || '';
            default: return row.dataset.name || '';
        }
    }

    function sortRows(visibleRows) {
        const field = orderBy?.value || 'username';
        const dir = orderDirection === 'asc' ? 1 : -1;
        return visibleRows.sort((a, b) => {
            const va = getSortValue(a, field);
            const vb = getSortValue(b, field);
            if (field === 'ultimoAcceso') {
                const da = va ? new Date(va).getTime() : 0;
                const db = vb ? new Date(vb).getTime() : 0;
                return (da - db) * dir;
            }
            return normalizeText(va).localeCompare(normalizeText(vb), 'es') * dir;
        });
    }

    function getCheckedIds() {
        return rows.filter(row => row.style.display !== 'none')
            .map(row => row.querySelector('.row-check'))
            .filter(Boolean)
            .filter(checkbox => checkbox.checked)
            .map(checkbox => checkbox.value);
    }

    function updateBulkBar() {
        if (!bulkBar) return;
        const ids = getCheckedIds();
        bulkBar.classList.toggle('hidden', ids.length === 0);
        if (selectedCountEl) selectedCountEl.textContent = ids.length.toString();
        if (bulkIds) bulkIds.value = ids.join(',');

        const visibleChecks = rows.filter(row => row.style.display !== 'none')
            .map(row => row.querySelector('.row-check'))
            .filter(Boolean);
        if (!selectAll) return;
        if (!visibleChecks.length) {
            selectAll.checked = false;
            selectAll.indeterminate = false;
            return;
        }
        selectAll.checked = visibleChecks.every(checkbox => checkbox.checked);
        selectAll.indeterminate = !selectAll.checked && visibleChecks.some(checkbox => checkbox.checked);
    }

    function applyFilters() {
        const visibleRows = sortRows([...getVisibleRows()]);
        rows.forEach(row => { row.style.display = 'none'; });
        visibleRows.forEach(row => {
            row.style.display = '';
            tableBody?.appendChild(row);
        });
        if (visibleCount) visibleCount.textContent = visibleRows.length.toString();
        emptyFilterState?.classList.toggle('hidden', visibleRows.length > 0);
        updateBulkBar();
        refreshUsersScrollAffordance();
    }

    function clearAllFilters() {
        if (searchInput) searchInput.value = '';
        if (filterRol) filterRol.value = '';
        if (filterSucursal) filterSucursal.value = '';
        if (filterEstado) filterEstado.value = '';
        if (orderBy) orderBy.value = 'username';
        orderDirection = 'asc';
        if (orderDirIcon) orderDirIcon.textContent = 'arrow_upward';
        applyFilters();
    }

    function submitBulkAction(action, extra = {}) {
        if (!bulkForm || !bulkAccion || !bulkIds?.value) return;
        bulkAccion.value = action;
        if (bulkRol) bulkRol.value = extra.rol || '';
        if (bulkSucursalId) bulkSucursalId.value = extra.sucursalId || '';
        bulkForm.requestSubmit();
    }

    function clearBulkConfigError() {
        if (!bulkConfigError) return;
        bulkConfigError.textContent = '';
        bulkConfigError.classList.add('hidden');
    }

    function showBulkConfigError(message) {
        if (!bulkConfigError) return;
        bulkConfigError.textContent = message;
        bulkConfigError.classList.remove('hidden');
    }

    let bulkConfigModal = null;

    function closeBulkConfigModal() {
        currentBulkModalAction = null;
        clearBulkConfigError();
        if (bulkRoleSelect) bulkRoleSelect.value = '';
        if (bulkSucursalSelect) bulkSucursalSelect.value = '';
        bulkConfigModal?.close();
    }

    function openBulkConfigModal(action) {
        currentBulkModalAction = action;
        clearBulkConfigError();
        bulkConfigContainer?.classList.remove('hidden');
        if (action === 'asignarRol') {
            if (bulkConfigTitle) bulkConfigTitle.textContent = 'Asignar rol en lote';
            if (bulkConfigDescription) bulkConfigDescription.textContent = 'El rol seleccionado se agregará a todos los usuarios marcados.';
            bulkRoleField?.classList.remove('hidden');
            bulkSucursalField?.classList.add('hidden');
            if (submitBulkConfig) submitBulkConfig.textContent = 'Asignar rol';
        } else {
            if (bulkConfigTitle) bulkConfigTitle.textContent = 'Cambiar sucursal en lote';
            if (bulkConfigDescription) bulkConfigDescription.textContent = 'La sucursal seleccionada se aplicará a todos los usuarios marcados.';
            bulkRoleField?.classList.add('hidden');
            bulkSucursalField?.classList.remove('hidden');
            if (submitBulkConfig) submitBulkConfig.textContent = 'Cambiar sucursal';
        }
        applyModalFooterLayout(submitBulkConfig?.closest('div'), [cancelBulkConfig, submitBulkConfig]);
        bulkConfigModal = bindModalFrame({
            container: bulkConfigContainer,
            backdrop: bulkConfigBackdrop,
            card: bulkConfigCard,
            closeButtons: [closeBulkConfig, cancelBulkConfig],
            onClosed: () => {
                bulkConfigContainer?.classList.add('hidden');
                bulkConfigModal = null;
            }
        });
    }

    function bindCreateModal(modal) {
        const form = $('formCreateUser');
        const errBox = $('createUserErrors');
        const errList = $('createUserErrorList');
        const submitButton = $('submitCreateUser');
        const defaultButtonHtml = submitButton?.innerHTML || '';
        form?.addEventListener('submit', event => {
            event.preventDefault();
            errBox?.classList.add('hidden');
            if (errList) errList.innerHTML = '';
            if (submitButton) {
                submitButton.disabled = true;
                submitButton.innerHTML = '<span class=\"material-symbols-outlined text-sm animate-spin\">progress_activity</span> Guardando...';
            }
            fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(response => response.json())
                .then(result => {
                    if (result.success) {
                        modal.close();
                        navigateTo(resolveReturnUrl(form, result));
                        return;
                    }
                    if (errBox && errList) {
                        errList.innerHTML = (result.errors || ['No se pudo crear el usuario.']).map(message => `<p>${message}</p>`).join('');
                        errBox.classList.remove('hidden');
                    }
                    if (submitButton) {
                        submitButton.disabled = false;
                        submitButton.innerHTML = defaultButtonHtml;
                    }
                })
                .catch(() => {
                    if (errBox && errList) {
                        errList.innerHTML = '<p>Error de conexión. Intentá de nuevo.</p>';
                        errBox.classList.remove('hidden');
                    }
                    if (submitButton) {
                        submitButton.disabled = false;
                        submitButton.innerHTML = defaultButtonHtml;
                    }
                });
        });
    }

    function updateRequirementState(element, met) {
        if (!element) return;
        element.className = `flex items-center gap-2 text-xs ${met ? 'text-green-600 dark:text-green-400' : 'text-slate-500 dark:text-slate-400'}`;
        const icon = element.querySelector('.material-symbols-outlined');
        if (icon) icon.textContent = met ? 'check_circle' : 'radio_button_unchecked';
    }

    function evaluatePasswordPolicy(password, policy) {
        const checks = { length: password.length >= policy.requiredLength };
        if (policy.requireUppercase) checks.uppercase = /[A-Z]/.test(password);
        if (policy.requireLowercase) checks.lowercase = /[a-z]/.test(password);
        if (policy.requireDigit) checks.digit = /\\d/.test(password);
        if (policy.requireSymbol) checks.symbol = /[^A-Za-z0-9]/.test(password);
        return checks;
    }

    function getStrengthFromChecks(checks, password) {
        const values = Object.values(checks);
        const met = values.filter(Boolean).length;
        const total = values.length || 1;
        if (!password) return { level: 0, label: '—', color: 'bg-slate-200 dark:bg-slate-700', textColor: 'text-slate-400' };
        if (met < total) return { level: Math.max(1, Math.round((met / total) * 4)), label: 'Incompleta', color: 'bg-amber-500', textColor: 'text-amber-500' };
        if (password.length >= 10) return { level: 4, label: 'Fuerte', color: 'bg-green-500', textColor: 'text-green-500' };
        return { level: 4, label: 'Válida', color: 'bg-primary', textColor: 'text-primary' };
    }

    function bindChangePassModal(modal) {
        const form = $('formChangePass');
        const errBox = $('changePassErrors');
        const errList = $('changePassErrorList');
        const newPw = $('newPasswordInput');
        const confirmPw = $('confirmPasswordInput');
        const bars = $('strengthBars');
        const strengthLabel = $('strengthLabel');
        const matchIndicator = $('matchIndicator');
        const submitButton = $('submitChangePass');
        const defaultButtonHtml = submitButton?.innerHTML || '';
        const policy = {
            requiredLength: Number(form?.dataset.requiredLength || 6),
            requireUppercase: form?.dataset.requireUppercase === 'true',
            requireLowercase: form?.dataset.requireLowercase === 'true',
            requireDigit: form?.dataset.requireDigit === 'true',
            requireSymbol: form?.dataset.requireSymbol === 'true'
        };
        const requirementElements = {
            length: $('passwordRequirementLength'),
            uppercase: $('passwordRequirementUppercase'),
            lowercase: $('passwordRequirementLowercase'),
            digit: $('passwordRequirementDigit'),
            symbol: $('passwordRequirementSymbol')
        };

        changePassContainer?.querySelectorAll('.toggle-pass-visibility').forEach(button => {
            button.addEventListener('click', () => {
                const target = $(button.dataset.target);
                if (!target) return;
                const isPassword = target.type === 'password';
                target.type = isPassword ? 'text' : 'password';
                const icon = button.querySelector('.material-symbols-outlined');
                if (icon) icon.textContent = isPassword ? 'visibility_off' : 'visibility';
            });
        });

        const hideErrors = () => {
            if (errBox) errBox.classList.add('hidden');
            if (errList) errList.innerHTML = '';
        };

        const showErrors = messages => {
            if (!errBox || !errList) return;
            errList.innerHTML = messages.map(message => `<p>${message}</p>`).join('');
            errBox.classList.remove('hidden');
        };

        function renderPasswordPolicy() {
            const checks = evaluatePasswordPolicy(newPw?.value || '', policy);
            Object.entries(requirementElements).forEach(([key, element]) => {
                if (key in checks) updateRequirementState(element, Boolean(checks[key]));
            });
            if (bars && strengthLabel) {
                const barEls = bars.children;
                const inactive = 'bg-slate-200 dark:bg-slate-700';
                const strength = getStrengthFromChecks(checks, newPw?.value || '');
                for (let index = 0; index < 4; index++) {
                    barEls[index].className = `flex-1 ${index < strength.level ? strength.color : inactive} rounded-full transition-colors`;
                }
                strengthLabel.textContent = `Fortaleza: ${strength.label}`;
                strengthLabel.className = `text-[11px] font-medium ${strength.textColor}`;
            }
            return checks;
        }

        function updateMatch() {
            if (!matchIndicator || !confirmPw || !newPw) return false;
            if (confirmPw.value && newPw.value && confirmPw.value === newPw.value) {
                matchIndicator.classList.remove('hidden');
                matchIndicator.className = 'text-[11px] flex items-center gap-1 text-green-500';
                matchIndicator.innerHTML = '<span class=\"material-symbols-outlined text-[14px]\">check_circle</span> Las contraseñas coinciden';
                return true;
            }
            if (confirmPw.value && newPw.value) {
                matchIndicator.classList.remove('hidden');
                matchIndicator.className = 'text-[11px] flex items-center gap-1 text-red-500';
                matchIndicator.innerHTML = '<span class=\"material-symbols-outlined text-[14px]\">cancel</span> Las contraseñas no coinciden';
                return false;
            }
            matchIndicator.classList.add('hidden');
            return false;
        }

        newPw?.addEventListener('input', () => { renderPasswordPolicy(); updateMatch(); hideErrors(); });
        confirmPw?.addEventListener('input', () => { updateMatch(); hideErrors(); });
        renderPasswordPolicy();
        updateMatch();

        form?.addEventListener('submit', event => {
            event.preventDefault();
            hideErrors();
            const checks = renderPasswordPolicy();
            const errors = [];
            if (!checks.length) errors.push(`La contraseña debe tener al menos ${policy.requiredLength} caracteres.`);
            if (policy.requireUppercase && !checks.uppercase) errors.push('La contraseña debe incluir al menos una letra mayúscula.');
            if (policy.requireLowercase && !checks.lowercase) errors.push('La contraseña debe incluir al menos una letra minúscula.');
            if (policy.requireDigit && !checks.digit) errors.push('La contraseña debe incluir al menos un número.');
            if (policy.requireSymbol && !checks.symbol) errors.push('La contraseña debe incluir al menos un símbolo.');
            if (!updateMatch()) errors.push('La contraseña y la confirmación no coinciden.');
            if (errors.length) {
                showErrors(errors);
                return;
            }
            if (submitButton) {
                submitButton.disabled = true;
                submitButton.innerHTML = '<span class=\"material-symbols-outlined text-sm animate-spin\">progress_activity</span> Guardando...';
            }
            fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(response => response.json())
                .then(result => {
                    if (result.success) {
                        modal.close();
                        navigateTo(resolveReturnUrl(form, result));
                        return;
                    }
                    showErrors(result.errors || ['No se pudo resetear la contraseña.']);
                    if (submitButton) {
                        submitButton.disabled = false;
                        submitButton.innerHTML = defaultButtonHtml;
                    }
                })
                .catch(() => {
                    showErrors(['Error de conexión. Intentá de nuevo.']);
                    if (submitButton) {
                        submitButton.disabled = false;
                        submitButton.innerHTML = defaultButtonHtml;
                    }
                });
        });
    }

    function bindBlockUserModal(modal) {
        const form = $('formBlockUser');
        const errBox = $('blockUserErrors');
        const errList = $('blockUserErrorList');
        const motivoInput = form?.querySelector('[name=\"MotivoBloqueo\"]');
        const bloqueadoHastaInput = form?.querySelector('[name=\"BloqueadoHasta\"]');
        const submitButton = $('submitBlockUser');
        const defaultButtonHtml = submitButton?.innerHTML || '';
        const footer = submitButton?.closest('div');
        const cancelButton = $('cancelBlockUser');

        applyModalFooterLayout(footer, [cancelButton, submitButton]);

        const hideErrors = () => {
            if (errBox) errBox.classList.add('hidden');
            if (errList) errList.innerHTML = '';
        };

        const showErrors = messages => {
            if (!errBox || !errList) return;
            errList.innerHTML = messages.map(message => `<p>${message}</p>`).join('');
            errBox.classList.remove('hidden');
        };

        motivoInput?.addEventListener('input', hideErrors);
        bloqueadoHastaInput?.addEventListener('input', hideErrors);

        form?.addEventListener('submit', event => {
            event.preventDefault();
            hideErrors();
            const errors = [];
            const motivo = (motivoInput?.value || '').trim();
            const bloqueadoHasta = bloqueadoHastaInput?.value || '';
            if (!motivo) errors.push('El motivo de bloqueo es requerido.');
            if (bloqueadoHasta) {
                const bloqueoDate = new Date(bloqueadoHasta);
                if (Number.isNaN(bloqueoDate.getTime()) || bloqueoDate.getTime() <= Date.now()) {
                    errors.push('La fecha de bloqueo debe ser posterior a la fecha actual.');
                }
            }
            if (errors.length) {
                showErrors(errors);
                return;
            }
            if (submitButton) {
                submitButton.disabled = true;
                submitButton.innerHTML = '<span class=\"material-symbols-outlined text-sm animate-spin\">progress_activity</span> Bloqueando...';
            }
            fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(response => response.json())
                .then(result => {
                    if (result.success) {
                        modal.close();
                        navigateTo(resolveReturnUrl(form, result));
                        return;
                    }
                    showErrors(result.errors || ['Error al bloquear el usuario.']);
                    if (submitButton) {
                        submitButton.disabled = false;
                        submitButton.innerHTML = defaultButtonHtml;
                    }
                })
                .catch(() => {
                    showErrors(['Error de conexión. Intentá de nuevo.']);
                    if (submitButton) {
                        submitButton.disabled = false;
                        submitButton.innerHTML = defaultButtonHtml;
                    }
                });
        });
    }

    function openCreateModal() {
        openInjectedModal({ url: '/Usuarios/Create', container: $('createUserContainer'), backdropId: 'createUserBackdrop', cardId: 'createUserCard', closeButtonId: 'closeCreateUser', cancelButtonId: 'cancelCreateUser', onOpen: bindCreateModal });
    }

    function openChangePassModal(userId) {
        openInjectedModal({ url: `/Usuarios/CambiarPassword?id=${encodeURIComponent(userId)}`, container: $('changePassContainer'), backdropId: 'changePassBackdrop', cardId: 'changePassCard', closeButtonId: 'closeChangePass', cancelButtonId: 'cancelChangePass', onOpen: bindChangePassModal });
    }

    function openBlockUserModal(userId, returnUrl) {
        const params = new URLSearchParams({ id: userId });
        if (returnUrl) params.set('returnUrl', returnUrl);
        openInjectedModal({ url: `/Usuarios/Bloquear?${params.toString()}`, container: $('blockUserContainer'), backdropId: 'blockUserBackdrop', cardId: 'blockUserCard', closeButtonId: 'closeBlockUser', cancelButtonId: 'cancelBlockUser', onOpen: bindBlockUserModal });
    }

    searchInput?.addEventListener('input', applyFilters);
    filterRol?.addEventListener('change', applyFilters);
    filterSucursal?.addEventListener('change', applyFilters);
    filterEstado?.addEventListener('change', applyFilters);
    orderBy?.addEventListener('change', applyFilters);
    toggleOrderDir?.addEventListener('click', () => {
        orderDirection = orderDirection === 'asc' ? 'desc' : 'asc';
        if (orderDirIcon) orderDirIcon.textContent = orderDirection === 'asc' ? 'arrow_upward' : 'arrow_downward';
        applyFilters();
    });
    clearBtn?.addEventListener('click', clearAllFilters);
    clearFiltersEmpty?.addEventListener('click', clearAllFilters);

    selectAll?.addEventListener('change', () => {
        const checked = selectAll.checked;
        rows.forEach(row => {
            if (row.style.display !== 'none') {
                const checkbox = row.querySelector('.row-check');
                if (checkbox) checkbox.checked = checked;
            }
        });
        updateBulkBar();
    });
    document.querySelectorAll('.row-check').forEach(checkbox => checkbox.addEventListener('change', updateBulkBar));
    clearSelection?.addEventListener('click', () => {
        document.querySelectorAll('.row-check').forEach(checkbox => { checkbox.checked = false; });
        if (selectAll) {
            selectAll.checked = false;
            selectAll.indeterminate = false;
        }
        updateBulkBar();
    });
    bulkActionButtons.forEach(button => button.addEventListener('click', () => {
        const action = seguridad.getBulkAction(button);
        if (action) submitBulkAction(action);
    }));
    bulkModalButtons.forEach(button => button.addEventListener('click', () => {
        const action = seguridad.getBulkModal(button);
        if (action) openBulkConfigModal(action);
    }));
    submitBulkConfig?.addEventListener('click', () => {
        if (!currentBulkModalAction) return;
        if (currentBulkModalAction === 'asignarRol') {
            const selectedRole = (bulkRoleSelect?.value || '').trim();
            if (!selectedRole) {
                showBulkConfigError('Seleccioná un rol para continuar.');
                return;
            }
            closeBulkConfigModal();
            submitBulkAction(currentBulkModalAction, { rol: selectedRole });
            return;
        }
        const selectedSucursalId = (bulkSucursalSelect?.value || '').trim();
        if (!selectedSucursalId) {
            showBulkConfigError('Seleccioná una sucursal para continuar.');
            return;
        }
        closeBulkConfigModal();
        submitBulkAction(currentBulkModalAction, { sucursalId: selectedSucursalId });
    });

    document.addEventListener('click', event => {
        const createButton = event.target.closest('.btn-open-create-user, #btnNuevoUsuario');
        if (createButton) {
            openCreateModal();
            return;
        }
        const changePasswordButton = event.target.closest('.btn-change-pass');
        if (changePasswordButton) {
            openChangePassModal(seguridad.getUserId(changePasswordButton));
            return;
        }
        const blockUserButton = event.target.closest('.btn-block-user');
        if (blockUserButton) {
            openBlockUserModal(seguridad.getUserId(blockUserButton), seguridad.getReturnUrl(blockUserButton));
        }
    });

    document.addEventListener('submit', event => {
        const unlockForm = event.target.closest('.js-unlock-user-form');
        if (!unlockForm) return;
        event.preventDefault();
        const userName = seguridad.getUserName(unlockForm) || 'este usuario';
        seguridad.confirmAction(`¿Desbloquear a ${userName}?`, () => unlockForm.submit());
    });

    applyFilters();
    initUsersScrollAffordance();
    refreshUsersScrollAffordance();
})();

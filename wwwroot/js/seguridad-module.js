(() => {
    'use strict';

    window.TheBury = window.TheBury || {};
    if (window.TheBury.SeguridadModule) return;

    const theBury = window.TheBury;

    function fallbackNormalizeText(value) {
        return String(value || '').trim().toLowerCase();
    }

    function createState() {
        return {
            activeModalClose: null,
            scrollControllers: []
        };
    }

    function initSharedUi() {
        theBury.autoDismissToasts?.();
    }

    function normalizeText(value) {
        return typeof theBury.normalizeText === 'function'
            ? theBury.normalizeText(value)
            : fallbackNormalizeText(value);
    }

    function getCurrentUrl() {
        return `${location.pathname}${location.search}`;
    }

    function setBodyScrollLock(locked) {
        document.documentElement.classList.toggle('overflow-hidden', locked);
        document.body.classList.toggle('overflow-hidden', locked);
    }

    function confirmAction(message, callback) {
        if (typeof theBury.confirmAction === 'function') {
            theBury.confirmAction(message, callback);
            return;
        }
        callback();
    }

    function resolveReturnUrl(form, result, fallbackUrl) {
        return result?.redirectUrl
            || form?.querySelector('[name="returnUrl"]')?.value
            || fallbackUrl
            || getCurrentUrl();
    }

    function navigateTo(url, fallbackUrl) {
        location.assign(url || fallbackUrl || getCurrentUrl());
    }

    function initScrollAffordance(state, options = {}) {
        const selector = options.selector || '[data-oc-scroll]';
        const boundAttr = options.boundAttr || 'seguridadScrollBound';

        document.querySelectorAll(selector).forEach(root => {
            if (root.dataset[boundAttr] === 'true') return;
            const controller = theBury.initHorizontalScrollAffordance?.(root);
            if (controller) {
                root.dataset[boundAttr] = 'true';
                state.scrollControllers.push(controller);
            }
        });
    }

    function refreshScrollAffordance(state) {
        state.scrollControllers.forEach(controller => controller.update?.());
    }

    function applyResponsiveFooterLayout(footer, buttons = [], options = {}) {
        if (!footer) return;

        const mobile = window.matchMedia('(max-width: 639px)').matches;
        const mobileDirection = options.mobileDirection || 'column';
        const desktopDirection = options.desktopDirection || 'row';
        const mobileJustify = options.mobileJustify || 'flex-start';
        const desktopJustify = options.desktopJustify || 'flex-end';
        const mobileAlign = options.mobileAlign || 'stretch';
        const desktopAlign = options.desktopAlign || 'center';

        footer.style.display = 'flex';
        footer.style.gap = footer.style.gap || '0.75rem';
        footer.style.flexDirection = mobile ? mobileDirection : desktopDirection;
        footer.style.alignItems = mobile ? mobileAlign : desktopAlign;
        footer.style.justifyContent = mobile ? mobileJustify : desktopJustify;

        buttons.filter(Boolean).forEach(button => {
            button.style.width = mobile ? '100%' : 'auto';
            button.style.justifyContent = 'center';
        });
    }

    function bindModalFrame(state, { container, backdrop, card, closeButtons = [], onClosed }) {
        if (!container || !backdrop || !card) return null;
        if (typeof state.activeModalClose === 'function') state.activeModalClose();

        let closed = false;
        const stopPropagation = event => event.stopPropagation();
        const handleClose = () => close();

        function cleanup() {
            closeButtons.filter(Boolean).forEach(button => button.removeEventListener('click', handleClose));
            backdrop.removeEventListener('click', handleClose);
            card.removeEventListener('click', stopPropagation);
        }

        function close() {
            if (closed) return;
            closed = true;
            cleanup();
            if (state.activeModalClose === close) state.activeModalClose = null;
            backdrop.classList.replace('opacity-100', 'opacity-0');
            card.classList.replace('scale-100', 'scale-95');
            card.classList.replace('opacity-100', 'opacity-0');
            setTimeout(() => {
                setBodyScrollLock(false);
                onClosed?.();
            }, 300);
        }

        closeButtons.filter(Boolean).forEach(button => button.addEventListener('click', handleClose));
        backdrop.addEventListener('click', handleClose);
        card.addEventListener('click', stopPropagation);
        state.activeModalClose = close;
        setBodyScrollLock(true);
        requestAnimationFrame(() => {
            backdrop.classList.replace('opacity-0', 'opacity-100');
            card.classList.replace('scale-95', 'scale-100');
            card.classList.replace('opacity-0', 'opacity-100');
        });

        return { close };
    }

    function bindEscapeToState(state) {
        if (state.escapeBound) return;
        state.escapeBound = true;
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && typeof state.activeModalClose === 'function') {
                state.activeModalClose();
            }
        });
    }

    function openInjectedModal(state, {
        url,
        container,
        backdropId,
        cardId,
        closeButtonId,
        cancelButtonId,
        onOpen,
        onClosed,
        onError
    }) {
        if (!container) return;

        fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(response => {
                if (!response.ok) throw new Error(response.status);
                return response.text();
            })
            .then(html => {
                container.innerHTML = html;
                const modal = bindModalFrame(state, {
                    container,
                    backdrop: document.getElementById(backdropId),
                    card: document.getElementById(cardId),
                    closeButtons: [
                        document.getElementById(closeButtonId),
                        document.getElementById(cancelButtonId)
                    ],
                    onClosed: () => {
                        container.innerHTML = '';
                        onClosed?.();
                    }
                });

                if (modal) {
                    onOpen?.(modal);
                }
            })
            .catch(error => {
                onError?.(error);
            });
    }

    function getDatasetValue(element, keys) {
        if (!element) return '';
        for (const key of keys) {
            const value = element.dataset?.[key];
            if (value !== undefined && value !== null && value !== '') {
                return value;
            }
        }
        return '';
    }

    const SeguridadModule = {
        createState,
        initSharedUi,
        normalizeText,
        getCurrentUrl,
        setBodyScrollLock,
        confirmAction,
        resolveReturnUrl,
        navigateTo,
        initScrollAffordance,
        refreshScrollAffordance,
        applyResponsiveFooterLayout,
        bindModalFrame,
        bindEscapeToState,
        openInjectedModal,
        getReturnUrl(element) {
            return getDatasetValue(element, ['seguridadReturnUrl', 'returnUrl']);
        },
        getUserId(element) {
            return getDatasetValue(element, ['seguridadUserId', 'userId']);
        },
        getUserName(element) {
            return getDatasetValue(element, ['seguridadUserName', 'userName']);
        },
        getRoleId(element) {
            return getDatasetValue(element, ['seguridadRoleId', 'roleId']);
        },
        getRoleName(element) {
            return getDatasetValue(element, ['seguridadRoleName', 'roleName']);
        },
        getHasUsers(element) {
            return getDatasetValue(element, ['seguridadHasUsers', 'hasUsers']) === 'true';
        },
        getBulkAction(element) {
            return getDatasetValue(element, ['seguridadBulkAction', 'bulkAction']);
        },
        getBulkModal(element) {
            return getDatasetValue(element, ['seguridadBulkModal', 'bulkModal']);
        }
    };

    theBury.SeguridadModule = SeguridadModule;
})();

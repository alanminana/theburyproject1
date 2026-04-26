/* cliente-modal.js — Modal de alta / edición de clientes (slide-in panel) */
(function () {
    'use strict';

    var modal, backdrop, panel, loadingEl, contentEl;

    /* ── helpers ───────────────────────────────────────────────── */

    function toggleSection(name, forceOpen) {
        var content = document.getElementById('section-' + name);
        var chevron = document.getElementById('chevron-' + name);
        if (!content || !chevron) return;
        var isOpen = !content.classList.contains('hidden');
        var shouldOpen = forceOpen !== undefined ? forceOpen : !isOpen;
        content.classList.toggle('hidden', !shouldOpen);
        chevron.classList.toggle('rotate-180', !shouldOpen);
    }

    function validarMontos() {
        var minInput = document.getElementById('montoMinimo');
        var maxInput = document.getElementById('montoMaximo');
        var errorEl  = document.getElementById('montoError');
        if (!minInput || !maxInput || !errorEl) return true;
        var min = parseFloat(minInput.value);
        var max = parseFloat(maxInput.value);
        if (!isNaN(min) && !isNaN(max) && min > max) {
            errorEl.classList.remove('hidden');
            maxInput.classList.add('border-red-500');
            return false;
        }
        errorEl.classList.add('hidden');
        maxInput.classList.remove('border-red-500');
        return true;
    }

    function showErrors(errors) {
        var box  = document.getElementById('cliente-modal-validation');
        var list = document.getElementById('cliente-modal-error-list');
        if (!box || !list) return;
        list.innerHTML = '';
        errors.forEach(function (msg) {
            var li = document.createElement('li');
            li.textContent = msg;
            list.appendChild(li);
        });
        box.classList.remove('hidden');
        box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function clearErrors() {
        var box = document.getElementById('cliente-modal-validation');
        if (box) box.classList.add('hidden');
    }

    /* ── open / close ───────────────────────────────────────────── */

    function openModal() {
        modal.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
    }

    function showLoading() {
        loadingEl.classList.remove('hidden');
        contentEl.classList.add('hidden');
    }

    function showContent() {
        loadingEl.classList.add('hidden');
        contentEl.classList.remove('hidden');
    }

    function close() {
        modal.classList.add('hidden');
        contentEl.innerHTML = '';
        contentEl.classList.add('hidden');
        loadingEl.classList.remove('hidden');
        document.body.style.overflow = '';
    }

    /* ── load partial ──────────────────────────────────────────── */

    function loadPartial(url) {
        openModal();
        showLoading();

        fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (res) {
                if (!res.ok) throw new Error('Error ' + res.status);
                return res.text();
            })
            .then(function (html) {
                contentEl.innerHTML = html;
                showContent();
                initFormHandlers();
            })
            .catch(function (err) {
                console.error('ClienteModal: error cargando formulario', err);
                close();
            });
    }

    /* ── form handlers (re-init after each partial load) ────────── */

    function initFormHandlers() {
        var cancelBtn = document.getElementById('modal-cliente-cancel');
        if (cancelBtn) {
            cancelBtn.addEventListener('click', close);
        }

        var minInput = document.getElementById('montoMinimo');
        var maxInput = document.getElementById('montoMaximo');
        if (minInput) minInput.addEventListener('input', validarMontos);
        if (maxInput) maxInput.addEventListener('input', validarMontos);

        var form = document.getElementById('cliente-modal-form');
        if (!form) return;

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            clearErrors();

            if (!validarMontos()) {
                toggleSection('credito', true);
                return;
            }

            var submitBtn = form.closest('#modal-cliente-panel')
                .querySelector('button[type="submit"]');
            if (submitBtn) submitBtn.disabled = true;

            var data = new URLSearchParams(new FormData(form));

            fetch(form.action, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: data
            })
            .then(function (res) { return res.json(); })
            .then(function (json) {
                if (json.success) {
                    close();
                    window.location.reload();
                } else {
                    if (submitBtn) submitBtn.disabled = false;
                    showErrors(json.errors || ['Error al guardar el cliente.']);
                }
            })
            .catch(function (err) {
                console.error('ClienteModal: error en submit', err);
                if (submitBtn) submitBtn.disabled = false;
                showErrors(['Error de conexión. Intentá nuevamente.']);
            });
        });
    }

    /* ── public API ─────────────────────────────────────────────── */

    window.ClienteModal = {
        openCreate: function () {
            loadPartial('/Cliente/ModalCreate');
        },
        openEdit: function (id) {
            loadPartial('/Cliente/ModalEdit/' + id);
        },
        close: close
    };

    /* ── init (delegated events, ESC, backdrop) ─────────────────── */

    function init() {
        modal     = document.getElementById('modal-cliente');
        backdrop  = document.getElementById('modal-cliente-backdrop');
        panel     = document.getElementById('modal-cliente-panel');
        loadingEl = document.getElementById('modal-cliente-loading');
        contentEl = document.getElementById('modal-cliente-content');

        if (!modal) return;

        /* Accordion delegated (inside modal content) */
        modal.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-cliente-section]');
            if (!btn) return;
            toggleSection(btn.getAttribute('data-cliente-section'));
        });

        /* Backdrop click */
        backdrop.addEventListener('click', close);

        /* ESC key */
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && !modal.classList.contains('hidden')) {
                close();
            }
        });

        /* Delegated open-create button */
        document.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-cliente-modal-open]');
            if (!btn) return;
            var mode = btn.getAttribute('data-cliente-modal-open');
            if (mode === 'create') {
                window.ClienteModal.openCreate();
            } else if (mode === 'edit') {
                var id = btn.getAttribute('data-cliente-id');
                if (id) window.ClienteModal.openEdit(id);
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();

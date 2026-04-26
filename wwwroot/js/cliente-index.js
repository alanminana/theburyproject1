/**
 * cliente-index.js — Lógica del índice de clientes.
 * Mantiene el modal AJAX de límites, feedback inline y affordance estándar para tablas anchas.
 */
(function () {
    'use strict';

    var modalContainer = document.getElementById('limitesModalContainer');
    var feedbackSlot = document.getElementById('clienteIndexFeedback');
    var scrollRoot = document.querySelector('[data-oc-scroll]');

    function clearFeedback() {
        if (!feedbackSlot) return;
        feedbackSlot.hidden = true;
        feedbackSlot.className = 'hidden rounded-xl border px-4 py-3 text-sm font-semibold';
        feedbackSlot.textContent = '';
    }

    function showFeedback(message, tone) {
        if (!feedbackSlot) return;
        clearFeedback();

        var toneClass = tone === 'success'
            ? 'border-emerald-500/20 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
            : 'border-red-500/20 bg-red-500/10 text-red-500';

        feedbackSlot.hidden = false;
        feedbackSlot.className = 'rounded-xl border px-4 py-3 text-sm font-semibold ' + toneClass;
        feedbackSlot.textContent = message;
    }

    function closeLimitesModal() {
        if (modalContainer) {
            modalContainer.innerHTML = '';
        }
    }

    function closeRowMenus(exceptMenu) {
        document.querySelectorAll('[data-cliente-row-menu][open]').forEach(function (menu) {
            if (menu !== exceptMenu) {
                menu.removeAttribute('open');
            }
        });
    }

    function bindFormSubmit() {
        var form = document.getElementById('formLimites');
        if (!form || form.dataset.bound === 'true') return;

        form.dataset.bound = 'true';
        form.addEventListener('submit', function (e) {
            e.preventDefault();

            var btn = document.getElementById('btnGuardarLimites');
            var errDiv = document.getElementById('limitesErrores');
            var okDiv = document.getElementById('limitesExito');
            var originalButtonHtml = btn ? btn.innerHTML : '';

            if (btn) {
                btn.disabled = true;
                btn.textContent = 'Guardando...';
            }
            if (errDiv) errDiv.classList.add('hidden');
            if (okDiv) okDiv.classList.add('hidden');

            fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                credentials: 'same-origin'
            })
                .then(function (res) {
                    return res.json();
                })
                .then(function (data) {
                    if (data.ok) {
                        if (okDiv) {
                            okDiv.textContent = data.mensaje || 'Configuración guardada.';
                            okDiv.classList.remove('hidden');
                        }
                        window.setTimeout(function () {
                            closeLimitesModal();
                            showFeedback(data.mensaje || 'Configuración guardada.', 'success');
                        }, 1200);
                        return;
                    }

                    if (errDiv && Array.isArray(data.errores) && data.errores.length > 0) {
                        errDiv.innerHTML = data.errores.join('<br>');
                        errDiv.classList.remove('hidden');
                    }
                })
                .catch(function () {
                    if (errDiv) {
                        errDiv.textContent = 'Error de red al guardar.';
                        errDiv.classList.remove('hidden');
                    }
                })
                .finally(function () {
                    if (btn) {
                        btn.disabled = false;
                        btn.innerHTML = originalButtonHtml;
                    }
                });
        });
    }

    function openLimitesModal() {
        clearFeedback();
        if (!modalContainer) return;

        modalContainer.innerHTML = '<div class="fixed inset-0 z-50 flex items-center justify-center bg-background-dark/80 p-4 backdrop-blur-sm"><div class="rounded-lg bg-slate-900/90 px-4 py-3 text-sm font-semibold text-white shadow-lg">Cargando configuración...</div></div>';

        fetch('/Cliente/LimitesPorPuntajePartial', { credentials: 'same-origin' })
            .then(function (res) {
                if (!res.ok) {
                    throw new Error('Error ' + res.status);
                }
                return res.text();
            })
            .then(function (html) {
                modalContainer.innerHTML = html;
                bindFormSubmit();
            })
            .catch(function (err) {
                closeLimitesModal();
                showFeedback('No se pudo cargar la configuración. ' + err.message, 'error');
            });
    }

    document.addEventListener('click', function (event) {
        var rowMenuSummary = event.target.closest('[data-cliente-row-menu] > summary');
        if (rowMenuSummary) {
            closeRowMenus(rowMenuSummary.parentElement);
            return;
        }

        if (!event.target.closest('[data-cliente-row-menu]')) {
            closeRowMenus();
        }

        if (event.target.closest('[data-cliente-open-limites]')) {
            event.preventDefault();
            openLimitesModal();
            return;
        }

        if (event.target.closest('[data-cliente-close-limites-modal]')) {
            event.preventDefault();
            closeLimitesModal();
            return;
        }

        var modal = document.getElementById('limitesModal');
        if (modal && event.target === modal) {
            closeLimitesModal();
        }
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') {
            closeRowMenus();
        }

        if (event.key === 'Escape' && document.getElementById('limitesModal')) {
            closeLimitesModal();
        }
    });

    if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
        window.TheBury.autoDismissToasts();
    }

    if (window.TheBury && typeof window.TheBury.initHorizontalScrollAffordance === 'function' && scrollRoot) {
        window.TheBury.initHorizontalScrollAffordance(scrollRoot);
    }
})();

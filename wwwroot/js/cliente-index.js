/**
 * cliente-index.js — Logic for Cliente Index page: limites modal + interactions.
 */
(function () {
    'use strict';

    // ── Limites Modal ──
    var modalContainer = document.getElementById('limitesModalContainer');

    window.abrirLimitesModal = function () {
        if (!modalContainer) return;
        modalContainer.innerHTML = '<div class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-background-dark/80 backdrop-blur-sm"><div class="text-white text-sm">Cargando...</div></div>';

        fetch('/Cliente/LimitesPorPuntajePartial', { credentials: 'same-origin' })
            .then(function (res) {
                if (!res.ok) throw new Error('Error ' + res.status);
                return res.text();
            })
            .then(function (html) {
                modalContainer.innerHTML = html;
                bindFormSubmit();
            })
            .catch(function (err) {
                modalContainer.innerHTML = '';
                alert('No se pudo cargar la configuración: ' + err.message);
            });
    };

    window.cerrarLimitesModal = function () {
        if (modalContainer) modalContainer.innerHTML = '';
    };

    function bindFormSubmit() {
        var form = document.getElementById('formLimites');
        if (!form) return;

        form.addEventListener('submit', function (e) {
            e.preventDefault();

            var btn = document.getElementById('btnGuardarLimites');
            if (btn) { btn.disabled = true; btn.textContent = 'Guardando...'; }

            var errDiv = document.getElementById('limitesErrores');
            var okDiv = document.getElementById('limitesExito');
            if (errDiv) errDiv.classList.add('hidden');
            if (okDiv) okDiv.classList.add('hidden');

            var formData = new FormData(form);

            fetch(form.action, {
                method: 'POST',
                body: formData,
                credentials: 'same-origin'
            })
            .then(function (res) { return res.json(); })
            .then(function (data) {
                if (data.ok) {
                    if (okDiv) {
                        okDiv.textContent = data.mensaje || 'Configuración guardada.';
                        okDiv.classList.remove('hidden');
                    }
                    setTimeout(function () { cerrarLimitesModal(); }, 1500);
                } else {
                    if (errDiv && data.errores) {
                        errDiv.innerHTML = data.errores.join('<br>');
                        errDiv.classList.remove('hidden');
                    }
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
                    btn.innerHTML = '<span class="material-symbols-outlined text-lg">save</span> Guardar Configuración';
                }
            });
        });
    }
})();

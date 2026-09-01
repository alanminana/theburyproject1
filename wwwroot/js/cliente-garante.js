/* cliente-garante.js — Módulo de garante en Cliente/Details (card "Garante")
   Antes vivía como <script> inline al final de Details_tw.cshtml, sin manejo
   de Escape propio (a diferencia de los otros modales de la pantalla,
   gobernados por cliente-details.js). Extraído a archivo propio siguiendo el
   mismo patrón de autoridad única por modal que cliente-modal.js. */
(function () {
    'use strict';

    var modal, buscador, resultados, selBox, selNombre, selInfo, obsInput, errBox, btnConfirmar;
    var selectedClienteId = null;
    var activoClienteId = null; // clienteId del titular

    function abrirModal(clienteId) {
        activoClienteId = clienteId;
        selectedClienteId = null;
        if (buscador) buscador.value = '';
        if (resultados) resultados.innerHTML = '';
        if (selBox) selBox.classList.add('hidden');
        if (obsInput) obsInput.value = '';
        if (errBox) { errBox.textContent = ''; errBox.classList.add('hidden'); }
        if (btnConfirmar) btnConfirmar.disabled = true;
        modal.classList.remove('hidden');
        buscador && buscador.focus();
    }

    function cerrarModal() {
        modal.classList.add('hidden');
    }

    function seleccionarCandidato(row) {
        selectedClienteId = row.dataset.id;
        selNombre.textContent = row.dataset.nombre;
        selInfo.textContent = 'DNI ' + row.dataset.dni + ' — Puntaje ' + row.dataset.puntaje + '/5 — ' +
            row.dataset.compras + ' compra(s) — ' + row.dataset.garantias + ' garantía(s) activa(s)';
        selBox.classList.remove('hidden');
        resultados.innerHTML = '';
        if (buscador) buscador.value = row.dataset.nombre;
        btnConfirmar.disabled = false;
        errBox.textContent = '';
        errBox.classList.add('hidden');
    }

    function init() {
        modal = document.getElementById('garanteModal');
        if (!modal) return; // no está en el DOM (sin permiso o sin sección)

        buscador = document.getElementById('garanteBuscador');
        resultados = document.getElementById('garanteResultados');
        selBox = document.getElementById('garanteSeleccionado');
        selNombre = document.getElementById('garanteSelNombre');
        selInfo = document.getElementById('garanteSelInfo');
        obsInput = document.getElementById('garanteObservacion');
        errBox = document.getElementById('garanteError');
        btnConfirmar = document.getElementById('garanteConfirmar');

        document.querySelectorAll('[data-garante-asignar],[data-garante-cambiar]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                abrirModal(btn.dataset.clienteId ?? btn.dataset.garanteAsignar ?? btn.dataset.garanteCambiar);
            });
        });

        document.querySelectorAll('[data-garante-modal-cerrar]').forEach(function (btn) {
            btn.addEventListener('click', cerrarModal);
        });

        modal.addEventListener('click', function (e) {
            if (e.target === modal) cerrarModal();
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && !modal.classList.contains('hidden')) cerrarModal();
        });

        // ── Búsqueda AJAX ────────────────────────────────────────────────────
        var debounce = null;
        buscador && buscador.addEventListener('input', function () {
            clearTimeout(debounce);
            debounce = setTimeout(function () {
                var q = buscador.value.trim();
                if (q.length < 2) { resultados.innerHTML = ''; return; }

                resultados.innerHTML = '<div class="text-xs muted-2" style="padding:.4rem">Buscando...</div>';
                fetch('/Cliente/BuscarPosiblesGarantes?q=' + encodeURIComponent(q) + '&clienteId=' + activoClienteId)
                    .then(function (resp) { return resp.json(); })
                    .then(function (data) {
                        if (!data.length) {
                            resultados.innerHTML = '<div class="text-xs muted-2" style="padding:.4rem">Sin resultados.</div>';
                            return;
                        }

                        resultados.innerHTML = data.map(function (d) {
                            return '<div class="doc-row" style="cursor:pointer;padding:.4rem .5rem;border-radius:4px"' +
                                ' data-garante-candidato' +
                                ' data-id="' + d.clienteId + '"' +
                                ' data-nombre="' + d.nombreCompleto + '"' +
                                ' data-dni="' + d.numeroDocumento + '"' +
                                ' data-puntaje="' + d.puntajeCliente + '"' +
                                ' data-compras="' + d.cantidadCompras + '"' +
                                ' data-garantias="' + d.garantiasActivas + '">' +
                                '<div class="flex-1 min-w-0">' +
                                '<div class="text-sm text-white">' + d.nombreCompleto + '</div>' +
                                '<div class="text-xs muted-2">DNI ' + d.numeroDocumento + ' &bull; Puntaje ' + d.puntajeCliente + '/5 &bull; ' +
                                d.cantidadCompras + ' compra(s) &bull; ' + d.garantiasActivas + ' garantia(s)</div>' +
                                '</div></div>';
                        }).join('');

                        resultados.querySelectorAll('[data-garante-candidato]').forEach(function (row) {
                            row.addEventListener('click', function () { seleccionarCandidato(row); });
                        });
                    })
                    .catch(function () {
                        resultados.innerHTML = '<div class="text-xs" style="color:#fb7185;padding:.4rem">Error al buscar.</div>';
                    });
            }, 300);
        });

        // ── Confirmar asignación ─────────────────────────────────────────────
        btnConfirmar && btnConfirmar.addEventListener('click', function () {
            if (!selectedClienteId) return;
            btnConfirmar.disabled = true;

            var token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
            fetch('/Cliente/AsignarGarante', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({
                    clienteId: parseInt(activoClienteId),
                    garanteClienteId: parseInt(selectedClienteId),
                    observacion: obsInput ? obsInput.value.trim() : null
                })
            })
                .then(function (resp) {
                    if (resp.ok) {
                        cerrarModal();
                        window.location.reload();
                        return;
                    }
                    return resp.json().then(function (data) {
                        errBox.textContent = data.error ?? 'Error al asignar garante.';
                        errBox.classList.remove('hidden');
                        btnConfirmar.disabled = false;
                    });
                })
                .catch(function () {
                    errBox.textContent = 'Error de red. Intentá de nuevo.';
                    errBox.classList.remove('hidden');
                    btnConfirmar.disabled = false;
                });
        });

        // ── Remover garante ──────────────────────────────────────────────────
        document.querySelectorAll('[data-garante-remover]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                if (!confirm('¿Confirmás que querés quitar el garante de este cliente?')) return;

                var clienteId = btn.dataset.clienteId;
                var token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
                fetch('/Cliente/RemoverGarante', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify({ clienteId: parseInt(clienteId), motivo: 'Removido desde ficha de cliente.' })
                })
                    .then(function (resp) {
                        if (resp.ok) {
                            window.location.reload();
                            return;
                        }
                        return resp.json().then(function (data) {
                            alert(data.error ?? 'Error al quitar el garante.');
                        });
                    })
                    .catch(function () {
                        alert('Error de red. Intentá de nuevo.');
                    });
            });
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();

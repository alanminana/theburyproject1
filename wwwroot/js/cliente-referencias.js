/* cliente-referencias.js — solapa "Referencias" de Cliente/Details (D-02).
   Alta, edición, verificación y baja lógica por JSON hacia /ClienteReferencia/*;
   el listado se vuelve a pedir como parcial server-rendered tras cada cambio.
   Las referencias son informativas: no modifican aptitud ni límite de crédito. */
(function () {
    'use strict';

    var card, lista, mensaje, modal, form, verifModal, verifForm, bajaModal;
    var abierto = null;   // modal abierto
    var origenFoco = null;
    var bajaId = null;

    function csrf() {
        var meta = document.querySelector('meta[name="csrf-token"]');
        if (meta && meta.getAttribute('content')) return meta.getAttribute('content');
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function clienteId() { return card.getAttribute('data-cliente-id'); }
    function $(id) { return document.getElementById(id); }

    function abrir(m, foco) {
        origenFoco = document.activeElement;
        abierto = m;
        m.classList.remove('hidden');
        if (foco) foco.focus();
    }

    function cerrar() {
        if (!abierto) return;
        abierto.classList.add('hidden');
        abierto = null;
        if (origenFoco && typeof origenFoco.focus === 'function' && document.body.contains(origenFoco)) origenFoco.focus();
    }

    function mostrarMensaje(texto, tipo) {
        mensaje.textContent = texto;
        mensaje.classList.remove('hidden', 'alert-warn', 'alert-ok', 'alert-bad');
        mensaje.classList.add(tipo === 'error' ? 'alert-bad' : 'alert-ok');
    }

    function mostrarError(el, texto) {
        el.textContent = texto;
        el.classList.remove('hidden');
    }

    function limpiarError(el) {
        el.textContent = '';
        el.classList.add('hidden');
    }

    function recargarLista() {
        return fetch('/ClienteReferencia/Lista?clienteId=' + encodeURIComponent(clienteId()), { credentials: 'same-origin' })
            .then(function (r) {
                if (!r.ok) throw new Error('lista');
                return r.text();
            })
            .then(function (html) {
                lista.innerHTML = html;
                enlazarLista();
            })
            .catch(function () {
                mostrarMensaje('No se pudo actualizar el listado. Recargá la página.', 'error');
            });
    }

    function post(url, cuerpo) {
        var opciones = {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf() }
        };
        if (cuerpo !== undefined) opciones.body = JSON.stringify(cuerpo);
        return fetch(url, opciones).then(function (r) {
            return r.json().catch(function () { return {}; }).then(function (data) {
                return { ok: r.ok && data.ok !== false, data: data, status: r.status };
            });
        });
    }

    function textoError(res) {
        if (res.status === 403) return 'No tenés permiso para esta acción.';
        return (res.data && res.data.error) || 'No se pudo completar la operación.';
    }

    function actualizarEmpresa() {
        var tipo = $('refTipo').value;
        $('refEmpresaWrap').classList.toggle('hidden', tipo === '1');
    }

    function abrirFormulario(datos) {
        var editar = !!(datos && datos.id);
        $('referenciaModalTitle').textContent = editar ? 'Editar referencia' : 'Agregar referencia';
        $('refId').value = editar ? datos.id : '';
        $('refRowVersion').value = editar ? (datos.rowversion || '') : '';
        $('refTipo').value = editar ? datos.tipo : '1';
        $('refNombre').value = editar ? (datos.nombre || '') : '';
        $('refRelacion').value = editar ? (datos.relacion || '') : '';
        $('refTelefono').value = editar ? (datos.telefono || '') : '';
        $('refEmail').value = editar ? (datos.email || '') : '';
        $('refEmpresa').value = editar ? (datos.empresa || '') : '';
        $('refObservaciones').value = editar ? (datos.observaciones || '') : '';
        limpiarError($('refError'));
        actualizarEmpresa();
        abrir(modal, $('refNombre'));
    }

    function validarFormulario() {
        if (!$('refNombre').value.trim()) return 'El nombre es obligatorio.';
        if (!$('refTelefono').value.trim()) return 'El teléfono es obligatorio.';
        return null;
    }

    function guardar(e) {
        e.preventDefault();
        var error = validarFormulario();
        if (error) { mostrarError($('refError'), error); return; }

        var id = $('refId').value;
        var cuerpo = {
            tipo: parseInt($('refTipo').value, 10),
            nombre: $('refNombre').value.trim(),
            relacion: $('refRelacion').value.trim() || null,
            telefono: $('refTelefono').value.trim(),
            email: $('refEmail').value.trim() || null,
            empresa: $('refEmpresa').value.trim() || null,
            observaciones: $('refObservaciones').value.trim() || null
        };
        if (id && $('refRowVersion').value) cuerpo.rowVersion = $('refRowVersion').value;

        var url = id
            ? '/ClienteReferencia/Editar?clienteId=' + encodeURIComponent(clienteId()) + '&id=' + encodeURIComponent(id)
            : '/ClienteReferencia/Crear?clienteId=' + encodeURIComponent(clienteId());

        var btn = $('refGuardar');
        btn.disabled = true;
        post(url, cuerpo).then(function (res) {
            btn.disabled = false;
            if (!res.ok) { mostrarError($('refError'), textoError(res)); return; }
            cerrar();
            mostrarMensaje(id ? 'Referencia actualizada.' : 'Referencia agregada.', 'ok');
            return recargarLista();
        }).catch(function () {
            btn.disabled = false;
            mostrarError($('refError'), 'Error de red. Intentá de nuevo.');
        });
    }

    function guardarVerificacion(e) {
        e.preventDefault();
        var id = verifForm.getAttribute('data-id');
        var btn = $('refVerifGuardar');
        btn.disabled = true;
        post('/ClienteReferencia/Verificar?clienteId=' + encodeURIComponent(clienteId()) + '&id=' + encodeURIComponent(id), {
            estado: parseInt($('refVerifEstado').value, 10),
            observaciones: $('refVerifObs').value.trim() || null
        }).then(function (res) {
            btn.disabled = false;
            if (!res.ok) { mostrarError($('refVerifError'), textoError(res)); return; }
            cerrar();
            mostrarMensaje('Verificación registrada.', 'ok');
            return recargarLista();
        }).catch(function () {
            btn.disabled = false;
            mostrarError($('refVerifError'), 'Error de red. Intentá de nuevo.');
        });
    }

    function confirmarBaja() {
        var btn = $('refBajaConfirmar');
        btn.disabled = true;
        post('/ClienteReferencia/Baja?clienteId=' + encodeURIComponent(clienteId()) + '&id=' + encodeURIComponent(bajaId)).then(function (res) {
            btn.disabled = false;
            if (!res.ok) { mostrarError($('refBajaError'), textoError(res)); return; }
            cerrar();
            mostrarMensaje('Referencia dada de baja.', 'ok');
            return recargarLista();
        }).catch(function () {
            btn.disabled = false;
            mostrarError($('refBajaError'), 'Error de red. Intentá de nuevo.');
        });
    }

    function enlazarLista() {
        lista.querySelectorAll('[data-ref-editar]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                abrirFormulario({
                    id: btn.dataset.id, tipo: btn.dataset.tipo, nombre: btn.dataset.nombre, relacion: btn.dataset.relacion,
                    telefono: btn.dataset.telefono, email: btn.dataset.email, empresa: btn.dataset.empresa,
                    observaciones: btn.dataset.observaciones, rowversion: btn.dataset.rowversion
                });
            });
        });

        lista.querySelectorAll('[data-ref-verificar]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                verifForm.setAttribute('data-id', btn.dataset.id);
                $('refVerifNombre').textContent = btn.dataset.nombre;
                $('refVerifEstado').value = btn.dataset.estado;
                $('refVerifObs').value = '';
                limpiarError($('refVerifError'));
                abrir(verifModal, $('refVerifEstado'));
            });
        });

        lista.querySelectorAll('[data-ref-baja]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                bajaId = btn.dataset.id;
                $('referenciaBajaDesc').textContent = 'Se dará de baja la referencia de ' + btn.dataset.nombre +
                    '. Dejará de mostrarse en la ficha; el registro se conserva para auditoría.';
                limpiarError($('refBajaError'));
                abrir(bajaModal, $('refBajaConfirmar'));
            });
        });
    }

    function init() {
        card = $('referencias-card');
        if (!card) return;
        lista = $('referenciasLista');
        mensaje = $('referenciasMensaje');
        modal = $('referenciaModal');
        form = $('referenciaForm');
        verifModal = $('referenciaVerifModal');
        verifForm = $('referenciaVerifForm');
        bajaModal = $('referenciaBajaModal');

        if (!modal) return; // sin permiso de edición: solo lectura, sin acciones

        var agregar = card.querySelector('[data-ref-agregar]');
        if (agregar) agregar.addEventListener('click', function () { abrirFormulario(null); });

        [modal, verifModal, bajaModal].forEach(function (m) {
            m.addEventListener('click', function (e) { if (e.target === m) cerrar(); });
            m.querySelectorAll('[data-ref-cerrar]').forEach(function (b) { b.addEventListener('click', cerrar); });
        });

        document.addEventListener('keydown', function (e) {
            if (!abierto) return;
            if (e.key === 'Escape') { cerrar(); return; }
            if (e.key !== 'Tab') return;
            // Foco contenido dentro del diálogo abierto.
            var f = abierto.querySelectorAll('button:not([disabled]), input:not([type="hidden"]), select, textarea');
            if (!f.length) return;
            var primero = f[0], ultimo = f[f.length - 1];
            if (e.shiftKey && document.activeElement === primero) { e.preventDefault(); ultimo.focus(); }
            else if (!e.shiftKey && document.activeElement === ultimo) { e.preventDefault(); primero.focus(); }
        });

        $('refTipo').addEventListener('change', actualizarEmpresa);
        form.addEventListener('submit', guardar);
        verifForm.addEventListener('submit', guardarVerificacion);
        $('refBajaConfirmar').addEventListener('click', confirmarBaja);
        enlazarLista();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();

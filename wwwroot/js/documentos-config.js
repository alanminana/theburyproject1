/* Configuración documental: editor de plantillas (variables + vista previa) y editor de reglas
   (condiciones estructuradas -> JSON). No contiene reglas de negocio: el servidor valida todo. */
(function () {
    'use strict';

    function token() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function el(tag, attrs, children) {
        var node = document.createElement(tag);
        Object.keys(attrs || {}).forEach(function (k) {
            if (k === 'text') node.textContent = attrs[k];
            else node.setAttribute(k, attrs[k]);
        });
        (children || []).forEach(function (c) { node.appendChild(c); });
        return node;
    }

    // ---------------------------------------------------------------- Plantilla
    function initPlantilla(root) {
        var contenido = document.getElementById('doc-contenido');
        if (!contenido) return;

        root.querySelectorAll('[data-doc-var]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var texto = btn.getAttribute('data-doc-var');
                var ini = contenido.selectionStart || 0;
                var fin = contenido.selectionEnd || 0;
                contenido.value = contenido.value.slice(0, ini) + texto + contenido.value.slice(fin);
                var pos = ini + texto.length;
                contenido.focus();
                contenido.setSelectionRange(pos, pos);
            });
        });

        var out = document.getElementById('doc-preview-out');
        var btnPreview = document.getElementById('doc-preview-btn');
        if (!btnPreview || !out) return;

        function lista(titulo, items, clase) {
            if (!items || !items.length) return null;
            var ul = el('ul', { 'class': 'doc-warn-list' });
            items.forEach(function (i) { ul.appendChild(el('li', { text: i })); });
            return el('div', { 'class': clase || '' }, [el('strong', { text: titulo }), ul]);
        }

        // Resultado final: PDF con el mismo formato de impresión que el documento real, en el modal compartido.
        var btnPdf = document.getElementById('doc-preview-pdf-btn');
        if (btnPdf) {
            btnPdf.addEventListener('click', function () {
                var body = new FormData();
                body.append('contenido', contenido.value);
                body.append('variablesRequeridas', (document.getElementById('doc-requeridas') || {}).value || '');
                body.append('evento', (document.getElementById('doc-prev-evento') || {}).value || '');
                body.append('firmantes', (document.getElementById('FirmantesRequeridos') || {}).value || '');
                ['ventaId', 'pagoCuotaId', 'cotizacionId'].forEach(function (k) {
                    var v = (document.getElementById('doc-prev-' + k) || {}).value;
                    if (v) body.append(k, v);
                });
                body.append('__RequestVerificationToken', token());
                var promesa = fetch(btnPdf.getAttribute('data-url'), { method: 'POST', body: body, credentials: 'same-origin' })
                    .then(function (r) {
                        if (!r.ok) return r.text().then(function (m) { throw new Error(m); });
                        return r.blob();
                    });
                window.docVistaPrevia('Vista previa del editor (sin guardar)', promesa);
            });
        }

        btnPreview.addEventListener('click', function () {
            var body = new FormData();
            body.append('contenido', contenido.value);
            body.append('variablesRequeridas', (document.getElementById('doc-requeridas') || {}).value || '');
            body.append('evento', (document.getElementById('doc-prev-evento') || {}).value || '');
            ['ventaId', 'pagoCuotaId', 'cotizacionId'].forEach(function (k) {
                var v = (document.getElementById('doc-prev-' + k) || {}).value;
                if (v) body.append(k, v);
            });
            body.append('__RequestVerificationToken', token());

            btnPreview.disabled = true;
            out.textContent = 'Generando vista previa...';
            fetch(btnPreview.getAttribute('data-url'), { method: 'POST', body: body, credentials: 'same-origin' })
                .then(function (r) { return r.json(); })
                .then(function (d) {
                    out.textContent = '';
                    if (!d.success) { out.appendChild(el('p', { 'class': 'doc-error', text: d.message || 'No se pudo generar la vista previa.' })); return; }
                    out.appendChild(el('p', { 'class': 'doc-hint', text: d.datosReales ? 'Con datos de la operación indicada.' : 'Con datos de ejemplo.' }));
                    [lista('Errores de la plantilla', d.errores, 'doc-error'),
                     lista('Datos requeridos que faltan (bloquearían la emisión)', d.requeridasFaltantes, 'doc-error'),
                     lista('Variables sin valor', d.vacias),
                     lista('Variables que no existen para este evento', d.noResueltas)].forEach(function (n) { if (n) out.appendChild(n); });
                    out.appendChild(el('div', { 'class': 'doc-preview', text: d.texto }));
                })
                .catch(function () { out.textContent = 'No se pudo generar la vista previa.'; })
                .then(function () { btnPreview.disabled = false; });
        });

        // La vista previa de texto se actualiza sola: al abrir el editor y poco después de dejar de escribir.
        var temporizador = null;
        function refrescar() { if (!btnPreview.disabled) btnPreview.click(); }
        contenido.addEventListener('input', function () { clearTimeout(temporizador); temporizador = setTimeout(refrescar, 900); });
        ['doc-requeridas', 'doc-prev-evento', 'doc-prev-ventaId', 'doc-prev-pagoCuotaId', 'doc-prev-cotizacionId'].forEach(function (id) {
            var n = document.getElementById(id);
            if (n) n.addEventListener('change', refrescar);
        });
        refrescar();
    }

    // ---------------------------------------------------------------- Regla
    var ETIQUETA_OP = {
        igual: 'es igual a', distinto: 'es distinto de', mayor: 'es mayor que', mayorIgual: 'es mayor o igual que',
        menor: 'es menor que', menorIgual: 'es menor o igual que', contiene: 'contiene', en: 'está en la lista',
        noEn: 'no está en la lista', existe: 'tiene valor', verdadero: 'es verdadero', falso: 'es falso'
    };
    // Espejo de OperadoresDocumento.Permitidos (el servidor revalida; esto solo guía la UI).
    var OPS = {
        1: ['igual', 'distinto', 'contiene', 'en', 'noEn', 'existe'],
        2: ['igual', 'distinto', 'mayor', 'mayorIgual', 'menor', 'menorIgual', 'en', 'noEn', 'existe'],
        3: ['verdadero', 'falso', 'existe'],
        4: ['igual', 'distinto', 'mayor', 'mayorIgual', 'menor', 'menorIgual', 'existe']
    };
    var SIN_VALOR = ['existe', 'verdadero', 'falso'];

    function initRegla(root) {
        var catalogEl = document.getElementById('doc-catalog');
        var jsonField = document.getElementById('doc-condicion-json');
        var rowsBox = document.getElementById('doc-cond-rows');
        if (!catalogEl || !jsonField || !rowsBox) return;

        var catalog = JSON.parse(catalogEl.textContent);
        var eventoSel = document.getElementById('doc-evento');
        var grupoSel = document.getElementById('doc-grupo-op');
        var rawBox = document.getElementById('doc-cond-raw');
        var builder = document.getElementById('doc-cond-builder');
        var addBtn = document.getElementById('doc-cond-add');

        function anclaActual() {
            var ev = catalog.eventos.filter(function (e) { return e.codigo === eventoSel.value; })[0];
            return ev ? ev.ancla : 1;
        }

        function camposDisponibles() {
            var ancla = anclaActual();
            return catalog.campos.filter(function (c) { return !c.anclas || c.anclas.indexOf(ancla) >= 0; });
        }

        function campoInfo(ruta) {
            return catalog.campos.filter(function (c) { return c.ruta === ruta; })[0];
        }

        function sync() {
            var filas = Array.prototype.slice.call(rowsBox.querySelectorAll('.doc-cond-row'));
            var condiciones = filas.map(function (fila) {
                var campo = fila.querySelector('[data-f="campo"]').value;
                var operador = fila.querySelector('[data-f="operador"]').value;
                var leaf = { campo: campo, operador: operador };
                if (SIN_VALOR.indexOf(operador) < 0) {
                    var v = fila.querySelector('[data-f="valor"]').value.trim();
                    leaf.valor = (operador === 'en' || operador === 'noEn')
                        ? v.split(',').map(function (x) { return x.trim(); }).filter(Boolean)
                        : v;
                }
                return leaf;
            });
            jsonField.value = condiciones.length === 0 ? '' : JSON.stringify({ op: grupoSel.value, condiciones: condiciones });
            if (rawBox) rawBox.value = jsonField.value;
        }

        function construirValor(fila, info, operador, valorInicial) {
            var cont = fila.querySelector('[data-f="valor-cont"]');
            cont.textContent = '';
            if (SIN_VALOR.indexOf(operador) >= 0) return;
            var input;
            var lista = operador === 'en' || operador === 'noEn';
            if (info.valores && !lista) {
                input = el('select', { 'class': 'doc-select', 'data-f': 'valor', 'aria-label': 'Valor' });
                info.valores.forEach(function (v) { input.appendChild(el('option', { value: v, text: v })); });
            } else {
                var tipo = lista ? 'text' : (info.tipo === 2 ? 'number' : info.tipo === 4 ? 'date' : 'text');
                input = el('input', { 'class': 'doc-input', 'data-f': 'valor', type: tipo, 'aria-label': 'Valor' });
                if (tipo === 'number') input.setAttribute('step', 'any');
                if (lista) input.setAttribute('placeholder', 'valor1, valor2');
            }
            if (valorInicial != null) input.value = Array.isArray(valorInicial) ? valorInicial.join(', ') : String(valorInicial);
            input.addEventListener('input', sync);
            input.addEventListener('change', sync);
            cont.appendChild(input);
        }

        function agregarFila(leaf) {
            var fila = el('div', { 'class': 'doc-cond-row' });
            var campoSel = el('select', { 'class': 'doc-select', 'data-f': 'campo', 'aria-label': 'Campo' });
            camposDisponibles().forEach(function (c) { campoSel.appendChild(el('option', { value: c.ruta, text: c.grupo + ' · ' + c.etiqueta })); });
            var opSel = el('select', { 'class': 'doc-select', 'data-f': 'operador', 'aria-label': 'Operador' });
            var valCont = el('div', { 'data-f': 'valor-cont' });
            var quitar = el('button', { type: 'button', 'class': 'doc-btn doc-btn-danger', 'aria-label': 'Quitar condición', text: 'Quitar' });

            function refrescarOperadores(operadorInicial, valorInicial) {
                var info = campoInfo(campoSel.value);
                opSel.textContent = '';
                OPS[info.tipo].forEach(function (o) { opSel.appendChild(el('option', { value: o, text: ETIQUETA_OP[o] })); });
                if (operadorInicial && OPS[info.tipo].indexOf(operadorInicial) >= 0) opSel.value = operadorInicial;
                construirValor(fila, info, opSel.value, valorInicial);
            }

            campoSel.addEventListener('change', function () { refrescarOperadores(); sync(); });
            opSel.addEventListener('change', function () { construirValor(fila, campoInfo(campoSel.value), opSel.value); sync(); });
            quitar.addEventListener('click', function () { fila.remove(); sync(); });

            fila.appendChild(campoSel); fila.appendChild(opSel); fila.appendChild(valCont); fila.appendChild(quitar);
            rowsBox.appendChild(fila);

            if (leaf && campoInfo(leaf.campo)) {
                campoSel.value = leaf.campo;
                refrescarOperadores(leaf.operador, leaf.valor);
            } else {
                refrescarOperadores();
            }
        }

        function cargarInicial() {
            var inicial = (jsonField.value || '').trim();
            if (!inicial) return;
            var parsed;
            try { parsed = JSON.parse(inicial); } catch (e) { parsed = null; }

            var hojas = null;
            if (parsed && parsed.op && Array.isArray(parsed.condiciones) &&
                parsed.condiciones.every(function (c) { return c && c.campo; })) {
                grupoSel.value = parsed.op;
                hojas = parsed.condiciones;
            } else if (parsed && parsed.campo) {
                hojas = [parsed];
            }

            if (hojas) {
                hojas.forEach(agregarFila);
            } else {
                // Condición con grupos anidados u otro formato: se edita como JSON avanzado.
                builder.hidden = true;
                rawBox.hidden = false;
                document.getElementById('doc-cond-advanced-note').hidden = false;
            }
        }

        addBtn.addEventListener('click', function () { agregarFila(); sync(); });
        grupoSel.addEventListener('change', sync);
        if (rawBox) rawBox.addEventListener('input', function () { jsonField.value = rawBox.value; });
        eventoSel.addEventListener('change', function () {
            // Cambia el evento => cambian los campos disponibles: se reconstruyen las filas.
            var actuales = Array.prototype.slice.call(rowsBox.querySelectorAll('.doc-cond-row')).map(function (f) {
                return { campo: f.querySelector('[data-f="campo"]').value, operador: f.querySelector('[data-f="operador"]').value,
                         valor: (f.querySelector('[data-f="valor"]') || {}).value };
            });
            rowsBox.textContent = '';
            var ancla = anclaActual();
            actuales.forEach(function (a) {
                var info = campoInfo(a.campo);
                if (info && (!info.anclas || info.anclas.indexOf(ancla) >= 0)) agregarFila(a);
            });
            sync();
        });

        cargarInicial();
        sync();
    }

    document.addEventListener('DOMContentLoaded', function () {
        var root = document.querySelector('[data-doc-page]');
        if (!root) return;
        var page = root.getAttribute('data-doc-page');
        if (page === 'plantilla') initPlantilla(root);
        if (page === 'regla') initRegla(root);
    });
}());

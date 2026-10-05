/* Modal de vista previa del documento final (PDF con el mismo formato de impresión). Compartido por todas las
   pantallas de configuración documental. Botones: <button data-doc-preview-url="..." data-doc-preview-title="...">.
   También expone window.docVistaPrevia(titulo, fuente) para el editor, donde fuente es una URL o una Promise<Blob>. */
(function () {
    'use strict';

    var dlg = document.getElementById('doc-preview-modal');
    if (!dlg) return;
    var frame = dlg.querySelector('iframe');
    var titulo = dlg.querySelector('[data-doc-modal-title]');
    var estado = dlg.querySelector('[data-doc-modal-status]');
    var abrir = dlg.querySelector('[data-doc-modal-open]');
    var blobUrl = null;

    function limpiar() {
        if (blobUrl) { URL.revokeObjectURL(blobUrl); blobUrl = null; }
        frame.removeAttribute('src');
    }

    function mostrar(url) {
        estado.hidden = true;
        frame.hidden = false;
        frame.src = url;
        abrir.href = url;
        abrir.hidden = false;
    }

    function error(msg) {
        frame.hidden = true;
        abrir.hidden = true;
        estado.hidden = false;
        estado.textContent = msg || 'No se pudo generar la vista previa.';
        estado.classList.add('doc-error');
    }

    function abrirModal(t, fuente) {
        limpiar();
        titulo.textContent = t || 'Vista previa';
        estado.classList.remove('doc-error');
        estado.hidden = false;
        estado.textContent = 'Generando vista previa...';
        frame.hidden = true;
        abrir.hidden = true;
        if (!dlg.open) dlg.showModal();

        var promesa = typeof fuente === 'string'
            ? fetch(fuente, { credentials: 'same-origin' }).then(function (r) {
                if (!r.ok) return r.text().then(function (m) { throw new Error(m); });
                return r.blob();
            })
            : fuente;

        promesa.then(function (blob) {
            blobUrl = URL.createObjectURL(blob);
            mostrar(blobUrl);
        }).catch(function (e) { error(e && e.message && e.message.length < 300 ? e.message : null); });
    }

    window.docVistaPrevia = abrirModal;

    document.addEventListener('click', function (ev) {
        var btn = ev.target.closest('[data-doc-preview-url]');
        if (!btn) return;
        ev.preventDefault();
        abrirModal(btn.getAttribute('data-doc-preview-title'), btn.getAttribute('data-doc-preview-url'));
    });

    dlg.querySelectorAll('[data-doc-modal-close]').forEach(function (b) { b.addEventListener('click', function () { dlg.close(); }); });
    dlg.addEventListener('click', function (ev) { if (ev.target === dlg) dlg.close(); });
    dlg.addEventListener('close', limpiar);
}());

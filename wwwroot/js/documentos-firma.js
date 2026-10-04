/* Firma manuscrita opcional en el panel de documentos: dibuja en un <canvas> (mouse, touch o lápiz) y, al enviar el
   formulario, deja la imagen PNG en el campo oculto `firmaImagen`. El servidor valida que sea un PNG acotado. */
(function () {
    'use strict';

    function iniciar(pad) {
        if (pad.dataset.firmaListo === '1') return;
        pad.dataset.firmaListo = '1';

        var canvas = pad.querySelector('canvas');
        var campo = pad.querySelector('input[name="firmaImagen"]');
        var limpiar = pad.querySelector('[data-firma-limpiar]');
        var form = pad.closest('form');
        if (!canvas || !campo || !form) return;

        var ctx = canvas.getContext('2d');
        var dibujando = false;
        var hayTrazo = false;

        function reiniciar() {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            ctx.fillStyle = '#ffffff';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.lineWidth = 3;
            ctx.lineCap = 'round';
            ctx.lineJoin = 'round';
            ctx.strokeStyle = '#111111';
            hayTrazo = false;
            campo.value = '';
        }

        function punto(e) {
            var r = canvas.getBoundingClientRect();
            return {
                x: (e.clientX - r.left) * (canvas.width / r.width),
                y: (e.clientY - r.top) * (canvas.height / r.height)
            };
        }

        canvas.addEventListener('pointerdown', function (e) {
            dibujando = true;
            canvas.setPointerCapture(e.pointerId);
            var p = punto(e);
            ctx.beginPath();
            ctx.moveTo(p.x, p.y);
            ctx.lineTo(p.x + 0.01, p.y + 0.01);
            ctx.stroke();
            hayTrazo = true;
            e.preventDefault();
        });
        canvas.addEventListener('pointermove', function (e) {
            if (!dibujando) return;
            var p = punto(e);
            ctx.lineTo(p.x, p.y);
            ctx.stroke();
            e.preventDefault();
        });
        ['pointerup', 'pointercancel', 'pointerleave'].forEach(function (ev) {
            canvas.addEventListener(ev, function () { dibujando = false; });
        });

        if (limpiar) limpiar.addEventListener('click', reiniciar);

        form.addEventListener('submit', function () {
            campo.value = hayTrazo ? canvas.toDataURL('image/png') : '';
        });

        reiniciar();
    }

    function iniciarTodos() {
        document.querySelectorAll('[data-firma-pad]').forEach(iniciar);
    }

    document.addEventListener('DOMContentLoaded', iniciarTodos);
    // El pad vive dentro de <details>: se inicializa también al abrirlo.
    document.addEventListener('toggle', iniciarTodos, true);
}());

// H-17 (auditoría funcional): mensajes por defecto de jQuery Validate en español (los de reglas sin ErrorMessage propio
// llegaban en inglés, p. ej. «Please enter a value greater than or equal to 0»). No toca los mensajes definidos en el servidor.
(function ($) {
    'use strict';
    if (!$ || !$.validator || !$.validator.messages) return;
    $.extend($.validator.messages, {
        required: 'Este campo es obligatorio.',
        remote: 'Corregí este campo.',
        email: 'Ingresá un correo electrónico válido.',
        url: 'Ingresá una URL válida.',
        date: 'Ingresá una fecha válida.',
        dateISO: 'Ingresá una fecha válida (AAAA-MM-DD).',
        number: 'Ingresá un número válido.',
        digits: 'Ingresá solo dígitos.',
        equalTo: 'Los valores no coinciden.',
        maxlength: $.validator.format('No puede superar {0} caracteres.'),
        minlength: $.validator.format('Debe tener al menos {0} caracteres.'),
        rangelength: $.validator.format('Ingresá un valor de entre {0} y {1} caracteres.'),
        range: $.validator.format('Ingresá un valor entre {0} y {1}.'),
        max: $.validator.format('Ingresá un valor menor o igual a {0}.'),
        min: $.validator.format('Ingresá un valor mayor o igual a {0}.'),
        step: $.validator.format('Ingresá un múltiplo de {0}.')
    });
})(window.jQuery);

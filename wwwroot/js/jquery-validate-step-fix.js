// Corrige el metodo "step" de jQuery Validate: la implementacion original rechaza
// valores validos comparando la cantidad de decimales del string (ej. "10.0000" con
// step="0.01" se rechaza por tener 4 decimales en vez de 2), en lugar de verificar si
// el valor es realmente multiplo del step. Afecta a cualquier campo number con step
// alimentado por un decimal(8,4) de SQL Server (TasaMensual, GastosAdministrativos, etc).
(function ($) {
    if (!$ || !$.validator) {
        return;
    }

    function countDecimals(num) {
        var match = ("" + num).match(/(?:\.(\d+))?$/);
        return match && match[1] ? match[1].length : 0;
    }

    $.validator.methods.step = function (value, element, param) {
        if (this.optional(element)) {
            return true;
        }

        var val = parseFloat(value);
        var step = parseFloat(param);

        if (isNaN(val) || isNaN(step) || step <= 0) {
            return true;
        }

        var factor = Math.pow(10, Math.max(countDecimals(step), countDecimals(val)));
        var stepInt = Math.round(step * factor);

        if (stepInt === 0) {
            return true;
        }

        return Math.round(val * factor) % stepInt === 0;
    };
})(window.jQuery);

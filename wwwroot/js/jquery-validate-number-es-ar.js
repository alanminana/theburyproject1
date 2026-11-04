// jQuery Validate valida "number" con formato anglosajón (punto decimal) y rechaza "10,00" o "1.234,56", que es como el
// servidor (es-AR) muestra los decimales precargados en los formularios de edición; "range", "min" y "max" comparan el
// texto crudo y fallan igual. DecimalModelBinder ya acepta coma o punto en el servidor, así que acá se acepta lo mismo:
// la validación real del monto sigue en [MontoArgentino] y en los rangos del servidor.
(function ($) {
    if (!$ || !$.validator) {
        return;
    }

    // Formato original de jQuery Validate: "1234.56" o "1,234.56".
    var anglosajon = /^(?:-?\d+|-?\d{1,3}(?:,\d{3})+)?(?:\.\d+)?$/;
    // es-AR: "1234,56" o "1.234,56" (la coma decimal es obligatoria; sin ella rige el formato anterior).
    var esAr = /^-?(?:\d+|\d{1,3}(?:\.\d{3})+),\d+$/;

    // Valor numérico de un texto ya validado como número; NaN si no lo es.
    function aNumero(value) {
        var texto = ('' + value).trim();
        if (esAr.test(texto)) {
            return parseFloat(texto.replace(/\./g, '').replace(',', '.'));
        }
        if (anglosajon.test(texto) && texto !== '') {
            return parseFloat(texto.replace(/,/g, ''));
        }
        return NaN;
    }

    var methods = $.validator.methods;
    var minOriginal = methods.min;
    var maxOriginal = methods.max;
    var rangeOriginal = methods.range;

    methods.number = function (value, element) {
        return this.optional(element) || anglosajon.test(value) || esAr.test(value);
    };

    // Con un valor en formato es-AR se compara el número real; el resto sigue en los métodos originales.
    methods.min = function (value, element, param) {
        return esAr.test(('' + value).trim()) ? this.optional(element) || aNumero(value) >= param : minOriginal.call(this, value, element, param);
    };
    methods.max = function (value, element, param) {
        return esAr.test(('' + value).trim()) ? this.optional(element) || aNumero(value) <= param : maxOriginal.call(this, value, element, param);
    };
    methods.range = function (value, element, param) {
        if (!esAr.test(('' + value).trim())) {
            return rangeOriginal.call(this, value, element, param);
        }
        var n = aNumero(value);
        return this.optional(element) || (n >= param[0] && n <= param[1]);
    };
})(window.jQuery);

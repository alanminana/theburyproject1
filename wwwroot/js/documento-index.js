document.addEventListener('DOMContentLoaded', function () {
    var btnLimpiar = document.getElementById('btnLimpiar');
    if (btnLimpiar) {
        btnLimpiar.addEventListener('click', function () {
            var form = document.getElementById('filterForm');
            if (!form) return;
            form.querySelectorAll('select').forEach(function (s) { s.value = ''; });
            form.querySelectorAll('input[type="checkbox"]').forEach(function (c) { c.checked = false; });
            form.submit();
        });
    }
});

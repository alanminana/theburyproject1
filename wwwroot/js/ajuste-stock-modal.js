/**
 * ajuste-stock-modal.js
 * Modal "Registrar ajuste de stock" (_AjusteStockModal.cshtml), reusado en
 * MovimientoStock/Index_tw, MovimientoStock/Kardex_tw y Producto/FichaInventario en
 * reemplazo de la navegación de página completa a MovimientoStock/Create_tw.
 *
 * El submit es un POST real de formulario (no AJAX) — este script solo abre/cierra el
 * modal y actualiza la ayuda contextual según el tipo de movimiento elegido.
 */
(() => {
    const modal = document.getElementById('modal-ajuste-stock');
    if (!modal) return;

    let _openTrigger = null;

    function open(trigger) {
        _openTrigger = (trigger instanceof Element) ? trigger : null;
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
        setTimeout(() => {
            var firstField = modal.querySelector('#ajuste-stock-producto, #ajuste-stock-tipo');
            if (firstField) firstField.focus();
        }, 50);
    }

    function close() {
        var trigger = _openTrigger;
        _openTrigger = null;
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        document.body.style.overflow = '';
        if (trigger) trigger.focus();
    }

    document.addEventListener('click', (e) => {
        if (e.target.closest('[data-ajuste-stock-open]')) {
            open(e.target.closest('[data-ajuste-stock-open]'));
            return;
        }
        if (e.target.closest('[data-ajuste-stock-close]')) {
            close();
        }
    });

    document.addEventListener('keydown', (e) => {
        if (modal.classList.contains('hidden')) return;
        if (e.key === 'Escape') close();
    });

    // ── Ayuda contextual por tipo de movimiento ──
    const selectTipo = document.getElementById('ajuste-stock-tipo');
    const hints = {
        'Entrada': document.getElementById('ajuste-stock-hint-entrada'),
        'Salida': document.getElementById('ajuste-stock-hint-salida'),
        'Ajuste': document.getElementById('ajuste-stock-hint-ajuste')
    };

    function updateHint() {
        const val = selectTipo?.options[selectTipo.selectedIndex]?.text;
        Object.values(hints).forEach(h => h?.classList.add('hidden'));
        if (val && hints[val]) hints[val].classList.remove('hidden');
    }

    selectTipo?.addEventListener('change', updateHint);
    updateHint();
})();

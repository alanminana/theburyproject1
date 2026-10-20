// @ts-check
/**
 * VENTA-CREATE-ACCESIBILIDAD — nombre accesible de los controles de /Venta/Create.
 *
 * El nombre se lee del árbol de accesibilidad REAL del navegador (CDP Accessibility.getPartialAXTree),
 * es decir lo que consumen los lectores de pantalla; no se busca `aria-label` en el código fuente.
 * Los paneles condicionales (cheque, crédito, excepción documental, modales) están `hidden`
 * hasta que el flujo los abre: para auditarlos se muestran con CSS inyectado y se calcula el nombre
 * ya renderizado. Los controles `type=hidden` no son controles de usuario y no se auditan.
 */
const { test, expect } = require('playwright/test');
const { searchAndSelectClient, CTA_PRIMARIO_VISIBLE, addTwoDistinctProducts } = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });

const SELECTOR_CONTROLES = 'input:not([type="hidden"]), select, textarea';

/** Controles con nombre accesible esperado (id → patrón): defectos documentados en el informe del PROMPT 44. */
const NOMBRES_ESPERADOS = {
    'txt-cantidad': /cantidad/i,
    'select-producto-unidad': /unidad f[ií]sica/i,
    'txt-descuento-item': /descuento/i,
    'txt-num-cheque': /n[uú]mero.*cheque/i,
    'txt-banco-cheque': /banco/i,
    'txt-titular-cheque': /titular/i,
    'txt-cuit-cheque': /cuit/i,
    'txt-fecha-emision-cheque': /emisi[oó]n/i,
    'txt-fecha-vencimiento-cheque': /vencimiento/i,
    'txt-monto-cheque': /monto/i,
    'txt-excepcion-documental': /motivo/i,
    'filtro-categoria': /categor[ií]a/i,
    'filtro-marca': /marca/i,
    'filtro-precio-min': /m[ií]nimo/i,
    'filtro-precio-max': /m[aá]ximo/i,
    'input-buscar-producto': /buscar/i,
    'txt-producto-seleccionado': /producto seleccionado/i,
    'select-tipo-documento': /tipo de documento/i,
    'plan-excepcion-motivo': /motivo/i,
    'confirmModalNote': /observaci[oó]n|nota/i,
};

/**
 * Devuelve {id, name, role} de cada control, tomado del árbol de accesibilidad del navegador.
 * @param {import('playwright/test').Page} page
 */
async function nombresDelNavegador(page, { mostrarOcultos = true } = {}) {
    if (mostrarOcultos) {
        // `.hidden` de Tailwind v4 vive en un @layer: un <style> inyectado no lo pisa. Se muestran los
        // paneles quitando la clase/atributo en el DOM (solo en esta página de prueba, sin tocar el servidor).
        await page.evaluate(() => {
            document.querySelectorAll('.hidden, [hidden], .invisible').forEach((el) => {
                el.classList.remove('hidden', 'invisible');
                el.removeAttribute('hidden');
            });
            document.querySelectorAll('[style*="display: none"]').forEach((el) => {
                /** @type {HTMLElement} */ (el).style.display = '';
            });
            document.querySelectorAll('details').forEach((d) => (d.open = true));
            // Con Cotizar activo el CSS oculta todo el workspace (Cliente..Revisión).
            document.querySelectorAll('[data-paso-activo]').forEach((el) => el.removeAttribute('data-paso-activo'));
        });
    }
    const total = await page.evaluate(({ sel, mostrar }) => {
        let n = 0;
        document.querySelectorAll(sel).forEach((el) => {
            el.setAttribute('data-a11y-probe', String(n++));
            if (!mostrar) return;
            // Modales/paneles ocultos por otros mecanismos (display:none propio del componente).
            for (let a = /** @type {HTMLElement|null} */ (el); a; a = a.parentElement) {
                if (getComputedStyle(a).display === 'none') a.style.setProperty('display', 'block', 'important');
                if (getComputedStyle(a).visibility === 'hidden') a.style.setProperty('visibility', 'visible', 'important');
                a.removeAttribute('aria-hidden');
                a.removeAttribute('inert');
            }
        });
        return n;
    }, { sel: SELECTOR_CONTROLES, mostrar: mostrarOcultos });

    const cdp = await page.context().newCDPSession(page);
    await cdp.send('DOM.enable');
    await cdp.send('Accessibility.enable');
    const { root } = await cdp.send('DOM.getDocument', { depth: 0 });
    const { nodeIds } = await cdp.send('DOM.querySelectorAll', { nodeId: root.nodeId, selector: '[data-a11y-probe]' });
    expect(nodeIds.length).toBe(total);

    const resultado = [];
    for (const nodeId of nodeIds) {
        const { attributes } = await cdp.send('DOM.getAttributes', { nodeId });
        /** @type {Record<string,string>} */
        const attrs = {};
        for (let i = 0; i < attributes.length; i += 2) attrs[attributes[i]] = attributes[i + 1];
        const { nodes } = await cdp.send('Accessibility.getPartialAXTree', { nodeId, fetchRelatives: false });
        const ax = nodes[0];
        resultado.push({
            id: attrs.id || attrs.name || '(sin id)',
            type: attrs.type || '',
            name: ((ax && ax.name && ax.name.value) || '').trim(),
            // Origen del nombre calculado por el navegador: relatedElement (<label>), attribute (aria-*),
            // placeholder/title (fallbacks frágiles: el placeholder desaparece al escribir).
            origen: (((ax && ax.name && ax.name.sources) || []).find(
                (/** @type {any} */ src) => src.value && src.value.value && !src.superseded) || {}).type || '',
            atributo: (((ax && ax.name && ax.name.sources) || []).find(
                (/** @type {any} */ src) => src.value && src.value.value && !src.superseded) || {}).attribute || '',
            role: (ax && ax.role && ax.role.value) || '',
        });
    }
    await cdp.detach();
    return resultado;
}

async function abrirCreate(page) {
    await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
    await expect(page.locator('#venta-form')).toBeVisible();
}

test.describe('Venta/Create — nombre accesible de los controles (árbol de accesibilidad real)', () => {
    test('ningún control de usuario queda sin nombre accesible', async ({ page }) => {
        await abrirCreate(page);
        const controles = await nombresDelNavegador(page);
        const sinNombre = controles.filter((c) => !c.name).map((c) => `${c.id} [${c.role || c.type}]`);
        const soloPlaceholder = controles
            .filter((c) => c.name && (c.atributo === 'placeholder' || c.atributo === 'title'))
            .map((c) => `${c.id} (${c.atributo})`);
        console.log(`controles auditados: ${controles.length}; sin nombre: ${sinNombre.length} -> ${sinNombre.join(', ')}`);
        console.log(`nombre solo por placeholder/title: ${soloPlaceholder.length} -> ${soloPlaceholder.join(', ')}`);
        expect(sinNombre, `Controles sin nombre accesible: ${sinNombre.join(', ')}`).toEqual([]);
        expect(soloPlaceholder, `Controles cuyo nombre es solo placeholder/title: ${soloPlaceholder.join(', ')}`).toEqual([]);
    });

    test('los controles documentados exponen un nombre descriptivo y específico', async ({ page }) => {
        await abrirCreate(page);
        const controles = await nombresDelNavegador(page);
        const porId = new Map(controles.map((c) => [c.id, c]));
        for (const [id, patron] of Object.entries(NOMBRES_ESPERADOS)) {
            const c = porId.get(id);
            expect(c, `El control #${id} debe existir en /Venta/Create`).toBeTruthy();
            expect(c && c.name, `nombre de #${id}`).toMatch(patron);
        }
    });

    test('cada <label for> apunta a un control existente y no hay ids duplicados', async ({ page }) => {
        await abrirCreate(page);
        const { duplicados, huerfanas } = await page.evaluate(() => {
            /** @type {Map<string, number>} */
            const ids = new Map();
            document.querySelectorAll('[id]').forEach((el) => ids.set(el.id, (ids.get(el.id) || 0) + 1));
            const duplicados = [...ids].filter(([, n]) => n > 1).map(([id]) => id);
            const huerfanas = [...document.querySelectorAll('label[for]')]
                .filter((l) => {
                    const t = document.getElementById(l.getAttribute('for') || '');
                    return !t || !t.matches('input,select,textarea,button,meter,output,progress');
                })
                .map((l) => l.getAttribute('for'));
            return { duplicados, huerfanas };
        });
        expect(duplicados, `IDs duplicados: ${duplicados.join(', ')}`).toEqual([]);
        expect(huerfanas, `<label for> sin control: ${huerfanas.join(', ')}`).toEqual([]);
    });

    test('la etiqueta de cada control documentado pertenece al propio control (clic en la etiqueta lo enfoca)', async ({ page }) => {
        await abrirCreate(page);
        await page.evaluate(() => {
            document.querySelectorAll('.hidden, [hidden]').forEach((el) => {
                el.classList.remove('hidden');
                el.removeAttribute('hidden');
            });
            document.querySelectorAll('details').forEach((d) => (d.open = true));
            document.querySelectorAll('[data-paso-activo]').forEach((el) => el.removeAttribute('data-paso-activo'));
        });
        for (const id of Object.keys(NOMBRES_ESPERADOS)) {
            // Controles cuya etiqueta es visualmente oculta (sr-only) no son clicables: se validan por nombre.
            const etiqueta = page.locator(`label[for="${id}"]`);
            if ((await etiqueta.count()) !== 1) continue;
            const caja = await etiqueta.boundingBox();
            if (!caja || caja.width < 2 || caja.height < 2) continue;
            await etiqueta.evaluate((el) => /** @type {HTMLElement} */ (el).click());
            await expect(page.locator(`#${id}`), `la etiqueta de #${id} enfoca su control`).toBeFocused();
        }
        // Y las del defecto original deben existir con <label for> explícito.
        for (const id of ['txt-cantidad', 'txt-descuento-item', 'txt-monto-cheque', 'txt-excepcion-documental', 'select-producto-unidad']) {
            await expect(page.locator(`label[for="${id}"]`), `#${id} debe tener <label for>`).toHaveCount(1);
        }
    });

    test('el alta de una línea funciona con teclado y con los controles ubicados por su nombre accesible', async ({ page }) => {
        await abrirCreate(page);
        const cliente = await searchAndSelectClient(page);
        test.skip(!cliente, 'El entorno no expone cliente de QA.');
        await page.locator(CTA_PRIMARIO_VISIBLE).first().click();
        await expect(page.locator('#step-panel-productos')).toBeVisible();

        // Los filtros se ubican por rol + nombre (no por id): lo que usaría un lector de pantalla.
        await expect(page.getByRole('combobox', { name: 'Filtrar por categoría' })).toBeVisible();
        await expect(page.getByRole('combobox', { name: 'Filtrar por marca' })).toBeVisible();
        await expect(page.getByRole('spinbutton', { name: 'Precio mínimo' })).toBeVisible();
        await expect(page.getByRole('spinbutton', { name: 'Precio máximo' })).toBeVisible();

        const buscador = page.getByRole('textbox', { name: /Buscar producto/ });
        await buscador.fill('an');
        const primero = page.locator('#dropdown-productos [data-id]').first();
        await expect(primero).toBeVisible();
        await primero.click();
        await expect(page.locator('#panel-agregar-producto')).toBeVisible();
        await expect(page.getByRole('textbox', { name: /Producto Seleccionado/i })).not.toHaveValue('');

        const precio = Number(await page.locator('#hdn-producto-precio').inputValue());
        expect(precio).toBeGreaterThan(0);

        // Teclado: Tab desde Cantidad llega a Desc. %; Espacio sobre "Agregar" agrega la línea.
        const cantidad = page.getByRole('spinbutton', { name: /^Cantidad/ });
        const descuento = page.getByRole('spinbutton', { name: /^Desc\./ });
        await cantidad.focus();
        await page.keyboard.press('Control+A');
        await page.keyboard.type('2');
        await page.keyboard.press('Tab');
        await expect(descuento).toBeFocused();
        await page.keyboard.press('Control+A');
        await page.keyboard.type('10');
        await page.keyboard.press('Tab');
        const agregar = page.getByRole('button', { name: /Agregar/ });
        await expect(agregar).toBeFocused();
        // Espacio activa un <button>; Enter dentro del formulario lo toma el wizard para avanzar de paso.
        await page.keyboard.press('Space');

        await expect(page.locator('#tbody-detalles tr')).toHaveCount(1);
        await expect(page.locator('#tbody-detalles [data-quantity-input]')).toHaveValue('2');
        // Valores sin cambios respecto de la regla comercial: subtotal = precio × cantidad × (1 − desc%).
        const subtotal = (await page.locator('#tbody-detalles [data-line-subtotal]').first().textContent()) ?? '';
        const digitos = subtotal.replace(/[^0-9,]/g, '').replace(',', '.');
        expect(Number(digitos)).toBeCloseTo(precio * 2 * 0.9, 0);
    });

    test('los controles dinámicos de las líneas agregadas conservan su nombre accesible', async ({ page }) => {
        await abrirCreate(page);
        const cliente = await searchAndSelectClient(page);
        test.skip(!cliente, 'El entorno no expone cliente de QA.');
        await page.locator(CTA_PRIMARIO_VISIBLE).first().click();
        await expect(page.locator('#step-panel-productos')).toBeVisible();
        const productos = await addTwoDistinctProducts(page);
        test.skip(!productos, 'El entorno no expone dos productos distintos con stock.');
        await expect(page.locator('#tbody-detalles tr')).toHaveCount(2);

        // Controles que genera el JS por fila (cantidad, unidad física, armado, envío por producto).
        // El panel de envío está oculto hasta activarlo: se muestra para leer su nombre ya renderizado.
        const controles = await nombresDelNavegador(page);
        const dinamicos = controles.filter((c) => /^(detalle-|envio-producto-)/.test(c.id));
        expect(dinamicos.filter((c) => /^detalle-cantidad-/.test(c.id))).toHaveLength(2);
        expect(dinamicos.filter((c) => /^envio-producto-/.test(c.id))).toHaveLength(2);
        const sinNombre = dinamicos.filter((c) => !c.name).map((c) => c.id);
        expect(sinNombre, `controles dinámicos sin nombre: ${sinNombre.join(', ')}`).toEqual([]);
        // Cada fila tiene un nombre propio (incluye el producto), no uno genérico repetido.
        const cantidades = dinamicos.filter((c) => /^detalle-cantidad-/.test(c.id)).map((c) => c.name);
        expect(new Set(cantidades).size).toBe(cantidades.length);
    });
});

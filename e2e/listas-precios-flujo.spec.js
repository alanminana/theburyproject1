// @ts-check
/**
 * D-01 / AC-01-10: listas de precios por la UI real: alta con validaciones, predeterminada unica, edicion, precio manual por
 * producto y lista (con margen minimo), historial y baja (la predeterminada no se puede dar de baja).
 *
 * El spec crea su propia categoria, marca y producto (AJAX del catalogo) con codigos unicos: no modifica datos existentes.
 * Las listas creadas llevan un codigo unico de la corrida; la predeterminada que se crea se deja marcada (el spec asume una
 * instancia descartable/QA, no la base real).
 *
 * Variables: E2E_USER / E2E_PASS (via e2e/global-setup.js); el usuario necesita precios.*, productos.create y categorias/marcas.create.
 * E2E_SHOTS_DIR (opcional): carpeta donde guardar capturas por viewport.
 */
const { test, expect } = require('playwright/test');
const path = require('path');
const fs = require('fs');

test.use({ storageState: 'e2e/.auth/user.json' });

test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route =>
        route.fulfill({ status: 200, contentType: 'text/css', body: '' }));
    await page.route('**/fonts.gstatic.com/**', route =>
        route.fulfill({ status: 204, body: '' }));
});

const SUFIJO = Date.now().toString(36).toUpperCase().slice(-6);
const SHOTS = process.env.E2E_SHOTS_DIR;

function trackFailures(page) {
    const consoleErrors = [];
    const serverErrors = [];
    page.on('console', message => {
        if (message.type() !== 'error') return;
        const url = message.location().url || '';
        if (url.includes('fonts.g')) return;
        consoleErrors.push(message.text());
    });
    page.on('response', response => {
        if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`);
    });
    return { consoleErrors, serverErrors };
}

function expectNoFailures(failures) {
    // Un 400 esperado de una validacion no es un error de consola de la app: solo se filtran los de recursos bloqueados.
    const reales = failures.consoleErrors.filter(m => !/Failed to load resource/.test(m));
    expect(reales, `errores de consola: ${reales.join(' | ')}`).toHaveLength(0);
    expect(failures.serverErrors, `HTTP 5xx: ${failures.serverErrors.join(' | ')}`).toHaveLength(0);
}

async function expectSinDesbordeHorizontal(page) {
    const desborde = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(desborde, 'la pagina no debe tener scroll horizontal').toBeLessThanOrEqual(1);
}

async function captura(page, nombre) {
    if (!SHOTS) return;
    fs.mkdirSync(SHOTS, { recursive: true });
    await page.screenshot({ path: path.join(SHOTS, `${nombre}.png`), fullPage: true });
}

async function postJson(page, url, form) {
    await page.goto('/ListaPrecio', { waitUntil: 'domcontentloaded' });
    const token = await page.locator('meta[name="csrf-token"]').getAttribute('content');
    const resp = await page.request.post(url, { headers: { RequestVerificationToken: token || '' }, form });
    expect(resp.ok(), `${url} respondio ${resp.status()}`).toBeTruthy();
    const json = await resp.json();
    expect(json.success, `${url}: ${JSON.stringify(json)}`).toBeTruthy();
    return json.entity;
}

async function enviar(page, nombreBoton) {
    await Promise.all([
        page.waitForResponse(r => r.request().method() === 'POST'),
        page.getByRole('button', { name: nombreBoton }).click(),
    ]);
    await page.waitForLoadState('domcontentloaded');
}

async function llenarLista(page, datos) {
    for (const [campo, valor] of Object.entries(datos)) {
        const control = page.locator(`#${campo}`);
        if (campo === 'Tipo' || campo === 'ReglaRedondeo') await control.selectOption(String(valor));
        else await control.fill(String(valor));
    }
}

test.describe.serial('Listas de precios (D-01)', () => {
    const codigoA = `E2EA${SUFIJO}`;
    const codigoB = `E2EB${SUFIJO}`;
    let producto;

    test('prepara categoria, marca y producto propios', async ({ page }) => {
        const cat = await postJson(page, '/Categoria/CreateAjax', { Codigo: `CAT${SUFIJO}`, Nombre: `Categoria E2E ${SUFIJO}`, Activo: 'true' });
        const marca = await postJson(page, '/Marca/CreateAjax', { Codigo: `MAR${SUFIJO}`, Nombre: `Marca E2E ${SUFIJO}`, Activo: 'true' });
        producto = await postJson(page, '/Producto/CreateAjax', {
            Codigo: `PRD${SUFIJO}`, Nombre: `Producto E2E ${SUFIJO}`, CategoriaId: String(cat.id), MarcaId: String(marca.id),
            PrecioCompra: '1000', PrecioVenta: '1500', PorcentajeIVA: '21', StockActual: '10', StockMinimo: '1', Activo: 'true',
        });
        expect(producto.id).toBeGreaterThan(0);
    });

    test('el menu lleva a Listas de precios y se crean dos listas con sus validaciones', async ({ page }) => {
        const failures = trackFailures(page);
        // Precios es una pestaña de Inventario (ya no tiene entrada propia en el menú lateral).
        await page.goto('/Catalogo', { waitUntil: 'domcontentloaded' });
        await page.locator('nav[aria-label="Secciones de Inventario"] a[data-modulo-tab="precios"]').click();
        await expect(page).toHaveURL(/\/ListaPrecio$/);
        await expect(page.getByRole('heading', { name: 'Listas de precios', level: 2 })).toBeVisible();

        // Alta con margen minimo mayor que el margen: se rechaza y se conserva lo escrito.
        await page.getByRole('link', { name: 'Nueva lista' }).first().click();
        await llenarLista(page, { Nombre: `Contado ${SUFIJO}`, Codigo: codigoA, MargenPorcentaje: '10', MargenMinimoPorcentaje: '30' });
        await enviar(page, 'Crear lista');
        await expect(page.locator('#lp-error')).toContainText('El margen mínimo no puede ser mayor que el margen de la lista.');
        await expect(page.locator('#Nombre')).toHaveValue(`Contado ${SUFIJO}`);

        // Corregido, queda predeterminada.
        await llenarLista(page, { MargenPorcentaje: '50', MargenMinimoPorcentaje: '20', ReglaRedondeo: 'ninguno' });
        await page.getByRole('checkbox', { name: /Lista predeterminada/ }).check();
        await enviar(page, 'Crear lista');
        await expect(page).toHaveURL(/\/ListaPrecio$/);
        const filaA = page.locator('tr', { hasText: codigoA });
        await expect(filaA).toContainText('Predeterminada');
        await expect(filaA.locator('form[data-lp-baja]')).toHaveCount(0);

        // Codigo repetido (en minusculas).
        await page.getByRole('link', { name: 'Nueva lista' }).first().click();
        await llenarLista(page, { Nombre: 'Duplicada', Codigo: codigoA.toLowerCase() });
        await enviar(page, 'Crear lista');
        await expect(page.locator('#lp-error')).toContainText('Ya existe una lista con el código');

        // Segunda lista, y al marcarla predeterminada la primera deja de serlo.
        await llenarLista(page, { Nombre: `Tarjeta ${SUFIJO}`, Codigo: codigoB, Tipo: 'Tarjeta', CantidadCuotas: '6', RecargoPorcentaje: '15' });
        await enviar(page, 'Crear lista');
        await expect(page.locator('tr', { hasText: codigoB })).toContainText('Tarjeta');
        await expect(page.locator('tr', { hasText: codigoB })).not.toContainText('Predeterminada');
        expectNoFailures(failures);
    });

    test('editar conserva todos los campos y designar otra predeterminada mueve la marca', async ({ page }) => {
        const failures = trackFailures(page);
        await page.goto('/ListaPrecio', { waitUntil: 'domcontentloaded' });
        await page.locator('tr', { hasText: codigoB }).getByRole('link', { name: /Editar/ }).click();
        await expect(page.locator('#CantidadCuotas')).toHaveValue('6');
        await llenarLista(page, { MargenPorcentaje: '40', MargenMinimoPorcentaje: '25', ReglaRedondeo: 'decena' });
        await page.getByRole('checkbox', { name: /Lista predeterminada/ }).check();
        await enviar(page, 'Guardar cambios');
        await expect(page).toHaveURL(/\/ListaPrecio$/);
        await expect(page.locator('tr', { hasText: codigoB })).toContainText('Predeterminada');
        await expect(page.locator('tr', { hasText: codigoA })).not.toContainText('Predeterminada');

        // Reabierta: los campos que antes se perdian al editar quedaron.
        await page.locator('tr', { hasText: codigoB }).getByRole('link', { name: /Editar/ }).click();
        await expect(page.locator('#MargenMinimoPorcentaje')).toHaveValue('25');
        await expect(page.locator('#ReglaRedondeo')).toHaveValue('decena');
        await expect(page.getByRole('checkbox', { name: /Lista predeterminada/ })).toHaveCount(0); // la predeterminada no se desmarca desde su formulario
        expectNoFailures(failures);
    });

    test('precio manual por producto: rechaza bajo el margen minimo, guarda y queda en el historial', async ({ page }) => {
        const failures = trackFailures(page);
        await page.goto('/ListaPrecio', { waitUntil: 'domcontentloaded' });
        await page.locator('tr', { hasText: codigoA }).getByRole('link', { name: /precios de la lista/i }).click();
        await page.getByLabel('Producto', { exact: true }).fill(`PRD${SUFIJO}`);
        await page.getByRole('button', { name: 'Buscar' }).click();
        const fila = page.locator('tr', { hasText: `PRD${SUFIJO}` });
        await expect(fila).toContainText('Sin precio');
        await fila.getByRole('link', { name: /fijar el precio/i }).click();
        await expect(page.getByRole('heading', { name: /Fijar precio de/ })).toBeVisible();

        // Costo 1000 y minimo 20 %: 1100 (10 %) se rechaza.
        await page.locator('#precio').fill('1100');
        await enviar(page, 'Fijar precio');
        await expect(page.locator('#lp-error')).toContainText('inferior al mínimo permitido');

        await page.locator('#precio').fill('1450.50');
        await page.locator('#notas').fill('Precio de prueba E2E');
        await enviar(page, 'Fijar precio');
        await expect(page).toHaveURL(/\/ListaPrecio\/Precios\//);
        await expect(page.locator('.alert-erp-success')).toContainText('actualizado');
        // La vuelta a la lista no conserva la busqueda: con muchos productos hay que volver a buscarlo.
        const buscar = async () => {
            await page.getByLabel('Producto', { exact: true }).fill(`PRD${SUFIJO}`);
            await page.getByRole('button', { name: 'Buscar' }).click();
        };
        await buscar();
        await expect(page.locator('tr', { hasText: `PRD${SUFIJO}` })).toContainText('1.450,50');

        // Segundo precio: el anterior queda en el historial.
        await page.locator('tr', { hasText: `PRD${SUFIJO}` }).getByRole('link', { name: /fijar el precio/i }).click();
        await page.locator('#precio').fill('1600');
        await enviar(page, 'Fijar precio');
        await expect(page).toHaveURL(/\/ListaPrecio\/Precios\//);
        await buscar();
        await page.locator('tr', { hasText: `PRD${SUFIJO}` }).getByRole('link', { name: /fijar el precio/i }).click();
        const historial = page.getByRole('region', { name: 'Historial de precios del producto en la lista' });
        await expect(historial.locator('tbody tr')).toHaveCount(2);
        await expect(historial).toContainText('1.600,00');
        await expect(historial).toContainText('1.450,50');
        expectNoFailures(failures);
    });

    test('baja de una lista no predeterminada con confirmacion; el historial se conserva', async ({ page }) => {
        const failures = trackFailures(page);
        await page.goto('/ListaPrecio', { waitUntil: 'domcontentloaded' });
        await page.locator('tr', { hasText: codigoA }).getByRole('button', { name: /dar de baja/i }).click();
        // Confirmación compartida de Inventario: nombra la lista y enfoca la opción segura.
        const confirmacion = page.getByRole('dialog', { name: /dar de baja esta lista/i });
        await expect(confirmacion).toContainText(`Contado ${SUFIJO}`);
        await expect(confirmacion.getByRole('button', { name: 'Cancelar' })).toBeFocused();
        await confirmacion.getByRole('button', { name: 'Dar de baja la lista' }).click();
        await page.waitForLoadState('domcontentloaded');
        await expect(page.locator('.alert-erp-success')).toContainText('Lista dada de baja');
        await expect(page.locator('tr', { hasText: codigoA })).toHaveCount(0);
        await expect(page.locator('tr', { hasText: codigoB }).locator('form[data-lp-baja]')).toHaveCount(0);
        expectNoFailures(failures);
    });

    for (const vp of [{ nombre: '1366', width: 1366, height: 768 }, { nombre: '1024', width: 1024, height: 768 }, { nombre: '390', width: 390, height: 844 }]) {
        test(`responsive ${vp.nombre}: sin desborde horizontal ni errores en las cuatro pantallas`, async ({ page }) => {
            await page.setViewportSize({ width: vp.width, height: vp.height });
            const failures = trackFailures(page);

            await page.goto('/ListaPrecio', { waitUntil: 'networkidle' });
            await expectSinDesbordeHorizontal(page);
            await captura(page, `${vp.nombre}-1-index`);

            await page.locator('tr', { hasText: codigoB }).getByRole('link', { name: /Editar/ }).click();
            await expectSinDesbordeHorizontal(page);
            await captura(page, `${vp.nombre}-2-form`);

            await page.goto('/ListaPrecio', { waitUntil: 'domcontentloaded' });
            await page.locator('tr', { hasText: codigoB }).getByRole('link', { name: /precios de la lista/i }).click();
            await expectSinDesbordeHorizontal(page);
            await captura(page, `${vp.nombre}-3-precios`);

            await page.getByLabel('Producto', { exact: true }).fill(`PRD${SUFIJO}`);
            await page.getByRole('button', { name: 'Buscar' }).click();
            await page.locator('tr', { hasText: `PRD${SUFIJO}` }).getByRole('link', { name: /fijar el precio/i }).click();
            await expectSinDesbordeHorizontal(page);
            await captura(page, `${vp.nombre}-4-fijar`);
            expectNoFailures(failures);
        });
    }
});

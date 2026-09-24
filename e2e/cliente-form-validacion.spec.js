// @ts-check
/**
 * E2E de /Cliente/Create (cliente-form.js): errores de validación en solapas ocultas y Escape.
 *
 * Teléfono y Domicilio son obligatorios pero viven en la solapa Contacto. Antes, "Crear cliente"
 * desde Personales se enviaba, el servidor lo rechazaba y la página volvía a Personales sin ningún
 * error visible; y Escape descartaba el formulario aunque tuviera datos. No crea clientes: todos
 * los envíos se detienen en la validación del navegador (se verifica que no salga ningún POST).
 */
const { test, expect } = require('playwright/test');

test.use({ storageState: 'e2e/.auth/user.json' });

test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route =>
        route.fulfill({ status: 200, contentType: 'text/css', body: '' }));
    await page.route('**/fonts.gstatic.com/**', route =>
        route.fulfill({ status: 204, body: '' }));
});

async function completarObligatoriosDePersonales(page) {
    await page.locator('#NumeroDocumento').fill('12345678');
    await page.locator('#Apellido').fill('Prueba');
    await page.locator('#Nombre').fill('Validacion');
}

test.describe('Cliente Create — validación entre solapas y Escape', () => {
    test('un obligatorio faltante en Contacto abre esa solapa, la marca y enfoca el campo sin enviar', async ({ page }) => {
        /** @type {string[]} */
        const posts = [];
        page.on('request', request => {
            if (request.method() === 'POST' && request.url().includes('/Cliente/Create')) posts.push(request.url());
        });

        await page.goto('/Cliente/Create', { waitUntil: 'domcontentloaded' });
        await completarObligatoriosDePersonales(page);

        await page.getByRole('button', { name: /Crear cliente/ }).click();

        const contacto = page.locator('#form-tabs [data-cliente-tab="t-contacto"]');
        await expect(contacto).toHaveAttribute('aria-selected', 'true');
        await expect(contacto).toHaveClass(/has-error/);
        await expect(contacto).toContainText('(con errores)');
        await expect(page.locator('#Telefono')).toBeFocused();
        await expect(page.getByText('El teléfono es requerido')).toBeVisible();
        await expect(page.getByText('El domicilio es requerido')).toBeVisible();

        // Personales quedó completo: no se marca como solapa con errores.
        await expect(page.locator('#form-tabs [data-cliente-tab="t-personal"]')).not.toHaveClass(/has-error/);
        expect(posts, 'no debe haber POST al servidor con obligatorios sin completar').toHaveLength(0);
    });

    test('editar un cliente existente sin cambios es válido en el navegador (decimales con coma no bloquean)', async ({ page }) => {
        await page.goto('/Cliente', { waitUntil: 'domcontentloaded' });
        const detalle = page.locator('a[href*="/Cliente/Details/"]').first();
        test.skip(!(await detalle.count()), 'No hay clientes cargados en esta base para ejercitar el spec.');
        const id = (await detalle.getAttribute('href'))?.match(/\/Details\/(\d+)/)?.[1];

        /** @type {string[]} */
        const posts = [];
        page.on('request', request => {
            if (request.method() === 'POST' && request.url().includes('/Cliente/Edit')) posts.push(request.url());
        });

        await page.goto(`/Cliente/Edit/${id}`, { waitUntil: 'domcontentloaded' });

        // Sueldo y Monto máximo llegan con coma decimal ("5000000,00"): la regla "number" de
        // jQuery los rechaza, así que la validación entre solapas solo puede exigir [Required].
        const valido = await page.evaluate(() => window.jQuery('#clienteForm').valid());
        expect(valido, 'un cliente cargado y sin cambios debe pasar la validación del navegador').toBe(true);
        expect(posts).toHaveLength(0);
    });

    test('Escape con datos escritos no descarta el formulario', async ({ page }) => {
        await page.goto('/Cliente/Create', { waitUntil: 'domcontentloaded' });
        await page.locator('#Apellido').fill('Prueba');

        await page.locator('#Apellido').press('Escape');
        await page.waitForTimeout(400);

        await expect(page).toHaveURL(/\/Cliente\/Create/);
        await expect(page.locator('#Apellido')).toHaveValue('Prueba');
    });

    test('Escape con el formulario intacto vuelve al listado', async ({ page }) => {
        await page.goto('/Cliente/Create', { waitUntil: 'domcontentloaded' });

        await page.keyboard.press('Escape');

        await expect(page).toHaveURL(/\/Cliente(\?|$)/);
        await expect(page).not.toHaveURL(/\/Create/);
    });
});

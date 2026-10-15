// @ts-check
/**
 * MORA-VISTAS lote 2: smoke de SOLO LECTURA de /Mora/Alertas y /Mora/Logs.
 * Único POST permitido: el login (en la instancia clon). Cualquier otro POST/PUT/DELETE
 * same-origin se aborta y hace fallar el test: nunca se ejecuta MarcarLeida, Resolver ni EjecutarJob.
 */
const { test, expect } = require('playwright/test');
const path = require('path');
const fs = require('fs');

const OUT = path.join(process.cwd(), 'qa-evidence', 'mora-alertas-logs');

/** @param {import('playwright/test').Page} page */
async function login(page) {
    const user = process.env.E2E_USER, pass = process.env.E2E_PASS;
    if (!user || !pass) throw new Error('Definir E2E_USER y E2E_PASS (instancia clon).');
    await page.goto('/Identity/Account/Login', { waitUntil: 'domcontentloaded' });
    await page.fill('#Input_UserName, input[name="Input.UserName"]', user);
    await page.fill('#input-password, input[name="Input.Password"]', pass);
    await page.click('button[type="submit"]');
    const nombre = page.locator('input[placeholder="Nombre y apellido"]');
    if (await nombre.isVisible({ timeout: 4000 }).catch(() => false)) {
        await nombre.fill(`E2E ${user}`);
        await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
        await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
    }
    await page.waitForURL(u => !u.toString().toLowerCase().includes('/login'), { timeout: 15000 });
}

/** Bloquea escrituras same-origin a partir de este punto y junta errores de consola/HTTP. */
async function vigilar(page) {
    const errores = [];
    const origen = new URL(page.url()).origin;
    await page.route('**/*', route => {
        const req = route.request();
        if (req.url().startsWith(origen) && req.method() !== 'GET' && !req.url().includes('/hubs/')) {
            errores.push(`escritura bloqueada: ${req.method()} ${req.url()}`);
            return route.abort();
        }
        return route.continue();
    });
    page.on('console', m => {
        if (m.type() === 'error' && (m.location().url || '').startsWith(origen)) errores.push(`console: ${m.text()}`);
    });
    page.on('pageerror', e => errores.push(`pageerror: ${e.message}`));
    page.on('response', r => {
        if (r.url().startsWith(origen) && r.status() >= 400) errores.push(`http ${r.status()}: ${r.url()}`);
    });
    return errores;
}

async function sinDesborde(page, etiqueta) {
    const desborda = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(desborda, `overflow horizontal (${etiqueta})`).toBeFalsy();
}

async function capturar(page, nombre) {
    const vp = page.viewportSize();
    await sinDesborde(page, 'viewport real');
    // El contenido scrollea dentro del shell: alto grande para capturar la pantalla completa.
    await page.setViewportSize({ width: vp.width, height: 2600 });
    await sinDesborde(page, 'viewport alto');
    fs.mkdirSync(OUT, { recursive: true });
    await page.screenshot({ path: path.join(OUT, `${nombre}.png`), fullPage: true });
    await page.setViewportSize(vp);
}

test.beforeEach(async ({ page }) => { await login(page); });

test('/Mora/Alertas responde 200, filtra por GET y no desborda', async ({ page }, testInfo) => {
    const errores = await vigilar(page);

    const resp = await page.goto('/Mora/Alertas', { waitUntil: 'networkidle' });
    expect(resp?.status()).toBe(200);
    await expect(page.getByRole('heading', { level: 1, name: 'Alertas de mora' })).toBeVisible();

    // Listado o estado explícito (vacío / error); nunca una pantalla en blanco.
    const listado = page.locator('[data-mora-alertas-listado]');
    const error = page.locator('[data-mora-estado="error"]');
    expect((await listado.count()) + (await error.count())).toBe(1);

    // Filtros reales: los controles tienen label accesible.
    for (const nombre of ['Cliente', 'Prioridad', 'Tipo', 'Estado'])
        await expect(page.getByLabel(nombre, { exact: true })).toBeVisible();

    await capturar(page, `alertas-${testInfo.project.name}`);

    // Aplicar un filtro sin coincidencias: viaja por GET y muestra el estado "sin resultados".
    await page.getByLabel('Cliente', { exact: true }).fill('zz-sin-coincidencias-qa');
    await page.getByRole('button', { name: 'Aplicar filtros' }).click();
    await page.waitForURL(/\/Mora\/Alertas\?.*cliente=zz-sin-coincidencias-qa/);
    await expect(page.locator('[data-mora-estado="sin-resultados"]')).toBeVisible();
    await expect(page.getByLabel('Cliente', { exact: true })).toHaveValue('zz-sin-coincidencias-qa');
    await capturar(page, `alertas-sin-resultados-${testInfo.project.name}`);

    await page.getByRole('link', { name: 'Limpiar filtros' }).first().click();
    await page.waitForURL(u => u.pathname.endsWith('/Mora/Alertas') && !u.search);

    expect(errores, errores.join('\n')).toEqual([]);
});

test('/Mora/Logs responde 200 y no desborda', async ({ page }, testInfo) => {
    const errores = await vigilar(page);

    const resp = await page.goto('/Mora/Logs', { waitUntil: 'networkidle' });
    expect(resp?.status()).toBe(200);
    await expect(page.getByRole('heading', { level: 1, name: 'Historial de ejecuciones' })).toBeVisible();

    const historial = page.locator('[data-mora-historial]');
    const error = page.locator('[data-mora-estado="error"]');
    expect((await historial.count()) + (await error.count())).toBe(1);

    await capturar(page, `logs-${testInfo.project.name}`);
    expect(errores, errores.join('\n')).toEqual([]);
});

test('los accesos desde /Mora llevan a Alertas y Logs sin 500', async ({ page }) => {
    const errores = await vigilar(page);
    await page.goto('/Mora', { waitUntil: 'domcontentloaded' });

    for (const [nombre, ruta] of [['Alertas', '/Mora/Alertas'], ['Historial', '/Mora/Logs']]) {
        // El nombre accesible incluye la ligadura del ícono ("notifications_active Alertas").
        const href = await page.locator('.mora-actions').getByRole('link', { name: new RegExp(`${nombre}$`) }).getAttribute('href');
        expect(href).toBe(ruta);
        const r = await page.request.get(href);
        expect(r.status(), `${ruta}`).toBe(200);
    }
    expect(errores, errores.join('\n')).toEqual([]);
});

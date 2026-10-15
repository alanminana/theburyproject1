// @ts-check
/**
 * MORA-VISTAS lote 1: smoke de SOLO LECTURA de /Mora (GET). Único POST: el login (en la instancia clon).
 * No hace click en "Ejecutar proceso ahora" ni en ningún enlace de escritura.
 */
const { test, expect } = require('playwright/test');
const path = require('path');
const fs = require('fs');

const OUT = path.join(process.cwd(), 'qa-evidence', 'mora-index');

test.beforeEach(async ({ page }) => {
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
});

test('/Mora responde 200, renderiza la pantalla y no desborda', async ({ page }, testInfo) => {
    const errores = [];
    page.on('console', m => { if (m.type() === 'error') errores.push(`console: ${m.text()}`); });
    page.on('pageerror', e => errores.push(`pageerror: ${e.message}`));
    page.on('response', r => {
        if (r.url().startsWith(page.url().split('/').slice(0, 3).join('/')) && r.status() >= 400)
            errores.push(`http ${r.status()}: ${r.url()}`);
    });

    const resp = await page.goto('/Mora', { waitUntil: 'networkidle' });
    expect(resp?.status()).toBe(200);
    await expect(page).not.toHaveURL(/Home\/Error|AccessDenied|[Ll]ogin/);
    await expect(page.getByRole('heading', { level: 1, name: 'Mora y cobranzas' })).toBeVisible();

    // O hay KPIs (carga correcta) o hay estado de error explícito; nunca una pantalla en blanco.
    const kpis = page.locator('[data-mora-kpis]');
    const error = page.locator('[data-mora-estado="error"]');
    expect((await kpis.count()) + (await error.count())).toBe(1);

    const desborda = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(desborda, 'overflow horizontal').toBeFalsy();

    // El contenido scrollea dentro del shell: alto de viewport grande para capturar toda la pantalla.
    const vp = page.viewportSize();
    await page.setViewportSize({ width: vp.width, height: 2400 });
    await page.waitForTimeout(300);
    const desbordaAlto = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(desbordaAlto, 'overflow horizontal (viewport alto)').toBeFalsy();
    fs.mkdirSync(OUT, { recursive: true });
    await page.screenshot({ path: path.join(OUT, `${testInfo.project.name}.png`), fullPage: true });

    expect(errores, errores.join('\n')).toEqual([]);
});

test('el enlace "Volver"/migas de Configuración ya no lleva a un 500', async ({ page }) => {
    const resp = await page.goto('/Mora/ConfiguracionExpandida', { waitUntil: 'domcontentloaded' });
    expect(resp?.status()).toBe(200);
    const href = await page.getByRole('link', { name: 'Volver' }).first().getAttribute('href');
    const destino = await page.request.get(href ?? '/Mora');
    expect(destino.status()).toBe(200);
});

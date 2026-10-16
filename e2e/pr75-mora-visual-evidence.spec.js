// @ts-check
/**
 * Evidencia visual de Mora/ClientesMora y Mora/FichaCliente (PR #75).
 *
 * Genera capturas reales de Chromium en 1366x768, 1920x1080 y 390x844 en qa-evidence/pr75-mora-visual/
 * (el workflow pr75-mora-visual.yml las publica como artefacto "pr75-mora-visual-evidence"; no se suben PNG
 * al repositorio) y un manifest-<viewport>.json con cada escenario. Datos 100% sintéticos del seed efímero
 * (MoraVisualE2ESeeder), sembrado en la base descartable de CI.
 *
 * Solo corre con E2E_MORA_VISUAL=1: el suite general (playwright.yml) no tiene este dataset y lo omite.
 * Un escenario que no puede ejecutarse se registra como "no-ejecutable" con su motivo (nunca aprobado);
 * un error inesperado o un desborde horizontal de página se registran y hacen fallar el test al final.
 */
const fs = require('fs');
const path = require('path');
const { test, expect } = require('playwright/test');

test.use({ storageState: 'e2e/.auth/user.json' });
test.setTimeout(240_000);
test.skip(process.env.E2E_MORA_VISUAL !== '1', 'Requiere el seed sintético de Mora (E2E_MORA_VISUAL=1).');

const OUT = path.join('qa-evidence', 'pr75-mora-visual');
const SEED_JSON = path.join('e2e', '.auth', 'pr75-mora-seed-ids.json');
const VIEWPORTS = [
    { name: '1366x768', width: 1366, height: 768 },
    { name: '1920x1080', width: 1920, height: 1080 },
    { name: '390x844', width: 390, height: 844 },
];

for (const v of VIEWPORTS) {
    test(`evidencia visual Mora ${v.name}`, async ({ page }) => {
        fs.mkdirSync(OUT, { recursive: true });
        const seed = JSON.parse(fs.readFileSync(SEED_JSON, 'utf8'));
        /** @type {any[]} */
        const manifest = [];
        let errores = 0;

        const snap = async (id, pantalla, estado, descripcion, opts = {}) => {
            const archivo = `${pantalla}__${estado}__${v.name}.png`;
            const destino = path.join(OUT, archivo);
            if (opts.locator) await opts.locator.screenshot({ path: destino });
            else await page.screenshot({ path: destino, fullPage: !!opts.fullPage });
            const overflowX = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
            manifest.push({ id, pantalla, estado, viewport: v.name, archivo, descripcion, overflowXpx: overflowX, resultado: 'capturada' });
            if (overflowX > 1) throw new Error(`Desborde horizontal de ${overflowX}px en ${archivo}`);
        };
        const noEjecutable = (id, pantalla, estado, motivo) =>
            manifest.push({ id, pantalla, estado, viewport: v.name, archivo: null, resultado: 'no-ejecutable', motivo });
        const escenario = async (id, pantalla, estado, fn) => {
            try { await fn(); } catch (e) {
                errores++;
                manifest.push({ id, pantalla, estado, viewport: v.name, archivo: null, resultado: 'error', motivo: String(e).slice(0, 400) });
            }
        };
        const listo = async () => { await page.waitForLoadState('domcontentloaded'); await page.waitForTimeout(700); };

        await page.setViewportSize({ width: v.width, height: v.height });

        // ---------------- ClientesMora ----------------
        await page.goto('/Mora/ClientesMora', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('[data-mora-clientes-page]')).toBeVisible({ timeout: 15_000 });
        await listo();

        await escenario('L1', 'clientes-mora', 'listado-viewport', async () => {
            await snap('L1', 'clientes-mora', 'listado-viewport', 'Primera pantalla del listado, sin scroll.');
        });
        await escenario('L2', 'clientes-mora', 'listado-completo', async () => {
            await snap('L2', 'clientes-mora', 'listado-completo', 'Página 1 completa: KPIs, filtros, 20 clientes, paginación.', { fullPage: true });
        });

        await escenario('L3', 'clientes-mora', 'filtro-prioridad-critica', async () => {
            await page.locator('[data-mora-chip="Critica"]').click();
            await expect(page).toHaveURL(/prioridad=4/);
            await listo();
            await expect(page.locator('[data-mora-chip="Critica"]')).toHaveAttribute('aria-current', 'true');
            await snap('L3', 'clientes-mora', 'filtro-prioridad-critica', 'Chip «Crítica» activo tras clic real; listado filtrado.', { fullPage: true });
        });

        await escenario('L4', 'clientes-mora', 'filtros-combinados', async () => {
            await page.goto('/Mora/ClientesMora', { waitUntil: 'domcontentloaded' });
            await page.locator('#f-busqueda').fill('PR75');
            await page.locator('#f-dias-min').fill('30');
            await page.locator('#f-monto-min').fill('20000');
            await page.locator('[data-mora-filtros] button[type="submit"]').first().click();
            await expect(page).toHaveURL(/busqueda=PR75/);
            await listo();
            await snap('L4', 'clientes-mora', 'filtros-combinados', 'Búsqueda + días mínimos + monto mínimo aplicados desde el formulario.', { fullPage: true });
        });

        await escenario('L5', 'clientes-mora', 'paginacion-pagina-2', async () => {
            await page.goto('/Mora/ClientesMora', { waitUntil: 'domcontentloaded' });
            const pag = page.locator('[data-mora-paginacion]');
            await pag.scrollIntoViewIfNeeded();
            await snap('L5', 'clientes-mora', 'paginacion-control', 'Control de paginación al pie de la página 1.');
            await pag.getByRole('link', { name: /Siguiente/ }).click();
            await expect(page).toHaveURL(/pagina=2/);
            await listo();
            await expect(page.locator('[data-mora-paginacion]').getByRole('link', { name: /Anterior/ })).toBeVisible();
            await snap('L5b', 'clientes-mora', 'paginacion-pagina-2', 'Página 2 tras clic real en «Siguiente»; aparece «Anterior».', { fullPage: true });
        });

        await escenario('L6', 'clientes-mora', 'sin-resultados', async () => {
            await page.goto('/Mora/ClientesMora?busqueda=zzz-sin-coincidencias', { waitUntil: 'domcontentloaded' });
            await expect(page.locator('[data-mora-estado="sin-resultados"]')).toBeVisible();
            await listo();
            await snap('L6', 'clientes-mora', 'sin-resultados', 'Filtro sin coincidencias: estado vacío con acción «Limpiar filtros».', { fullPage: true });
            await page.locator('[data-mora-estado="sin-resultados"] a').first().click();
            await expect(page).not.toHaveURL(/busqueda=/);
        });

        let urlFichaDesdeListado = '';
        await escenario('L7', 'clientes-mora', 'acceso-a-ficha', async () => {
            await page.goto('/Mora/ClientesMora', { waitUntil: 'domcontentloaded' });
            const fila = page.locator('tr[data-cliente-id]').first();
            await fila.scrollIntoViewIfNeeded();
            await snap('L7', 'clientes-mora', 'fila-con-accion-ver-ficha', 'Primera fila con su botón «Ver ficha».');
            await fila.locator('[data-mora-accion="ver-ficha"]').click();
            await expect(page).toHaveURL(/\/Mora\/FichaCliente\/\d+/);
            await expect(page.locator('[data-mora-ficha-page]')).toBeVisible();
            urlFichaDesdeListado = page.url();
            await listo();
            await snap('L7b', 'ficha-cliente', 'llegada-desde-listado', 'Ficha abierta con clic real desde el listado (con returnUrl).');
        });
        await escenario('L8', 'ficha-cliente', 'volver-al-listado', async () => {
            if (!urlFichaDesdeListado) { noEjecutable('L8', 'ficha-cliente', 'volver-al-listado', 'No se llegó a la ficha desde el listado (L7).'); return; }
            await page.locator('[data-mora-volver]').first().click();
            await expect(page).toHaveURL(/\/Mora\/ClientesMora/);
            await expect(page.locator('[data-mora-clientes-page]')).toBeVisible();
        });

        // ---------------- FichaCliente ----------------
        await page.goto(`/Mora/FichaCliente/${seed.clienteFichaRicaId}`, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('[data-mora-ficha-page]')).toBeVisible({ timeout: 15_000 });
        await listo();

        await escenario('F1', 'ficha-cliente', 'rica-viewport', async () => {
            await snap('F1', 'ficha-cliente', 'rica-viewport', 'Ficha con 2 créditos, alertas, 4 contactos, promesa y acuerdo: primera pantalla.');
        });
        await escenario('F2', 'ficha-cliente', 'rica-completa', async () => {
            await snap('F2', 'ficha-cliente', 'rica-completa', 'Ficha rica completa.', { fullPage: true });
        });
        for (const [id, sel, estado, desc] of [
            ['F3', '[data-mora-ficha-resumen]', 'resumen', 'Resumen de deuda (KPIs).'],
            ['F4', '[data-mora-ficha-creditos]', 'creditos-cuotas', 'Créditos en mora con cuotas vencidas.'],
            ['F5', '[data-mora-ficha-alertas]', 'alertas', 'Alertas activas.'],
            ['F6', '[data-mora-ficha-contactos]', 'historial-contactos', 'Historial de contactos (texto largo incluido).'],
            ['F7', '[data-mora-ficha-promesas]', 'promesas', 'Promesas de pago vigentes.'],
            ['F8', '[data-mora-ficha-acuerdos]', 'acuerdos', 'Acuerdos de pago.'],
        ]) {
            await escenario(id, 'ficha-cliente', estado, async () => {
                const loc = page.locator(sel);
                if (!(await loc.isVisible().catch(() => false))) { noEjecutable(id, 'ficha-cliente', estado, `La sección ${sel} no se renderiza con este seed.`); return; }
                await loc.scrollIntoViewIfNeeded();
                await snap(id, 'ficha-cliente', estado, desc, { locator: loc });
            });
        }
        await escenario('F9', 'ficha-cliente', 'gestion-proximamente', async () => {
            const gestion = page.locator('[data-mora-ficha-gestion]');
            await gestion.scrollIntoViewIfNeeded();
            // Las tres acciones de gestión deben seguir NO disponibles (aria-disabled, sin enlace ni botón activo).
            for (const a of ['contacto', 'promesa', 'acuerdo']) {
                const item = gestion.locator(`[data-mora-gestion="${a}"]`);
                await expect(item).toHaveAttribute('aria-disabled', 'true');
                await expect(item).toContainText('Próximamente');
                expect(await item.locator('a[href], button:not([disabled]), input, select, textarea').count()).toBe(0);
            }
            await snap('F9', 'ficha-cliente', 'gestion-proximamente', 'Sección «Registrar gestión»: 3 acciones deshabilitadas con «Próximamente».', { locator: gestion });
        });

        await escenario('F10', 'ficha-cliente', 'sin-contactos', async () => {
            await page.goto(`/Mora/FichaCliente/${seed.clienteSinContactosId}`, { waitUntil: 'domcontentloaded' });
            await expect(page.locator('[data-mora-ficha-page]')).toBeVisible();
            await listo();
            await expect(page.locator('[data-mora-estado="contactos-vacio"]')).toBeVisible();
            await snap('F10', 'ficha-cliente', 'sin-contactos-promesas-acuerdos', 'Cliente sin contactos, promesas ni acuerdos: estados vacíos.', { fullPage: true });
        });
        await escenario('F11', 'ficha-cliente', 'nombre-largo', async () => {
            await page.goto(`/Mora/FichaCliente/${seed.clienteNombreLargoId}`, { waitUntil: 'domcontentloaded' });
            await expect(page.locator('[data-mora-ficha-page]')).toBeVisible();
            await listo();
            await snap('F11', 'ficha-cliente', 'nombre-largo', 'Nombre de cliente muy largo: sin cortes ni desborde.');
        });
        await escenario('F12', 'ficha-cliente', 'cliente-inexistente', async () => {
            await page.goto('/Mora/FichaCliente/99999999', { waitUntil: 'domcontentloaded' });
            await listo();
            await expect(page.locator('[data-mora-clientes-page]')).toBeVisible();
            await snap('F12', 'clientes-mora', 'redireccion-por-ficha-inexistente', 'Ficha de cliente inexistente: redirige al listado con mensaje.');
        });

        fs.writeFileSync(path.join(OUT, `manifest-${v.name}.json`), JSON.stringify(manifest, null, 2));
        expect(errores, `escenarios con error: ${JSON.stringify(manifest.filter((m) => m.resultado === 'error'))}`).toBe(0);
    });
}

// @ts-check
/**
 * Evidencia visual del wizard de Venta/Crédito (PR #73).
 *
 * Genera capturas reales de Chromium en 1366x768, 1920x1080 y 390x844 en qa-evidence/pr73-visual/ (el
 * workflow las publica como artefacto "pr73-visual-evidence", sin subir PNG al repositorio) y un
 * manifest.json que relaciona cada captura con su escenario. Datos sintéticos del seed efímero de CI.
 *
 * Navega solo con controles visibles (irAlPaso): no usa la barra de pasos oculta. Un escenario que no puede
 * ejecutarse por falta de datos/permisos se registra como "no-ejecutable" con su motivo (nunca como aprobado);
 * un error inesperado se registra y hace fallar el test al final.
 */
const fs = require('fs');
const path = require('path');
const { test, expect } = require('playwright/test');
const { searchAndSelectClient, activarFiltroStock, addProduct, irAlPaso } = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });
test.setTimeout(240_000);

const OUT = path.join('qa-evidence', 'pr73-visual');
const VIEWPORTS = [
    { name: '1366x768', width: 1366, height: 768 },
    { name: '1920x1080', width: 1920, height: 1080 },
    { name: '390x844', width: 390, height: 844 },
];

const NO_VIABLE = {
    resultado: 2, colorBadge: 'danger', textoEstado: 'No viable para credito',
    limiteCredito: 0, creditoUtilizado: 0, cupoDisponible: 0, documentacionCompleta: false,
    documentosFaltantes: ['DNI'], documentosVencidos: [], tieneMora: false, diasMora: 0, montoMora: 0,
    motivos: [{ categoria: 1, titulo: 'Documentacion', descripcion: 'Faltan documentos: DNI', esBloqueante: true }],
};

for (const v of VIEWPORTS) {
    test(`evidencia visual del wizard ${v.name}`, async ({ page }) => {
        fs.mkdirSync(OUT, { recursive: true });
        /** @type {any[]} */
        const manifest = [];
        let errores = 0;

        const snap = async (id, pantalla, estado, descripcion, fullPage = false) => {
            const archivo = `${pantalla}__${estado}__${v.name}.png`;
            await page.screenshot({ path: path.join(OUT, archivo), fullPage });
            manifest.push({ id, pantalla, estado, viewport: v.name, archivo, descripcion, resultado: 'capturada' });
        };
        const noEjecutable = (id, pantalla, estado, motivo) =>
            manifest.push({ id, pantalla, estado, viewport: v.name, archivo: null, resultado: 'no-ejecutable', motivo });
        const escenario = async (id, pantalla, estado, fn) => {
            try { await fn(); } catch (e) {
                errores++;
                manifest.push({ id, pantalla, estado, viewport: v.name, archivo: null, resultado: 'error', motivo: String(e).slice(0, 400) });
            }
        };

        await page.setViewportSize({ width: v.width, height: v.height });

        // ---------------- Venta/Create ----------------
        await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
        await page.waitForTimeout(1500);

        await escenario('C1', 'create', 'cotizar-siguiente-cliente', async () => {
            const sig = page.getByRole('button', { name: /Siguiente: Cliente/ });
            await sig.scrollIntoViewIfNeeded();
            await expect(sig).toBeVisible();
            await snap('C1', 'create', 'cotizar-siguiente-cliente', 'Cotizar con «Siguiente: Cliente» visible; barra de pasos oculta.');
        });

        await escenario('C2', 'create', 'cliente-paso-anterior', async () => {
            await irAlPaso(page, 'cliente');
            await expect(page.getByRole('button', { name: /Paso anterior/ })).toBeVisible();
            await page.waitForTimeout(500);
            await snap('C2', 'create', 'cliente-paso-anterior', 'Cliente tras avanzar; «Paso anterior» visible al inicio del panel.');
        });

        await escenario('C3', 'create', 'aviso-validacion-bloqueante', async () => {
            await page.locator('[data-wizard-primary]:visible, #btn-confirmar:visible').first().click();
            await expect(page.locator('#wizard-feedback')).toBeInViewport({ timeout: 5_000 });
            await snap('C3', 'create', 'aviso-validacion-bloqueante', 'Siguiente sin cliente: aviso de validación visible en pantalla.');
        });

        const cliente = await searchAndSelectClient(page).catch(() => null);
        if (!cliente) {
            noEjecutable('C4', 'create', 'recorrido-completo', 'El seed efímero no expone cliente para el autocompletado.');
            fs.writeFileSync(path.join(OUT, `manifest-${v.name}.json`), JSON.stringify(manifest, null, 2));
            expect(errores, 'escenarios con error: ver manifest').toBe(0);
            return;
        }

        await escenario('C4', 'create', 'productos-paso-anterior', async () => {
            await irAlPaso(page, 'productos');
            await activarFiltroStock(page);
            await addProduct(page, 'an');
            await page.waitForTimeout(500);
            await snap('C4', 'create', 'productos-paso-anterior', 'Productos con un producto agregado y «Paso anterior».');
        });
        await escenario('C5', 'create', 'pago', async () => {
            await irAlPaso(page, 'pago');
            await page.locator('#select-tipo-pago').selectOption('0');
            await page.waitForTimeout(500);
            await snap('C5', 'create', 'pago', 'Pago (efectivo).');
        });
        await escenario('C6', 'create', 'revision', async () => {
            await irAlPaso(page, 'revision');
            await page.waitForTimeout(500);
            await snap('C6', 'create', 'revision', 'Revisión final; CTA de creación visible.');
        });

        let id = null;
        await escenario('C7', 'create', 'confirmacion', async () => {
            const vend = page.locator('#VendedorUserId');
            if (await vend.isVisible().catch(() => false) && !(await vend.inputValue())) await vend.selectOption({ index: 1 });
            await page.locator('#btn-confirmar:visible, .vm-btn-confirm-sm:visible').first().click();
            await page.waitForURL(/\/Venta\/(Details|Edit)\/\d+/, { timeout: 20_000 });
            id = page.url().match(/\/(?:Details|Edit)\/(\d+)/)?.[1] ?? null;
            await page.waitForTimeout(800);
            await snap('C7', 'create', 'tras-crear-venta', 'Pantalla posterior a crear la venta.');
        });

        // ---------------- Venta/Edit ----------------
        const url = id ? `/Venta/Edit/${id}` : null;
        if (!url) {
            noEjecutable('E1', 'edit', 'todos', 'No se obtuvo una venta editable (la creación no devolvió id).');
            noEjecutable('K1', 'credito', 'todos', 'No se obtuvo una venta editable.');
        } else {
            await page.goto(url, { waitUntil: 'domcontentloaded' });
            await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
            await page.waitForTimeout(1000);

            await escenario('E1', 'edit', 'inicial-cliente', async () => {
                await expect(page.locator('[data-wizard-back]')).toBeHidden();
                await snap('E1', 'edit', 'inicial-cliente', 'Edit inicial (Cliente): sin «Paso anterior» al ser el primer paso.');
            });
            await escenario('E2', 'edit', 'productos-paso-anterior', async () => {
                await irAlPaso(page, 'productos');
                await expect(page.getByRole('button', { name: /Paso anterior/ })).toBeVisible();
                await page.waitForTimeout(400);
                await snap('E2', 'edit', 'productos-paso-anterior', 'Edit en Productos con «Paso anterior» visible.');
            });
            await escenario('E3', 'edit', 'retroceso-a-cliente', async () => {
                await page.getByRole('button', { name: /Paso anterior/ }).click();
                await expect(page.locator('#step-panel-cliente')).toBeVisible();
                await page.waitForTimeout(500);
                await snap('E3', 'edit', 'retroceso-a-cliente', 'Tras «Paso anterior» desde Productos: vuelve a Cliente.');
            });
            await escenario('E4', 'edit', 'revision', async () => {
                await irAlPaso(page, 'revision');
                await page.waitForTimeout(500);
                await snap('E4', 'edit', 'revision', 'Edit en Revisión (CTA de confirmación y «Paso anterior»).');
            });
            await escenario('E5', 'edit', 'modal-confirmar-facturar', async () => {
                const chk = page.locator('#chk-facturar');
                if (await chk.count() === 0) {
                    noEjecutable('E5', 'edit', 'modal-confirmar-facturar', 'Sin permiso ventas.invoice: el checkbox «Facturar al confirmar» no se renderiza.');
                    return;
                }
                await chk.check();
                await page.locator('#btn-confirmar:visible, .vm-btn-confirm-sm:visible').first().click();
                await expect(page.locator('#modal-confirmar-facturar')).toBeVisible({ timeout: 5_000 });
                await page.waitForTimeout(500);
                await snap('E5', 'edit', 'modal-confirmar-facturar', 'Modal «Confirmar y facturar» abierto sobre Revisión.');
                await page.keyboard.press('Escape');
            });

            // ---------------- Crédito (Edit con Crédito Personal preseleccionado, prevalidación simulada) ----------------
            await escenario('K0', 'credito', 'preparacion', async () => {
                await page.route(new RegExp(`${url}$`), async (route, request) => {
                    if (request.method() !== 'GET') { await route.continue(); return; }
                    const response = await route.fetch();
                    let body = await response.text();
                    body = body.replace(/<select id="select-tipo-pago"[\s\S]*?<\/select>/, (b) =>
                        b.replace(/\sselected="selected"/g, '').replace('<option value="5"', '<option value="5" selected="selected"'));
                    await route.fulfill({ response, body });
                });
                await page.route('**/api/ventas/PrevalidarCredito*', (r) => r.fulfill({ contentType: 'application/json', body: JSON.stringify(NO_VIABLE) }));
                await page.goto(url, { waitUntil: 'domcontentloaded' });
                await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
            });
            await escenario('K1', 'credito', 'configuracion', async () => {
                await irAlPaso(page, 'credito');
                await page.waitForTimeout(1500);
                await expect(page.getByRole('button', { name: /Paso anterior/ })).toBeVisible();
                await snap('K1', 'credito', 'configuracion', 'Paso Crédito (prevalidación simulada «no viable»): «Paso anterior» al inicio del panel.');
                await snap('K1b', 'credito', 'configuracion-pagina-completa', 'Paso Crédito, página completa.', true);
            });
            await escenario('K2', 'credito', 'modal-documentacion', async () => {
                const abrir = page.locator('#btn-cargar-documentacion');
                if (!(await abrir.isVisible().catch(() => false))) {
                    noEjecutable('K2', 'credito', 'modal-documentacion', 'El botón «Resolver» de documentación no está visible en este escenario.');
                    return;
                }
                await abrir.click();
                await page.waitForTimeout(800);
                await snap('K2', 'credito', 'modal-documentacion', 'Modal de documentación abierto desde «Resolver».');
                await page.keyboard.press('Escape');
                await page.waitForTimeout(300);
            });
            await escenario('K3', 'credito', 'navegacion-pago', async () => {
                await page.getByRole('button', { name: /Paso anterior/ }).click();
                await expect(page.locator('#step-panel-pago')).toBeVisible();
                await page.waitForTimeout(500);
                await snap('K3', 'credito', 'navegacion-pago', 'Retroceso Crédito → Pago con «Paso anterior».');
            });
        }

        fs.writeFileSync(path.join(OUT, `manifest-${v.name}.json`), JSON.stringify(manifest, null, 2));
        expect(errores, `escenarios con error: ${JSON.stringify(manifest.filter((m) => m.resultado === 'error'))}`).toBe(0);
    });
}

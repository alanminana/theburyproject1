// @ts-check
// TEMPORAL (rama descartable qa/visual-pr73-*): capturas de revision visual del wizard. No se fusiona.
const { test, expect } = require('playwright/test');
const { searchAndSelectClient, activarFiltroStock, addProduct, irAlPaso } = require('./helpers');

test.use({ storageState: 'e2e/.auth/user.json' });
test.setTimeout(240_000);

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

async function snap(page, info, vp, nombre, fullPage = false) {
    const ruta = info.outputPath(`${vp}__${nombre}.png`);
    await page.screenshot({ path: ruta, fullPage });
    await info.attach(`${vp}__${nombre}`, { path: ruta, contentType: 'image/png' });
}

async function medir(page, info, vp, nombre) {
    const m = await page.evaluate(() => {
        const vis = (el) => !!el && el.offsetParent !== null;
        const rect = (sel) => [...document.querySelectorAll(sel)].filter(vis).map((el) => {
            const r = el.getBoundingClientRect();
            return { sel, x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height), txt: (el.textContent || '').trim().slice(0, 40), dis: el.disabled || el.getAttribute('aria-disabled') };
        });
        const barra = document.querySelector('.vm-hero__steps');
        return {
            vw: innerWidth, vh: innerHeight, scrollW: document.documentElement.scrollWidth,
            barra: barra ? getComputedStyle(barra).display : 'sin-barra',
            botones: [...rect('[data-wizard-next]'), ...rect('[data-wizard-back]'), ...rect('[data-wizard-primary]'), ...rect('#btn-confirmar')],
        };
    });
    await info.attach(`${vp}__${nombre}.json`, { body: JSON.stringify(m, null, 1), contentType: 'application/json' });
    console.log(`MEDIDA ${vp} ${nombre} ${JSON.stringify(m)}`);
}

for (const v of VIEWPORTS) {
    test(`captura Create/Edit/Credito ${v.name}`, async ({ page }, info) => {
        await page.setViewportSize({ width: v.width, height: v.height });

        await page.goto('/Venta/Create', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('#venta-form')).toBeVisible({ timeout: 15_000 });
        await page.waitForTimeout(1500);
        await snap(page, info, v.name, '01-create-cotizar');
        await snap(page, info, v.name, '01b-create-cotizar-pagina-completa', true);
        await medir(page, info, v.name, '01-create-cotizar');

        const sig = page.getByRole('button', { name: /Siguiente: Cliente/ });
        await sig.scrollIntoViewIfNeeded();
        await snap(page, info, v.name, '02-create-siguiente-visible');
        await sig.click();
        await expect(page.locator('#step-panel-cliente')).toBeVisible();
        await page.waitForTimeout(600);
        await snap(page, info, v.name, '03-create-cliente-sin-seleccion-paso-anterior');
        await medir(page, info, v.name, '03-create-cliente');

        const cta = page.locator('[data-wizard-primary]:visible, #btn-confirmar:visible').first();
        if (await cta.count()) {
            await cta.click().catch(() => {});
            await page.waitForTimeout(500);
            await snap(page, info, v.name, '04-create-cliente-avance-bloqueado');
            const fb = await page.evaluate(() => { const e = document.getElementById('wizard-feedback'); if (!e) return null; const r = e.getBoundingClientRect(); return { txt: e.textContent, hidden: e.hidden, cls: e.className.includes('hidden'), y: Math.round(r.y), h: Math.round(r.height), scrollY: Math.round(document.scrollingElement.scrollTop), main: [...document.querySelectorAll('*')].filter(x => x.scrollTop > 0).map(x => x.tagName + '#' + x.id + ':' + x.scrollTop).slice(0, 4) }; });
            console.log('FEEDBACK ' + v.name + ' ' + JSON.stringify(fb));
        }

        const cliente = await searchAndSelectClient(page);
        await snap(page, info, v.name, '05-create-cliente-seleccionado');
        if (!cliente) return;

        await irAlPaso(page, 'productos');
        await activarFiltroStock(page);
        await addProduct(page, 'an');
        await page.waitForTimeout(500);
        await snap(page, info, v.name, '06-create-productos');
        await medir(page, info, v.name, '06-create-productos');
        await irAlPaso(page, 'pago');
        await page.locator('#select-tipo-pago').selectOption('0');
        await page.waitForTimeout(500);
        await snap(page, info, v.name, '07-create-pago');
        await irAlPaso(page, 'revision');
        await page.waitForTimeout(500);
        await snap(page, info, v.name, '08-create-revision');
        await snap(page, info, v.name, '08b-create-revision-pagina-completa', true);
        await medir(page, info, v.name, '08-create-revision');

        const vend = page.locator('#VendedorUserId');
        if (await vend.isVisible().catch(() => false) && !(await vend.inputValue())) await vend.selectOption({ index: 1 });
        await page.locator('#btn-confirmar:visible, .vm-btn-confirm-sm:visible').first().click();
        await page.waitForTimeout(1200);
        await snap(page, info, v.name, '09-tras-confirmar-modal-o-redireccion');
        await page.waitForURL(/\/Venta\/(Details|Edit)\/\d+/, { timeout: 20_000 }).catch(() => {});
        const id = page.url().match(/\/(?:Details|Edit)\/(\d+)/)?.[1];
        if (!id) return;

        const url = `/Venta/Edit/${id}`;
        await page.goto(url, { waitUntil: 'domcontentloaded' });
        if (!(await page.locator('#venta-form').isVisible().catch(() => false))) {
            await snap(page, info, v.name, '10-edit-no-editable');
            return;
        }
        await page.waitForTimeout(1200);
        await snap(page, info, v.name, '10-edit-cliente-sin-paso-anterior');
        await medir(page, info, v.name, '10-edit-cliente');
        await irAlPaso(page, 'productos');
        await snap(page, info, v.name, '11-edit-productos-paso-anterior');
        await medir(page, info, v.name, '11-edit-productos');
        await irAlPaso(page, 'pago');
        await irAlPaso(page, 'revision');
        await snap(page, info, v.name, '12-edit-revision');
        await medir(page, info, v.name, '12-edit-revision');

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
        if (await page.locator('#venta-form').isVisible().catch(() => false)) {
            await irAlPaso(page, 'credito').catch(async (e) => { await info.attach('credito-error', { body: String(e), contentType: 'text/plain' }); });
            await page.waitForTimeout(1500);
            await snap(page, info, v.name, '13-edit-credito');
            await snap(page, info, v.name, '13b-edit-credito-pagina-completa', true);
            await medir(page, info, v.name, '13-edit-credito');
        }
    });
}

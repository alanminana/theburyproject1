// @ts-check
/**
 * COM-S-001 / COM-S-002 — regresión de XSS por render inseguro (CWE-79).
 *
 *  - COM-S-001: historial de precios (wwwroot/js/historial-precio-modal.js). `motivo` y `usuario` son texto libre
 *    persistido y se concatenaban a `row.innerHTML`.
 *  - COM-S-002: buscador de garantes (wwwroot/js/cliente-garante.js). `nombreCompleto` y `numeroDocumento`
 *    (TipoDocumento libre => hasta 20 caracteres de texto arbitrario) se concatenaban a HTML y a atributos data-*.
 *
 * Contrato verificado acá:
 *   - un payload HTML/atributo viene del servidor y NO ejecuta JavaScript (ni al renderizar ni al hacer click);
 *   - el dato se muestra literal (`<`, `>`, comillas, `&`, apóstrofes) y llega intacto a la selección;
 *   - la funcionalidad legítima sigue igual (fila, variación, botón Revertir, selección de candidato).
 *
 * No necesita app, base de datos ni Docker: carga el JS real en una página sintética y controla las respuestas
 * HTTP con page.route.
 *
 * Ejecutar:  npx.cmd playwright test -c playwright.js-harness.config.js e2e-js/com-xss-render.spec.js
 * Contra el JS anterior (reproducir la falla):
 *   $env:HISTORIAL_JS = "ruta\vieja\historial-precio-modal.js"; $env:GARANTE_JS = "ruta\vieja\cliente-garante.js"
 */
const { test, expect } = require('playwright/test');
const fs = require('fs');
const path = require('path');

const ORIGIN = 'http://harness.test';
const JS_DIR = path.join(__dirname, '..', 'wwwroot', 'js');
const HISTORIAL_JS = process.env.HISTORIAL_JS || path.join(JS_DIR, 'historial-precio-modal.js');
const GARANTE_JS = process.env.GARANTE_JS || path.join(JS_DIR, 'cliente-garante.js');

/** Payloads: nodo HTML con handler, ruptura de atributo y handler por click (onload/onerror no siempre disparan post-carga). */
const PAYLOADS = [
    `<img src=x onerror="window.__xss=(window.__xss||0)+1">`,
    `<svg onclick="window.__xss=(window.__xss||0)+1"><rect width="400" height="400"/></svg>`,
    // Sin comillas: no rompe el atributo data-nombre y llega intacto al nodo del <div> (clickeable).
    `<svg onclick=window.__xss=(window.__xss||0)+1 width=300 height=100>`,
    `" onclick="window.__xss=(window.__xss||0)+1" x="`,
    `</div><img src=x onerror="window.__xss=(window.__xss||0)+1">`,
];
const LEGIT = `O'Brien <Ana> & "Cía" 'x'`;

async function pageWith(page, bodyHtml, scriptPath) {
    const errors = [];
    const dialogs = [];
    page.on('pageerror', (e) => errors.push(e.message));
    page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
    page.on('dialog', (d) => { dialogs.push(d.message()); d.dismiss(); });
    await page.route(`${ORIGIN}/**`, (route) => {
        const url = route.request().url();
        if (url === `${ORIGIN}/`) {
            return route.fulfill({ contentType: 'text/html', body: `<!doctype html><html><head><meta charset="utf-8"><style>.hidden{display:none}</style></head><body>${bodyHtml}</body></html>` });
        }
        return route.fulfill({ status: 404, body: '' });
    });
    await page.goto(`${ORIGIN}/`);
    await page.addScriptTag({ content: fs.readFileSync(scriptPath, 'utf8') });
    return { errors, dialogs };
}

// ───────────────────────────── COM-S-001 ─────────────────────────────
const HIST_BODY = `
<button id="trigger">abrir</button>
<div id="modal-historial-precio" class="hidden">
  <span id="historial-producto-nombre"></span><span id="historial-producto-codigo"></span>
  <span id="historial-precio-count"></span><div id="historial-precio-feedback" class="hidden"></div>
  <div id="historial-precio-loading" class="hidden"></div><div id="historial-precio-empty" class="hidden"></div>
  <table><tbody id="historial-precio-tbody"></tbody></table>
  <button type="button" data-catalogo-modal-close="historial-precio">x</button>
</div>`;

function historialResponse(items) {
    return { success: true, producto: { productoId: 1, codigo: 'C-1', nombre: 'Prod' }, items };
}
const evento = (over) => ({
    eventoId: 11, fecha: '20 oct, 2026 · 10:30', usuario: 'admin', motivo: 'Ajuste de proveedor',
    precioAnterior: 100, precioNuevo: 120, puedeRevertir: true, ...over,
});

async function abrirHistorial(page, items) {
    await page.route(`${ORIGIN}/Catalogo/HistorialPrecioProductoApi**`, (route) =>
        route.fulfill({ contentType: 'application/json', body: JSON.stringify(historialResponse(items)) }));
    await page.evaluate(() => HistorialPrecioModal.open(1));
    await page.waitForFunction(() => document.querySelectorAll('#historial-precio-tbody tr').length > 0);
}

test.describe('COM-S-001 historial de precios', () => {
    for (const payload of PAYLOADS) {
        test(`motivo/usuario no ejecutan JS: ${payload.slice(0, 40)}`, async ({ page }) => {
            const { errors, dialogs } = await pageWith(page, HIST_BODY, HISTORIAL_JS);
            await abrirHistorial(page, [evento({ motivo: payload, usuario: payload })]);
            // Click sobre todo lo renderizado: cubre handlers onclick además de onerror.
            for (const handle of await page.locator('#historial-precio-tbody *').elementHandles()) {
                await handle.click({ trial: false, force: true, timeout: 1000 }).catch(() => {});
            }
            expect(await page.evaluate(() => window.__xss || 0)).toBe(0);
            expect(await page.locator('#historial-precio-tbody img, #historial-precio-tbody svg, #historial-precio-tbody [onclick], #historial-precio-tbody [onerror]').count()).toBe(0);
            expect(dialogs).toEqual([]);
            expect(errors.filter((e) => !/Failed to load resource/.test(e))).toEqual([]);
        });
    }

    test('el texto legítimo con caracteres especiales se muestra literal', async ({ page }) => {
        await pageWith(page, HIST_BODY, HISTORIAL_JS);
        await abrirHistorial(page, [evento({ motivo: LEGIT, usuario: LEGIT })]);
        const tds = page.locator('#historial-precio-tbody tr td');
        await expect(tds.nth(2)).toHaveText(LEGIT);
        await expect(tds.nth(1)).toContainText(LEGIT);
        await expect(tds.nth(1).locator('span').first()).toHaveText('O');
    });

    test('el botón Revertir generado dispara la confirmación con el eventoId', async ({ page }) => {
        await pageWith(page, HIST_BODY, HISTORIAL_JS);
        await page.evaluate(() => { window.__confirm = []; window.TheBury = { confirmAction: (msg) => window.__confirm.push(msg) }; });
        await abrirHistorial(page, [evento({ eventoId: 42 })]);
        await page.locator('button[data-catalogo-action="historial-revert"]').click();
        expect(await page.evaluate(() => window.__confirm.length)).toBe(1);
    });

    test('funcionalidad intacta: fecha, hora, precios, variación y botón Revertir', async ({ page }) => {
        await pageWith(page, HIST_BODY, HISTORIAL_JS);
        await abrirHistorial(page, [
            evento({}),
            evento({ eventoId: 12, precioAnterior: 100, precioNuevo: 80, puedeRevertir: false, motivo: 'Oferta' }),
            evento({ eventoId: 13, precioAnterior: 0, precioNuevo: 50, usuario: '', motivo: null }),
        ]);
        await expect(page.locator('#historial-precio-count')).toHaveText('3 eventos');
        const r1 = page.locator('#historial-precio-tbody tr').nth(0);
        await expect(r1.locator('td').nth(0)).toContainText('20 oct, 2026');
        await expect(r1.locator('td').nth(0)).toContainText('10:30');
        await expect(r1.locator('td').nth(2)).toHaveText('Ajuste de proveedor');
        await expect(r1.locator('td').nth(3)).toContainText('100');
        await expect(r1.locator('td').nth(5)).toContainText('+20.0%');
        const btn = r1.locator('button[data-catalogo-action="historial-revert"]');
        await expect(btn).toHaveAttribute('data-catalogo-evento-id', '11');
        await expect(btn).toContainText('Revertir');
        const r2 = page.locator('#historial-precio-tbody tr').nth(1);
        await expect(r2.locator('td').nth(5)).toContainText('-20.0%');
        await expect(r2.locator('button')).toHaveCount(0);
        const r3 = page.locator('#historial-precio-tbody tr').nth(2);
        await expect(r3.locator('td').nth(1)).toContainText('—');
        await expect(r3.locator('td').nth(5)).toHaveText('—');
    });
});

// ───────────────────────────── COM-S-002 ─────────────────────────────
const GARANTE_BODY = `
<button type="button" data-garante-asignar data-cliente-id="5">Asignar</button>
<div id="garanteModal" class="hidden">
  <input id="garanteBuscador" />
  <div id="garanteResultados"></div>
  <div id="garanteSeleccionado" class="hidden"><span id="garanteSelNombre"></span><span id="garanteSelInfo"></span></div>
  <textarea id="garanteObservacion"></textarea><div id="garanteError" class="hidden"></div>
  <button type="button" id="garanteConfirmar" disabled>Confirmar</button>
  <button type="button" data-garante-modal-cerrar>cerrar</button>
</div>`;

const candidato = (over) => ({
    clienteId: 9, nombreCompleto: 'Ana Pérez', numeroDocumento: '30111222', puntajeCliente: 4,
    cantidadCompras: 3, garantiasActivas: 1, ...over,
});

async function buscar(page, items) {
    await page.route(`${ORIGIN}/Cliente/BuscarPosiblesGarantes**`, (route) =>
        route.fulfill({ contentType: 'application/json', body: JSON.stringify(items) }));
    await page.click('[data-garante-asignar]');
    await page.fill('#garanteBuscador', 'ana');
    await page.waitForSelector('[data-garante-candidato]');
}

test.describe('COM-S-002 buscador de garantes', () => {
    for (const payload of PAYLOADS) {
        test(`nombre/documento no ejecutan JS: ${payload.slice(0, 40)}`, async ({ page }) => {
            const { errors, dialogs } = await pageWith(page, GARANTE_BODY, GARANTE_JS);
            await buscar(page, [candidato({ nombreCompleto: payload, numeroDocumento: payload.slice(0, 20) })]);
            // Primero los nodos inyectados (seleccionar la fila vacía el contenedor y los desconectaría antes del clic).
            for (const handle of await page.locator('#garanteResultados svg, #garanteResultados img').elementHandles()) {
                await handle.click({ force: true, timeout: 1000 }).catch(() => {});
            }
            for (const handle of await page.locator('#garanteResultados *').elementHandles()) {
                await handle.click({ force: true, timeout: 1000 }).catch(() => {});
            }
            // Tras seleccionar, el resultado también se vuelca a la ficha: tampoco debe ejecutar nada.
            expect(await page.evaluate(() => window.__xss || 0)).toBe(0);
            expect(await page.locator('#garanteModal img, #garanteModal svg, #garanteModal [onclick], #garanteModal [onerror]').count()).toBe(0);
            expect(dialogs).toEqual([]);
            expect(errors.filter((e) => !/Failed to load resource/.test(e))).toEqual([]);
        });
    }

    test('texto legítimo con caracteres especiales: literal y llega intacto a la selección', async ({ page }) => {
        await pageWith(page, GARANTE_BODY, GARANTE_JS);
        await buscar(page, [candidato({ nombreCompleto: LEGIT, numeroDocumento: `<A-1>"'&` })]);
        const row = page.locator('[data-garante-candidato]');
        await expect(row).toHaveCount(1);
        await expect(row).toContainText(LEGIT);
        await expect(row).toContainText(`DNI <A-1>"'&`);
        await expect(row).toHaveAttribute('data-nombre', LEGIT);
        await row.click();
        await expect(page.locator('#garanteSelNombre')).toHaveText(LEGIT);
        await expect(page.locator('#garanteSelInfo')).toHaveText(`DNI <A-1>"'& — Puntaje 4/5 — 3 compra(s) — 1 garantía(s) activa(s)`);
        await expect(page.locator('#garanteBuscador')).toHaveValue(LEGIT);
        await expect(page.locator('#garanteConfirmar')).toBeEnabled();
    });

    test('múltiples candidatos, selección y "sin resultados"', async ({ page }) => {
        await pageWith(page, GARANTE_BODY, GARANTE_JS);
        await buscar(page, [candidato({ clienteId: 1, nombreCompleto: 'Uno' }), candidato({ clienteId: 2, nombreCompleto: 'Dos' })]);
        await expect(page.locator('[data-garante-candidato]')).toHaveCount(2);
        await page.locator('[data-garante-candidato]').nth(1).click();
        await expect(page.locator('#garanteSelNombre')).toHaveText('Dos');
        await expect(page.locator('[data-garante-candidato]')).toHaveCount(0);
        await page.unroute(`${ORIGIN}/Cliente/BuscarPosiblesGarantes**`);
        await page.route(`${ORIGIN}/Cliente/BuscarPosiblesGarantes**`, (r) => r.fulfill({ contentType: 'application/json', body: '[]' }));
        await page.fill('#garanteBuscador', 'zzz');
        await expect(page.locator('#garanteResultados')).toContainText('Sin resultados.');
    });
});

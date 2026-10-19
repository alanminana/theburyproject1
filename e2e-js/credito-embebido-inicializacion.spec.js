// @ts-check
/**
 * RESCATE-CREDITO-EMBEBIDO-INICIALIZACION-P0 — regresión determinista del configurador de Crédito embebido en el
 * wizard de Venta (wwwroot/js/venta-page-wizard.js + venta-credito-embebido.js + configurar-venta-credito.js).
 *
 * Riesgo cubierto: al (re)inyectar el fragmento, configurar-venta-credito.js agenda la simulación inicial. Esa
 * agenda NO es un cambio del usuario, pero antes avisaba `onCambioPlan`, que emitía `venta:credito-validado
 * {configurado:false}` mientras el servidor todavía decía "configurado". El wizard deshabilitaba Revisión (exige
 * creditoValidado) y, si el operador estaba parado en ella (p. ej. tras "Generar contrato", que recarga el
 * fragmento), lo expulsaba al paso anterior.
 *
 * Contrato verificado acá:
 *   - inicializar / recargar un fragmento ya configurado no invalida nada ni mueve de paso;
 *   - generar el contrato desde Revisión no saca al operador de Revisión;
 *   - un cambio REAL (cuotas, anticipo, gastos, fecha) sí invalida la configuración;
 *   - la simulación inicial se ejecuta igual (server-authoritative, con ventaId) y habilita "Confirmar";
 *   - "Confirmar crédito" solo marca configurado si el servidor lo confirma;
 *   - una respuesta de simulación tardía/fuera de orden no pisa a la más nueva;
 *   - RowVersion se resincroniza desde el fragmento recargado;
 *   - la página standalone (sin onCambioPlan) sigue simulando.
 *
 * No necesita app, base de datos ni Docker: carga el JS real en una página sintética y controla cada respuesta
 * HTTP con page.route (compuertas explícitas, sin temporizadores arbitrarios).
 *
 * Ejecutar:  npx.cmd playwright test -c playwright.js-harness.config.js e2e-js/credito-embebido-inicializacion.spec.js
 * Contra otra carpeta de JS (p. ej. para reproducir el defecto anterior):  $env:CREDITO_JS_DIR = "ruta\al\viejo\js"
 */
const { test, expect } = require('playwright/test');
const fs = require('fs');
const path = require('path');

const ORIGIN = 'http://harness.test';
const JS_DIR = process.env.CREDITO_JS_DIR || path.join(__dirname, '..', 'wwwroot', 'js');
const SCRIPTS = ['venta-module.js', 'venta-page-wizard.js', 'configurar-venta-credito.js', 'venta-credito-embebido.js'];

const SIM = (over = {}) => ({
    cuotaEstimada: 1000, tasaAplicada: 5, interesTotal: 100, totalVenta: 10000, anticipo: 0, montoFinanciado: 10000,
    gastosAdministrativos: 0, totalAPagar: 10100, cuotas: [], cuotasSinRecargo: [], fuentePorcentaje: 'Plan',
    semaforoEstado: 'sinDatos', ...over,
});

/** Fragmento que devolvería /Credito/ConfigurarVenta?embedded=true (solo los nodos que el JS consulta). */
function fragmento({ configurado, contrato = false, rowVersion = 'rv-1' }) {
    return `
<div data-credito-config data-credito-config-embedded>
  <input type="hidden" id="hdn-venta-row-version" value="${rowVersion}">
  <input type="hidden" name="__RequestVerificationToken" value="tok">
  <input type="hidden" id="hdn-monto-venta" name="Monto" value="10000">
  <input type="hidden" id="hdn-monto-financiado" name="MontoFinanciado" value="10000">
  <input type="hidden" id="hdn-venta-id" name="VentaId" value="1">
  <input type="hidden" name="CreditoId" value="5">
  <input type="hidden" name="ClienteId" value="1">
  <input id="txt-anticipo" name="Anticipo" value="0">
  <span id="txt-monto-financiado"></span>
  <input id="txt-cuotas" name="CantidadCuotas" value="12">
  <input id="txt-tasa" readonly>
  <input id="txt-gastos" name="GastosAdministrativos" value="0">
  <input id="txt-fecha-primera-cuota" name="FechaPrimeraCuota" value="2030-01-15">
  <span id="cuotas-rango-info"></span><span id="badge-tasa-fuente" class="hidden"></span>
  <span id="plan-cuota-estimada"></span><span id="plan-tasa"></span><span id="plan-interes"></span>
  <span id="plan-capital"></span><span id="plan-gastos"></span><span id="plan-total"></span>
  <div id="plan-fecha-container" class="hidden"><span id="plan-fecha-pago"></span></div>
  <div id="plan-simulando" class="hidden"></div><div id="plan-error" class="hidden" tabindex="-1"></div>
  <button type="button" id="btn-confirmar-credito" data-credito-embebido-confirmar>Confirmar crédito</button>
  <div id="credito-embebido-confirmar-feedback" class="hidden"></div>
  ${configurado ? '<span data-credito-embebido-ya-configurado></span><button type="button" data-credito-embebido-continuar-revision>Continuar</button>' : ''}
  <section data-credito-embebido-contrato-seccion>
    ${contrato ? '<a id="contrato-link" href="/c/1">Ver / imprimir contrato</a>'
        : '<button type="button" id="btn-generar-contrato-embebido" data-credito-embebido-generar-contrato>Generar contrato</button>'}
  </section>
</div>`;
}

function pagina({ configurado = true } = {}) {
    const seed = JSON.stringify({ id: 1, creditoId: 5, creditoConfigurado: configurado });
    return `<!doctype html><html><head><meta charset="utf-8"><style>.hidden{display:none}</style></head><body>
<div id="venta-edit-page">
  <form id="venta-form" data-credito-configurado="${configurado}">
    <input type="hidden" name="RowVersion" value="rv-0">
    <input type="hidden" id="hdn-cliente-id" value="1">
    <select id="select-tipo-pago" data-credito-personal-value="CP"><option value="CP" selected>Crédito Personal</option></select>
    <table><tbody id="tbody-detalles"><tr><td>producto</td></tr></tbody></table>
    <div role="tablist">
      <button type="button" data-step="cliente" id="step-btn-cliente">Cliente</button>
      <button type="button" data-step="productos" id="step-btn-productos">Productos</button>
      <button type="button" data-step="pago" id="step-btn-pago">Pago</button>
      <button type="button" data-step="credito" id="step-btn-credito" aria-selected="false">Crédito</button>
      <button type="button" data-step="revision" id="step-btn-revision">Revisión</button>
    </div>
    <p id="wizard-feedback" class="hidden" hidden></p>
    <section id="step-panel-cliente" class="hidden" hidden></section>
    <section id="step-panel-productos" class="hidden" hidden></section>
    <section id="step-panel-pago" class="hidden" hidden></section>
    <section id="step-panel-credito" class="hidden" hidden>
      <div id="credito-embebido-cargando" class="hidden"></div><div id="credito-embebido-error" class="hidden"></div>
      <div id="credito-embebido-contenedor"></div>
    </section>
    <section id="step-panel-revision" class="hidden" hidden><div id="revision-credito-contrato-slot"></div></section>
  </form>
  <input type="hidden" id="venta-inicial-json" value='${seed}'>
</div>
<script>
  window.TheBury = { formatCurrency: function (v) { return '$' + Number(v).toFixed(2); } };
  window.__eventos = [];
  document.addEventListener('venta:credito-validado', function (e) { window.__eventos.push(e.detail.configurado); });
</script>
${SCRIPTS.map(s => `<script src="${ORIGIN}/js/${s}"></script>`).join('\n')}
</body></html>`;
}

/** Compuerta controlable: la respuesta HTTP no se entrega hasta llamar a release(). */
function gate() {
    let release;
    const promise = new Promise((r) => { release = r; });
    return { promise, release };
}

/**
 * Monta la página y el "servidor". `servidor` es mutable: cada test decide qué responde cada endpoint.
 * Registra las solicitudes en `calls`.
 */
async function montar(page, { configurado = true, servidor = {} } = {}) {
    const calls = [];
    const s = {
        fragmentos: [fragmento({ configurado })],           // se consumen en orden; el último se repite
        simular: async () => SIM(),
        confirmar: async () => ({ success: true }),
        generar: async () => ({ success: true, verUrl: '/c/1' }),
        ...servidor,
    };
    let nFrag = 0;

    await page.route(`${ORIGIN}/**`, async (route) => {
        const url = new URL(route.request().url());
        const p = url.pathname;
        if (p === '/') return route.fulfill({ contentType: 'text/html', body: pagina({ configurado }) });
        if (p.startsWith('/js/')) {
            return route.fulfill({ contentType: 'application/javascript', body: fs.readFileSync(path.join(JS_DIR, p.slice(4)), 'utf8') });
        }
        calls.push({ method: route.request().method(), path: p, search: url.search });
        const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
        if (p === '/Credito/ConfigurarVenta' && route.request().method() === 'GET') {
            const html = s.fragmentos[Math.min(nFrag++, s.fragmentos.length - 1)];
            return route.fulfill({ contentType: 'text/html', body: typeof html === 'function' ? await html() : html });
        }
        if (p === '/Credito/ConfigurarVenta') return json(await s.confirmar(url));
        if (p === '/Credito/SimularPlanVenta') return json(await s.simular(url));
        if (p === '/ContratoVentaCredito/Generar') return json(await s.generar());
        return route.fulfill({ status: 404, body: '' });
    });

    await page.goto(`${ORIGIN}/`);
    return { calls, eventos: () => page.evaluate(() => window.__eventos.slice()) };
}

const pasoActivo = (page) => page.evaluate(() => document.querySelector('[data-step][aria-selected="true"]')?.getAttribute('data-step'));
const tabDeshabilitada = (page, step) => page.evaluate((st) => document.getElementById('step-btn-' + st).disabled, step);

/** Lleva el wizard a un paso como lo haría el operador (clic en la pestaña). */
async function irA(page, step) {
    await page.click(`#step-btn-${step}`);
    await expect.poll(() => pasoActivo(page)).toBe(step);
}

/**
 * Abre Revisión y espera a que el fragmento de Crédito termine de cargarse y de simular. Se afirma el paso
 * DESPUÉS de esa espera: con el defecto, el wizard entra en Revisión y, al inicializar el fragmento, lo expulsa.
 */
async function abrirRevisionYEsperarCarga(page) {
    await page.click('#step-btn-revision');
    await expect(page.locator('#revision-credito-contrato-slot [data-credito-embebido-generar-contrato]')).toBeAttached();
    await expect(page.locator('#btn-confirmar-credito')).toBeEnabled();
    expect(await pasoActivo(page), 'el operador sigue en Revisión tras cargar el fragmento').toBe('revision');
}

test.describe('Crédito embebido: inicialización vs. cambios reales', () => {
    test('cargar el fragmento de un crédito ya configurado no lo invalida', async ({ page }) => {
        const { eventos } = await montar(page);
        await irA(page, 'credito');
        await expect(page.locator('[data-credito-embebido-ya-configurado]')).toBeAttached();
        await expect(page.locator('#btn-confirmar-credito')).toBeEnabled(); // la simulación inicial terminó

        expect(await eventos(), 'ningún evento configurado:false al inicializar').not.toContain(false);
        expect(await tabDeshabilitada(page, 'revision')).toBe(false);
    });

    test('abrir Revisión (carga del fragmento) no expulsa al operador de Revisión', async ({ page }) => {
        const { eventos } = await montar(page);
        await abrirRevisionYEsperarCarga(page);
        expect(await eventos()).not.toContain(false);
    });

    test('generar el contrato desde Revisión recarga el fragmento sin saltar a Crédito', async ({ page }) => {
        const { calls, eventos } = await montar(page, {
            servidor: { fragmentos: [fragmento({ configurado: true }), fragmento({ configurado: true, contrato: true, rowVersion: 'rv-2' })] },
        });
        await abrirRevisionYEsperarCarga(page);
        await page.click('#revision-credito-contrato-slot [data-credito-embebido-generar-contrato]');

        await expect(page.locator('#contrato-link')).toBeAttached(); // fragmento recargado con el contrato
        await expect(page.locator('#btn-confirmar-credito')).toBeEnabled();
        expect(calls.filter(c => c.path === '/ContratoVentaCredito/Generar')).toHaveLength(1);
        expect(calls.filter(c => c.path === '/Credito/ConfigurarVenta' && c.method === 'GET')).toHaveLength(2);

        expect(await pasoActivo(page), 'sigue en Revisión').toBe('revision');
        expect(await eventos()).not.toContain(false);
        expect(await tabDeshabilitada(page, 'revision')).toBe(false);
    });

    test('RowVersion se resincroniza desde el fragmento recargado', async ({ page }) => {
        await montar(page, {
            servidor: { fragmentos: [fragmento({ configurado: true }), fragmento({ configurado: true, contrato: true, rowVersion: 'rv-2' })] },
        });
        await abrirRevisionYEsperarCarga(page);
        await expect(page.locator('[name="RowVersion"]')).toHaveValue('rv-1');
        await page.click('#revision-credito-contrato-slot [data-credito-embebido-generar-contrato]');
        await expect(page.locator('#contrato-link')).toBeAttached();
        await expect(page.locator('[name="RowVersion"]')).toHaveValue('rv-2');
    });

    for (const [campo, selector, valor, evento] of [
        ['cuotas', '#txt-cuotas', '6', 'input'],
        ['anticipo', '#txt-anticipo', '500', 'input'],
        ['gastos administrativos', '#txt-gastos', '50', 'input'],
        ['fecha de primera cuota', '#txt-fecha-primera-cuota', '2030-02-20', 'change'],
    ]) {
        test(`un cambio real de ${campo} sí invalida la configuración`, async ({ page }) => {
            const { eventos } = await montar(page);
            await irA(page, 'credito');
            await expect(page.locator('#btn-confirmar-credito')).toBeEnabled();
            expect(await eventos()).not.toContain(false);

            await page.locator(selector).evaluate((el, [v, ev]) => {
                el.value = v;
                el.dispatchEvent(new Event(ev, { bubbles: true }));
            }, [valor, evento]);

            await expect.poll(() => eventos()).toContain(false);
            expect(await tabDeshabilitada(page, 'revision'), 'Revisión queda bloqueada hasta reconfirmar').toBe(true);
            // y la simulación se vuelve a pedir y vuelve a habilitar Confirmar
            await expect(page.locator('#btn-confirmar-credito')).toBeEnabled();
        });
    }

    test('la simulación inicial se ejecuta una vez, con ventaId, y pinta lo que manda el servidor', async ({ page }) => {
        const { calls } = await montar(page, { servidor: { simular: async () => SIM({ cuotaEstimada: 1234.5, tasaAplicada: 7 }) } });
        await irA(page, 'credito');
        await expect(page.locator('#plan-cuota-estimada')).toHaveText('$1234.50');
        await expect(page.locator('#txt-tasa')).toHaveValue('7.00');

        const sims = calls.filter(c => c.path === '/Credito/SimularPlanVenta');
        expect(sims).toHaveLength(1);
        expect(new URLSearchParams(sims[0].search).get('ventaId')).toBe('1');
    });

    test('un crédito NO configurado sigue sin habilitar Revisión tras inicializar', async ({ page }) => {
        const { eventos } = await montar(page, { configurado: false, servidor: { fragmentos: [fragmento({ configurado: false })] } });
        await irA(page, 'credito');
        await expect(page.locator('#btn-confirmar-credito')).toBeEnabled();
        expect(await tabDeshabilitada(page, 'revision')).toBe(true);
        expect(await eventos()).not.toContain(true);
    });

    test('"Confirmar crédito" solo marca configurado si el servidor lo confirma', async ({ page }) => {
        // 1) el servidor rechaza: el fragmento recargado sigue sin estar configurado y no se emite true.
        const { calls, eventos } = await montar(page, {
            configurado: false,
            servidor: { fragmentos: [fragmento({ configurado: false })], confirmar: async () => ({ success: false, message: 'Cupo insuficiente' }) },
        });
        await irA(page, 'credito');
        await expect(page.locator('#btn-confirmar-credito')).toBeEnabled();
        await page.click('#btn-confirmar-credito');
        await expect(page.locator('#credito-embebido-error')).toContainText('Cupo insuficiente');
        expect(calls.filter(c => c.path === '/Credito/ConfigurarVenta' && c.method === 'POST')).toHaveLength(1);
        expect(calls.filter(c => c.path === '/Credito/ConfigurarVenta' && c.method === 'GET')).toHaveLength(1); // sin recarga
        expect(await eventos()).not.toContain(true);
        expect(await tabDeshabilitada(page, 'revision')).toBe(true);
    });

    test('"Confirmar crédito" aceptado por el servidor: el fragmento recargado decide y habilita Revisión', async ({ page }) => {
        const { eventos } = await montar(page, {
            configurado: false,
            servidor: { fragmentos: [fragmento({ configurado: false }), fragmento({ configurado: true, rowVersion: 'rv-2' })] },
        });
        await irA(page, 'credito');
        await expect(page.locator('#btn-confirmar-credito')).toBeEnabled();
        await page.click('#btn-confirmar-credito');
        await expect(page.locator('[data-credito-embebido-ya-configurado]')).toBeAttached();
        await expect.poll(() => tabDeshabilitada(page, 'revision')).toBe(false);
        expect(await eventos()).toEqual([true]);
        expect(await pasoActivo(page)).toBe('credito');
    });

    test('una simulación tardía (fuera de orden) no pisa a la más nueva', async ({ page }) => {
        const vieja = gate();
        let n = 0;
        const { calls } = await montar(page, {
            servidor: {
                simular: async (url) => {
                    const mi = ++n;
                    if (mi === 1) { await vieja.promise; return SIM({ cuotaEstimada: 111 }); } // la inicial queda colgada
                    return SIM({ cuotaEstimada: 222 });
                },
            },
        });
        await irA(page, 'credito');
        await expect.poll(() => calls.filter(c => c.path === '/Credito/SimularPlanVenta').length).toBe(1);

        // el operador cambia las cuotas -> segunda simulación, responde primero
        await page.locator('#txt-cuotas').evaluate((el) => { el.value = '6'; el.dispatchEvent(new Event('input', { bubbles: true })); });
        await expect(page.locator('#plan-cuota-estimada')).toHaveText('$222.00');

        vieja.release(); // llega la respuesta obsoleta
        await page.waitForTimeout(150); // margen para que, si se pintara, se vea (no hay evento observable que esperar)
        await expect(page.locator('#plan-cuota-estimada')).toHaveText('$222.00');
        await expect(page.locator('#btn-confirmar-credito')).toBeEnabled();
    });
});

test.describe('Crédito standalone (ConfigurarVenta_tw)', () => {
    test('sin onCambioPlan: simula al cargar y reacciona a cambios', async ({ page }) => {
        const calls = [];
        await page.route(`${ORIGIN}/**`, async (route) => {
            const url = new URL(route.request().url());
            if (url.pathname === '/') {
                return route.fulfill({
                    contentType: 'text/html',
                    body: `<!doctype html><html><head><meta charset="utf-8"></head><body>${fragmento({ configurado: false }).replace('data-credito-config-embedded', '')}
<script>window.TheBury = { formatCurrency: function (v) { return '$' + Number(v).toFixed(2); } };</script>
<script src="${ORIGIN}/js/configurar-venta-credito.js"></script></body></html>`,
                });
            }
            if (url.pathname.startsWith('/js/')) {
                return route.fulfill({ contentType: 'application/javascript', body: fs.readFileSync(path.join(JS_DIR, url.pathname.slice(4)), 'utf8') });
            }
            calls.push(url.pathname + url.search);
            return route.fulfill({ contentType: 'application/json', body: JSON.stringify(SIM({ cuotaEstimada: 333 })) });
        });
        const errores = [];
        page.on('pageerror', (e) => errores.push(String(e)));

        await page.goto(`${ORIGIN}/`);
        await expect(page.locator('#plan-cuota-estimada')).toHaveText('$333.00');
        await expect(page.locator('#btn-confirmar-credito')).toBeEnabled();

        await page.locator('#txt-cuotas').evaluate((el) => { el.value = '6'; el.dispatchEvent(new Event('input', { bubbles: true })); });
        await expect.poll(() => calls.length).toBe(2);
        expect(errores).toEqual([]);
    });
});

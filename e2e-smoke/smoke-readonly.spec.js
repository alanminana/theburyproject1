// @ts-check
/**
 * QA fase 1 — smoke funcional + responsive de SOLO LECTURA.
 *
 * Contrato de seguridad:
 *  - Solo page.goto (GET). Ningún click, fill, submit ni request de escritura.
 *  - Toda ruta incluida fue revisada en su controller: sus GET no llaman métodos que persistan
 *    (excluida a propósito: /Credito/CuotasVencidas, cuyo GET llama AplicarAlertasMoraAsync).
 *  - Los IDs de detalle se toman de los propios índices, nunca hardcodeados.
 *
 * Por ruta valida: status HTTP del documento, que no redirija a login, título de error,
 * título de página visible, landmark principal, errores de consola/página, respuestas
 * same-origin >= 400 y desbordamiento horizontal. Guarda captura en qa-evidence/smoke-readonly/<project>/.
 */
const { test, expect } = require('playwright/test');
const path = require('path');
const fs = require('fs');

const OUT = path.join(process.cwd(), 'qa-evidence', 'smoke-readonly');

/** @typedef {{ name: string, url: string | ((page: import('playwright/test').Page) => Promise<string|null>), title: RegExp, landmark?: (page: import('playwright/test').Page) => import('playwright/test').Locator }} Ruta */

/** Toma el primer href que matchee `re` en `fromUrl` (GET de solo lectura). */
function primerDetalle(fromUrl, re) {
    return async (page) => {
        await page.goto(fromUrl, { waitUntil: 'domcontentloaded' });
        const hrefs = await page.locator('a[href]').evaluateAll(as => as.map(a => a.getAttribute('href') || ''));
        return hrefs.find(h => re.test(h)) ?? null;
    };
}

/** @type {Record<string, Ruta[]>} */
const MODULOS = {
    Dashboard: [
        { name: 'dashboard', url: '/Dashboard', title: /Dashboard/, landmark: p => p.getByRole('heading', { name: 'Accesos rápidos' }) },
    ],
    Venta: [
        { name: 'venta-index', url: '/Venta', title: /Centro de Ventas/, landmark: p => p.getByRole('heading', { name: 'Centro de Ventas' }) },
        { name: 'venta-create', url: '/Venta/Create', title: /Nueva Venta|Centro de Ventas/, landmark: p => p.getByText('Nueva Venta').first() },
        { name: 'venta-details', url: primerDetalle('/Venta', /^\/Venta\/Details\/\d+$/), title: /Detalle|Venta/, landmark: p => p.locator('main h1').first() },
    ],
    Caja: [
        { name: 'caja-index', url: '/Caja', title: /Cajas/, landmark: p => p.getByRole('heading', { name: 'Cajas', exact: true }) },
        { name: 'caja-historial', url: '/Caja/Historial', title: /Historial de Cierres/, landmark: p => p.getByRole('heading', { name: 'Historial de cierres' }) },
        { name: 'caja-detalle-apertura', url: primerDetalle('/Caja', /^\/Caja\/DetallesApertura\/\d+$/), title: /caja|apertura/i },
    ],
    Credito: [
        { name: 'credito-index', url: '/Credito', title: /Créditos/, landmark: p => p.getByRole('heading', { name: 'Cartera de créditos' }) },
        { name: 'credito-details', url: primerDetalle('/Cliente/Details/1', /^\/Credito\/Details\/\d+$/), title: /Crédito|Credito/ },
    ],
    Cotizacion: [
        { name: 'cotizacion-simulador', url: '/Cotizacion', title: /Cotización/, landmark: p => p.getByRole('heading', { name: 'Productos' }) },
        { name: 'cotizacion-listado', url: '/Cotizacion/Listado', title: /Cotizaciones guardadas/, landmark: p => p.getByRole('heading', { name: 'Cotizaciones guardadas' }) },
        { name: 'cotizacion-detalles', url: primerDetalle('/Cotizacion/Listado', /^\/Cotizacion\/Detalles\/\d+$/), title: /Cotizaci/ },
    ],
    Producto: [
        { name: 'catalogo-inventario', url: '/Catalogo', title: /Inventario/, landmark: p => p.getByRole('tab', { name: /Productos/ }) },
        { name: 'producto-index-redirect', url: '/Producto', title: /Inventario/, landmark: p => p.getByRole('tab', { name: /Productos/ }) },
        { name: 'producto-unidades-global', url: '/Producto/UnidadesGlobal', title: /Inventario global/, landmark: p => p.getByRole('heading', { name: 'Inventario global' }) },
    ],
    Cliente: [
        { name: 'cliente-index', url: '/Cliente', title: /Gestión de Clientes/, landmark: p => p.getByRole('heading', { name: 'Listado de clientes' }) },
        { name: 'cliente-details', url: primerDetalle('/Cliente', /^\/Cliente\/Details\/\d+$/), title: /Cliente/, landmark: p => p.getByRole('tab', { name: /Resumen/ }) },
    ],
    Seguridad: [
        { name: 'seguridad-index', url: '/Seguridad', title: /Seguridad/, landmark: p => p.locator('main table').first() },
        { name: 'seguridad-auditoria', url: '/Seguridad/Auditoria', title: /Seguridad/ },
    ],
    Mora: [
        { name: 'mora-index', url: '/Mora', title: /Mora/ },
        { name: 'mora-clientes', url: '/Mora/ClientesMora', title: /Mora/ },
        { name: 'mora-alertas', url: '/Mora/Alertas', title: /Mora|Alertas/ },
        { name: 'mora-logs', url: '/Mora/Logs', title: /Mora|Logs/ },
        { name: 'mora-configuracion-expandida', url: '/Mora/ConfiguracionExpandida', title: /mora/i, landmark: p => p.getByRole('heading', { name: 'Configuración de mora' }) },
    ],
};

test.beforeEach(async ({ page }) => {
    // Igual que ui-4e: Google Fonts fuera para no depender de red externa.
    await page.route('**/fonts.googleapis.com/**', r => r.abort());
    await page.route('**/fonts.gstatic.com/**', r => r.abort());
});

for (const [modulo, rutas] of Object.entries(MODULOS)) {
    test.describe(modulo, () => {
        for (const ruta of rutas) {
            test(ruta.name, async ({ page, baseURL }, info) => {
                const origin = new URL(baseURL ?? 'http://127.0.0.1:18787').origin;
                const consola = /** @type {string[]} */ ([]);
                const http = /** @type {string[]} */ ([]);
                const escrituras = /** @type {string[]} */ ([]);

                // Bloqueo duro (activo desde antes de cualquier navegación, incluida la búsqueda de IDs):
                // todo request same-origin no-GET se aborta antes de salir, salvo el negotiate de
                // SignalR (/hubs/*: OnConnectedAsync solo agrega la conexión a un grupo en memoria).
                await page.route(u => u.origin === origin, route => {
                    const req = route.request();
                    if (['GET', 'HEAD', 'OPTIONS'].includes(req.method()) || new URL(req.url()).pathname.startsWith('/hubs/')) {
                        return route.continue();
                    }
                    escrituras.push(`${req.method()} ${req.url().replace(origin, '')}`);
                    return route.abort('blockedbyclient');
                });

                let url = typeof ruta.url === 'string' ? ruta.url : null;
                if (typeof ruta.url === 'function') {
                    url = await ruta.url(page);
                    test.skip(!url, `Sin datos: no hay enlace de detalle disponible para ${ruta.name}`);
                }
                info.annotations.push({ type: 'url', description: String(url) });

                page.on('console', m => {
                    if (m.type() !== 'error') return;
                    const src = m.location()?.url ?? '';
                    // Ruido propio del test: Google Fonts se aborta en beforeEach a propósito.
                    if (/fonts\.(googleapis|gstatic)\.com/.test(src)) return;
                    consola.push(`${m.text()} @ ${src.replace(origin, '')}`);
                });
                page.on('pageerror', e => consola.push('pageerror: ' + e.message));
                page.on('response', r => {
                    if (r.url().startsWith(origin) && r.status() >= 400) http.push(`${r.status()} ${r.request().method()} ${r.url().replace(origin, '')}`);
                });

                const resp = await page.goto(/** @type {string} */ (url), { waitUntil: 'load' });
                await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => null);

                const dir = path.join(OUT, info.project.name);
                fs.mkdirSync(dir, { recursive: true });
                await page.screenshot({ path: path.join(dir, `${ruta.name}.png`) });

                // 1. Navegación y HTTP
                expect.soft(resp?.status(), `HTTP del documento ${url}`).toBe(200);
                expect.soft(page.url(), 'No debe redirigir a login').not.toMatch(/\/Account\/Login/i);
                const title = await page.title();
                expect.soft(title, 'Título sin error').not.toMatch(/Error|Exception/i);
                expect.soft(title, 'Título esperado').toMatch(ruta.title);

                // 2. Elementos principales
                await expect.soft(page.locator('main'), 'Landmark <main> visible').toBeVisible();
                if (ruta.landmark) await expect.soft(ruta.landmark(page), 'Elemento principal del módulo visible').toBeVisible();

                // 3. Overflow horizontal (con detalle de culpables para el informe)
                const ov = await page.evaluate(() => {
                    const vw = window.innerWidth;
                    const sw = document.documentElement.scrollWidth;
                    const culpables = [...document.querySelectorAll('body *')]
                        .filter(e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.right > vw + 1; })
                        .slice(0, 5)
                        .map(e => `${e.tagName.toLowerCase()}${e.id ? '#' + e.id : ''}${typeof e.className === 'string' && e.className ? '.' + e.className.trim().split(/\s+/).slice(0, 2).join('.') : ''} right=${Math.round(e.getBoundingClientRect().right)}`);
                    return { vw, sw, culpables };
                });
                info.annotations.push({ type: 'overflow', description: JSON.stringify(ov) });
                expect.soft(ov.sw, `Sin scroll horizontal (scrollWidth ${ov.sw} vs viewport ${ov.vw}) ${ov.culpables.join(' | ')}`).toBeLessThanOrEqual(ov.vw + 1);

                // 4. Consola y red
                expect.soft(consola, 'Sin errores de consola').toEqual([]);
                expect.soft(http, 'Sin respuestas same-origin >= 400').toEqual([]);

                // 5. Garantía de solo lectura: ningún request mutante salió del navegador
                expect(escrituras, 'Ningún request no-GET al servidor').toEqual([]);
            });
        }
    });
}

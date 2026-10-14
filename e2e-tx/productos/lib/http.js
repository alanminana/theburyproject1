// @ts-check
const { AUTH_DIR } = require('./env');
const path = require('path');

const AUTH = n => path.join(AUTH_DIR, `${n}.json`);

/** Login por UI (acepta el interstitial de Terminos si aparece). */
async function loginUi(browser, user, pass, file) {
    const ctx = await browser.newContext({ baseURL: 'http://127.0.0.1:18092', locale: 'es-AR' });
    const page = await ctx.newPage();
    await page.goto('/Identity/Account/Login', { waitUntil: 'domcontentloaded' });
    await page.fill('#Input_UserName, input[name="Input.UserName"]', user);
    await page.fill('#input-password, input[name="Input.Password"]', pass);
    await page.click('button[type="submit"]');
    const nombre = page.locator('input[placeholder="Nombre y apellido"]');
    if (await nombre.isVisible({ timeout: 5000 }).catch(() => false)) {
        await nombre.fill(`QA ${user}`);
        await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
        await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
    }
    await page.waitForURL(u => !u.toString().toLowerCase().includes('/login'), { timeout: 20000 });
    await ctx.storageState({ path: file });
    await ctx.close();
}

/** Cliente HTTP autenticado con manejo de antiforgery. `request` = APIRequestContext con cookies. */
function client(request) {
    let tok = null;
    async function token() {
        if (tok) return tok;
        const r = await request.get('/Catalogo');
        const html = await r.text();
        const m = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/) ||
                  html.match(/value="([^"]+)"[^>]*name="__RequestVerificationToken"/);
        if (!m) throw new Error(`Sin antiforgery token (GET /Catalogo -> ${r.status()} ${r.url()})`);
        return (tok = m[1]);
    }
    return {
        token,
        get: (url, opts = {}) => request.get(url, { maxRedirects: 0, ...opts }),
        /** POST form-urlencoded (como los modales con FormData). */
        async form(url, data, opts = {}) {
            const t = await token();
            return request.post(url, {
                form: { ...data, __RequestVerificationToken: t },
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                maxRedirects: 0, ...opts,
            });
        },
        /** POST JSON con header antiforgery (como precio-aumento-modal.js). */
        async json(url, data, opts = {}) {
            const t = await token();
            return request.post(url, {
                data, headers: { RequestVerificationToken: t, 'Content-Type': 'application/json' },
                maxRedirects: 0, ...opts,
            });
        },
        raw: request,
    };
}

/** Payload minimo valido para Producto/CreateAjax. */
function productoForm(st, over = {}) {
    return {
        Codigo: '', Nombre: '', Descripcion: 'QA-TX',
        CategoriaId: st.categoriaId, MarcaId: st.marcaId,
        PrecioCompra: '1000', PrecioVenta: '1500', PorcentajeIVA: '21',
        CostoEnvio: '0', PercepcionesCompra: '0', OtrosCostosCompra: '0',
        ComisionPorcentaje: '0', StockActual: '0', StockMinimo: '0', Activo: 'true',
        ...over,
    };
}

module.exports = { AUTH, loginUi, client, productoForm };

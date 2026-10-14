// @ts-check
/**
 * Cliente HTTP autenticado (Playwright APIRequestContext) para la suite financiera.
 * Solo habla con la aplicación autorizada (BASE) y verifica ese destino antes de cada escritura.
 * Los formularios se envían como lo haría el navegador: GET para obtener el token antiforgery,
 * POST x-www-form-urlencoded. No se inventan endpoints: cada ruta sale de un controller real.
 */
const { request, chromium } = require('playwright/test');
const fs = require('fs');
const path = require('path');

const BASE = process.env.E2E_BASE_URL || 'http://127.0.0.1:18093';
const AUTORIZADA = 'http://127.0.0.1:18093';

function assertDestino() {
    if (BASE.replace(/\/$/, '') !== AUTORIZADA) {
        throw new Error(`Destino NO autorizado: ${BASE}. Solo se opera sobre ${AUTORIZADA}.`);
    }
}

/** Contraseña del SuperAdmin del stack QA-FIN: se lee del archivo de secretos local y NUNCA se imprime/loguea. */
function passSuperAdminQa() {
    try {
        const f = path.join(process.env.USERPROFILE || process.env.HOME || '', '.bury-qa', 'secrets', 'fin.env');
        const m = fs.readFileSync(f, 'utf8').match(/^ADMIN_PASSWORD=(.*)$/m);
        return m ? m[1].trim() : '';
    } catch { return ''; }
}

const USUARIOS = {
    super: { user: 'admin_qa_fin', get pass() { return passSuperAdminQa(); } },
    admin: { user: process.env.E2E_USER || 'administrador', pass: process.env.E2E_PASS || 'Admin123!' },
    cajero: { user: 'cajero', pass: 'Cajero123!' },
    vendedor: { user: 'vendedor', pass: 'Vendedor123!' },
    gerente: { user: 'gerente', pass: 'Gerente123!' },
};

/** @param {string} html */
function extraerToken(html) {
    const m = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)
        || html.match(/value="([^"]+)"[^>]*name="__RequestVerificationToken"/);
    return m ? m[1] : null;
}

/** Lista name="" de inputs/selects/textarea de un HTML (para descubrir contratos de formulario). */
function camposDeFormulario(html) {
    const out = new Set();
    for (const m of html.matchAll(/<(?:input|select|textarea)\b[^>]*\bname="([^"]+)"/g)) out.add(m[1]);
    return [...out];
}


const decodeHtml = v => v.replace(/&quot;/g, '"').replace(/&#x([0-9A-Fa-f]+);/g, (_, h) => String.fromCharCode(parseInt(h, 16)))
    .replace(/&#(\d+);/g, (_, d) => String.fromCharCode(Number(d))).replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
const flag = (attrs, n) => new RegExp('(^|\\s)' + n + '(\\s|=|/|$)').test(attrs);
const attr = (tag, name) => { const m = tag.match(new RegExp('\\b' + name + '="([^"]*)"')); return m ? decodeHtml(m[1]) : undefined; };

/**
 * Serializa un <form> del HTML como lo haría el navegador (inputs, checkboxes marcados, selects, textarea).
 * `contiene`: nombre de un campo que debe existir dentro del form elegido. Devuelve [[name, value], ...].
 */
function serializarForm(html, contiene) {
    const forms = [...html.matchAll(/<form\b[\s\S]*?<\/form>/g)].map(m => m[0]);
    const tieneCampo = f => !contiene || f.includes('name="' + contiene + '"');
    // Fragmentos AJAX (p. ej. ConfigurarVenta embebido) no traen <form>: se toma el HTML completo.
    const fuente = forms.find(tieneCampo) ?? (tieneCampo(html) ? html : undefined);
    if (!fuente) throw new Error(`Campo "${contiene}" no encontrado`);
    const out = [];
    for (const m of fuente.matchAll(/<(input|select|textarea)\b((?:[^>"']|"[^"]*"|'[^']*')*)>(?:([\s\S]*?)<\/\1>)?/g)) {
        const [, tagName, attrs, inner] = m;
        const tag = '<' + tagName + attrs + '>';
        const name = attr(tag, 'name');
        if (!name || flag(attrs, 'disabled')) continue;
        if (tagName === 'input') {
            const type = (attr(tag, 'type') || 'text').toLowerCase();
            if (['submit', 'button', 'image', 'file', 'reset'].includes(type)) continue;
            if ((type === 'checkbox' || type === 'radio') && !flag(attrs, 'checked')) continue;
            out.push([name, attr(tag, 'value') ?? (type === 'checkbox' ? 'on' : '')]);
        } else if (tagName === 'select') {
            const opts = [...(inner || '').matchAll(/<option\b((?:[^>"']|"[^"]*"|'[^']*')*)>/g)].map(o => o[1]);
            const sel = opts.find(o => flag(o, 'selected')) ?? opts[0];
            if (sel !== undefined) out.push([name, attr('<o ' + sel + '>', 'value') ?? '']);
        } else {
            out.push([name, decodeHtml((inner || '').replace(/^\r?\n/, ''))]);
        }
    }
    return out;
}

/** Aplica overrides (reemplaza todas las ocurrencias del name) y agrega los faltantes. */
function conOverrides(pares, overrides) {
    const claves = new Set(Object.keys(overrides));
    const res = pares.filter(([n]) => !claves.has(n));
    for (const [k, v] of Object.entries(overrides)) if (v !== null && v !== undefined) res.push([k, String(v)]);
    return res;
}

class Sesion {
    /** @param {import('playwright/test').APIRequestContext} ctx @param {string} rol */
    constructor(ctx, rol) { this.ctx = ctx; this.rol = rol; }

    /**
     * Login REAL con navegador (el sistema exige aceptar Términos y Condiciones en el primer ingreso),
     * guarda storageState por rol y devuelve un APIRequestContext autenticado con esas cookies.
     */
    static async login(rol = 'admin') {
        assertDestino();
        const dir = path.join(__dirname, '..', '.auth');
        fs.mkdirSync(dir, { recursive: true });
        const file = path.join(dir, `${rol}.json`);
        const { user, pass } = USUARIOS[rol];
        const browser = await chromium.launch({ channel: process.env.E2E_CHROME_CHANNEL || undefined });
        try {
            const page = await (await browser.newContext({ baseURL: BASE })).newPage();
            await page.goto('/Identity/Account/Login', { waitUntil: 'domcontentloaded' });
            await page.locator('input[autocomplete="username"]').fill(user);
            await page.locator('#input-password').fill(pass);
            await page.locator('button[type="submit"]').click();
            const nombre = page.locator('input[placeholder="Nombre y apellido"]');
            if (await nombre.isVisible({ timeout: 5_000 }).catch(() => false)) {
                await nombre.fill(`QA FIN ${user}`);
                await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
                await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
            }
            await page.waitForURL(u => !/[Ll]ogin|Terminos|Terms/i.test(u.pathname), { timeout: 20_000 });
            await page.context().storageState({ path: file });
        } finally { await browser.close(); }
        const ctx = await request.newContext({ baseURL: BASE, storageState: file });
        return new Sesion(ctx, rol);
    }

    async get(url, opts = {}) { return this.ctx.get(url, opts); }

    async html(url) {
        const r = await this.ctx.get(url);
        return { status: r.status(), url: r.url(), html: await r.text() };
    }

    /**
     * POST de formulario con token tomado de `urlForm` (la página que contiene el form).
     * @param {string} urlForm
     * @param {string} urlPost
     * @param {Record<string,string|number|boolean>} campos
     */
    async postForm(urlForm, urlPost, campos, opts = {}) {
        assertDestino();
        const g = await this.html(urlForm);
        const token = extraerToken(g.html);
        const r = await this.ctx.post(urlPost, {
            form: { ...Object.fromEntries(Object.entries(campos).map(([k, v]) => [k, String(v)])), __RequestVerificationToken: token ?? '' },
            maxRedirects: opts.maxRedirects ?? 0,
            headers: opts.headers,
        });
        return r;
    }

    /** POST de pares (permite nombres repetidos, p.ej. checkbox + hidden). */
    async postPares(urlPost, pares, opts = {}) {
        assertDestino();
        const body = new URLSearchParams();
        for (const [k, v] of pares) body.append(k, v);
        return this.ctx.post(urlPost, {
            data: body.toString(),
            headers: { 'content-type': 'application/x-www-form-urlencoded', ...(opts.headers || {}) },
            maxRedirects: opts.maxRedirects ?? 0,
        });
    }

    async dispose() { await this.ctx.dispose(); }
}

module.exports = { serializarForm, conOverrides, BASE, USUARIOS, Sesion, extraerToken, camposDeFormulario, assertDestino };

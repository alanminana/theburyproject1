// @ts-check
/**
 * QA-CFG-FIX — concurrencia de /Mora/ConfiguracionExpandida contra SQL Server REAL (stack cfg).
 * Ejecutar SOLO con qa-run.ps1 cfg -- node <playwright cli> test -c e2e-tx/config/playwright.config.js
 * Destino autorizado: 127.0.0.1:18094 (BuryQA_CFG). Solo se escribe en ConfiguracionesMora vía la app.
 */
const { test, expect, request, chromium } = require('playwright/test');
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const BASE = process.env.E2E_BASE_URL || 'http://127.0.0.1:18094';
const USER = process.env.E2E_USER || 'administrador';
const PASS = process.env.E2E_PASS || 'Admin123!';
const FORM = '/Mora/ConfiguracionExpandida';
const POST = '/Mora/GuardarConfiguracionExpandida';
const EVID = path.join(__dirname, 'evidencia');

test.beforeAll(() => {
    if (BASE.replace(/\/$/, '') !== 'http://127.0.0.1:18094') throw new Error(`Destino NO autorizado: ${BASE}`);
    fs.mkdirSync(EVID, { recursive: true });
});

function sql(q) {
    const b64 = Buffer.from(`SET NOCOUNT ON;\n${q}\n`).toString('base64');
    return execFileSync('docker', ['exec', 'bury-qa-cfg-db', 'bash', '-c',
        `export SQLCMDPASSWORD=$MSSQL_SA_PASSWORD; echo ${b64} | base64 -d | /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d BuryQA_CFG -s '|' -W -b -i /dev/stdin`],
        { encoding: 'utf8' }).trim();
}
const estado = () => sql('SELECT TOP 1 Id, TasaMoraBase, DiasGracia, TipoTasaMora, PorcentajeMaximoCondonacion, CONVERT(varchar(32), CAST(RowVersion AS varbinary(8)), 2) AS RowVersionHex FROM ConfiguracionesMora WHERE IsDeleted=0 ORDER BY Id');

async function login(nombre) {
    const file = path.join(EVID, `.auth-${nombre}.json`);
    const browser = await chromium.launch({ channel: process.env.E2E_CHROME_CHANNEL || undefined });
    try {
        const page = await (await browser.newContext({ baseURL: BASE })).newPage();
        await page.goto('/Identity/Account/Login', { waitUntil: 'domcontentloaded' });
        await page.locator('input[autocomplete="username"]').fill(USER);
        await page.locator('#input-password').fill(PASS);
        await page.locator('button[type="submit"]').click();
        const n = page.locator('input[placeholder="Nombre y apellido"]');
        if (await n.isVisible({ timeout: 5_000 }).catch(() => false)) {
            await n.fill('QA CFG');
            await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
            await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
        }
        await page.waitForURL(u => !/[Ll]ogin|Terminos|Terms/i.test(u.pathname), { timeout: 20_000 });
        await page.context().storageState({ path: file });
    } finally { await browser.close(); }
    return request.newContext({ baseURL: BASE, storageState: file });
}

const attr = (tag, n) => {
    const m = tag.match(new RegExp(`\\s${n}="([^"]*)"`, 'i'));
    return m ? m[1].replace(/&quot;/g, '"').replace(/&#x2B;/g, '+').replace(/&amp;/g, '&') : undefined;
};
async function leerForm(ctx) {
    const r = await ctx.get(FORM);
    expect(r.status()).toBe(200);
    const html = await r.text();
    const f = html.match(/<form[^>]*GuardarConfiguracionExpandida[^>]*>([\s\S]*?)<\/form>/i);
    expect(f, 'form de configuración expandida').toBeTruthy();
    const pares = [];
    for (const m of f[1].matchAll(/<input\b[^>]*>/gi)) {
        const tag = m[0]; const name = attr(tag, 'name'); if (!name) continue;
        const type = (attr(tag, 'type') || 'text').toLowerCase();
        if ((type === 'checkbox' || type === 'radio') && !/\schecked\b/i.test(tag)) continue;
        pares.push([name, attr(tag, 'value') ?? (type === 'checkbox' ? 'on' : '')]);
    }
    return pares;
}
const set = (pares, k, v) => {
    const i = pares.findIndex(p => p[0] === k);
    expect(i, `campo ${k}`).toBeGreaterThanOrEqual(0);
    pares[i] = [k, v];
};
const post = (ctx, pares) => ctx.post(POST, {
    data: new URLSearchParams(pares).toString(),
    headers: { 'content-type': 'application/x-www-form-urlencoded' }, maxRedirects: 0,
});
const decodeHtml = v => v.replace(/&#x([0-9A-Fa-f]+);/g, (_, h) => String.fromCharCode(parseInt(h, 16)))
    .replace(/&#(\d+);/g, (_, d) => String.fromCharCode(Number(d))).replace(/&quot;/g, '"').replace(/&amp;/g, '&');
// El resultado (éxito/error) viaja en TempData: se lee de la página a la que redirige.
async function mensaje(ctx, resp) {
    expect(resp.status()).toBe(302);
    const html = decodeHtml(await (await ctx.get(FORM)).text());
    const m = html.match(/(Configuración guardada correctamente|La configuración fue modificada por otro usuario[^<]*|Falta información de concurrencia[^<]*|Datos inválidos)/);
    return m ? m[1] : '(sin mensaje)';
}
const log = [];
const reg = (k, v) => { log.push(`${k}: ${v}`); fs.writeFileSync(path.join(EVID, 'sqlserver-evidencia.txt'), log.join('\n') + '\n'); };

test.describe.serial('CFG-12/13 SQL Server real', () => {
    let A, B;
    test.beforeAll(async () => {
        reg('destino', `${BASE} / BuryQA_CFG`);
        reg('identidad', sql('SELECT @@SERVERNAME AS srv, DB_NAME() AS db;').replace(/\n/g, ' | '));
        A = await login('a'); B = await login('b');
    });
    test.afterAll(async () => { await A?.dispose(); await B?.dispose(); });

    test('dos administradores: el segundo con formulario viejo es rechazado sin sobrescribir', async () => {
        const formA = await leerForm(A); const formB = await leerForm(B);
        const rvA = formA.find(p => p[0] === 'RowVersion')?.[1]; const rvB = formB.find(p => p[0] === 'RowVersion')?.[1];
        expect(rvA).toBeTruthy(); expect(rvA).toBe(rvB);
        reg('ANTES', '\n' + estado());

        set(formA, 'TasaMoraBase', '9');
        const msgA = await mensaje(A, await post(A, formA));
        reg('A guarda TasaMoraBase=9', msgA);
        reg('DESPUES de A', '\n' + estado());
        expect(msgA).toBe('Configuración guardada correctamente');

        set(formB, 'DiasGracia', '20');
        const msgB = await mensaje(B, await post(B, formB));
        reg('B (formulario viejo) guarda DiasGracia=20', msgB);
        reg('DESPUES de B', '\n' + estado());
        expect(msgB).toContain('modificada por otro usuario');
        const fila = estado();
        expect(fila).toMatch(/\|9(\.0+)?\|/);   // TasaMoraBase = 9: el cambio de A sobrevive
        expect(fila).not.toMatch(/\|20\|/);      // el de B no se persistió
    });

    test('formulario sin RowVersion no elude la protección', async () => {
        const f = await leerForm(A);
        const antes = estado();
        set(f, 'TasaMoraBase', '7');
        const sinRv = f.filter(p => p[0] !== 'RowVersion');
        const msg = await mensaje(A, await post(A, sinRv));
        reg('POST sin RowVersion', msg);
        expect(msg).toContain('Falta información de concurrencia');
        expect(estado()).toBe(antes);
    });

    test('recarga + reintento: con la versión nueva el guardado funciona', async () => {
        const f = await leerForm(B);
        set(f, 'DiasGracia', '12');
        const msg = await mensaje(B, await post(B, f));
        reg('B recarga y guarda DiasGracia=12', msg);
        reg('DESPUES (reintento)', '\n' + estado());
        expect(msg).toBe('Configuración guardada correctamente');
        expect(estado()).toMatch(/\|12\|/);
    });

    test('valor fuera de rango (100.01) se rechaza sin modificar datos', async () => {
        const f = await leerForm(A);
        const antes = estado();
        set(f, 'TasaMoraBase', '100.01');
        const msg = await mensaje(A, await post(A, f));
        reg('POST TasaMoraBase=100.01', msg);
        expect(msg).toBe('Datos inválidos');
        expect(estado()).toBe(antes);
    });
});

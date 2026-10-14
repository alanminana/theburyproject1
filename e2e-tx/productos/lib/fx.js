// @ts-check
const base = require('playwright/test');
const fs = require('fs');
const path = require('path');
const { readState, EVIDENCE_DIR } = require('./env');
const { AUTH, client } = require('./http');

const BASE = 'http://127.0.0.1:18092';

function roleClient(name) {
    return async ({ playwright }, use) => {
        const ctx = await playwright.request.newContext({ baseURL: BASE, storageState: AUTH(name) });
        await use(client(ctx));
        await ctx.dispose();
    };
}

/** Fixtures: st (estado global), admin/lector/oper (clientes HTTP), adminPage (UI). */
const test = base.test.extend({
    st: async ({}, use) => use(readState()),
    admin: roleClient('admin'),
    lector: roleClient('qaprd_lector'),
    oper: roleClient('qaprd_oper'),
    adminPage: async ({ browser }, use) => {
        const ctx = await browser.newContext({ baseURL: BASE, storageState: AUTH('admin'), locale: 'es-AR', viewport: { width: 1440, height: 900 } });
        const page = await ctx.newPage();
        await page.route(/fonts\.(googleapis|gstatic)\.com/, r => r.abort());
        await use(page);
        await ctx.close();
    },
});

/** Guarda antes/despues de una operacion como evidencia JSON por caso. */
function evidence(id, data) {
    const f = path.join(EVIDENCE_DIR, 'cases');
    fs.mkdirSync(f, { recursive: true });
    fs.writeFileSync(path.join(f, `${id}.json`), JSON.stringify(data, null, 2));
}

module.exports = { test, expect: base.expect, evidence };

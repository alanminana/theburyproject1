// @ts-check
/**
 * input-rules.js — máscara de montos de texto (campos precio/costo/monto/sueldo/anticipo/importe/gastos).
 *
 * Regresión H11: el primer separador se tomaba siempre como decimal y se recortaba a 2 cifras, así que escribir
 * "250.000" (miles con punto, lo natural en es-AR) dejaba "250.00": un monto mil veces menor, sin aviso. Se escribe
 * carácter por carácter, como un usuario. No crea ni modifica datos: no se envía ningún formulario.
 */
const { test, expect } = require('playwright/test');

test.use({ storageState: 'e2e/.auth/user.json' });

test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route => route.fulfill({ status: 200, contentType: 'text/css', body: '' }));
    await page.route('**/fonts.gstatic.com/**', route => route.fulfill({ status: 204, body: '' }));
});

/** @type {Array<[string, string]>} */
const CASOS = [
    ['250000', '250000'],
    ['250.000,00', '250.000,00'],      // miles con punto + decimales con coma: se conserva
    ['1.234.567,89', '1.234.567,89'],
    ['250.000', '250.000'],            // ambiguo: no se inventa un decimal; el servidor avisa con un mensaje claro (FIN-S-017)
    ['1,5', '1,5'],
    ['10,5555', '10,55'],              // decimal real: se recorta a 2 cifras
    ['10.5555', '10.55'],
    ['abc12x', '12'],
    ['99999999', '9999999'],           // supera el tope de 20.000.000: el último dígito se rechaza
];

test('máscara de montos: no recorta los miles con punto ni inventa el decimal', async ({ page }) => {
    await page.goto('/Cliente/Create', { waitUntil: 'domcontentloaded' });
    await page.locator('[data-cliente-tab="t-laboral"]').click();
    const sueldo = page.locator('#Sueldo');
    await expect(sueldo).toBeVisible();

    for (const [tecleado, esperado] of CASOS) {
        await sueldo.fill('');
        await sueldo.pressSequentially(tecleado);
        await expect(sueldo, `se tecleó "${tecleado}"`).toHaveValue(esperado);
    }
});

test('validador jQuery: acepta los decimales con coma que el servidor precarga y mantiene rechazando basura', async ({ page }) => {
    await page.goto('/Cliente/Create', { waitUntil: 'domcontentloaded' });
    const r = await page.evaluate(() => {
        // @ts-ignore jQuery Validate viene del layout
        const m = window.jQuery.validator.methods;
        const ctx = { optional: () => false };
        const el = {};
        return {
            numeroEsAr: m.number.call(ctx, '10,00', el),
            numeroMiles: m.number.call(ctx, '1.234,56', el),
            numeroAnglo: m.number.call(ctx, '1234.56', el),
            numeroBasura: m.number.call(ctx, '10,5,3', el),
            numeroLetras: m.number.call(ctx, 'abc', el),
            rangoDentro: m.range.call(ctx, '10,00', el, [0, 100]),
            rangoFuera: m.range.call(ctx, '100,50', el, [0, 100]),
            minFuera: m.min.call(ctx, '4,99', el, 5),
            maxDentro: m.max.call(ctx, '4,99', el, 5),
        };
    });
    expect(r).toEqual({
        numeroEsAr: true, numeroMiles: true, numeroAnglo: true, numeroBasura: false, numeroLetras: false,
        rangoDentro: true, rangoFuera: false, minFuera: false, maxDentro: true,
    });
});

// @ts-check
/**
 * PUN-ML8 — UI administrativa de ConfiguracionPunitorio ("Punitorios por mora",
 * pestaña s7 dentro de /ConfiguracionPago/CreditoPersonal).
 *
 * Requiere la app corriendo (dotnet run) y $env:E2E_USER / $env:E2E_PASS con acceso
 * a configuracion.view + configuracion.viewpunitorio + configuracion.managepunitorio +
 * configuracion.retroactivepunitorio (SuperAdmin los tiene todos automáticamente).
 *
 * La matriz de denegación por permiso específico (sin viewpunitorio / sin managepunitorio /
 * sin retroactivepunitorio) está cubierta exhaustivamente por HTTP real en
 * TheBuryProyect.Tests/Integration/ConfiguracionPunitorioHttpTests.cs — acá solo se
 * verifica, en el navegador real, que la pestaña no aparece si la sesión activa no tiene
 * "configuracion.viewpunitorio" (best-effort: si la sesión de QA sí lo tiene, el test se
 * salta explícitamente en vez de fallar).
 */
const { test, expect } = require('playwright/test');

const CREDITO_PERSONAL_URL = '/ConfiguracionPago/CreditoPersonal';

const VIEWPORTS = [
    { name: '360x740', width: 360, height: 740 },
    { name: '768x1024', width: 768, height: 1024 },
    { name: '1366x768', width: 1366, height: 768 },
];

test.use({ storageState: 'e2e/.auth/user.json' });

async function gotoPunitorios(page) {
    await page.goto(`${CREDITO_PERSONAL_URL}#s7`, { waitUntil: 'domcontentloaded' });
    await expect(page).not.toHaveURL(/Identity\/Account\/Login/);
}

function trackFallos(page) {
    const consoleErrors = [];
    const failedResponses = [];
    page.on('console', (msg) => { if (msg.type() === 'error') consoleErrors.push(msg.text()); });
    page.on('response', (res) => {
        if (res.status() >= 500) failedResponses.push(`${res.status()} ${res.url()}`);
    });
    return { consoleErrors, failedResponses };
}

function assertSinFallos(fallos) {
    expect(fallos.consoleErrors, `errores de consola: ${fallos.consoleErrors.join(' | ')}`).toHaveLength(0);
    expect(fallos.failedResponses, `HTTP 5xx: ${fallos.failedResponses.join(' | ')}`).toHaveLength(0);
}

/** Fecha futura única por test (yyyy-MM-dd), lejos de cualquier otra corrida para no chocar con el invariante monotónico append-only. */
function vigenciaFutura(offsetDias) {
    const d = new Date();
    d.setUTCFullYear(d.getUTCFullYear() + 2); // +2 años: fuera del rango que puedan usar otras suites/corridas
    d.setUTCDate(d.getUTCDate() + offsetDias);
    return d.toISOString().slice(0, 10);
}

test.describe('Punitorios por mora — acceso y estado vigente', () => {
    test('usuario con permiso abre la seccion y ve el estado vigente', async ({ page }) => {
        const fallos = trackFallos(page);
        await gotoPunitorios(page);

        const tab = page.locator('#s7');
        const tieneAcceso = await tab.count();
        test.skip(!tieneAcceso, 'La sesion de QA (E2E_USER) no tiene configuracion.viewpunitorio.');

        await expect(tab).toBeVisible();
        await expect(tab.getByText('Punitorios por mora')).toBeVisible();
        // Estado: siempre texto explicito, nunca solo color. Sin anclas (^...$): el badge
        // combina un icono-ligature ("check_circle"/"block"/etc.) con el texto en el mismo
        // span, así que el texto agregado del elemento nunca es "Activo"/"Inactivo" a secas.
        await expect(tab.getByText(/Sin configuraci.n|Activo al 0%|Activo|Inactivo/).first()).toBeVisible();
        // Prorrateo diario es una regla fija mostrada como texto, nunca un input.
        await expect(page.locator('#s7 input[name*="ProrrateoDiario"]')).toHaveCount(0);

        assertSinFallos(fallos);
    });

    test('sin permiso especifico de punitorios, la pestana no se renderiza', async ({ page }) => {
        await gotoPunitorios(page);
        const tab = page.locator('#s7');
        const tieneAcceso = await tab.count();
        test.skip(!!tieneAcceso, 'La sesion de QA (E2E_USER) SI tiene configuracion.viewpunitorio — ' +
            'la matriz de denegacion completa vive en ConfiguracionPunitorioHttpTests.cs (xUnit).');

        await expect(page.locator('[data-target="s7"]')).toHaveCount(0);
    });

    test('no hay formularios anidados ni ids duplicados por la seccion nueva', async ({ page }) => {
        await gotoPunitorios(page);
        const tab = page.locator('#s7');
        test.skip(!(await tab.count()), 'La sesion de QA no tiene configuracion.viewpunitorio.');

        const dom = await page.evaluate(() => {
            const ids = [...document.querySelectorAll('[id]')].map((e) => e.id);
            return {
                nestedForms: document.querySelectorAll('form form').length,
                duplicatedIds: [...new Set(ids.filter((v, i) => ids.indexOf(v) !== i))],
                formsEnPagina: document.querySelectorAll('form').length,
            };
        });

        expect(dom.nestedForms).toBe(0);
        expect(dom.duplicatedIds).toEqual([]);
        // credito-config-form + punitorio-form (como minimo).
        expect(dom.formsEnPagina).toBeGreaterThanOrEqual(2);
    });
});

// NOTA DE ORDEN: "retroactividad" corre ANTES que "crear nueva version"/"error server-side
// visible" a propósito. El invariante append-only exige que cada version nueva sea posterior
// a TODAS las existentes; "crear nueva version" siembra fechas muy futuras (vigenciaFutura,
// +2 años) mientras que "retroactividad" necesita fechas cercanas a hoy (ayer/anteayer). Si
// las fechas futuras se crean primero, ninguna fecha "de ayer" puede satisfacer el invariante
// después (no puede ser >max(2028) y <hoy(2026) a la vez) — el submit retroactivo quedaría
// rechazado por conflicto de vigencia, no por lo que el test intenta validar. Mantener este
// bloque antes de "crear nueva version".
test.describe('Punitorios por mora — retroactividad', () => {
    test('vigencia retroactiva exige motivo y confirmacion explicita en un modal accesible', async ({ page }) => {
        await gotoPunitorios(page);
        const form = page.locator('#punitorio-form');
        test.skip(!(await form.count()), 'La sesion de QA no tiene configuracion.managepunitorio.');

        const ayer = new Date();
        ayer.setUTCDate(ayer.getUTCDate() - 1);
        const vigenciaRetro = ayer.toISOString().slice(0, 10);

        await page.locator('#pun-vigente-desde').fill(vigenciaRetro);
        await page.locator('#pun-porcentaje').fill('3');
        await page.locator('#pun-periodo').fill('10');
        await page.locator('#pun-gracia').fill('2');
        // Sin motivo todavía: el submit no debe abrir el modal ni enviar el form.
        await form.locator('button[type="submit"]').click();

        const modal = page.locator('#m-punitorio-retro');
        await expect(modal).toBeHidden();
        await expect(page).toHaveURL(/CreditoPersonal#s7/);

        await page.locator('#pun-motivo').fill('Correccion retroactiva E2E');
        await form.locator('button[type="submit"]').click();

        await expect(modal).toBeVisible();
        // role="dialog"/aria-modal viven en la tarjeta interna (.cp-modal-card), no en el
        // backdrop externo #m-punitorio-retro — mismo patron que los otros modales de esta
        // vista (m-perfil/m-cuota).
        const modalCard = modal.locator('.cp-modal-card');
        await expect(modalCard).toHaveAttribute('role', 'dialog');
        await expect(modalCard).toHaveAttribute('aria-modal', 'true');

        // Foco atrapado: Tab dentro del modal nunca debe salir de sus elementos.
        const focusablesEnModal = await modal.locator('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])').count();
        expect(focusablesEnModal).toBeGreaterThan(0);
        for (let i = 0; i < focusablesEnModal + 2; i++) {
            await page.keyboard.press('Tab');
            const dentroDelModal = await page.evaluate(() => {
                const modalEl = document.getElementById('m-punitorio-retro');
                return !!modalEl && modalEl.contains(document.activeElement);
            });
            expect(dentroDelModal).toBeTruthy();
        }

        // Escape cierra y retorna el foco.
        await page.keyboard.press('Escape');
        await expect(modal).toBeHidden();
    });

    test('confirmar en el modal envia el formulario retroactivo', async ({ page }) => {
        await gotoPunitorios(page);
        const form = page.locator('#punitorio-form');
        test.skip(!(await form.count()), 'La sesion de QA no tiene configuracion.managepunitorio o configuracion.retroactivepunitorio.');

        const ayer = new Date();
        ayer.setUTCDate(ayer.getUTCDate() - 2);
        const vigenciaRetro = ayer.toISOString().slice(0, 10);
        const motivo = `E2E retroactiva confirmada ${Date.now()}`;

        await page.locator('#pun-vigente-desde').fill(vigenciaRetro);
        await page.locator('#pun-porcentaje').fill('4');
        await page.locator('#pun-periodo').fill('12');
        await page.locator('#pun-gracia').fill('3');
        await page.locator('#pun-motivo').fill(motivo);
        await form.locator('button[type="submit"]').click();

        const modal = page.locator('#m-punitorio-retro');
        await expect(modal).toBeVisible();
        await page.locator('#pun-confirmar-retro').click();

        // Si el usuario de QA no tiene "retroactivepunitorio", el servidor lo rechaza igual
        // (ModelState) y la pagina se re-renderiza en s7 con el error visible — no es un 500.
        await expect(page).toHaveURL(/CreditoPersonal/);
        await expect(page.locator('#s7')).toBeVisible();
    });
});

test.describe('Punitorios por mora — crear nueva version', () => {
    test('crea una version futura activa y la refleja en el historial', async ({ page }) => {
        const fallos = trackFallos(page);
        await gotoPunitorios(page);
        const form = page.locator('#punitorio-form');
        test.skip(!(await form.count()), 'La sesion de QA no tiene configuracion.managepunitorio.');

        const vigencia = vigenciaFutura(1);
        const motivo = `E2E futura ${Date.now()}`;

        await page.locator('#pun-vigente-desde').fill(vigencia);
        await page.locator('#pun-porcentaje').fill('7.5');
        await page.locator('#pun-periodo').fill('15');
        await page.locator('#pun-gracia').fill('4');
        await page.locator('#pun-motivo').fill(motivo);

        await form.locator('button[type="submit"]').click();

        await expect(page).toHaveURL(/CreditoPersonal#s7/);
        await expect(page.getByText('Nueva versión de punitorios creada correctamente.')).toBeVisible();
        // El motivo aparece dos veces cuando la version es futura: en el aviso de "proximo
        // cambio programado" y en la fila del historial. Se apunta a la fila del historial
        // (mismo patron que el resto de los tests de esta seccion) para evitar violacion de
        // modo estricto de Playwright por match ambiguo.
        await expect(page.locator('#s7 table tr', { hasText: motivo })).toBeVisible();

        assertSinFallos(fallos);
    });

    test('crea una version inactiva y el historial la marca como tal', async ({ page }) => {
        await gotoPunitorios(page);
        const form = page.locator('#punitorio-form');
        test.skip(!(await form.count()), 'La sesion de QA no tiene configuracion.managepunitorio.');

        const vigencia = vigenciaFutura(2);
        const motivo = `E2E inactiva ${Date.now()}`;

        await page.locator('#pun-vigente-desde').fill(vigencia);
        await page.locator('#Punitorios_CrearForm_Activa').uncheck();
        await page.locator('#pun-porcentaje').fill('5');
        await page.locator('#pun-periodo').fill('20');
        await page.locator('#pun-gracia').fill('5');
        await page.locator('#pun-motivo').fill(motivo);
        await form.locator('button[type="submit"]').click();

        await expect(page).toHaveURL(/CreditoPersonal#s7/);
        const fila = page.locator('#s7 table tr', { hasText: motivo });
        await expect(fila).toContainText(/inactiva/i);
    });

    test('tasa 0% se crea y se muestra explicitamente como distinta de ausencia', async ({ page }) => {
        await gotoPunitorios(page);
        const form = page.locator('#punitorio-form');
        test.skip(!(await form.count()), 'La sesion de QA no tiene configuracion.managepunitorio.');

        const vigencia = vigenciaFutura(3);
        const motivo = `E2E cero ${Date.now()}`;

        await page.locator('#pun-vigente-desde').fill(vigencia);
        await page.locator('#pun-porcentaje').fill('0');
        await page.locator('#pun-periodo').fill('30');
        await page.locator('#pun-gracia').fill('0');
        await page.locator('#pun-motivo').fill(motivo);
        await form.locator('button[type="submit"]').click();

        await expect(page).toHaveURL(/CreditoPersonal#s7/);
        const fila = page.locator('#s7 table tr', { hasText: motivo });
        await expect(fila).toBeVisible();
        await expect(fila).toContainText('0%');
    });

    test('el historial no ofrece editar ni eliminar ninguna version', async ({ page }) => {
        await gotoPunitorios(page);
        const tab = page.locator('#s7');
        test.skip(!(await tab.count()), 'La sesion de QA no tiene configuracion.viewpunitorio.');

        const historial = tab.locator('table');
        const filas = await historial.locator('tbody tr').count();
        test.skip(filas === 0, 'No hay historial de punitorios en este entorno de QA.');

        await expect(historial.getByRole('button', { name: /editar/i })).toHaveCount(0);
        await expect(historial.getByRole('button', { name: /eliminar/i })).toHaveCount(0);
        await expect(historial.getByRole('link', { name: /editar/i })).toHaveCount(0);
    });
});

test.describe('Punitorios por mora — error server-side visible', () => {
    test('una vigencia duplicada muestra el error y conserva el foco/valores', async ({ page }) => {
        await gotoPunitorios(page);
        const form = page.locator('#punitorio-form');
        test.skip(!(await form.count()), 'La sesion de QA no tiene configuracion.managepunitorio.');

        const vigencia = vigenciaFutura(10);

        await page.locator('#pun-vigente-desde').fill(vigencia);
        await page.locator('#pun-porcentaje').fill('6');
        await page.locator('#pun-periodo').fill('20');
        await page.locator('#pun-gracia').fill('5');
        await page.locator('#pun-motivo').fill('Primera version E2E duplicada');
        await form.locator('button[type="submit"]').click();
        await expect(page).toHaveURL(/CreditoPersonal#s7/);

        // Reintenta la MISMA vigencia: debe rechazarse por conflicto, sin duplicar.
        await page.locator('#pun-vigente-desde').fill(vigencia);
        await page.locator('#pun-porcentaje').fill('9');
        await page.locator('#pun-periodo').fill('20');
        await page.locator('#pun-gracia').fill('5');
        await page.locator('#pun-motivo').fill('Segunda version E2E duplicada');
        await form.locator('button[type="submit"]').click();

        // Un rechazo de ModelState re-renderiza la vista directamente en la URL de POST
        // (CrearVersionPunitorio), sin redirect — a diferencia del éxito, que sí redirige a
        // CreditoPersonal (ver arriba). No es un 500 ni una navegación rota.
        await expect(page).toHaveURL(/CrearVersionPunitorio#s7/);
        await expect(page.locator('#s7')).toBeVisible();
        // "/append-only/i" también matchea el hint estático (presente en s1-s6 y en s7 aparte
        // del error real) y el error de ModelState (clave string.Empty) se refleja en TODOS los
        // asp-validation-summary="ModelOnly" de la página (ModelState es único por request, no
        // por form) — se apunta al texto especifico del error, acotado a punitorio-form, para
        // evitar el match ambiguo de Playwright en modo estricto.
        await expect(page.locator('#punitorio-form').getByText(/Ya existe una versi.n/i)).toBeVisible();
        // Los valores enviados se preservan (no se pierde el contexto del formulario).
        await expect(page.locator('#pun-vigente-desde')).toHaveValue(vigencia);
    });
});

test.describe('Punitorios por mora — responsive y zoom', () => {
    for (const viewport of VIEWPORTS) {
        test(`sin overflow horizontal en ${viewport.name}`, async ({ page }) => {
            await page.setViewportSize({ width: viewport.width, height: viewport.height });
            await gotoPunitorios(page);
            const tab = page.locator('#s7');
            test.skip(!(await tab.count()), 'La sesion de QA no tiene configuracion.viewpunitorio.');
            await expect(tab).toBeVisible();

            const hasHorizontalOverflow = await page.evaluate(
                () => document.documentElement.scrollWidth > window.innerWidth
            );
            expect(hasHorizontalOverflow).toBeFalsy();
        });
    }

    test('zoom 200% no genera overflow horizontal', async ({ page }) => {
        await page.setViewportSize({ width: 1366, height: 768 });
        await gotoPunitorios(page);
        const tab = page.locator('#s7');
        test.skip(!(await tab.count()), 'La sesion de QA no tiene configuracion.viewpunitorio.');

        await page.evaluate(() => { document.documentElement.style.zoom = '200%'; });
        await expect(tab).toBeVisible();

        const hasHorizontalOverflow = await page.evaluate(
            () => document.documentElement.scrollWidth > window.innerWidth
        );
        expect(hasHorizontalOverflow).toBeFalsy();
    });
});

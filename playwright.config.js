// @ts-check
/**
 * Playwright E2E — Fase 16.7
 * Ejecutar: npx playwright test
 *
 * Prerequisitos:
 *   1. App corriendo: dotnet run --project TheBuryProject.csproj
 *   2. Variables de entorno:
 *      $env:E2E_USER = "nombreusuario"
 *      $env:E2E_PASS = "contraseña"
 *   3. Browsers instalados: npx playwright install chromium
 */
const { defineConfig, devices } = require('playwright/test');

const BASE_URL = process.env.E2E_BASE_URL || 'http://localhost:5187';
const AUTH_FILE = 'e2e/.auth/user.json';

module.exports = defineConfig({
    testDir: './e2e',
    fullyParallel: false,
    forbidOnly: !!process.env.CI,
    retries: 0,
    workers: 1,
    reporter: [
        ['list'],
        ['html', { outputFolder: 'qa-evidence/e2e/playwright-report', open: 'never' }],
    ],
    outputDir: 'qa-evidence/e2e/test-results',

    use: {
        baseURL: BASE_URL,
        screenshot: 'only-on-failure',
        trace: 'retain-on-failure',
        video: 'off',
        locale: 'es-AR',
        actionTimeout: 10_000,
        navigationTimeout: 20_000,
    },

    projects: [
        // ── Proyecto de setup (login único) ─────────────────────────────
        {
            name: 'setup',
            testMatch: /global-setup\.js/,
            use: { baseURL: BASE_URL },
        },

        // ── Viewports requeridos en Fase 16.7 ───────────────────────────
        {
            name: '1366x768',
            use: {
                ...devices['Desktop Chrome'],
                viewport: { width: 1366, height: 768 },
                storageState: AUTH_FILE,
            },
            dependencies: ['setup'],
        },
        {
            name: '1280x720',
            use: {
                ...devices['Desktop Chrome'],
                viewport: { width: 1280, height: 720 },
                storageState: AUTH_FILE,
            },
            dependencies: ['setup'],
        },
        {
            name: '768x1024',
            use: {
                ...devices['Desktop Chrome'],
                viewport: { width: 768, height: 1024 },
                storageState: AUTH_FILE,
            },
            dependencies: ['setup'],
        },
        {
            name: '390x844',
            use: {
                ...devices['Desktop Chrome'],
                viewport: { width: 390, height: 844 },
                storageState: AUTH_FILE,
            },
            dependencies: ['setup'],
        },
    ],
});

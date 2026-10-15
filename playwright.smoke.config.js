// @ts-check
/**
 * Smoke de SOLO LECTURA (QA fase 1).
 * Separado de playwright.config.js a propósito: testDir propio para que CI (testDir ./e2e) no lo levante,
 * y sin proyecto "setup" para no loguear (el login escribe UltimoAcceso): reutiliza e2e/.auth/user.json.
 *
 * Ejecutar:
 *   $env:E2E_BASE_URL = "http://127.0.0.1:18787"
 *   npx.cmd playwright test -c playwright.smoke.config.js
 */
const { defineConfig, devices } = require('playwright/test');

module.exports = defineConfig({
    testDir: './e2e-smoke',
    fullyParallel: false,
    workers: 1,
    retries: 0,
    reporter: [
        ['list'],
        ['json', { outputFile: 'qa-evidence/smoke-readonly/results.json' }],
        ['html', { outputFolder: 'qa-evidence/smoke-readonly/report', open: 'never' }],
    ],
    outputDir: 'qa-evidence/smoke-readonly/test-results',
    use: {
        baseURL: process.env.E2E_BASE_URL || 'http://127.0.0.1:18787',
        storageState: 'e2e/.auth/user.json',
        locale: 'es-AR',
        screenshot: 'only-on-failure',
        trace: 'retain-on-failure',
        actionTimeout: 10_000,
        navigationTimeout: 30_000,
    },
    projects: [
        { name: 'smoke-1366', use: { ...devices['Desktop Chrome'], viewport: { width: 1366, height: 768 } } },
        { name: 'smoke-390', use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 } } },
    ],
});

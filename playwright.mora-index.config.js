// @ts-check
/**
 * Smoke aislado de /Mora (MORA-VISTAS lote 1). Contexto propio (login propio, sin storageState compartido).
 * Pensado para correr contra una instancia sobre un CLON de la base, nunca la compartida:
 *   $env:E2E_BASE_URL = "http://127.0.0.1:5199"; $env:E2E_USER = "admin"; $env:E2E_PASS = "..."
 *   npx.cmd playwright test -c playwright.mora-index.config.js
 */
const { defineConfig, devices } = require('playwright/test');

module.exports = defineConfig({
    testDir: './e2e-smoke',
    testMatch: /mora-index\.spec\.js/,
    fullyParallel: false,
    workers: 1,
    retries: 0,
    reporter: [['list']],
    outputDir: 'qa-evidence/mora-index/test-results',
    use: {
        baseURL: process.env.E2E_BASE_URL || 'http://127.0.0.1:5199',
        locale: 'es-AR',
        actionTimeout: 10_000,
        navigationTimeout: 30_000,
    },
    projects: [
        { name: 'mora-1366', use: { ...devices['Desktop Chrome'], viewport: { width: 1366, height: 768 } } },
        { name: 'mora-390', use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 } } },
    ],
});

// @ts-check
/**
 * QA-TX Productos / Precios / Stock.
 * Ejecutar SIEMPRE contra el stack aislado bury-qa-prd (http://127.0.0.1:18092):
 *   & "$env:USERPROFILE\.bury-qa\qa-run.ps1" prd -- npx playwright test -c e2e-tx/productos/playwright.config.js
 * lib/env.js aborta si la URL no es la autorizada o si la DB no es BuryQA_PRD.
 */
const { defineConfig } = require('playwright/test');

module.exports = defineConfig({
    testDir: __dirname,
    testMatch: '**/*.spec.js',
    fullyParallel: false,
    workers: 1,
    retries: 0,
    timeout: 90_000,
    globalSetup: require.resolve('./global-setup.js'),
    reporter: [
        ['list'],
        ['json', { outputFile: require('path').join(__dirname, 'evidence', 'results.json') }],
    ],
    outputDir: require('path').join(__dirname, 'test-results'),
    use: {
        baseURL: 'http://127.0.0.1:18092',
        channel: 'chrome',
        headless: true,
        locale: 'es-AR',
        screenshot: 'only-on-failure',
        trace: 'retain-on-failure',
        actionTimeout: 10_000,
        navigationTimeout: 30_000,
    },
});

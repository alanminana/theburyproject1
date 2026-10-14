// @ts-check
const { defineConfig } = require('playwright/test');
const path = require('path');

module.exports = defineConfig({
    testDir: __dirname,
    testMatch: /mora-config-concurrencia.spec.js/,
    fullyParallel: false,
    workers: 1,
    retries: 0,
    timeout: 120_000,
    reporter: [['list'], ['json', { outputFile: path.join(__dirname, 'evidencia', 'resultados.json') }]],
    outputDir: path.join(__dirname, 'evidencia', 'test-results'),
    use: { baseURL: process.env.E2E_BASE_URL || 'http://127.0.0.1:18094', locale: 'es-AR' },
});

// @ts-check
const { defineConfig } = require('playwright/test');
const path = require('path');

module.exports = defineConfig({
    testDir: __dirname,
    testMatch: /financiero-concurrencia.spec.js/,
    fullyParallel: false,
    workers: 1,
    retries: 0,
    timeout: 180_000,
    reporter: [['list'], ['json', { outputFile: path.join(__dirname, 'evidencia', 'playwright-concurrencia-results.json') }]],
    outputDir: path.join(__dirname, 'evidencia', 'test-results-concurrencia'),
    use: { baseURL: process.env.E2E_BASE_URL || 'http://127.0.0.1:18093', locale: 'es-AR', trace: 'on', screenshot: 'on' },
});

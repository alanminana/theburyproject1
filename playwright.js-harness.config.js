// @ts-check
/**
 * Regresiones de JavaScript del front que NO necesitan la app, la base ni Docker: cargan el JS real en una
 * página sintética y controlan las respuestas HTTP con page.route. Separado de playwright.config.js (testDir
 * ./e2e, que exige app + login) a propósito.
 *
 * Ejecutar:  npx.cmd playwright test -c playwright.js-harness.config.js
 */
const { defineConfig, devices } = require('playwright/test');

module.exports = defineConfig({
    testDir: './e2e-js',
    fullyParallel: true,
    retries: 0,
    reporter: [['list']],
    outputDir: 'qa-evidence/e2e-js/test-results',
    use: { ...devices['Desktop Chrome'], actionTimeout: 5_000 },
});

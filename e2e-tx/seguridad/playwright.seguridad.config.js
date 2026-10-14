// Suite QA de Seguridad/Usuarios/Roles contra el stack aislado bury-qa-seg.
// Ejecutar (desde la raíz del worktree):
//   NODE_PATH=<repo>/node_modules SEG_STACK_READY=1 node <repo>/node_modules/@playwright/test/cli.js test -c e2e-tx/seguridad/playwright.seguridad.config.js
const { defineConfig } = require('@playwright/test');
const path = require('path');

// RUN_ID estable para toda la ejecución: Playwright reinicia el worker tras un fallo y re-evalúa los módulos.
process.env.SEG_RUN_ID = process.env.SEG_RUN_ID || `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;

const BASE_URL = process.env.SEG_BASE_URL || 'http://127.0.0.1:18091';

// Barrera: esta suite escribe (usuarios, roles, permisos, bloqueos). Solo se permite el stack aislado.
if (!/^http:\/\/127\.0\.0\.1:18091\/?$/.test(BASE_URL) && process.env.SEG_ALLOW_OTHER_TARGET !== '1') {
  throw new Error(`Suite de seguridad: destino no permitido ${BASE_URL}. Solo http://127.0.0.1:18091 (bury-qa-seg).`);
}

const out = path.join(__dirname, 'artifacts');

module.exports = defineConfig({
  testDir: __dirname,
  testMatch: /.*\.spec\.js$/,
  outputDir: path.join(out, 'test-results'),
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 10_000 },
  reporter: [
    ['list'],
    ['html', { outputFolder: path.join(out, 'report'), open: 'never' }],
    ['json', { outputFile: path.join(out, 'results.json') }],
  ],
  use: {
    baseURL: BASE_URL,
    channel: process.env.SEG_PW_CHANNEL || 'chrome',
    headless: true,
    trace: 'on',
    screenshot: 'on',
    video: 'off',
    viewport: { width: 1366, height: 768 },
    locale: 'es-AR',
  },
});

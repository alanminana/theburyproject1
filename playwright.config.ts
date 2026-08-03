// Canonical Playwright entrypoint.
// Playwright prioritizes this file when both .ts and .js configs exist; delegating
// avoids silently running the starter suite instead of the authenticated ERP E2E suite.
import config = require('./playwright.config.js');

export default config;

const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto('https://example.com');
  const title = await page.title();
  console.log('Título de la página:', title);
  await browser.close();
})();

// Unlighthouse config — TheBuryProject ERP audit
// Requiere variables de entorno:
//   UNLIGHTHOUSE_USER  (email del admin)
//   UNLIGHTHOUSE_PASS  (contraseña)
// Ejemplo: UNLIGHTHOUSE_USER=admin@buryproyect.com UNLIGHTHOUSE_PASS=Admin123! npx unlighthouse

const user = process.env.UNLIGHTHOUSE_USER
const pass = process.env.UNLIGHTHOUSE_PASS

if (!user || !pass) {
  throw new Error(
    'Faltan variables de entorno.\n' +
    'Ejecutá: UNLIGHTHOUSE_USER=<email> UNLIGHTHOUSE_PASS=<pass> npx unlighthouse\n' +
    'O copiá .env.unlighthouse.example a .env.unlighthouse y completalo.'
  )
}

export default {
  site: 'http://localhost:5187',

  // Rutas a auditar — no crawl automático
  urls: [
    '/',
    '/Dashboard',
    '/Venta',
    '/Cliente',
    '/Catalogo',
    '/Proveedor',
    '/OrdenCompra',
    '/Caja',
    '/Seguridad',
    '/Credito',
    '/DocumentoCliente',
  ],

  scanner: {
    device: 'desktop',
    throttle: false,
    // Evita crawl automático fuera de la lista
    crawl: false,
    dynamicSampling: false,
  },

  puppeteerOptions: {
    headless: true,
    // Persiste sesión entre ejecuciones — la segunda corrida no necesita re-login
    userDataDir: './.unlighthouse-session',
    args: [
      '--no-sandbox',
      '--disable-setuid-sandbox',
      '--disable-dev-shm-usage',
    ],
  },

  puppeteerClusterOptions: {
    // 1 tab a la vez: más lento pero más estable con apps MVC con estado
    maxConcurrency: 1,
  },

  lighthouseOptions: {
    // No resetear storage entre páginas — mantiene la sesión autenticada
    disableStorageReset: true,
    skipAboutBlank: true,
    // Simular desktop real
    formFactor: 'desktop',
    screenEmulation: {
      mobile: false,
      width: 1440,
      height: 900,
      deviceScaleFactor: 1,
      disabled: false,
    },
    throttlingMethod: 'provided',
  },

  hooks: {
    async authenticate(page) {
      // Verificar si ya está autenticado (sesión persistida)
      await page.goto('http://localhost:5187/Dashboard', {
        waitUntil: 'networkidle0',
        timeout: 15000,
      }).catch(() => {})

      const currentUrl = page.url()
      if (!currentUrl.includes('/Account/Login') && !currentUrl.includes('/Identity/')) {
        // Ya autenticado via userDataDir
        console.log('[unlighthouse] Sesión reutilizada — no requiere login')
        return
      }

      console.log('[unlighthouse] Autenticando...')
      const loginUrl = 'http://localhost:5187/Identity/Account/Login?ReturnUrl=%2FDashboard'

      await page.goto(loginUrl, { waitUntil: 'networkidle0', timeout: 30000 })

      // Email — confirmado: asp-for="Input.Email" → name="Input.Email"
      await page.waitForSelector('#Input_Email', { timeout: 10000 })
      await page.click('#Input_Email', { clickCount: 3 })
      await page.type('#Input_Email', user, { delay: 40 })

      // Password — Login.cshtml tiene id="input-password" explícito (minúsculas)
      await page.waitForSelector('#input-password', { timeout: 5000 })
      await page.click('#input-password', { clickCount: 3 })
      await page.type('#input-password', pass, { delay: 40 })

      // RememberMe — id explícito en el template: id="remember-me"
      const rememberMe = await page.$('#remember-me')
      if (rememberMe) {
        const checked = await page.$eval('#remember-me', el => el.checked).catch(() => false)
        if (!checked) await page.click('#remember-me').catch(() => {})
      }

      // Submit
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle0', timeout: 30000 }),
        page.click('button[type="submit"]'),
      ])

      const postLoginUrl = page.url()
      if (postLoginUrl.includes('/Account/Login') || postLoginUrl.includes('/Identity/')) {
        throw new Error(
          `Login falló — sigue en ${postLoginUrl}. ` +
          'Verificá UNLIGHTHOUSE_USER y UNLIGHTHOUSE_PASS.'
        )
      }

      console.log(`[unlighthouse] Login exitoso → ${postLoginUrl}`)
    },
  },
}

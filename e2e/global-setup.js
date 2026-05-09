// @ts-check
/**
 * Fase 16.7 — Login único para toda la suite E2E.
 * Guarda el estado de autenticación en e2e/.auth/user.json.
 *
 * Variables requeridas:
 *   $env:E2E_USER = "nombreusuario"
 *   $env:E2E_PASS = "contraseña"
 */
const { test: setup, expect } = require('playwright/test');
const path = require('path');
const fs = require('fs');

const AUTH_FILE = path.join(__dirname, '.auth', 'user.json');

setup('autenticar usuario E2E', async ({ page }) => {
    const user = process.env.E2E_USER;
    const pass = process.env.E2E_PASS;

    if (!user || !pass) {
        throw new Error(
            '\n[E2E Fase 16.7] Credenciales no configuradas.\n' +
            'Configure las variables antes de ejecutar:\n' +
            '  PowerShell: $env:E2E_USER="usuario"; $env:E2E_PASS="contraseña"\n' +
            '  CMD:        set E2E_USER=usuario && set E2E_PASS=contraseña\n' +
            'Luego: npx playwright test\n'
        );
    }

    await page.goto('/Identity/Account/Login', { waitUntil: 'domcontentloaded', timeout: 30_000 });

    // El campo username usa el id generado por asp-for="Input.UserName" → Input_UserName
    // El campo password tiene id="input-password" explícito en la vista (no el generado por asp-for)
    const userSelector = '#Input_UserName, input[name="Input.UserName"], input[type="text"]';
    const passSelector = '#input-password, input[name="Input.Password"], input[type="password"]';

    await page.waitForSelector(userSelector, { state: 'visible', timeout: 20_000 });

    await page.fill(userSelector, user);
    await page.fill(passSelector, pass);
    await page.click('button[type="submit"]');

    // Esperar redirección fuera del login
    await page.waitForURL(
        url => !url.toString().toLowerCase().includes('/login'),
        { timeout: 15_000 }
    );

    // Confirmar autenticación exitosa
    await expect(page).not.toHaveURL(/[Ll]ogin/, { timeout: 5_000 });

    // Persistir estado
    fs.mkdirSync(path.dirname(AUTH_FILE), { recursive: true });
    await page.context().storageState({ path: AUTH_FILE });

    console.log('[E2E] Autenticación guardada en', AUTH_FILE);
});

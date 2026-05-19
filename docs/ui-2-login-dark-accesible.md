# UI-2 - Login visual dark accesible

Fecha: 2026-05-18
Responsable: Kira
Rama: kira/ui-2-login-dark-accesible

## A. Objetivo

Rediseñar visualmente la pantalla de Login aplicando el Design System dark accesible definido en UI-1. El objetivo no es cambiar autenticacion, logica ni lógica de acceso. El objetivo es mejorar UX/UI, contraste, legibilidad, foco, accesibilidad, estados y coherencia visual con UI-1.

## B. Archivos revisados

- `docs/ui-1-design-system-dark-accesible.md` — tokens, principios, criterios de accesibilidad, reglas de inputs y botones.
- `docs/ui-rework-guia-operativa.md` — reglas generales del rework.
- `CLAUDE.md` — reglas criticas del proyecto.
- `Areas/Identity/Pages/Account/Login.cshtml` — vista principal.
- `Areas/Identity/Pages/Account/Login.cshtml.cs` — model binding, validaciones, flujo Identity.
- `wwwroot/css/standalone-tokens.css` — tokens CSS del page standalone.
- `wwwroot/css/tailwind.css` — utilidades CSS.
- `TheBuryProyect.Tests/Integration/LoginPageTest.cs` — test HTTP existente.

## C. Archivos modificados

- `Areas/Identity/Pages/Account/Login.cshtml` — cambios visuales y de accesibilidad.

No se modifico ningun otro archivo.

## D. Cambios visuales aplicados

### Removidos (simplificacion)

- Tres `div` decorativos con `blur-[120px]`, `blur-[100px]` y gradiente de fondo inferior. Violaban el principio UI-1 "dark solido, no transparencias ni blur decorativo".
- `div` de esquina decorativa fija (`fixed bottom-0 right-0`). Sin funcion operativa.
- `overflow-hidden` del body (innecesario sin los decorativos).
- `relative` y `z-10` del `<main>` (innecesarios sin capas absolutas de fondo).
- `selection:bg-primary/30` del body: `--c-primary` no estaba definido en `standalone-tokens.css`.
- `tracking-tighter` del h1 → `tracking-tight` (mas legible).

### Mejorados

| Elemento | Antes | Despues |
|---|---|---|
| Subtitle "Sistema de Gestión" | `text-[10px]` | `text-xs` (12px) |
| Labels de campo | `text-[11px] uppercase tracking-widest font-bold` + color `--c-on-surface-var` | `text-[13px] uppercase tracking-wider font-semibold` + color `--c-on-surface` |
| Inputs (borde en reposo) | `border-0` — sin borde visible | `border: 1px solid var(--c-outline-var)` — borde visible |
| Inputs (transition) | `transition-all` | `transition-[border-color,box-shadow]` |
| Inputs (padding vertical) | `py-4` | `py-3.5` (mejor proporcion) |
| Validacion de campo | `<span>` sin `block` | `block text-xs` (ocupa espacio siempre) |
| Card border | `rgba(67,70,85,0.15)` (casi invisible) | `rgba(67,70,85,0.30)` (visible) |
| Card shadow | `0 25px 50px -12px rgba(0,0,0,0.5)` (excesivo) | `0 8px 24px -4px rgba(0,0,0,0.3)` |
| Logo glow | `0 0 40px -5px rgba(19,91,236,0.35)` | `0 4px 16px -4px rgba(19,91,236,0.2)` |
| Checkbox (borde) | `border-0` — invisible en reposo | `border: 1.5px solid var(--c-outline-var)` |
| Checkbox (foco) | `focus:ring-0` — foco eliminado explicitamente | `focus:ring-2` — foco visible por teclado |
| Checkbox (transition) | `transition-all` | `transition-[background-color,border-color,box-shadow]` |
| Boton submit (transition) | `transition-all` | `transition-[background-color,box-shadow,transform]` |
| Boton submit (foco) | Sin `focus-visible` explicito | `focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2` |
| Boton submit (shadow) | `0 10px 25px -5px rgba(19,91,236,0.25)` (dramatico) | `0 4px 16px -4px rgba(19,91,236,0.3)` |
| Boton toggle-pwd | Sin foco, sin `aria-label`, sin `type` implicito seguro | `focus-visible:ring-2`, `aria-label` correcto, `aria-label` dinamico por JS |
| Enlace "Olvidaste tu contraseña" | Sin `focus-visible` | `focus-visible:ring-2 focus-visible:ring-offset-1 rounded` |
| Error global | Sin `role="alert"` | `role="alert"` para screen readers; icono con `aria-hidden="true"` |
| Iconos decorativos (person, lock, check) | Sin `aria-hidden` | `aria-hidden="true"` agregado |
| Footer texto | `rgba(195,197,216,0.35)` (bajo contraste) | `rgba(195,197,216,0.55)` |
| Footer border | `rgba(67,70,85,0.08)` (casi invisible) | `rgba(67,70,85,0.20)` |
| Footer texto size | `text-[10px]` | `text-[11px]` |
| Footer font | `font-bold` | `font-semibold` |
| Texto secundario bold | Color igual que texto base (`--c-on-surface-var`) | `var(--c-on-surface)` — diferenciado visualmente |
| Separador del footer | `bg-opacity` via color | `rgba(67,70,85,0.5)` directo |
| `aria-label` toggle inicial | "Mostrar u ocultar contraseña" fijo | Actualizado dinamicamente por JS al hacer click |

## E. Reglas funcionales preservadas

- `Layout = null` — Login mantiene su propio HTML, sin `_Layout.cshtml`.
- `@Html.AntiForgeryToken()` — intacto.
- `<input type="hidden" name="ReturnUrl" value="@Model.ReturnUrl" />` — intacto.
- `asp-for="Input.UserName"` — intacto; genera `name="Input.UserName"` y `id` correcto.
- `asp-for="Input.Password"` — intacto.
- `asp-for="Input.RememberMe"` — intacto.
- `asp-validation-for` en ambos campos — intactos.
- `asp-page="./ForgotPassword"` — intacto.
- `id="input-password"`, `id="btn-toggle-pwd"`, `id="pwd-eye"` — usados por JS, preservados.
- `id="remember-me"`, `id="check-icon"` — usados por JS, preservados.
- `autocomplete="username"` y `autocomplete="current-password"` — preservados.
- `method="post"` en el form — preservado.
- Toda la logica JS (toggle password, sync checkbox, highlight icon on focus) — preservada e identica.
- No se modifico `.cshtml.cs`, `Program.cs`, controllers, services ni migraciones.

## F. Accesibilidad

- Labels reales asociados via `asp-for` — OK.
- No se usa placeholder como label — OK (placeholder es descriptivo, label es visible y separado).
- Foco visible en inputs por `focus:ring-2` con color de acento — OK.
- Foco visible en checkbox: se corrigio de `focus:ring-0` (violation) a `focus:ring-2` — OK.
- Foco visible en boton submit: agregado `focus-visible:ring-2 focus-visible:ring-offset-2` — OK.
- Foco visible en boton toggle-pwd: agregado `focus-visible:ring-2` — OK.
- Foco visible en enlace "Olvidaste tu contraseña": agregado `focus-visible:ring-2` — OK.
- Mensajes de error: `role="alert"` en bloque de error global — OK.
- `asp-validation-for` renderiza mensajes debajo de cada campo — OK.
- Iconos decorativos con `aria-hidden="true"` — OK.
- `aria-label` en boton toggle-pwd, dinamico por JS — OK.
- Target del checkbox: 20x20px (`w-5 h-5`) — aceptable; el `<label>` wrappea el control y amplia el area.
- Boton submit: `py-4 px-6` equivale a aproximadamente 48px alto — OK (supera 44px).
- No se depende solo del color para ningun estado — errores con texto + borde + color — OK.
- Texto operativo minimo: labels a 13px, inputs a 14px (`text-sm`), validaciones a 12px (`text-xs`) — OK.
- Navegacion por teclado: form → input usuario → input password → toggle → link olvidaste → checkbox → submit — alcanzable en orden logico.

## G. Mobile/Responsive

- `max-w-[440px]` con `px-6` — se ajusta a pantallas chicas sin overflow.
- Inputs con `py-3.5` — target comodo en touch.
- Boton submit full-width con `py-4` — target claro en mobile.
- Footer con `flex-col md:flex-row` — apila en mobile, horizontal en desktop.
- Sin tablas, sin scroll horizontal, sin modales — pantalla simple y directa.
- Sin elementos `fixed` decorativos que interfieran con la lectura en mobile.
- Fuente Inter legible en pantallas Retina y pantallas estandar.

## H. Validaciones ejecutadas

- `dotnet build --configuration Release` — OK, 0 warnings, 0 errores.
- `git diff --check` — OK (solo advertencia de CRLF por configuracion de git, no es un error de contenido).
- `dotnet test --configuration Release --filter "Login|Identity|Auth|Seguridad|UiContract"` — 156/156 pasados.
- `LoginPageTest.Get_LoginPage_Returns200` — incluido en los 156; OK.

## I. Riesgos y deudas

- El checkbox custom (appearance-none + Material Symbol como check visual) no tiene estado `checked` reflejado en background. Se preservo por compatibilidad con el patron ya existente. Una mejora futura seria usar `:checked:` de Tailwind para cambiar `background-color` sin JS.
- La sincronizacion del icono del checkbox en estado inicial sigue dependiendo de JS inline (`onchange`). Esto es una deuda cosmetic menor.
- `standalone-tokens.css` no tiene token `--c-border-input`; se usa `--c-outline-var` directamente para borders. Convencion valida por ahora.
- El `LoginPageTest` solo verifica HTTP 200. No hay test de contrato HTML para esta vista. Riesgo bajo dado que Login no tiene JS complejo ni IDs dependientes de otros modulos.
- Fuentes de Google (Inter + Material Symbols) requieren conexion de red en primer render. Comportamiento ya existente, no introducido en UI-2.

## J. Proximo paso recomendado

Ejecutar **UI-3 Home/Dashboard** — pantalla operativa de mayor valor, menor riesgo que Layout global. Aplicar mismo patron: tokens de UI-1, dark solido, contraste alto, foco visible, mobile-first, sin tocar logica del DashboardService.

# Preproducción: auditoría de seguridad, transición de cuentas y requisitos de producción

Fecha: 2026-11-05 · base: `main` tras #233 (`c0519f7b`) + #234. Sin credenciales ni secretos en este documento: el procedimiento los pide, no los contiene.
**No se cambió ninguna credencial real ni se desactivó ningún usuario.** Las pruebas se hicieron en clones.

## 1. Controles verificados (con evidencia)

| Control | Resultado | Evidencia |
|---|---|---|
| Bloqueo por intentos | 5 fallos → 5 min; también para usuarios nuevos | `Program.cs` (Identity), pruebas existentes de login |
| Política de contraseña | Production exige **10** caracteres (mayúscula, minúscula, dígito); Development/Testing conservan 6; la configuración solo puede endurecer | `IdentityPasswordPolicy` + `IdentityPasswordPolicyTests` (#233) |
| Revocación de sesión | Inmediata (siguiente request) al desactivar, bloquear, eliminar o **rotar el SecurityStamp** (cambio/reset de contraseña). El validador estándar de 30 min no interviene | `ActiveUserCookieValidator` + 5 pruebas (incluye `SecurityStampRotado_PorCambioDeCredenciales_PierdeLaSesionAnterior`) |
| Último SuperAdmin | No se puede desactivar/eliminar/quitar rol al último, también bajo concurrencia | `UltimoSuperAdminConcurrenciaSqlServerTests` (11) en SQL Server + CI `sqlserver-superadmin` |
| Escalada de privilegios, roles reservados, acciones masivas | Cerrado en entregas previas (#96–#100 y #103; ver `docs/aceptacion-erp.md`, módulo Seguridad) | Barrido dinámico de 506 acciones con 10 identidades, antiforgery, IDOR |
| Diagnóstico (`/Diagnostico/*`: reset de contraseñas, usuario de prueba, volcado de usuarios) | **404 fuera de Development**, incluso para SuperAdmin, en las 5 acciones POST y las GET | `DiagnosticoFueraDeDevelopmentHttpTests` (#233). Hoy el lanzador local arranca en **Development** → Diagnóstico existe (solo SuperAdmin) |
| Cuentas demo / `Admin123!` | En Production **no se crean**: `CreateTestUsersAsync` solo corre en Development y el seed del admin exige `Admin:Email`/`Admin:Password` (sin fallback) | `Program.cs`, `DbInitializer` |
| Secretos en Production | `ErpDb:User`/`ErpDb:Password` obligatorios, `sa` y `CHANGE_ME` rechazados, `ClientSecret` de ML exigido si hay `ClientId` | `ProductionSecrets`, `docs/despliegue-produccion.md` §1 |
| Webhook de Mercado Libre | Anónimo por contrato; ahora puede cerrarse con `MercadoLibre__Webhook__Enabled=false` (404 y consumidor apagado) | `MercadoLibreWebhookInterruptorTests` (#233) |
| Cabeceras / HTTPS | `nosniff`, `X-Frame-Options`, `Referrer-Policy`; HSTS fuera de Development; redirección HTTPS la hace Caddy. **CSP no aplicada** (requiere QA visual) | `Program.cs`, `docs/despliegue-produccion.md` §8 |

## 2. Hallazgos abiertos (requieren al propietario)

1. **Crítico: `admin` (único SuperAdmin) y `administrador` aceptan la contraseña de demostración en la base real.** No se tocó. Mientras exista, cualquiera que la conozca administra el sistema.
2. Las cuentas demo del seed de desarrollo (11 usuarios en total) siguen activas en la base real.
3. El entorno local corre en `Development` (Diagnóstico expuesto, `ErpDb` sin login SQL). Production **no puede usar LocalDB** (no admite autenticación SQL): requiere SQL Server en contenedor (pila Docker documentada) o instalado.
4. Cookies `Secure` dependen del proxy (`SameAsRequest` + `X-Forwarded-Proto`): con Kestrel directo por HTTP no serían `Secure`. Producción debe ir detrás de Caddy/HTTPS.
5. CSP pendiente; webhook ML anónimo si no se apaga.

## 3. Procedimiento de transición de cuentas (para ejecutar con el propietario, sin pasar contraseñas por chat ni logs)

Orden obligatorio; **no desactivar `admin` antes de los pasos 2 y 3**.

1. Con `admin`, crear la cuenta SuperAdmin **propia** del propietario (Seguridad → Usuarios): contraseña ≥ 10 caracteres elegida por el propietario.
2. Iniciar sesión con esa cuenta en una sesión independiente (navegador/perfil distinto) y aceptar Términos y Condiciones.
3. Crear una segunda cuenta SuperAdmin **de recuperación** (contraseña guardada offline por el propietario) y probar el ingreso.
4. Con la cuenta propia: cambiar la contraseña de `admin` (o desactivarlo). Verificar que una sesión abierta previa con `admin` pierde acceso en el siguiente request (comportamiento ya probado).
5. Desactivar `administrador` y las cuentas demo; verificar que siguen existiendo ≥ 2 SuperAdmin activos.
6. Antes de operar: pasar a `Production` (ver §4) para que las rutas de Diagnóstico desaparezcan y se aplique el mínimo de 10 caracteres.

Verificación posterior (solo lectura): `SELECT UserName, Activo FROM AspNetUsers` y que `/Diagnostico` responda 404.

## 4. Requisitos de infraestructura de producción

Ya implementado y documentado (`docs/despliegue-produccion.md`, `docs/docker-produccion.md`, `docs/backup-restore.md`, `docs/deploy-rollback.md`): pila Docker (app + SQL Server + Caddy), un solo contenedor `app`, login SQL de mínimo privilegio, Data Protection persistente en volumen, `uploads`/contratos en volumen, health check, backups completos + log cada 15 min, prueba de restauración, copia externa por rclone, deploy/rollback con migraciones, monitor.

Para la instalación local actual (LocalDB): `scripts/respaldo-local.ps1` (base + `keys` + `uploads` + contratos con manifiesto SHA256 y prueba de restauración) — ver `docs/despliegue-produccion.md` §7.2.

**Decisiones de infraestructura que solo puede tomar el propietario** (no se tocó nada externo):

| Decisión | Por qué bloquea |
|---|---|
| Servidor/host definitivo (¿esta PC con Docker, otro equipo, VPS?) | Define dónde corre la pila y de dónde salen los backups |
| Dominio y certificado (o acceso solo por red local/VPN) | Caddy necesita nombre y puertos 80/443 o una CA interna |
| Acceso remoto: VPN y reglas de firewall del host | Hoy sin definir (IP de la VPN pendiente) |
| Destino de la copia externa (disco/NAS/nube) y sus credenciales | `BACKUP_REQUIRE_OFFSITE=true` en producción; sin copia fuera del servidor no hay recuperación ante pérdida del equipo |
| Quién guarda `.env` y `backup-offsite.env` (gestor de contraseñas) | No van en los backups; sin ellos no se recupera |
| Migración de datos LocalDB → SQL Server de producción (momento y responsable) | La base real vive en LocalDB; pasarla requiere backup/restore autorizado |

Hasta que esas decisiones existan, el ERP **no debe declararse apto para producción**.

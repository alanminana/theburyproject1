# P-03: ensayo de la transición de cuentas reales (en clon)

Fecha: 2026-11-07 · base: `main` 0ecd8f43 (#254) · procedimiento ensayado: `docs/preproduccion-seguridad-y-transicion.md` §3 y `docs/preparacion-operativa-2026-11-05.md` (Usuarios).
Sin credenciales en este documento: el script genera las contraseñas en memoria y no las imprime.

**No se tocó la base real.** Solo se hizo `BACKUP ... COPY_ONLY` (lectura) de `TheBuryProjectDb`; todo el ensayo corrió sobre `TheBuryProjectDb_p03` (restaurada con `MOVE` en `C:\Users\Public\tbp_p03.*`), con una app propia (`dotnet exec` desde `bin/p03run`, puerto 5197), ya eliminada.

## 1. Método

- Script reutilizable: `scripts/qa/p03-ensayo-transicion-cuentas.ps1` (`-CrearClon`, `-Ejecutar`, `-Limpiar`, `-Entorno Development|Staging`). Guardas: `BaseClon` debe cumplir `^[A-Za-z0-9_]+_(p03|qa)$` y no ser `TheBuryProjectDb` (sin distinguir mayúsculas); la cadena de conexión se arma y se parsea con `SqlConnectionStringBuilder` y debe tener `InitialCatalog` igual a `BaseClon` y ningún `AttachDBFilename`; el resto de los parámetros (servidor, prefijo, carpeta de trabajo, cuentas demo) se validan con listas blancas y el SQL dinámico usa literales escapados y `QUOTENAME`. El borrado de archivos solo ocurre sobre lo que el propio ensayo creó (archivo sentinela en la carpeta de trabajo y marca `.p03-sentinel` en `bin\p03*`), nunca sobre el repositorio. `-ModoSeco` valida parámetros sin tocar SQL ni la app. Las consultas de verificación son `SELECT` fijos con un filtro de defensa en profundidad: **no es una garantía de solo lectura a nivel del motor**; el script no emite DML sobre el clon y las escrituras las hace únicamente la app.
- Las acciones se ejecutan por HTTP contra la app real (login, antiforgery, `Usuarios/Create`, `CambiarPassword`, `Desactivar`, `Reactivar`, `Delete`), con sesiones independientes (cookies separadas) y verificación por consultas `SELECT` fijas sobre el clon.
- Se corrió dos veces sobre clones recién restaurados: **Development** y **Staging** (mismo código; Staging sirve para el punto 6). **Production no es ejecutable en LocalDB** (exige `ErpDb:User/Password`, login SQL).

## 2. Resultado por paso

| # | Verificación | Resultado | Evidencia |
|---|---|---|---|
| 1.1 | Crear SuperAdmin principal desde Seguridad→Usuarios | OK | `POST /Usuarios/Create` devuelve `success:true`; fila en `AspNetUsers` con rol SuperAdmin. Una alta con 3 caracteres de contraseña se rechaza y no crea usuario |
| 1.2 | Login independiente del principal | OK | Sesión nueva; entra con Términos pendientes (la pantalla de login pasa al paso de aceptación) |
| 1.3 | Aceptación de Términos | OK | Acceso a `/Seguridad` = 200 tras aceptar; 1 fila en `TerminosCondicionesAceptaciones` |
| 1.4–1.5 | SuperAdmin de recuperación: alta (con la cuenta propia), login independiente, Términos | OK | Igual que 1.1–1.3 |
| 2.1–2.2 | Rotar la contraseña de `admin` | OK | La clave anterior ya no ingresa; la nueva sí |
| 2.3 | Desactivar `administrador` y las 6 cuentas demo restantes | OK | `Activo=0` por SQL; `JAVIER`, `Javier2`, `nikky` (correos personales) siguen activas, a propósito |
| 2.5 / 5.3 | ≥ 2 SuperAdmin activos tras la limpieza | OK | 2 (propietario y recuperación) |
| 3.1 | Sesión previa de `admin` pierde acceso tras rotar la contraseña | OK | Se exige explícitamente 302/303 con `Location` al login en el siguiente request a `/Seguridad` (antes: 200) |
| 3.2–3.3 | Idem al desactivar `admin`; `admin` desactivado no puede ingresar | OK | Mismo criterio (302/303 al login); además el login de `admin` desactivado se rechaza |
| 3.4 | Idem para el propietario desactivado por la recuperación | OK | 302/303 al login (exigido explícitamente) |
| 4.1 | Con 2 SuperAdmin se puede desactivar uno | OK | SuperAdmin activos: 2 → 1 |
| 4.2 | No se puede desactivar al último | OK | La cuenta sigue `Activo=1`; SuperAdmin activos = 1 |
| 4.3 | No se puede eliminar al último | OK | La cuenta sigue activa |
| 5.1 | Con la cuenta de recuperación se reactiva al propietario | OK | `Activo=1` y el propietario vuelve a ingresar (Términos ya aceptados, no los vuelve a pedir) |
| 5.2 | La recuperación reactiva `admin` y le resetea la contraseña | OK | Ingreso con la nueva; luego se volvió a desactivar (5.3) |
| 5.4 | `SELECT UserName, Activo FROM AspNetUsers` | OK | Final: `admin`, `administrador`, `cajero`, `contador`, `gerente`, `repositor`, `tecnico`, `vendedor` = 0; `JAVIER`, `Javier2`, `nikky` y las 2 cuentas del ensayo = 1 |
| 6 | `/Diagnostico` responde 404 | **Development: NO VALIDADO** (responde 200, esperado). **Staging: OK** (404 incluso para SuperAdmin) | El 404 se aplica a todo entorno distinto de Development (`DiagnosticoController.OnActionExecuting`). Production exacto **no ejecutado**: lo cubre `DiagnosticoFueraDeDevelopmentHttpTests` (#233) |

**No validado por este ensayo:** (a) el mínimo de 10 caracteres (solo rige en Production; Development/Staging aceptan 6: lo cubre `IdentityPasswordPolicyTests`); (b) asignación de rol y caja a usuarios reales (no se ejecutó); (c) HSTS, cookies `Secure` y proxy; (d) SQL Server en contenedor (la concurrencia del último SuperAdmin está cubierta por `UltimoSuperAdminConcurrenciaSqlServerTests`).

## 3. Defectos y observaciones

1. **Bajo, real:** el alta de usuario valida Nombre/Apellido con `SoloLetras` (máx. **10 letras por palabra**, 3 palabras). Un nombre o apellido legítimo de 11+ letras (p. ej. «Constantino») se rechaza con un mensaje que no aclara el límite. Afecta el alta de los usuarios reales; no se corrigió (decisión de producto: subir `MaxWordLength` en `CrearUsuarioViewModel` y la edición). Hasta entonces, cargar un nombre abreviado.
2. **Observación:** la guardia del último SuperAdmin no comprueba que alguien conozca la contraseña ni que haya aceptado Términos (ya documentado). Por eso el runbook obliga a **probar el ingreso** de cada cuenta antes de desactivar `admin`.
3. Con Términos pendientes el login responde 200 (mismo formulario, paso 2) y no 302: quien automatice debe verificar la cookie, no el redirect (el script lo hace).
4. Sin otros defectos de la aplicación: las fallas intermedias de la primera corrida eran del script (cookie/redirect, columna SQL, límite de nombre) y se corrigieron.

## 4. Runbook final para ejecutar con el propietario

Regla: las contraseñas las escribe el propietario en el formulario; no se dictan por chat ni se guardan en logs. Todo se hace con la app en el entorno definitivo (ideal: Production, que además aplica el mínimo de 10 y oculta Diagnóstico).

0. **Antes (mejora respecto de §3):** respaldo completo vigente y verificado (`scripts/respaldo-local.ps1` o backup Docker); confirmar quién es quién entre `JAVIER`, `Javier2`, `nikky` (¿personas reales? ¿qué rol y caja?); tener a mano un correo real por cuenta. Nombres y apellidos de ≤ 10 letras por palabra (defecto 1).
1. Con `admin`: Seguridad → Usuarios → Nuevo: crear el **SuperAdmin del propietario** (rol SuperAdmin, contraseña ≥ 10 con mayúscula, minúscula y dígito).
2. En **otro navegador/perfil**: ingresar con esa cuenta, aceptar Términos y confirmar que Seguridad abre.
3. Con la cuenta propia: crear el **SuperAdmin de recuperación**; la contraseña queda guardada **offline**. Ingresar con ella en una tercera sesión y aceptar Términos. *No avanzar hasta que ambas cuentas hayan ingresado.*
4. Con la cuenta propia: **cambiar la contraseña de `admin`** (clave fuerte guardada offline) y, si se prefiere, **desactivarlo**. Verificar que una sesión de `admin` abierta antes cae en el siguiente request (302 al login). Desactivar `admin` solo después del paso 3.
5. Desactivar `administrador` y las cuentas demo (`cajero`, `contador`, `gerente`, `repositor`, `tecnico`, `vendedor`). Verificar ≥ 2 SuperAdmin activos.
6. **Usuarios reales (no ensayado):** a cada persona real, rol mínimo necesario y sucursal; asignar caja (módulo Caja, asignaciones) a quien opere caja; resetear la contraseña de `JAVIER`, `Javier2` y `nikky` (hoy son demo o desconocidas) o desactivarlas; cada persona acepta Términos en su primer ingreso. Probar el ingreso de cada una.
7. **Prueba de recuperación:** con la cuenta de recuperación, reactivar o resetear una cuenta de prueba y dejarla como estaba; confirmar que se rechaza desactivar o eliminar al último SuperAdmin.
8. Pasar a Production (§4 del documento de preproducción) y verificar: `/Diagnostico` → 404, login con las cuentas nuevas, `SELECT UserName, Activo FROM AspNetUsers` (solo cuentas esperadas activas).
9. Guardar offline: contraseñas de propietario, recuperación y `admin`; `.env` y copia externa (fuera de este alcance).

Reensayo: `-CrearClon -Ejecutar -Limpiar` con `P03_ADMIN_PASSWORD` (contraseña actual de `admin` en el clon) en el entorno; requiere build previo en `bin/p03run` (`dotnet build -o bin/p03run`). Nota: `-Limpiar` borra ese directorio.

## 5. Riesgos

- Si el propietario pierde ambas contraseñas SuperAdmin no hay recuperación por correo (deshabilitada): solo queda acceso SQL o restauración. Mantener la cuenta de recuperación probada y custodiada.
- Las cuentas con correos personales conservan hoy contraseñas desconocidas: hasta el paso 6 son una superficie de acceso (roles Administrador/Cajero/Vendedor).
- El mínimo de 10 caracteres depende de arrancar en Production; en Development cualquier clave de 6 es válida.
- Ejecutar el procedimiento en la base real sin respaldo previo no tiene reversión directa (rotar contraseñas y desactivar usuarios solo se deshace desde la propia app o restaurando).

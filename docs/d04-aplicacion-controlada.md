# D-04 — procedimiento de aplicación controlada a la base real

**Estado: APLICADA el 2026-11-12 a `TheBuryProjectDb` y verificada** (ver «Registro de aplicación» al final; el resto del documento describe la preparación y el procedimiento). Fusionar el código (#267) no la aplica: el guard (#268) y la protección del CLI de EF lo impiden (ver «Qué impide aplicarla sin querer»).

## Qué cambia en el esquema
Solo índices de `AperturasCaja` (ninguna fila se modifica, borra ni recalcula):
- se elimina `IX_AperturasCaja_CajaId (CajaId)`;
- se crea `IX_AperturasCaja_CajaId_Cerrada (CajaId, Cerrada)`;
- se crea el índice **único filtrado** `UX_AperturasCaja_CajaId_Abierta (CajaId) WHERE Cerrada = 0 AND IsDeleted = 0`;
- se registra la migración (137 registros, antes 136).

Efecto funcional: la base rechaza una segunda apertura vigente de la misma caja (error 2601). La aplicación ya lo evitaba con locks (H-05/H-11); esto es defensa en profundidad. **El código nuevo no depende del índice**: se puede aplicar antes o después del despliegue.

## Artefactos
- `scripts/sql/d04-preflight-apertura-unica-caja.sql` — solo lectura.
- `scripts/sql/d04-migracion-apertura-unica-caja.sql` — transaccional, con bloqueo de aplicación, `DryRun`, idempotente y con guardas (base de destino, historial exacto de 136 migraciones, índices en el estado previo, **cajas con más de una apertura vigente → lista y aborta sin cambiar nada**). Incluye el bloque *Rollback*.
- Ambos se ejecutan con `sqlcmd -I` (el índice filtrado exige `QUOTED_IDENTIFIER ON`).

## Ensayo realizado (clon restaurado de un respaldo `COPY_ONLY` de la base real; nunca sobre la base real)
| Caso | Resultado |
|---|---|
| Preflight | 136 migraciones, 0 aperturas, 0 duplicados, índices previos correctos |
| `BaseEsperada` distinta | aborta (51051), sin cambios |
| `DryRun=1` | aplica y **revierte** (136 registros, índices previos) |
| Aplicación | 137 registros, los 2 índices nuevos, el viejo eliminado |
| Reaplicación | «ya está registrada… no se hace nada» |
| Dos aperturas vigentes de una caja (antes de aplicar) | aborta (51057), lista la caja, sin cambios |
| Historial alterado (falta una migración) | aborta (51054), sin cambios |
| Reversión (bloque *Rollback*) | vuelve a 136 y al índice original |
Además: 3 pruebas de SQL Server (`D-04 [IDX-1..3]`, incluye 8 inserciones simultáneas → 1 vigente) y 667 pruebas de Caja/Apertura en SQLite.

## Procedimiento propuesto (con la aplicación detenida y sin turno de caja en curso)
1. **Preflight** (solo lectura):
   `sqlcmd -S "(localdb)\MSSQLLocalDB" -d TheBuryProjectDb -E -C -I -b -f 65001 -i scripts\sql\d04-preflight-apertura-unica-caja.sql`
   Debe dar `BaseOk=1`, `Migraciones=136` (última `20261106072023_H01NumeroOrigenVenta`), `D04Registrada=0`, `CajasConDuplicadas=0`, índices `IX_AperturasCaja_CajaId` + PK y `ConexionesAjenas=0`. Si hay duplicados: **detenerse** y decidir cuál cerrar (no se depura automáticamente).
2. **Respaldo** inmediatamente antes:
   `BACKUP DATABASE TheBuryProjectDb TO DISK='D:\tmp\d04-previo-<fecha>.bak' WITH COPY_ONLY, CHECKSUM, INIT;`
   `RESTORE VERIFYONLY FROM DISK='D:\tmp\d04-previo-<fecha>.bak' WITH CHECKSUM;`
   Guardar la huella del preflight (conteos de Cajas, MovimientosCaja, Ventas, Cuotas, Creditos).
3. **Ensayo**: mismo comando de aplicación con `-v DryRun=1 -v BaseEsperada=TheBuryProjectDb`; debe imprimir «DryRun=1: se revierte» sin error.
4. **Aplicación**: `-v DryRun=0`; debe terminar en «Aplicado.».
5. **Verificación**: repetir el preflight (137 registros, `D04Registrada=1`, índices nuevos, huella idéntica); arrancar la aplicación con el código nuevo; abrir y cerrar una caja de prueba y comprobar que una segunda apertura de la misma caja sigue rechazándose con el mensaje de negocio.
6. **No** habilitar `Database:AllowMigrations` ni usar `--migrate`: la aplicación se hace solo con el script, que es explícito y auditable.

## Recuperación
- **Preferida:** restaurar el respaldo del paso 2 (`ALTER DATABASE … SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE … WITH REPLACE;`). Se pierde lo registrado después del respaldo.
- **Solo los índices:** bloque *Rollback* al final del script (inocuo para los datos; después no hay nada que revertir en el código).

## Qué impide aplicarla sin querer
| Camino | Protección |
|---|---|
| Arranque normal de la app (`InitializeOnStartup=true`) | `MigracionesGuard`: base con historial y pendientes → exit 1, sin migrar ni sembrar |
| `--configurar-documentos` | mismo guard antes de `MigrateAsync` |
| `dotnet ef database update` (con o sin `--connection`) | `AppDbContextFactory` rechaza el destino `TheBuryProjectDb` (o sin catálogo explícito) sin `Database:AllowMigrations=true`, y un interceptor de conexión (`GuardConexionRealInterceptor`) bloquea **abrir** la conexión aunque `--connection` cambie el destino después; sin conexión explícita usa `TheBuryProjectDb_DesignTime`. Verificado: los tres casos fallan y el historial real sigue en 136 |
| `--migrate` / Docker (`migrate` service) | explícito por diseño; apunta al SQL del stack, no a la LocalDB de desarrollo. Un deploy aplicaría D-04: **no desplegar sin aprobación** |
| Variable de usuario `Database__InitializeOnStartup=False` | primera capa ya presente en esta máquina |

## Ventana de mantenimiento (PREPARADA 2026-11-08, NO EJECUTADA — espera autorización expresa)

### Estado verificado (solo lectura, 2026-11-08, base `TheBuryProjectDb`)
Preflight: `BaseOk=1`, 136 migraciones (última `20261106072023_H01NumeroOrigenVenta`), `D04Registrada=0`, 0 aperturas, 0 duplicados, índices previos `PK_AperturasCaja` + `IX_AperturasCaja_CajaId`, **0 conexiones ajenas**, 8 cajas, 0 movimientos de caja, 0 ventas, 0 cuotas, 0 créditos. La aplicación no estaba conectada.

### Respaldo y restauración (ensayados, sin tocar la base real)
- Respaldo de ensayo `D:	mp\d04ventana\ensayo-respaldo.bak` (`COPY_ONLY, CHECKSUM`, 34 MB, `RESTORE VERIFYONLY … WITH CHECKSUM` válido; disco D: con 190 GB libres).
- Restaurado como base aislada `TheBuryProjectDb_vent` (0,7 s) y ahí se corrió el script **de `main`** tal cual: `DryRun=1` (revierte: 136 / índice previo), `DryRun=0` (137 / índices nuevos), bloque *Rollback* (vuelve a 136 / índice previo). Base aislada eliminada; la real quedó en 136.
- Duración esperada de la aplicación: menos de 1 segundo (base de 34 MB, tabla `AperturasCaja` vacía).

### Checklist de la ventana (a ejecutar solo con tu OK)
1. **Antes:** confirmar que no hay turno de caja abierto ni usuarios operando; anunciar la ventana (estimada: 10 minutos).
2. **Detener la aplicación** (proceso `TheBuryProyect`; hoy se ve con `Get-CimInstance Win32_Process | ? CommandLine -match 'TheBuryProyect'`). No se detiene hasta tu aprobación.
3. **Preflight** (`scripts\sql\d04-preflight-apertura-unica-caja.sql`): debe repetir el estado de arriba. **Cualquier diferencia (duplicados, migraciones, conexiones) = NO-GO**.
4. **Respaldo definitivo** `D:	mp\d04-previo-<fecha-hora>.bak` (`COPY_ONLY, CHECKSUM, INIT`) + `RESTORE VERIFYONLY … WITH CHECKSUM`; anotar la huella (conteos del preflight).
5. **Ensayo** `-v DryRun=1 -v BaseEsperada=TheBuryProjectDb` → «DryRun=1: se revierte».
6. **Aplicación** `-v DryRun=0` → «Aplicado.».
7. **Verificación:** preflight de nuevo (137 migraciones, `D04Registrada=1`, índices `IX_AperturasCaja_CajaId_Cerrada` y `UX_AperturasCaja_CajaId_Abierta`, huella idéntica).
8. **Arrancar la aplicación** con el código de `main` (sin `AllowMigrations`: no hay migraciones pendientes, el guard no interviene), abrir una caja de prueba y comprobar que una segunda apertura de la misma caja sigue rechazándose; cerrar la caja de prueba.
9. **Cierre:** conservar el respaldo; registrar la aplicación en este documento («Registro de aplicación»).

### NO-GO / reversión
- NO-GO en cualquier paso 3-7: no continuar; si el paso 6 ya corrió, bloque *Rollback* o restaurar el respaldo del paso 4 (`ALTER DATABASE … SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE … WITH REPLACE;`).
- **No** usar `--migrate`, Docker `migrate`, `dotnet ef database update` ni `Database__AllowMigrations=true`: la aplicación se hace solo con el script aprobado.

## Pendiente de aprobación
- [ ] Autorizar la ventana de aplicación (app detenida, sin turno abierto).
- [ ] Confirmar la ruta del respaldo.

---

## Preparación 2026-11-11 (ronda de decisiones del propietario)

**Estado final: PREPARADO, NO APLICADO.** Decisión del propietario: preparar preflight, ventana, respaldo verificado, ensayo y rollback. Nada se aplicó a `TheBuryProjectDb` (sigue en 136 migraciones, sin los índices de D-04). Todo lo que escribe se ensayó en un clon restaurado de un respaldo `COPY_ONLY` de la base real.

### Estado real verificado (solo lectura, 2026-11-11, `main` = `58c12c2a`)
| Dato | Valor |
|---|---|
| Migraciones aplicadas en `TheBuryProjectDb` | **136**, última `20261106072023_H01NumeroOrigenVenta` |
| Migraciones en el código de `main` (`Migrations/`, atributo `[Migration]`) | **137**: las 136 anteriores + `20261108185231_D04AperturaCajaUnicaAbierta` |
| Diferencia código vs base | **una sola pendiente: D-04**. No hay que aplicar otras antes: D-04 se aplica sola |
| ¿El script exige «136 exactas»? | Sí, y sigue siendo correcto: la lista embebida coincide con las 136 aplicadas (verificado por comparación). Desde el 2026-11-08 **no entró ninguna migración nueva**, por eso el script no se modificó |
| Índices previos de `AperturasCaja` | `PK_AperturasCaja`, `IX_AperturasCaja_CajaId` |
| Aperturas / vigentes / duplicados | 0 / 0 / 0 |
| Conexiones ajenas | 0 |
| Huella | Cajas=8, AperturasCaja=0, MovimientosCaja=0, Ventas=0, Cuotas=0, Creditos=0 |
| Tamaño | 208 MB (mdf 72 + ldf 136); respaldo `COPY_ONLY` 33 MB; D: con ~190 GB libres |
| Preflight del runner sobre la base real | **GO** en todas las guardas |

Riesgo de mantenimiento: la lista de 136 del script es estática. Si **antes de la ventana** entra una migración nueva a `main` y se aplica a la base real, el script abortará con 51054 (NO-GO seguro, sin cambios) y habrá que regenerar la lista. El runner lo detecta antes: su guarda `Historial` compara contra las migraciones del código (lo aplicado no tiene extras desconocidas y lo único pendiente es D-04) y marca NO-GO con la lista de pendientes ajenas.

### Artefactos nuevos
- `scripts/ops/d04-ventana.ps1` (PowerShell 5.1): runner seguro de la ventana.
- `scripts/sql/d04-rollback-apertura-unica-caja.sql`: reversión de los índices, con las mismas garantías que la migración (transaccional, `DryRun`, base esperada, abortos 51060-51064; aborta si hay migraciones registradas posteriores a D-04).
- `TheBuryProyect.Tests/Unit/D04VentanaScriptsContractTests.cs`: 2 pruebas (`D-04 [IDX-4]`, `[IDX-5]`) que fijan las guardas de reversión y runner sin tocar ninguna base.

### Runner: modos y garantías
`-Base` es obligatorio (sin valor por defecto). Salida `[GO]`/`[NO-GO]` por guarda, log en `<DirTrabajo>\logs\`. Códigos de salida: 0 = GO/OK, 2 = NO-GO (no se cambió nada), 3 = error de ejecución.

| Modo | Qué hace | Autorización |
|---|---|---|
| `Preflight` (default) | Solo lectura. Guardas: base, historial, D-04 no registrada, índices previos, sin duplicados, sin turnos abiertos, sin conexiones ajenas, espacio en disco. Imprime huella | ninguna |
| `Respaldo` | `BACKUP ... COPY_ONLY, CHECKSUM, INIT` + `RESTORE VERIFYONLY ... WITH CHECKSUM` + huella a `<respaldo>.huella.json` (no modifica la base) | ninguna |
| `Ensayo` | `DryRun=1` (aplica y revierte); comprueba que la base quedó intacta | base real: token + `-ConfirmarBaseReal`; otras: ninguna |
| `Aplicar` | Preflight GO + respaldo verificado y reciente (`-RespaldoMaxMinutos`, 120) + huella actual = huella del respaldo; corre el ensayo y luego aplica. Si ya está aplicada: no hace nada (idempotente) | token `-Autorizado D04-<yyyyMMdd de hoy>`; base real además `-ConfirmarBaseReal` |
| `Verificar` | Historial, índices, huella idéntica al respaldo. `-Esperado Previo` para después de un rollback. `-PruebaSegundaApertura` (solo bases que **no** sean la real): en una transacción siempre revertida inserta una caja y una apertura, intenta una segunda vigente (debe dar 2601 nombrando `UX_AperturasCaja_CajaId_Abierta`) y comprueba que con la primera cerrada se puede reabrir; no deja residuos | ninguna |
| `Rollback` | Ensayo de la reversión y luego reversión de índices e historial | mismo token (+ `-ConfirmarBaseReal` en la real) |

Ejemplo (la base real **solo** en la ventana autorizada): `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\ops\d04-ventana.ps1 -Modo Aplicar -Base TheBuryProjectDb -DirTrabajo D:\tmp\d04-ventana -Autorizado D04-<hoy> -ConfirmarBaseReal`. Parámetros útiles: `-Servidor` (default `(localdb)\MSSQLLocalDB`), `-DirTrabajo` (default `D:\tmp\d04-ventana`), `-PermitirTurnosAbiertos` (no usar en la ventana real).

### Resultados del ensayo (clon `TheBuryProjectDb_d04`, restaurado de un respaldo real `COPY_ONLY`; la base real no se tocó)
| Caso | Resultado | Duración |
|---|---|---|
| Restauración del clon (`RESTORE ... WITH MOVE`) | OK | ~1,2 s |
| Preflight | GO | ~2 s |
| Respaldo + `VERIFYONLY` + huella | OK, 33 MB | 0,8 s |
| Ensayo (`DryRun=1`) | GO, base intacta (136, índices previos) | 3,4 s (incluye preflight) |
| Aplicar | OK: 137 migraciones, índices `IX_AperturasCaja_CajaId_Cerrada` + `UX_AperturasCaja_CajaId_Abierta`, sin `IX_AperturasCaja_CajaId` | 0,3-0,4 s (ensayo + aplicación) |
| Verificar + `-PruebaSegundaApertura` | OK: segunda apertura vigente rechazada con 2601; reapertura tras cierre posible; huella idéntica | ~1 s |
| Reaplicación | «ya está aplicada… no se hace nada», exit 0 | <1 s |
| Rollback | OK, vuelve a 136 e índice original | 0,3 s |
| Verificar `-Esperado Previo` | OK, huella idéntica | ~1 s |
| Restauración desde el respaldo (`RESTORE ... WITH REPLACE`) sobre el clon ya aplicado | OK, vuelve a 136 / índice previo / huella idéntica | ~1 s |
| App (código de `main`) contra el clon aplicado, puerto 5311 | caja 2: primera apertura → 302 a `DetallesApertura`; segunda → 200 con «La caja ya tiene una apertura activa», sin 500 ni errores `fail:` en el log; 1 fila vigente | n/a |

Casos negativos (todos abortan sin cambiar nada; exit 2 en el runner, error SQL indicado en el script directo):
- sin token, token del día anterior (`D04-20261110`), `Rollback` sin token: `[NO-GO] Token`;
- base inexistente: `[NO-GO] Base` y `RespaldoExiste`;
- `TheBuryProjectDb` con token pero sin `-ConfirmarBaseReal` (Aplicar), y Ensayo sobre la real sin confirmar: `[NO-GO] BaseReal`;
- sin respaldo (directorio vacío): `[NO-GO] RespaldoExiste`;
- caja con dos aperturas vigentes sembrada en el clon: el runner da `[NO-GO] SinDuplicados`, `SinTurnoAbierto` y `HuellaVigente`; el script SQL directo aborta con **51057** listando la caja y deja 136 migraciones e índice previo;
- historial alterado (falta `H10ChequesRecibidos`): runner `[NO-GO] Historial` (pendiente ajena); script directo **51054**;
- migración extra desconocida en el historial: runner `[NO-GO] Historial` (extras);
- `BaseEsperada` distinta: **51051**;
- `Verificar -PruebaSegundaApertura` contra la base real: se niega a escribir.

Pruebas: xUnit de Caja/Apertura (SQLite) **669 correctas, 0 fallidas, 50 omitidas** (las omitidas son las de SQL Server, incl. `D-04 [IDX-1..3]`, que requieren contenedor y no se ejecutaron en esta preparación porque el script de migración no se modificó; la reversión y el índice se ejercitaron con el motor real de LocalDB en el clon); 2 pruebas nuevas `[IDX-4/5]` correctas. Evidencia cruda en `D:\tmp\mision\r2d04\` (logs por paso en `clon\logs\`).

### Checklist de la ventana (ACTUALIZADO; a ejecutar solo con la autorización del propietario)
Duración estimada total: **10-15 minutos** (la parte técnica dura menos de 10 segundos; el resto es detener/arrancar la aplicación y verificar). `$R` = `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\ops\d04-ventana.ps1 -Base TheBuryProjectDb -DirTrabajo D:\tmp\d04-ventana` y `$TOKEN` = `D04-<yyyyMMdd de la fecha de la ventana>`.

| # | Paso | Comando exacto | Criterio |
|---|---|---|---|
| 0 | Día anterior: comprobar que `main` no sumó migraciones nuevas | `$R -Modo Preflight` (con la aplicación en marcha fallará solo `SinConexionesAjenas`) | Historial GO: 136 aplicadas, única pendiente D-04 |
| 1 | Anunciar la ventana (~15 min sin sistema) y confirmar que no hay turno de caja abierto | — | Sin turnos abiertos |
| 2 | Detener la aplicación (proceso `TheBuryProyect`) | `Get-CimInstance Win32_Process \| ? CommandLine -match 'TheBuryProyect'` y detenerlo | 0 procesos |
| 3 | Preflight | `$R -Modo Preflight` | **GO en todas las guardas.** Cualquier NO-GO = detenerse |
| 4 | Respaldo definitivo | `$R -Modo Respaldo` | `RESPALDO: OK (verificado)`; anotar huella y ruta |
| 5 | Ensayo | `$R -Modo Ensayo -Autorizado $TOKEN -ConfirmarBaseReal` | `ENSAYO: GO` |
| 6 | Aplicación | `$R -Modo Aplicar -Autorizado $TOKEN -ConfirmarBaseReal` | `APLICAR: OK` |
| 7 | Verificación | `$R -Modo Verificar` | `VERIFICAR: OK`: 137 migraciones, índices nuevos, huella idéntica |
| 8 | Arrancar la aplicación con el código de `main` (sin `AllowMigrations`; el guard no interviene); abrir una caja de prueba, comprobar que una segunda apertura se rechaza con el mensaje de negocio y cerrarla | manual | Sin 500 |
| 9 | Cierre: conservar respaldo y log; registrar la aplicación en «Registro de aplicación» de este documento | — | — |

**No** habilitar `Database:AllowMigrations`, no usar `dotnet ef database update`, `--migrate` ni el servicio Docker `migrate`.
Presentes: el propietario (autoriza y da el OK a la apertura de prueba), quien opere el servidor (detener/arrancar la aplicación y ejecutar el runner) y, si se quiere, un usuario de caja para el paso 8.
Qué anunciar a los usuarios: «Mantenimiento programado de ventas/caja, de aproximadamente 15 minutos. No habrá acceso. Cerrar el turno de caja antes de la hora indicada; no se pierde información».

### Plan de rollback (con tiempos)
1. **Fallo del paso 6 o 7 (cualquier discrepancia):** `$R -Modo Rollback -Autorizado $TOKEN -ConfirmarBaseReal` (≈1 s; ensayo + reversión de índices e historial) y luego `$R -Modo Verificar -Esperado Previo` (≈1 s). Los datos no se tocaron: no se pierde nada.
2. **Si el rollback no deja el estado esperado o se sospecha daño:** restaurar el respaldo del paso 4: `ALTER DATABASE TheBuryProjectDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE TheBuryProjectDb FROM DISK='<ruta del paso 4>' WITH CHECKSUM, REPLACE; ALTER DATABASE TheBuryProjectDb SET MULTI_USER;` (≈1-2 s con 33 MB; se pierde lo registrado después del respaldo, que con la aplicación detenida es nada). Verificar con `-Modo Verificar -Esperado Previo`.
3. Rollback tras reabrir la aplicación: el código de `main` no depende del índice, así que se puede revertir sin redeploy; hacerlo con la aplicación detenida.

### Lo que debe autorizar el propietario
- [ ] Fecha y hora de la ventana (aplicación detenida ~15 min, sin turno de caja abierto).
- [ ] Ejecutar `Aplicar` contra `TheBuryProjectDb` (token del día + `-ConfirmarBaseReal`).
- [ ] Ruta del respaldo definitivo (propuesta: `D:\tmp\d04-ventana\`, ~190 GB libres) y su conservación posterior.
- [ ] Detener y volver a arrancar la aplicación real.
- [ ] La apertura/cierre de una caja de prueba en la base real (paso 8), o aceptar que se omita y quede solo la verificación del runner.

### PREPARADO vs EJECUTADO
| Procedimiento | PREPARADO | EJECUTADO |
|---|---|---|
| Preflight real | sí | sí, solo lectura sobre la base real (GO) |
| Respaldo `COPY_ONLY` + `VERIFYONLY` de la real | sí | sí, hacia `D:\tmp\mision\r2d04\real\` (para crear el clon) |
| Ensayo, aplicación, verificación, reaplicación, rollback, restore | sí | solo en el clon `TheBuryProjectDb_d04` |
| Aplicación a `TheBuryProjectDb` | sí | **NO** |
| Detener/arrancar la aplicación real | checklist | **NO** |


---

## Registro de aplicación (2026-11-12)

**Estado final: APLICADA Y VERIFICADA.** Autorizada expresamente por el propietario. Ejecutada con `scripts/ops/d04-ventana.ps1` (token `D04-20261112` + `-ConfirmarBaseReal`) desde `main` = `56ddfe55`, con la aplicación detenida. Directorio de trabajo, logs y huellas: `D:\respaldos-protegidos\d04-20261112\`.

### Identidad de la base y contexto
- Instancia `(localdb)\MSSQLLocalDB` (SQL Server 2025 Express/LocalDB); base `TheBuryProjectDb` creada el 2026-07-10, única con ese nombre.
- Estado: 0 ventas, 0 clientes, 0 proveedores, 16 productos, 11 usuarios, 8 cajas. Coincide con la limpieza de datos de prueba del 2026-11-05 (autorizada). Las 61 ventas anteriores están solo en el respaldo previo a esa limpieza (`D:\tmp\limpieza-REAL-previo-TheBuryProjectDb-20261105-151520.bak`, verificado; 61 ventas del 2026-09-26 al 2026-10-13, clientes de prueba). No se restauró nada.

### Preflight (01:15, solo lectura): GO en todas las guardas
Base existente; 136 migraciones aplicadas, 137 en el código, única pendiente D-04, sin extras; D-04 no registrada; índices previos `IX_AperturasCaja_CajaId` + `PK_AperturasCaja`; 0 duplicados; 0 turnos abiertos; 0 conexiones ajenas; 181 GB libres en D:. Huella: Cajas=8, AperturasCaja=0, MovimientosCaja=0, Ventas=0, Cuotas=0, Creditos=0 (208 MB).

### Respaldos
- Definitivo: `TheBuryProjectDb-previo-20261112-011521.bak` (`COPY_ONLY`, `CHECKSUM`, `RESTORE VERIFYONLY` correcto, 32,8 MB, SHA256 `FBB508061470…`), en `D:\respaldos-protegidos\d04-20261112\` y copia con hash idéntico en `E:\respaldos-protegidos\d04-20261112\`. Carpetas con permisos restringidos (usuario, SYSTEM, Administradores).
- Anteriores del mismo día: `…-010744.bak` (01:07) y el de `D:\tmp\d04-ronda3\real\`. Los 34 respaldos históricos de `D:\tmp` se conservaron intactos (manifiesto con SHA256 en `manifiesto-respaldos-historicos.csv`).

### Ejecución
| Paso | Resultado |
|---|---|
| Ensayo `DryRun=1` | GO; aplicó y revirtió, base intacta (136, índices previos) — 1,36 s |
| Aplicar (ensayo interno + aplicación) | `APLICAR: OK` — 0,47 s |
| Verificar | `VERIFICAR: OK` |

### Verificación posterior
- Migraciones: **137** (última `20261108185231_D04AperturaCajaUnicaAbierta`), sin pendientes ni extras.
- Índices de `AperturasCaja`: `PK_AperturasCaja`, `IX_AperturasCaja_CajaId_Cerrada` y `UX_AperturasCaja_CajaId_Abierta` (único, filtro `[Cerrada]=0 AND [IsDeleted]=0`), ninguno deshabilitado; `IX_AperturasCaja_CajaId` eliminado.
- Integridad: `DBCC CHECKDB` sin errores; 0 claves foráneas no confiables; huella idéntica a la del respaldo (ningún dato cambió).
- Aplicación: código de `main` compilado y levantado contra la base real con `Database:InitializeOnStartup=False` (sin sembrar): sin errores `fail`/`crit`, guard de migraciones sin intervenir, conexión a `TheBuryProjectDb`, login 200 y `/Caja` sin sesión redirige al login. Proceso detenido; 0 aperturas, 0 movimientos y 0 ventas tras la prueba.

### Pruebas no realizadas en la base real (por indicación del propietario)
No se abrió ni cerró una caja de prueba (paso 8): no se crearon movimientos. El rechazo de una segunda apertura vigente (error 2601 nombrando `UX_AperturasCaja_CajaId_Abierta`) y la reapertura tras cierre se probaron en el clon el 2026-11-12 (y el 2026-11-11 con la app contra el clon).

### Pendientes
- Los respaldos están en la misma máquina (D: y E:); la copia fuera de ella corresponde a P-06.
- Recuperación disponible si hiciera falta: `-Modo Rollback` del runner o restaurar el respaldo `…-011521.bak`.

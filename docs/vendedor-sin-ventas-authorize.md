# Vendedor sin `ventas.authorize` — aplicación a `TheBuryProjectDb`

**Estado: APLICADA el 2026-11-07 con autorización expresa del propietario.** Alcance exclusivo: retirar `ventas.authorize` del rol **Vendedor**. No autoriza migraciones ni otros cambios.

## Regla
Autorizar ventas (incluidas las de otros usuarios) se reserva a Gerente y superiores. El sembrador (`RolesPermisosSeeder`) ya no entrega el permiso al Vendedor; la base real lo conservaba como dato heredado y el sembrador no retira filas existentes.

## Procedimiento y evidencia
- **Estado previo (solo lectura):** `ventas.authorize` vigente en Administrador (Id 188), Gerente (312), SuperAdmin (39), **Vendedor (379)** y el rol `vendedor2` (3042, sin usuarios; **no autorizado, no se tocó**). Claim espejo en `AspNetRoleClaims`: Vendedor (204) y vendedor2 (241). 136 migraciones, 0 ventas, 0 conexiones.
- **Respaldo previo verificado:** `D:	mpendend-previo.bak` y `vend-previo-final.bak` (COPY_ONLY, CHECKSUM, `RESTORE VERIFYONLY` válido). Restauración de prueba en la base aislada `TheBuryProjectDb_vend_ens` (4.170 páginas).
- **Script:** `scripts/sql/vendedor-sin-ventas-authorize.sql` — transaccional (`XACT_ABORT`), idempotente, `DryRun` y `BaseEsperada` obligatorios, `THROW` si afecta más de 1 fila, si cambia cualquier otro permiso/claim o si el permiso sigue vigente. Baja lógica de la fila de `RolPermisos` y borrado del claim espejo del mismo rol; ningún otro rol, usuario ni dato.
- **Ensayo en la restauración aislada:** base equivocada rechazada (error 51071); DryRun reporta 1 fila (Id 379) + 1 claim y no cambia nada; aplicación; segunda corrida = 0 filas.
- **Verificación funcional (clon, app real, Playwright, sesiones ya abiertas):** antes, Vendedor, Gerente y Administrador pasan el filtro de Autorizar; se aplica el script con las sesiones abiertas; **en la solicitud siguiente el Vendedor queda denegado (GET y POST → AccessDenied)** y Gerente y Administrador conservan el acceso; el Vendedor sigue viendo `/Venta` y `/Cliente`.
- **Aplicación real:** DryRun=1 y luego DryRun=0 con `BaseEsperada=TheBuryProjectDb`. Resultado: `RolPermisos` 625 filas, bajas 4→5 (Id 379); `AspNetRoleClaims` 100→99; usuarios, roles, asignaciones de roles, migraciones (136), ventas, clientes y movimientos de caja **idénticos** (huellas antes/después); `DBCC CHECKDB` sin errores. Efectivo hoy: Vendedor 41 permisos vigentes (antes 42); `ventas.authorize` vigente solo en Administrador, Gerente, SuperAdmin y `vendedor2`; Gerente, Administrador y SuperAdmin conservan también `ventas.cancel` y `ventas.reject`. Usuarios reales con el permiso efectivo: admin, administrador, gerente y Javier2; sin él: vendedor, JAVIER (Cajero+Vendedor), cajero, contador, repositor, tecnico y nikky.
- **Pruebas de regresión:** `VendedorSinAutorizarVentasHttpTests` (sembrador; baja del permiso con sesión abierta → denegado en la solicitud siguiente; contrato del script). *Alcance honesto:* usa una venta inexistente (el filtro responde antes) y reconstruye la identidad en cada solicitud; la cookie real y la venta ajena se verificaron en el clon con Playwright. Estas pruebas no fallan sobre el código anterior porque el cambio es de **datos**, no de código.
- **Revisión de Codex (solo lectura):** sin bloqueantes; observaciones menores atendidas (reversión idempotente y atómica, aserciones más estrictas).

## Reversión
Ver el encabezado del script (reactivar el Id impreso e insertar el claim espejo con `NOT EXISTS`, en una transacción).

## Pendiente (decisión del propietario)
El rol `vendedor2` sigue teniendo `ventas.authorize` (sin usuarios asignados); no estaba en la autorización.

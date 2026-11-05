# Limpieza de datos operativos de prueba (base limpia para uso real)

Script: `scripts/sql/limpieza-datos-operativos.sql` (un solo batch, transaccional, sin deshabilitar restricciones).
Probado en clones de `TheBuryProjectDb` (135 migraciones, 2026-11-05). **No se ejecutó contra la base real** (requiere autorización).

## Qué elimina
- **Siempre (operativo):** ventas y sus detalles/envíos, cotizaciones y simulaciones de pago, facturas, notas de crédito (y su uso), devoluciones, RMA, garantías, contratos y documentos generados, solicitudes de autorización, créditos, cuotas, pagos, acuerdos, punitorios, alertas y logs de mora, historial de contactos, aperturas/cierres/movimientos de caja, cheques, órdenes de compra, movimientos de stock, unidades físicas, alertas de stock, notificaciones, tickets (y adjuntos/checklist), notas rápidas, pedidos/reclamos/mensajes de Mercado Libre.
- **Según variable:** `Clientes` (con documentos de cliente, historial de puntaje, garantes), `Proveedores` (con vínculos a categorías/marcas/productos), `Productos` (con precios históricos, lotes de precio, características, restricciones, publicaciones ML), `Auditoria` (`SeguridadEventosAuditoria`).
- **Además:** `Cajas.Estado` vuelve a `Cerrada` (columna independiente de `AperturasCaja`: sin esto la pantalla muestra «turno en curso»); si los productos se conservan, `StockActual = 0`.

## Qué conserva (verificado con huella conteo + checksum antes/después, aborta si cambia)
Historial de migraciones, usuarios, roles y permisos (`AspNetUsers`, `AspNetRoles`, `RolPermisos`, `AccionesModulo`, `ModulosSistema`, `RolesMetadata`), configuraciones (pago, tarjeta, planes, crédito, mora, punitorio, rentabilidad, empresa, sucursales, prefijos, umbrales, scoring), alícuotas IVA, categorías y marcas, plantillas de documento y versiones (incluida `RECIBO_COBRANZA` v2), tipos de documento, paquetes y reglas documentales, precios/costos de servicios, cajas (estructura) y asignaciones de vendedores, aceptaciones de términos, caches de Mercado Libre.

## Numeraciones (decisión independiente)
- `ReiniciarNumeraciones=0` (por defecto): no toca identities ni `TiposDocumento.UltimoNumero` (contrato, pagaré, recibo). Las series derivadas del máximo existente **reinician solas al borrar sus filas** y pueden repetir números ya exportados: cotizaciones (prefijo propio, fecha diaria, 4 dígitos), facturas (`FA-A-AAAAMM-000001`; `Facturas` no guarda CAE, la numeración es interna), órdenes de compra, créditos (`CRE-AAAAMM-NNNNNN`) y devoluciones. Las ventas usan `NumeracionVentaContadores` (hoy vacía → también empiezan en `VTA-AAAAMM-000001`). Anotar los últimos números usados antes de ejecutar si hay que conciliarlos.
- `ReiniciarNumeraciones=1`: además reinicia identities de las tablas vaciadas, `UltimoNumero` (contrato, pagaré, recibo, presupuesto) y contadores de venta.

## Procedimiento
1. Cerrar la aplicación y toda herramienta conectada a la base (el script lo exige).
2. Respaldo: `BACKUP DATABASE <base> TO DISK='...' WITH COPY_ONLY, CHECKSUM, INIT` + `RESTORE VERIFYONLY ... WITH CHECKSUM`.
3. Ensayo (`Ejecutar=0`, todo se revierte) y revisar el informe.
4. Ejecución (`Ejecutar=1`):
```
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -d TheBuryProjectDb -b -i scripts/sql/limpieza-datos-operativos.sql ^
  -v BaseEsperada=TheBuryProjectDb Ejecutar=1 Clientes=eliminar Proveedores=eliminar Productos=conservar ^
     Auditoria=conservar ReiniciarNumeraciones=0 MaxMinutosRespaldo=60
```
5. Archivos asociados (no los toca el script): `App_Data/contratos-venta-credito` y `wwwroot/uploads/tickets` (sus registros siempre se eliminan) y `wwwroot/uploads/documentos-clientes` (**solo archivarla si `Clientes=eliminar`**; con `conservar` los registros `DocumentosCliente` quedan y necesitan sus archivos). Copiar primero; mover a una carpeta de archivo con fecha **después** de confirmar el COMMIT. No mover `keys/` (claves de protección de datos).
6. Con `Clientes=conservar` el script reinicia su scoring y estadísticas derivadas (`PuntajeCliente`, `UltimaVentaFecha`, `CantidadComprasCliente`, `CreditosEnTermino`, `CreditosConAtraso`) y vacía `ClientesPuntajeHistorial`.
7. Con `Clientes=eliminar`, `MercadoLibreConfiguraciones` pierde su cliente asociado (SET NULL): reconfigurarlo antes de activar el canal. El script aborta si existen publicaciones de Mercado Libre.
8. Exclusividad: el script comprueba conexiones al empezar y justo antes del COMMIT, pero no impide que una app se conecte en el medio; mantener la aplicación detenida durante respaldo, ensayo y ejecución.

## Guardas (abortan sin cambiar nada)
Base distinta de `BaseEsperada` · historial ≠ 135 migraciones o sin `H12CostoServiciosVenta` · otras conexiones abiertas · respaldo completo de esta misma base (mismo `database_guid`) más viejo que `MaxMinutosRespaldo`, con más de un archivo o que no pasa `RESTORE VERIFYONLY ... WITH CHECKSUM` en ese momento · publicaciones de Mercado Libre existentes · tabla sin clasificar en el script · variables inválidas (`Ejecutar`/`ReiniciarNumeraciones` exactamente 0 o 1) · tablas a vaciar con filas · huella de tablas conservadas distinta · filas huérfanas (`DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS`) · caja abierta o stock restante.

## Recuperación
Restaurar el respaldo previo: `ALTER DATABASE <base> SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE <base> FROM DISK='<respaldo>' WITH REPLACE; ALTER DATABASE <base> SET MULTI_USER;` y restaurar la carpeta de archivos archivada. Un fallo dentro del script revierte todo (`XACT_ABORT ON`).

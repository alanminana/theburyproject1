# Módulo Cheque: acciones retiradas (FUN-D-001 / FE-D-001)

**Fecha:** 2026-10-26 · **Decisión:** el usuario eligió retirar las acciones (opción A) en lugar de crear las vistas.

## Qué pasaba
`ChequeController` exponía Index, Create, Details, Edit, Delete y CambiarEstado, pero `Views/Cheque` no existía: todas las pantallas respondían HTTP 500 (medido sobre `main` `f4417ba0`).

## Qué se hizo
- Se eliminó `Controllers/ChequeController.cs` (y su entrada en `DiagnosticoController`). `/Cheque/*` responde 404.
- **Se conserva** todo lo demás: `ChequeService`/`IChequeService` (registrado en DI), las entidades `Cheque` y `DatosCheque`, `EstadoCheque`, las tablas y los datos, y el uso de cheques como medio de pago en Venta/Caja. El módulo de permisos `cheques` sigue sembrado.
- Nada en `Views`, `wwwroot/js` ni otros controladores enlazaba esas rutas.

## Cómo reactivarlo
Diseñar las pantallas (Index/Create/Details/Edit/Delete) y restaurar el controlador desde el historial (`git show f4417ba0:Controllers/ChequeController.cs`). Antes de exponerlo, revisar `CambiarEstado` y los permisos por acción.

## Mismo cambio: otras rutas sin vista
- `ConfiguracionPago` Details/Create/Edit/Delete/ConfigurarTarjeta (legacy, sin vistas ni enlaces): retiradas (404). La configuración se administra en MediosPago y CreditoPersonal.
- `Mora/Configuracion` (sin vista): redirige a `Mora/ConfiguracionExpandida`.

# Circuito genérico de Autorizaciones: endpoints retirados (FE-D-001)

Base: `origin/main` `565cea8c`. Decisión funcional del usuario: **D1.B**, **U1** y **D4.A**.

## Qué se retiró y qué se conserva

| Se retira | Se conserva |
|---|---|
| `Controllers/AutorizacionController.cs` y por lo tanto todas las rutas `/Autorizacion/*` (Index, Solicitudes, DetallesSolicitud, CrearSolicitud, CrearUmbral, EditarUmbral, EliminarUmbral, AprobarSolicitud, RechazarSolicitud, CancelarSolicitud) | Entidades `SolicitudAutorizacion` / `UmbralAutorizacion`, tablas, migraciones y datos |
| | `IAutorizacionService` / `AutorizacionService` (registrado en DI), ViewModels y mapeos de AutoMapper |
| | Permisos `autorizaciones.view/approve/reject/managethresholds` en `RolesPermisosSeeder` y en los roles existentes (quedan sin efecto) |
| | **Todo** el circuito de autorización de Venta/Crédito (`Venta.EstadoAutorizacion`, `VentaService`, `CreditoController`, vistas `Autorizar_tw` / `Rechazar_tw`), que es otro dominio |

Motivo: el módulo genérico nunca tuvo vistas (`Views/Autorizacion` no existe en el historial), ningún código productivo consumía `Validar*Async`, y no había enlaces de navegación. Reactivarlo es un `git revert` del commit de retiro.

## U1 / D4.A: cancelación

`AutorizacionService.CancelarSolicitudAsync(int id)` solo permite cancelar al solicitante de una solicitud `Pendiente`:

- La identidad sale de `ICurrentUserService` (sesión autenticada), nunca de un parámetro del request.
- Sin usuario autenticado o con identidad vacía: `UnauthorizedAccessException` (falla cerrado).
- Solicitud ajena: `UnauthorizedAccessException`, antes de evaluar el estado, para no revelar información al no propietario. La comparación no distingue mayúsculas.
- No hay excepción administrativa. Un aprobador usa `RechazarSolicitudAsync`.

## Deuda y riesgos residuales del servicio conservado

El servicio sigue en DI pero sin superficie HTTP. Queda **sin atender**, por decisión, lo siguiente. Debe resolverse antes de reactivar cualquier endpoint:

- **H4:** `ValorPermitido`, `TipoOperacion` y `ReferenciaOperacionId` los fija quien llama; el servicio no los calcula ni valida.
- **H5:** no hay índice único `(Rol, TipoUmbral)`; la verificación de duplicados y el insert son pasos separados (carrera teórica, no verificada contra una base real).
- **H6:** el comentario de resolución no tiene contrato único: `Aprobar` acepta nulo (usa "Aprobado"), `Rechazar` lo exige solo a nivel de quien llama, y no hay límite de 500 caracteres en el servicio.
- **D3 (diferida):** `AprobarSolicitudAsync` / `RechazarSolicitudAsync` no comparan solicitante con autorizador; la autoaprobación es posible. Los tests HTTP que fijaban "Gerente con `approve` puede aprobar su propia solicitud" se retiraron junto con los endpoints; esa política no está decidida.
- **D2 (diferida):** `ObtenerTodasSolicitudesAsync` / `ObtenerSolicitudAsync` no filtran por propietario.
- Los permisos `autorizaciones.*` siguen apareciendo en el editor de roles y son asignables, pero no habilitan nada.
- **Uso real desconocido:** la evidencia es estática (código, tests, docs, JS, e2e). No se pueden descartar marcadores externos ni filas en bases reales; los datos se conservan.

## Checklist si se reactiva el circuito

Vistas Razor y su cobertura, D2–D4, H4–H6, índice único con revisión previa de duplicados, permisos por acción, y un contrato de pruebas HTTP/Playwright.

## Contratos de prueba

- `PrivilegeEscalationHttpTests`: las rutas retiradas devuelven 404 (incluso para Gerente y Administrador), no cambian datos, no existe ningún endpoint registrado con controller `Autorizacion` ni un tipo `AutorizacionController`, y `IAutorizacionService` sigue en DI.
- `AutorizacionServiceTests`: invariantes del servicio, incluida la cancelación solo por el solicitante.
- `AccionesMutantesPermisoContractTests`: ya no incluye `AutorizacionController`.

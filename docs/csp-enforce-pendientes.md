# CSP — qué falta antes de activar `Enforce` y defectos previos registrados

**Estado (2026-11-08):** `Csp:Mode=Off` por defecto. `ReportOnly` medido en clon: 0 violaciones en 161 páginas recorridas (admin, Chrome) y en las pantallas con datos sintéticos (`scripts/qa/csp-sintetico/`, PR #270). **No se activa `Enforce` hasta cerrar la lista de abajo.** Cada ítem es una prueba, no un cambio de código.

## Pruebas pendientes (orden de prioridad)

| # | Prueba | Cómo | Criterio de cierre |
|---|---|---|---|
| 1 | **Suite E2E existente con CSP en `ReportOnly`** | Levantar el stack de CI/clon con `Csp__Mode=ReportOnly`, correr `e2e/` completo y fallar si el log contiene `CSP violation` (o enganchar `securitypolicyviolation` en el fixture) | 0 violaciones en todos los specs (ventas, caja, crédito, cotizador, clientes, mora) |
| 2 | **Contenido cargado por AJAX / `innerHTML`** | Abrir cada modal/parcial remoto (Producto edit, Seguridad, Proveedor, Cliente, Catálogo) y las vistas armadas por JS (cotizador, carrito) con el medidor de violaciones activo | 0 violaciones; ningún `style`/`onclick` generado en cadenas JS |
| 3 | **Roles distintos del admin** | Recorrer Vendedor, Cajero, Contador, Repositor, Técnico, Gerente | 0 violaciones y mismas pantallas que `RolPermisos` |
| 4 | **Páginas sin layout, impresión y exportes** | `Cotizacion/Imprimir`, `Venta/ComprobanteFactura`, recibos, tickets, reportes (HTML/PDF/Excel) | Botones Imprimir/Volver funcionan (`data-action`), 0 violaciones |
| 5 | **Estados que el clon no tenía** | DocumentoCliente vacío (`click-id`), pestaña «ver» de Unidades, Borrador de ML, confirmaciones de ML en modo real (`modoSimulacionMensajes=false`) | Cada interacción verificada en `scripts/qa/csp-sintetico/` ampliado |
| 6 | **Otros navegadores** | Firefox y Safari/WebKit (nonce, `style-src-attr`, `script-src-attr`) y móvil real | 0 violaciones; mismas interacciones |
| 7 | **Proxy / despliegue** | Caddy + HTTPS: `connect-src 'self'` con WebSocket de SignalR, fuentes de Google, `frame-ancestors` | SignalR conecta; nada bloqueado en la consola |
| 8 | **Ventana en `ReportOnly` en uso real** | `Csp__Mode=ReportOnly` en el servidor durante un período acordado; revisar el WARN `CSP violation` (`/csp-report`, con rate limit) | 0 violaciones durante el período; recién entonces proponer `Enforce` |

Reglas: `Enforce` se habilita por configuración (`Csp__Mode=Enforce`), nunca por defecto; se prueba primero en un clon; la reversión es volver a `Off`/`ReportOnly` sin redeploy de código. Funciones llamables por `data-*` están en la lista cerrada de `wwwroot/js/csp-actions.js` (un test impide agregar una sin registrarla).

## Defectos previos registrados (no causados por la migración de handlers, no corregidos en #270)

### DEF-UNI-01 — Unidades: clic fuera del modal «Gestionar» no lo cierra
- **Dónde:** `Views/Producto/Unidades.cshtml`, `#modal-manage` (`data-action="call-self"` sobre el contenedor `fixed inset-0`).
- **Causa:** el contenedor interno `min-h-full flex items-center justify-center p-4` cubre todo el fondo; `event.target` nunca es el modal, así que la condición `target === this` (idéntica a la del `onclick` original) no se cumple.
- **Efecto:** solo se cierra con «Cancelar» o la X. Comportamiento previo a D-05 (verificado: clic directo sobre `#modal-manage` sí cierra).
- **Corrección propuesta (pequeña):** poner `data-action="call-self"` también en el contenedor interno, o `pointer-events-none` en él y `pointer-events-auto` en el panel.

### DEF-ML-01 — Mercado Libre / Listings: Vincular/Desvincular no hacen nada
- **Dónde:** `Views/MercadoLibre1/Listings.cshtml`: `<form id="ml-sync-form">` (línea 29) envuelve toda la tabla (cierra en 174) y contiene los formularios `Vincular` (125, 136) y `Desvincular` (160).
- **Causa:** formularios anidados son HTML inválido; el parser descarta los internos. `this.closest('tr').querySelector('.ml-unlink-form')` devuelve `null` (ya fallaba con el `onclick` original).
- **Efecto:** el botón «Desvincular» no envía nada; los formularios de vincular tampoco se envían como formularios propios.
- **Corrección propuesta:** sacar los formularios de la tabla (usar el atributo `form="..."` de los controles o formularios fuera del `<form>` de sincronización). Módulo ML diferido por el propietario: queda registrado, sin fecha.

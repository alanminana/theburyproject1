# Fase 16.2 - Cierre: decision final sobre Credito Personal y planes

Agente Kira | Fecha: 2026-05-08

## Decision adoptada: Opcion C

Credito Personal **no aplica ajustes por plan** en ninguna fase productiva activa ni proxima.

El resolver y la logica de venta conservan unicamente:

- Bloqueo por producto (`Permitido = false` en `ProductoCondicionPago` para `TipoPago.CreditoPersonal`).
- Maximo de cuotas escalar (`MaxCuotasCredito`) definido por `ProductoCondicionPago`.

No se implementa `AjustePorcentaje` ni ninguna otra semantica de plan para Credito Personal hasta que negocio defina explicitamente si el ajuste opera sobre capital, tasa mensual o total final (ver seccion G de Fase 15.1).

## Alcance de la decision

### Lo que NO se modifica

| Area | Estado |
|---|---|
| Logica productiva (VentaService, CreditoService, resolver) | Sin cambios |
| Base de datos y migraciones | Sin cambios |
| UI y JS (modal, venta, catalogo) | Sin cambios |
| CajaService | Sin cambios |
| Reportes y comprobantes | Sin cambios |
| Entidades y DTOs existentes | Sin cambios |

### Lo que SI permanece activo

- `ProductoCondicionPagoRules.ResolverCondicionesCarrito` aplica `MaxCuotasCredito` por producto como restriccion escalar.
- `VentaService` valida que la cuota seleccionada no exceda `MaxCuotasCredito` por producto.
- `ProductoCondicionPago.Permitido = false` bloquea Credito Personal por producto sin exponer cuotas.
- `ConfiguracionPago` conserva la configuracion global de credito personal como fallback.
- `TasaInteresMensual` del perfil de credito personal queda intacta como motor financiero.

## Razonamiento

Las opciones evaluadas fueron:

- **Opcion A**: Implementar planes activos para Credito Personal con `AjustePorcentaje`, integrando visibilidad y calculo.
  - Riesgo: requiere definicion de negocio no resuelta sobre si el ajuste opera sobre capital, tasa o total.
  - Riesgo: modifica motor financiero activo.
  - Descartada.

- **Opcion B**: Implementar planes para visibilidad de cuotas (sin calculo real), usando `AjustePorcentaje = 0`.
  - Riesgo: introduce deuda tecnica ambigua; si negocio despues define semantica distinta, los planes preexistentes sin ajuste generan ruido.
  - Descartada.

- **Opcion C** *(adoptada)*: Credito Personal conserva solo bloqueo por producto y `MaxCuotasCredito`. No se agregan planes ni ajustes hasta que negocio defina la semantica del ajuste porcentual.
  - Sin riesgo productivo.
  - Sin deuda tecnica inmediata.
  - La estructura de `ProductoCondicionPagoPlan` diseñada en Fase 15.1 puede extenderse a Credito Personal en una fase futura sin conflicto con esta decision.

## Preguntas pendientes de negocio (bloqueantes para futura extension)

Antes de implementar planes para Credito Personal, negocio debe resolver:

1. El `AjustePorcentaje` de un plan de Credito Personal, ¿opera sobre el capital antes de calcular cuotas, sobre la tasa mensual, sobre el total final o como descuento/recargo comercial aparte?
2. ¿El descuento maximo general (`PorcentajeDescuentoMaximo`) sigue vigente cuando un plan tiene ajuste negativo?
3. ¿Se permitiran pagos divididos por producto en el futuro? Si no, el carrito debe usar interseccion estricta tambien para Credito Personal.

## Validacion ejecutada

- `dotnet build --no-incremental`: OK, 0 advertencias, 0 errores.
- `git status --short`: solo cambios de configuracion y artefactos locales no versionados. Sin cambios en logica productiva.

## Checklist de cierre

- [x] Decision documentada como Opcion C.
- [x] Alcance sin modificaciones productivas, DB, migraciones, UI, JS, CajaService, reportes ni comprobantes.
- [x] Build limpio confirmado.
- [x] Git status sin cambios en codigo productivo.
- [x] Preguntas de negocio bloqueantes documentadas.
- [x] Estructura de Fase 15.1 compatible con extension futura a Credito Personal.

## Checklist general de fases

- Fase 8 - Condiciones de pago por producto: cerrada.
- Fase 9 - Credito personal por producto: cerrada.
- Fase 10 - Refactor ProductoController: cerrada.
- Fase 11 - Limpieza QA/release readiness: cerrada.
- Fase 12 - Refactor CreditoController: cerrada.
- Fase 13 - Validacion tecnica local: cerrada.
- Fase 14 - UX/UI modal condiciones de pago: cerrada.
- Fase 15.1 - Diseno cuotas por plan: cerrada.
- Fase 16.2 - Decision final Credito Personal y planes: cerrada con este documento.
- Fase 15.2 - Tests de caracterizacion/preparacion: proxima (no bloqueada por esta decision).
- Fase 15.3 - Entidades/migracion: pendiente (Credito Personal excluido hasta resolucion de negocio).
- Fase 15.4 - Service/resolver: pendiente.
- Fase 15.5 - UI modal cuotas por plan: pendiente.
- Fase 15.6 - Integracion venta/backend: pendiente.
- Staging/release final: pendiente.

## Prompt recomendado para proxima fase

```text
Agente Kira - Fase 15.2

Objetivo: agregar tests de caracterizacion/preparacion para cuotas por plan sin modificar DB, sin migraciones, sin UI y sin logica productiva.

Restricciones:
- No crear entidades nuevas todavia.
- No crear migraciones.
- No ejecutar database update.
- No modificar calculos de venta.
- No tocar CajaService, reportes ni comprobantes salvo lectura si hace falta.
- Credito Personal: cubrir solo bloqueo por producto y MaxCuotasCredito (Opcion C). No cubrir planes ni ajustes para Credito Personal.

Tareas:
- Cubrir comportamiento actual de ProductoCondicionPagoRules con maximos globales y por producto.
- Cubrir tarjeta especifica/general y fallback global.
- Cubrir credito personal con MaxCuotasCredito actual y bloqueo por Permitido.
- Cubrir que ajustes informativos no modifican TotalReferencia/TotalSinAplicarAjustes.
- Cubrir Venta/Create actual: diagnostico no cambia totales y solo limita cuotas.
- Dejar documentado que estos tests son baseline antes de entidad ProductoCondicionPagoPlan.

Validacion:
- Ejecutar dotnet test con filtros relacionados.
- Ejecutar dotnet build.
- Ejecutar git status --short al final.
```

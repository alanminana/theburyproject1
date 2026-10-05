# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Usuario principal: el dueño del comercio, que administra el sistema completo (ventas, caja, créditos, mora, inventario, proveedores, reportes y configuración). Además, 3 empleados lo usan a diario en distintos roles operativos: ventas, caja, mostrador y cobro de créditos/mora. Acceso por roles y permisos (módulo Seguridad).

## Product Purpose

ERP web para un comercio real: ventas (contado, tarjeta y crédito personal), cotizador, caja, clientes, productos e inventario (con trazabilidad por unidad física), proveedores y órdenes de compra, crédito propio y mora (scoring, evaluación, BCRA), y seguridad por roles y permisos. Existe para reemplazar procesos manuales/planillas con un sistema único que refleja el funcionamiento real del comercio.

## Positioning

Desarrollado completamente a medida para el funcionamiento real de este comercio específico, sin depender de módulos o productos de terceros: cada flujo y regla de negocio (ventas, caja, crédito propio, mora, inventario, proveedores) está diseñado para la operación real, en vez de adaptar el negocio a un ERP genérico. El motor de crédito/mora propio (scoring, evaluación, BCRA) integrado directamente a ventas y caja es parte del diferencial. A futuro incorporará integración directa con MercadoLibre.

## Operating Context

Aplicación ASP.NET Core (.NET 8, MVC + Razor) con Entity Framework Core sobre SQL Server. En producción corre en contenedores Docker detrás de Caddy (HTTPS), accesible solo por LAN/VPN (sin dependencia de servicios en la nube). Uso diario de mostrador: ventas, cobros, caja, y gestión de crédito/mora en el día a día del comercio.

## Capabilities and Constraints

- Módulos: Ventas (contado/tarjeta/crédito personal), Cotizador, Caja, Clientes, Productos/Inventario (trazabilidad por unidad física), Proveedores/Órdenes de compra, Crédito y mora (scoring, evaluación, autorización, BCRA), Seguridad (roles y permisos).
- Integración con MercadoLibre: prevista a futuro, no confirmada como implementada en este momento.
- Despliegue on-prem vía Docker/Caddy, sin nube pública, acceso restringido a LAN/VPN.
- Stack: .NET 8 MVC + Razor, EF Core, SQL Server; tests xUnit + SQLite en memoria y Playwright E2E.

## Brand Commitments

Ninguno confirmado más allá del nombre del proyecto (TheBuryProject). Sin compromisos de identidad visual fijados en esta ronda (eso se define en DESIGN.md / new-work, no aquí).

## Evidence on Hand

Documentación extensa de cierres de módulo y auditorías UX/UI en `docs/` (por fase y por módulo). No hay testimonios, casos de estudio, benchmarks ni datos de clientes externos para usar como evidencia: no inventar ninguno.

## Product Principles

- Las reglas de negocio reales (fiscales/legales AR: facturación, IVA, BCRA; crédito y mora) priman sobre cualquier conveniencia técnica o visual.
- El sistema se diseña para la operación real de este comercio, no para generalizar a un ERP de catálogo.
- Un único sistema integrado reemplaza planillas y herramientas sueltas: evitar fragmentar funcionalidad que hoy vive unificada.
- Mantenerse operable sin dependencia de servicios cloud externos (despliegue LAN/VPN on-prem).
- Permisos y roles reales gobiernan qué puede hacer cada uno de los 3 empleados y el dueño; no asumir acceso uniforme.

## Accessibility & Inclusion

Sin requisito de accesibilidad específico confirmado en esta ronda más allá del trabajo ya en curso documentado en `docs/ui/ERP-UI-STANDARD.md` y los cierres UX existentes (foco, aria, contraste, mobile). No se confirmó un estándar externo obligatorio (ej. WCAG nivel específico).

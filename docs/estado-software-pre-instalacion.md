# Estado del software antes de la instalación del servidor (2026-11-19)

Base: `main` bacc9744 + la tanda `task/software-cierre-20261119`. Alcance: todo lo que es exclusivo del **software** y verificable en un entorno descartable (Docker, SQL Server efímero, SQLite en memoria). Ninguna base real, usuario, respaldo ni servidor fue tocado. Este documento **no** reemplaza los criterios de aceptación de cada hito (`docs/hitos-d01-d03-inventario-y-criterios.md`, `docs/aceptacion-erp.md`): los consolida y separa lo terminado de lo bloqueado.

## 1. Software terminado y probado

| Área | Estado | Evidencia vigente |
|---|---|---|
| D-01 Listas de precios y costeo | Finalizado | `hitos…` §14-16, `ListaPrecioPrecioManualSqlServerTests` (SQL Server RCSI 0/1) |
| D-02 Referencias, crédito y autorizaciones | Finalizado | `hitos…` §16, `AutorizacionParcialSqlServerTests` D02-CONC-01..03 |
| D-03 Tesorería, anticipos, cheque único por pago | Finalizado | `hitos…` §17, `TesoreriaSqlServerTests` D03-CONC-01..09, D03-MIG-01 |
| V-02 Cheques recibidos como pago pendiente informativo | Finalizado (alcance informativo) | `hitos…` §18, `ChequeInformativoHttpTests`, SQL 29/29 en RCSI 0 y 1 |
| P-03 Seguridad de usuarios (parte software) | Verificado | Bootstrap (Production exige `Admin:Email`/`Admin:Password`; sin config no se crea cuenta), protección estructural y habilitada del último SuperAdmin, recuperación por la cuenta configurada, revocación de sesión (SecurityStamp) al rotar clave/desactivar/logout, auditoría, `TransicionCuentasCookieHttpTests`. **Nuevo en esta tanda:** `CreateTestUsersAsync` (cuentas con clave conocida) ahora se niega a ejecutarse fuera de Development aunque un llamador futuro lo pida (`PermiteUsuariosDePrueba`, probado) |
| D-05 CSP | Preparado y medido; **no activado** | `docs/csp-enforce-pendientes.md`, `docs/csp-enforce-activacion.md`. En esta tanda: recorrido de 7 roles × 3 tamaños con `Csp__Mode=Enforce` en un stack QA local: **0 violaciones** (`scripts/qa/a11y-responsive/crawl.js`, también mide HTTP 4xx/5xx y desborde; el hallazgo de la revisión de Codex —una auditoría axe que no se ejecuta no puede contarse como «0 violaciones»— se corrigió: el crawler cuenta auditorías completadas y falla si alguna no corrió; así se detectó que bajo `Enforce` el script inyectado era bloqueado por la CSP y se pasó a inyectarlo por CDP) |
| Accesibilidad (contraste y estructura) | Corregido y con pruebas | `ContrasteAccesibilidadTests` (D-03) + `AccesibilidadEstructuraContractTests` (esta tanda). axe-core (WCAG 2.0/2.1 A/AA + buenas prácticas): del recorrido inicial de 53 pantallas × 3 tamaños salieron 5 reglas incumplidas (landmarks duplicados, rol ARIA inválido en tarjetas de clientes, `aria-required-children` en «Filtrar cajas», `aria-pressed` en pestañas de Créditos, saltos de encabezado) y la pantalla «Acceso denegado» sin `h1` y con contraste 2,9:1. **Todas corregidas (más el texto decorativo «Restricted Area» de esa pantalla, 2,9:1, retirado): 0 violaciones en los 7 roles, con auditorías axe completadas (159 + 69 + 45 + 45 + 18 + 12 + 69), a 1366×768, 768×1024 y 390×844 y con `Csp__Mode=Enforce`**, 0 desbordes horizontales, 0 errores 5xx. También 180 vistas por nombre de archivo (las que exigen id responden 404, esperado). |
| Interfaz: chip «Banco / billetera» | Corregido | La clase `chip-violet` solo existía en el CSS de Crédito; se agregó a Caja (contraste ≥ 4,5:1) |
| Día comercial argentino (zona horaria del servidor) | Corregido | Reglas por fecha que usaban `DateTime.Today`/`UtcNow.Date` pasan al día comercial (UTC−3): alertas de mora, vencimiento y marcado de documentos del cliente, aptitud documental, reprogramación de envíos, promesas de pago, vigencia de plantillas, fechas por defecto de formularios y fechas impresas en documentos. En un servidor en UTC (Docker/VM Linux) el día cambiaba a las 21:00 de Argentina y adelantaba esas reglas. Evidencia viva: durante la tanda, a las 21:55 ART, dos pruebas que usaban UTC fallaron y se corrigieron; `DiaComercialServidorUtcTests` fija el instante 21:55 ART |
| Instalación limpia | Verificada | 144 migraciones sobre SQL Server descartable, 1 administrador, sin datos de negocio (repetida en esta tanda) |
| Autorización por URL forzada | Sin anomalías | `scripts/qa/seguridad/sondeo-url-forzada.js`: 7 roles + anónimo; el POST con inventario completo lo cubre `AutorizacionMutantesSinEfectosHttpTests` en CI |
| Suite automática | 8772 correctas, 0 fallos (253 omitidas por entorno: requieren SQL Server o navegador y corren en CI) | CI de la PR |

## 2. Decisiones fiscales o funcionales todavía bloqueadas (nada de esto se implementó)

**Contador / fiscal (P-02).** Ya está decidido que la facturación fiscal se emite **fuera** del ERP (P1/P2 de `docs/iva-decisiones-contador.md` quedan resueltas: el IVA del ERP es informativo). No se modificó IVA, intereses, notas de crédito fiscales, redondeos ni costos contables. Siguen abiertas: C1–C15 (redondeo del total, base imponible del descuento en efectivo, envío/armado al 21 %, recargos, IVA de punitorios e interés, crédito fiscal de compra, neteo de devoluciones, plazo de 7 días, período fiscal UTC−3) y las autorizaciones del propietario P3 (netear devoluciones en el reporte de IVA), P4 (leyenda «documento no válido como factura» en el impreso: con la facturación externa decidida, es la consecuencia directa y requiere el texto legal del contador), P5 (versionar el criterio por operación) y P6 (qué fila implementar y desde qué fecha).

**Propietario (funcional).**

1. Cheque = egreso (D-03): hoy el importe del pago/anticipo debe **igualar** el del cheque; alternativa «no mayor» (un cambio de una línea).
2. Cheques propios y endosos de cheques de clientes: no implementados.
3. Saldos por origen de fondos, arqueo y conciliación bancaria; exportación del estado de cuenta (PDF/Excel).
4. R-08 (rol Gerente en cuentas por pagar): hoy se asigna a mano desde Seguridad → Roles; **clasificado: diferido, sin brecha de software**.
5. R-11 (vínculo RMA → nota de crédito de proveedor): **diferido**, exige definir qué RMA respalda qué nota de crédito.
6. Rol `vendedor2` (A retirar permiso / B eliminar / C mantener): `docs/seguridad-cierre-f3.md` §2 y `docs/vendedor2-eliminacion.md`.
7. Nombres y apellidos de personas: el validador `SoloLetras` admite **10 letras por palabra y 3 palabras** (una prueba lo fija a propósito: «Maximiliano» se rechaza). Rechaza apellidos legítimos como «Constantino» o «Wojciechowski». Propuesta: 20 letras y 6 palabras (como ya usan Proveedor y otros campos); es una regla de negocio de alta de personas, por eso no se cambió sin autorización.

## 3. Validaciones que obligatoriamente requieren el servidor real

- Respaldos: capacidad real de disco, copia externa/SFTP, restauración cronometrada, custodia de la clave de Caddy (`BACKUP_CA_KEY`), arranque sin sesión de usuario.
- Plataforma: Windows Server + Hyper-V/VM, convivencia con la VM, Tailscale (identidades y ACL), firewall, Caddy/HTTPS reales, SignalR sobre el proxy real.
- CSP: ventana de observación en `ReportOnly` con uso real (ítem 8 de `csp-enforce-pendientes.md`) antes de proponer `Enforce`; Firefox/Safari y móvil físicos.
- Cuentas reales: creación de usuarios productivos, rotación/desactivación de `admin` y cuentas demo (runbook de `p03-ensayo-transicion-cuentas.md` §4) y el mínimo de 10 caracteres solo rige con `ASPNETCORE_ENVIRONMENT=Production`.
- Migraciones sobre la base real (`H10…`, `D03…`, etc.): con respaldo verificado y autorización expresa.
- Zona horaria del contenedor/VM: el software ya usa el día comercial argentino; la hora **mostrada en logs** sigue siendo UTC (esperado).
- Lectores de pantalla reales (NVDA/JAWS/VoiceOver): **no verificados**; axe y las pruebas de contraste no los sustituyen.

## 4. Funcionalidades opcionales o diferidas (no implementar sin autorización)

Facturación electrónica ARCA/AFIP (decidida fuera del ERP); cheques propios y endosos; saldos por origen/arqueo/conciliación bancaria; exportación del estado de cuenta; R-08; R-11; integración de Mercado Libre (módulo fuera del alcance del cierre; sus vistas viven en `Views/MercadoLibre1`); lotes de precios por listas (D-4 del Hito 11) más allá del cambio directo del Catálogo; seguimiento de acreditación de cheques (retirado en V-02, los servicios se conservan).

## 5. Documentación corregida o marcada como histórica en esta tanda

- `docs/auditoria-funcional-matriz.md` y `docs/auditoria-funcional-inventario.csv` son la **foto del 2026-11-07**: «Incompleta» en lotes de precios («sin listas de precios») y «No verificada» en acreditar cheques ya no describen el sistema (D-01 entregó las listas de precios; V-02 retiró la acreditación). Quedan como histórico con una nota al inicio.
- `docs/p03-ensayo-transicion-cuentas.md` §3.1 (límite de nombres) se remite a la decisión 7 de la sección 2.
- `docs/iva-decisiones-contador.md`: P1/P2 resueltas por el propietario (facturación fiscal externa).

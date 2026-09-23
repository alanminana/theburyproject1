# Red local y acceso remoto por VPN

**Estado: POSPUESTA HASTA INSTALACIÓN EN EL HOST/RED DEFINITIVOS.**

Decisión del usuario, 2026-09-22: el servidor se instalará en otra ubicación y todavía
no existe configuración de esa red. Este documento registra el objetivo y los
pendientes; no constituye una configuración de despliegue ni una validación de aislamiento.

## Arquitectura objetivo pendiente

```text
Usuarios locales ── LAN ─────────────────────────┐
                                                v
Acceso remoto futuro ── VPN ── LAN ──> Host definitivo
                                        Caddy
                                          |
                                          v
                                      app:8080
                                          |
                                          v
                                       SQL:1433
```

Caddy será el punto normal de acceso al ERP. El ERP no deberá publicarse
directamente a Internet. SQL 1433 y Kestrel 8080 deberán permanecer sin exposición
pública. Una eventual exposición del servicio VPN se decidirá según la solución
elegida; no se autoriza port forwarding al ERP.

## Estado observado en archivos, sin cambios de infraestructura

| Servicio | Puerto interno | Publicación actual en Compose | Interfaz host |
|---|---|---|---|
| SQL | 1433 TCP | Ninguna en producción; 1433 por defecto en overlay de desarrollo | 127.0.0.1 solo en desarrollo |
| app / Kestrel | 8080 TCP | Ninguna en producción; 8080 por defecto en overlay de desarrollo | 127.0.0.1 solo en desarrollo |
| Caddy | 80 TCP, 443 TCP/UDP | 80 TCP, 443 TCP/UDP | Sin IP explícita en los bindings |
| Uptime Kuma, Compose separado | 3001 TCP | 3001 TCP | 127.0.0.1 |
| Agente de monitoreo | Sin listener entrante | Ninguna | No corresponde |
| Inicialización, migración y herramientas de backup/restore | Sin listener entrante | Ninguna | No corresponde |

El Compose de producción usa la red bridge `bury-net`. La app confía en forwarded
headers de la subred Docker configurada y loopback; esto no se modificó.
El `Caddyfile` y la documentación previa todavía contemplan TLS/ACME y un dominio
público. Se conservan por instrucción expresa: **no representan una decisión de
publicar el ERP ni acreditan que la arquitectura privada ya esté implementada**.
Los bindings sin IP explícita tampoco prueban accesibilidad desde Internet: faltan
el host, el firewall y el router reales.

## Pendientes para la instalación final

| Tema | Decisión o comprobación pendiente |
|---|---|
| Host | Sistema operativo del servidor definitivo |
| LAN | IP reservada/estática, subnet y ausencia de conflictos con redes Docker/VPN |
| Nombre | Hostname interno; no hay nombre elegido |
| DNS | Inventariar router/DNS existente y definir resolución desde LAN y VPN; evaluar hosts temporal si hace falta |
| VPN | Descubrir infraestructura existente y elegir solución antes de instalar o generar claves |
| Rutas VPN | Definir únicamente destinos necesarios; no anunciar redes Docker ni rutas generales sin necesidad |
| Caddy | Bindings e interfaces definitivos; decidir HTTP 80 y HTTPS según el entorno |
| TLS interno | Elegir estrategia, persistencia, recuperación y relación con los backups existentes |
| Clientes | Definir confianza de CA en Windows/Linux/móviles si se elige CA privada; no distribuida |
| Firewall | Inventariar y aplicar reglas del host en el bloque final, preservando acceso administrativo |
| Router | Comprobar ausencia de exposición directa del ERP; definir por separado cualquier requisito de la VPN |
| CGNAT | NO VERIFICADO; evaluar con información de la conexión definitiva |
| Monitoreo | Validar acceso privado, confianza TLS y umbrales según certificados elegidos; sin cambios ahora |
| Aislamiento | Comprobar que 1433/8080 y administración de Kuma/Docker no sean accesibles desde LAN/VPN/Internet |
| Navegación | Probar hostname, certificado, redirects, login/logout, cookies y health desde LAN y VPN |
| Sesiones | Verificar comportamiento al pasar de LAN a VPN con el mismo hostname, si ese diseño es viable |
| Proxy | Verificar Scheme, Host, IP del cliente y rechazo de spoofing de forwarded headers |

Si ERP, agente y Kuma están en la misma ubicación, no queda garantizado un aviso
externo ante pérdida total de energía, router o conectividad. Un observador externo
con un canal independiente será necesario si se requiere esa cobertura; no se instala ahora.

## Integraciones a considerar al retomar

| Integración observada | Dirección | Consecuencia para el objetivo privado |
|---|---|---|
| BCRA (`SituacionCrediticiaBcraService`) | Saliente | No exige publicar el ERP; verificar salida en la red definitiva |
| API de Mercado Libre (`MercadoLibreApiClient`) | Saliente | La consulta a la API no exige entrada pública; revisar por separado autorización y notificaciones |
| OAuth de Mercado Libre (`/MercadoLibre/OAuthCallback`) | Retorno del navegador hacia el ERP | Verificar requisitos del proveedor y accesibilidad del navegador; no asumir compatibilidad de una URI privada |
| Webhook de Mercado Libre (`/api/mercadolibre/webhook`) | Entrante desde el proveedor | Requiere diseño específico si debe recibir notificaciones desde Internet |
| Otras integraciones con callbacks/webhooks entrantes | Pendiente de inventario final | Evaluar individualmente; no publicar todo el ERP para resolverlas |

**MERCADO LIBRE EXTERNO → PENDIENTE.** No se implementan callbacks públicos,
túneles ni excepciones de red en esta tarea.

## Evidencia y límites

Se revisaron los archivos de Compose, Caddy, configuración del proxy y monitoreo.
La inspección local no detectó servicios/adaptadores de WireGuard, Tailscale,
ZeroTier u OpenVPN ni perfiles VPN de Windows. Esa estación no es el servidor
definitivo y el resultado no determina la infraestructura de la otra ubicación.

No se hicieron pruebas funcionales de LAN/VPN/Internet, DNS definitivo, login,
cookies, certificados ni aislamiento del servidor real. Todos esos resultados
quedan **NO VERIFICADOS**. No se instalaron VPN, DNS ni CA en clientes, ni se
aplicaron reglas de firewall/router. Los ajustes locales preliminares de esta
tarea fueron retirados al recibir la instrucción de posponer; no se desplegaron.

El único entregable persistente de esta tarea es este documento. Se preservan
los cambios que ya existían en el working tree, sin staging, commit ni push.

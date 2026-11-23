# Puesta en marcha P-07 / P-02: preflight y bloqueos (2026-11-23)

Base: `main` 26873edb. Solo lectura sobre la VM `tbp` y su preproducción sintética; **no se instaló nada, no se capturó el instante fiscal y no se tocó ninguna base**. Complementa `puesta-en-marcha-p07-p02.md`.

## 1. Preflight (Etapa A) — resultado

| Control | Resultado |
|---|---|
| VM `tbp` (Ubuntu 26.04.1, 4 vCPU, 7,8 GB RAM, 72 GB libres, Docker 29.10 / Compose 5.8) | Suficiente para un segundo stack |
| Reloj | `System clock synchronized: yes`, NTP activo, zona `America/Argentina/Buenos_Aires` (-03) |
| Proyecto `theburyproject-prod` | **No existe** (ni contenedores ni volúmenes); solo `theburyproject` (preproducción sintética) |
| Verificadores SQL con `sqlcmd -b` | Salida ≠ 0 ante FALLA comprobada (`p07-verificar-credito-cerrado` y `p07-verificar-cuentas` → `EXIT=1`; `p02-verificar-instalacion` → `EXIT=0`), solo lectura, contra la base sintética |
| Semilla del puntaje 0 | Confirmada en $200.000 en una instalación nueva (hay que bajarla a $0 por pantalla) |
| Respaldos de la preproducción | Full diario, log cada 15 min, `verifyonly=OK`, copia local cifrada (LUKS) montada; **sin copia externa** (`BACKUP_REMOTE` vacío): riesgo aceptado por el propietario, no mitigado |
| CI de `main` | Verde sobre 26873edb |

## 2. Qué impide ejecutar la Etapa C hoy

1. **No hay servidor definitivo.** P-04 sigue diferido. La única máquina disponible es la VM `tbp`, que además es de la PC de desarrollo. Y su preproducción ocupa `0.0.0.0:443` (`ERP_BIND_ADDRESS=0.0.0.0`): un segundo Caddy no puede arrancar allí sin detener o reconfigurar ese entorno, cosa que la autorización excluye. Instalar la base real en una VM de pruebas es además una decisión de alcance del propietario.
2. **Falta la identidad del administrador inicial** (`--admin-email` obligatorio, `--admin-user`). No se inventa.
3. **Falta el destino de respaldos propio** (`--backup-dir` distinto de `/srv/bury-backups` de la preproducción).

El instante fiscal **no** es un bloqueo: se toma del reloj en el momento del corte (decisión del propietario) y se pasa una sola vez a `--fiscal-v2-desde`, en UTC con sufijo `Z`, repitiéndolo en `FISCAL_CRITERIO_V2_DESDE` del `.env`. Si el reloj no está sincronizado, no se corta.

## 3. Inconsistencia de interés por mora (`ConfiguracionMora.TasaMoraBase`)

### Origen
La fila de `ConfiguracionesMora` no se siembra: la crea `MoraService.GetConfiguracionAsync` al primer acceso con **TasaMoraBase = 5, DiasGracia = 3, ProcesoAutomaticoActivo = true, NotificacionesActivas = true**. Verificado en una instalación nueva (fila id 1 con esos valores). `ConfiguracionMoraService` tiene otro default (0,1) que no se usa si la fila ya existe.

### Consumidores reales

| Consumidor | Qué hace con la tasa | Afecta dinero real |
|---|---|---|
| `MoraService.CalcularMora` (→ `AlertaCobranza.MontoMoraCalculada`, KPIs de bandeja `MontoMora`/`MoraTotal`) | `monto × (tasa/100/30) × días`: **interpreta la tasa como mensual** | Solo informativo (alertas); no genera deuda ni cuota |
| `ContratoVentaCreditoService`, `DocumentoConfiguracionService` (empresa), `DocumentoContextoBuilder` (`contrato.interesMoraDiario`, `empresa.interesMoraDiario`) | Si `tasa > 0` **pisa** el interés de empresa/plantilla y lo imprime como **diario** | Texto contractual y pagaré: con 5 imprimiría «5 % diario» |
| Pantalla Mora (`ConfiguracionMoraService`/`AutoMapperProfile`) | Muestra/guarda la tasa | No |
| **Punitorio efectivo** (`ConfiguracionPunitorio`, cuotas) | **No lo lee** | Es la única fuente de la deuda punitoria real |

Tres conceptos distintos usan un mismo campo: interés contractual (diario), interés de alerta (mensual) y punitorio efectivo (otra entidad). Unidades contradictorias entre los dos primeros.

### Alcance exacto
- **Contado: no afectado.** Ningún importe de venta, caja, factura ni recibo usa `TasaMoraBase`.
- **Crédito cerrado (cupos $0, sin planes): sin exposición**, no hay cuotas vencidas ni contratos.
- Exposición real solo al abrir crédito: (a) texto del contrato/pagaré con 5 % diario si no se corrige; (b) alertas con mora estimada que no coincide con el punitorio (0 %) y que se mostraría a cobranza como «mora calculada».
- Poner `TasaMoraBase = 0` por pantalla **no basta**: `tasa > 0` falla y el contrato/documentos caen al valor de empresa/plantilla (semilla 0,05 %), y `ValidarPlantilla` exige interés > 0 (la generación de contratos queda bloqueada con error si ambos son 0). Por eso 0 no equivale a «sin interés contractual».

### Alternativas (sin cambiar reglas financieras todavía)
- **A.** Alinear al punitorio vigente: que contrato y alertas lean `ConfiguracionPunitorio` (única autoridad) y retirar `TasaMoraBase` de ambos usos. Mayor coherencia; cambia reglas contables.
- **B.** Mantener `TasaMoraBase` solo como parámetro de alertas, con unidad explícita (mensual) y rotulado «estimación informativa», y que el contrato lea del punitorio.
- **C.** Operativa mínima sin código: dejar crédito cerrado, y antes de abrirlo fijar por pantalla una tasa acordada y revisar texto de contrato; aceptar la discrepancia informativa de alertas.

Recomendación: A o B antes de abrir crédito; mientras tanto C. Requiere decisión comercial y contable; **no se estableció ninguna tasa**. No bloquea contado.

## 4. Configuración de mora al instalar (por pantalla)
La fila creada por defecto trae gracia 3 y proceso automático activo; el valor aprobado es **gracia 12** (igual a punitorios) y la bandeja no debe operar sobre crédito mientras esté cerrado. Verificar tras el alta de pantalla: gracia 12, punitorios 0 % / 30 / 12.

## 5. Para destrabar (una sola consulta al propietario)
1. Dónde instalar la base real (servidor físico/P-04, o autorizar reemplazar la preproducción sintética de la VM / asignar otra IP con ajuste del bind).
2. Correo y usuario reales del administrador inicial, y los dos SuperAdmin.
3. Directorio de respaldos propio y dónde se custodian `.env` y clave/cabecera LUKS.
4. Decisión A/B/C de §3 (no bloquea contado).

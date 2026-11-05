# Preparación operativa — estado y propuestas (2026-11-05, `main` 100e0219)

Base real `TheBuryProjectDb` ya limpia (PR #231). Todo lo probado acá se hizo en clones; la base real no se modificó. Este documento no cambia ninguna regla comercial ni fiscal.

## Entorno de ejecución

- Ejecutar **solo** desde un worktree limpio dedicado (`D:\git\theburyproject1-run`) con `scripts/ejecutar-erp-local.ps1`: lleva el árbol a `origin/main`, compila en `bin\app`, exige puerto libre y arranca con `Database:InitializeOnStartup=false`.
- No ejecutar desde `D:\git\theburyproject1` (rama `trabajo/cambios-pendientes`, 379 commits detrás de `main`, con cambios sin integrar).
- El lanzador exige `HEAD == origin/main` (hash completo), árbol limpio, puerto libre y ninguna otra instancia de `TheBuryProyect.dll`; verifica que la app responda antes de informar éxito.
- **`InitializeOnStartup=false` no detiene los 7 servicios en segundo plano**, que actúan solos mientras la app esté viva: mora (`ConfiguracionesMora.JobActivo=1`, cálculo automático, 08:00; crea su configuración si falta), incumplimiento de acuerdos (cada minuto), alertas de stock, vencimiento de cotizaciones, documentos vencidos, consumidor de webhooks de Mercado Libre (el webhook es anónimo, con límite de tamaño y tasa) y vencimiento de borradores de crédito (**deshabilitado** por configuración). Sin datos de crédito no hay efecto hoy; decidir antes de cargar créditos reales si el job de mora debe quedar activo.
- Entorno por defecto `Development` (LocalDB con Windows Auth, sin `ErpDb:*`): habilita `DiagnosticoController` (solo SuperAdmin + antiforgery; incluye crear usuarios de prueba y reiniciar contraseñas a `Test123!`). `Production` exige `ErpDb:User/Password` (login SQL dedicado) y HTTPS: decisión de despliegue pendiente, no se cambió.
- Los worktrees comparten el `UserSecretsId`; las claves de protección de datos (`keys/`) y los archivos subidos (`App_Data`, `wwwroot/uploads`) viven bajo el directorio de ejecución: **usar siempre el mismo worktree de ejecución** (cambiarlo invalida cookies y deja sin leer tokens cifrados de Mercado Libre) y respaldar la base **y** esos directorios. El backup documentado cubre Docker/Linux, no este LocalDB.

## Archivos de pruebas anteriores (huérfanos)

127 archivos sin ningún registro en la base (`ContratosVentaCredito`, `TicketAdjuntos`, `DocumentosCliente`, `DocumentosGenerados` = 0): 59 PDF de contratos (`App_Data/contratos-venta-credito`), 56 de tickets y 12 de documentos de clientes (`wwwroot/uploads`). Inventario con SHA256 y copia verificada en `D:\tmp\archivo-limpieza-20261105` (`INVENTARIO.csv`, `copia\`, `archivar.ps1` sin ejecutar, `restaurar.ps1`). No se movió ni borró nada; `keys/` no forma parte.

## Productos (16, stock 0) — propuesta, sin ejecutar

| Propuesta | Productos |
|---|---|
| Conservar (probable catálogo real, a confirmar precios) | 001002 TERMOZAFIRO80GN, 001003 TERMOZAFIRO50GN, 00501 TERMO 1.2LT, 001006 VASO TERMICO GRIP, 00107 GATO BLANCO, 002005 LAVADREAN8.1400KG, 002006 LAVADREAN6.08 |
| Eliminar (claramente de prueba) | 666666 «PERRITO DEL INFIERNO este problema», ppp «PERRITO DEL INFIERNO», 1111 «asd», vendedor «soy vendedor en productos», pepito, 0063 «bondiola» |
| Revisar (precio de venta $2, costo $0 o nombre dudoso) | cat-0099 «maquina de coser», 522035 «horno», 522037 «MAPACHEDALTONICO» |

Categorías/marcas con uso real: TERMOTANQUE, TERMOS Y VASOS TERMICOS, PELUCHES, LAVARROPAS, HORNOS; STANLEY, SEÑORIAL, PELUCHIN, DREAN, FLORENCIA. Sin productos (ficticias o sin uso): «maquina de coser» (categoría y su marca SINGER), PELUCHES/PELUCHIN si se descartan los peluches, QUIRINCHO, CARGA FRONTAL, HORNOS DE EMPOTRAR y 6 categorías de termotanques sin productos. Las eliminadas lógicamente («Categoria QA Refresh Test», «Marca QA Refresh Test», «Submarca QA Test») ya están dadas de baja. Tipo de documento «0063 bondiola» (inactivo, no de sistema) es de prueba.

## Cajas (8; 3 eliminadas lógicamente)

Activas: `central` (01), `NIKKY` (CAJA 6), `CERRADA` (02), `Akasha` (87), `caja compartida` (pepito). Ubicaciones ficticias («FAR FAR AWAY», «infierno», «cosmefeuclanito»). Sin aperturas ni movimientos, todas `Cerrada`. Asignaciones por caja: 2 → cajero/JAVIER/vendedor; 1004 → ídem; 1005 → cajero/JAVIER/nikky/vendedor. Propuesta: renombrar/ubicar las que se usarán y dar de baja el resto desde la pantalla.

## Usuarios, roles y permisos — hallazgos y propuesta

- 11 usuarios: 8 de demostración `@thebury.com` (admin, administrador, cajero, contador, gerente, repositor, tecnico, vendedor) y 3 con correos que parecen personales (JAVIER, Javier2, nikky). Roles: SuperAdmin 174 permisos (solo `admin`), Administrador 168 (administrador, Javier2), Gerente 107, Vendedor 45, Cajero 29, Contador 25, Tecnico 23, Repositor 17, `vendedor2` (inactivo, sin usuarios), `vendedor pobre` 2.
- **Crítico:** `admin` (único SuperAdmin) y `administrador` aceptan la contraseña de demostración documentada `Admin123!` (verificado en el clon; las otras 9 cuentas la rechazan). No se modificó ninguna credencial.
- Crear cuentas desde Seguridad funciona (se creó un SuperAdmin y 5 usuarios de rol en el clon). El primer ingreso de cada usuario exige aceptar «Términos y condiciones».
- Matriz de acceso por rol (21 rutas × 6 roles) coherente: p. ej. cajero sin Venta/Create, Catálogo, Reportes ni Seguridad; contador sin Caja ni Venta/Create; repositor solo Dashboard, Catálogo y Devolución; Seguridad, Envíos y armados y Medios de pago solo SuperAdmin.
- El último SuperAdmin está protegido por el servidor (tests de #98/#100; no se probó eliminarlo en la base real).
- Confirmado en código: cambiar la contraseña de `admin` está permitido (política de contraseña + antiforgery) y revoca sesiones previas; desactivarlo solo se permite si queda otro SuperAdmin habilitado (guardia con `UPDLOCK, HOLDLOCK`); el login bloquea 5 minutos tras 5 fallos; la recuperación de contraseña por correo está deshabilitada; `Admin:Password` no resetea contraseñas existentes. La guardia no comprueba que alguien conozca la contraseña ni que haya aceptado los términos.
- **Propuesta de transición sin perder acceso:** (1) crear la cuenta SuperAdmin propia con contraseña fuerte; (2) ingresar con ella **en una sesión independiente**, aceptar los términos y verificar Seguridad; (3) crear una **segunda cuenta SuperAdmin de recuperación** con contraseña fuerte custodiada aparte (no hay recuperación por correo); (4) cambiar la contraseña de `admin` por una fuerte propia o desactivarlo; (5) desactivar `administrador` y las demás cuentas de demostración, asignando rol y caja a los usuarios reales; (6) nunca desactivar `admin` antes de completar 2 y 3. Hasta decidirlo, **no operar con `admin`/`administrador` expuestos con `Admin123!`**.

## Numeraciones tras la limpieza

Mecanismo por serie (confirmado leyendo los servicios; valores concretos medidos en clones):

| Serie | Cómo se calcula | Después de la limpieza |
|---|---|---|
| Ventas | contador por prefijo/mes (`NumeracionVentaContadores`); si falta, parte del máximo numérico en `Ventas` | `VTA-AAAAMM-000001` (contador vacío y sin ventas) |
| Cotizaciones | máximo del día + 1 | reinicia en 1 |
| Facturas internas (`Facturas` sin CAE) | máximo por prefijo/mes + 1 | reinicia en 1 |
| Órdenes de compra | máximo del año (sin eliminadas) + 1 | `OC-2026-0001` (verificado) |
| Créditos | último Id visible + 1 | empieza en 1 y puede saltar después (Id no reiniciado) |
| Devoluciones, notas de crédito de devolución, garantías | número derivado del Id real | continúan por IDENTITY (primer `DEV-AAAAMM-000002`) |
| Contratos, pagarés, recibos | `TiposDocumento.UltimoNumero`, sin reinicio mensual | siguientes: contrato 22, pagaré 22, recibo 5 |

Sin decisión fiscal: ver `docs/iva-decisiones-contador.md`, `docs/factura-credito-personal.md`, `docs/envios-armados-costo-real.md`.

## Evidencia de esta etapa (clones de la base ya limpia)

xUnit completo en `main` 100e0219: 7955 superadas, 0 con error, 194 omitidas (las de SQL Server corren en CI). Recorridos con Playwright + SQL: alta de proveedor; orden de compra con recepción parcial (4) y total (6): stock = Σ movimientos = 10, OC Parcial → Recibida; venta en efectivo; devolución; cierre de caja con diferencia (exige justificación; −$123,85 exacto contra esperado $298.123,85); cupo de crédito personal bloquea y ofrece «Solicitar excepción»; envíos/armados y costos reales (validados en clon previo a la limpieza, mismo código).

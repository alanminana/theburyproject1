# Validación de campos del ERP — informe de cobertura

Auditoría funcional de los campos editables (octubre 2026, lotes VALID-L1 a VALID-L14).
Criterio: entender primero para qué sirve cada campo y recién después definir su regla; el servidor
valida siempre por su cuenta (el navegador es solo una ayuda); se probó por el pipeline HTTP real
(antiforgery, permisos, binding, servicio) con valores válidos, inválidos, de borde y manipulados.

## 1. Respuesta corta

| Pregunta | Respuesta |
|---|---|
| Campos editables que existen | **~764** (conteo aproximado, ver "Cómo se contó") |
| Campos en módulos analizados | **~713 (93 %)** — 29 módulos |
| Campos sin análisis propio | **~51 (7 %)** — carpeta `Shared` (parciales reutilizados) |
| Brechas corregidas | **~85 correcciones** en 14 lotes (ver sección 3) |
| Casos de prueba de validación agregados | **~560** (más de 190 métodos de prueba) |
| Módulos con pendientes declarados | 6 (sección 4) |

**No se declara ningún módulo como "totalmente auditado"**: todos tienen al menos un punto en la
lista de no validado de la sección 4, aunque sea la revisión visual.

### Cómo se contó (y su límite)

Se contaron los `<input>`, `<select>` y `<textarea>` visibles (sin `hidden`, botones ni submit) de las
vistas Razor, deduplicados por nombre dentro de cada carpeta de `Views/`. Por eso es una **estimación**:

- no incluye campos generados por JavaScript (armados dinámicos de Venta, wizard del cotizador);
- no incluye parámetros de endpoints JSON/API que no tienen vista (webhooks, API de tickets y notas);
- cuenta una vez el campo que se repite entre pantallas del mismo módulo.

No se llevó un registro campo por campo; los conteos de "analizados" son por módulo.

## 2. Cobertura por módulo

Estado: **Revisado** = reglas del servidor revisadas y probadas por HTTP o por reglas puras;
**Parcial** = hay superficies enumeradas en la sección 4 sin validar.

| Módulo (carpeta de vistas) | Campos | Lote | Estado |
|---|---|---|---|
| Catalogo | 83 | L3 | Parcial |
| MercadoLibre | 66 | L7 | Parcial |
| Venta | 62 | L1, L10 | Parcial |
| Shared | 51 | — | **Sin análisis propio** |
| Cliente | 50 | L2 | Parcial |
| Mora | 48 | L5 | Revisado |
| ConfiguracionPago | 47 | L5 | Parcial |
| DocumentosConfig | 46 | L5 | Parcial |
| Cotizacion | 38 | L13 | Revisado |
| Producto | 34 | L3 | Revisado |
| Caja | 29 | L1 | Revisado |
| Credito | 28 | L2, L14 | Revisado |
| Proveedor | 23 | L4 | Revisado |
| OrdenCompra | 20 | L4 | Revisado |
| Reporte | 16 | L8 | Revisado |
| PlantillaContratoCredito | 15 | L5 | Revisado |
| CambiosPrecios | 14 | L3 | Parcial |
| Usuarios | 14 | L6 | Revisado |
| ConfiguracionContratoCredito | 13 | L5 | Revisado |
| MovimientoStock | 13 | L4 | Revisado |
| Seguridad | 10 | L6, L12 | Revisado |
| DocumentoCliente | 9 | L2, L11 | Revisado |
| Ticket | 9 | L9, L11 | Revisado |
| Devolucion | 7 | L9 | Revisado |
| AlertaStock | 5 | L9 | Revisado |
| Documento | 5 | L5 | Revisado |
| ServiciosVenta | 4 | L10 | Revisado |
| ConfiguracionCliente / Rentabilidad / Prefijos | 5 | L5 | Revisado (ya estaban bien) |

## 3. Qué se encontró y se corrigió

Resumen por tipo de brecha (cada una con prueba que falla sin la corrección):

| Tipo de brecha | Ejemplos | Cantidad aprox. |
|---|---|---|
| Texto libre sin tope de largo (falla en SQL Server por truncamiento) | motivos de cancelar/anular/rechazar, observaciones, notas, nombres de archivo | ~30 |
| Importes sin tope o con decimales de más | total de venta, cheque, precio, liquidación ML, pago de cuota, servicios (de 99.999.999 o `decimal.Max` a 20.000.000) | ~15 |
| Fechas sin control o mal comparadas | nacimiento, vencimiento de cheque/cotización, primera cuota, promesa, rango de reportes, bloqueo de usuario (UTC vs hora local) | ~14 |
| Formato sin regla | alícuota de IVA, códigos, CUIL, nombre de usuario, nombre de rol, punto de venta, teléfono | ~12 |
| Recorte o descarte en silencio | respuestas a compradores de ML, edición de publicación ML, observaciones de cotización | ~6 |
| Coherencia entre campos | mínimo ≤ máximo, media ≤ alta ≤ crítica, desde ≤ hasta | ~5 |
| Mensajes de error poco claros | "Datos inválidos" en Mora, errores de binding en inglés, doble punto | ~4 |

Regla común de montos: hasta **20.000.000** con **2 decimales** (atributo `MontoArgentino`).
Los detalles y la prueba de cada corrección están en la descripción de los PR #140, #143, #145, #147, #149,
#155, #193 (lotes 7 a 12), #216 y #217.

### Errores introducidos y detectados durante la auditoría

Se dejan registrados porque cada uno mostró un punto frágil del sistema:

1. Regla de fecha de factura rechazaba el mismo día entre las 21:00 y las 24:00 (UTC vs hora argentina): corregido con tolerancia de 1 día.
2. Regla de código de caja "sin espacios" bloqueaba datos reales ("caja 01"): se detectó al compararla con la base real.
3. Regla de CUIT con verificador estricto contradecía una advertencia de la propia pantalla y una prueba: se **revirtió**.
4. Regla de nombre de rol rechazaba nombres con espacios en los bordes que el controlador ya recortaba.
5. Una prueba propia con ruta de Windows fallaba en Linux (`Path.GetFileName`).
6. Fixtures de pruebas existentes con fecha fija de 2026 se rompían con el paso del tiempo: ahora son relativas a hoy.

## 4. Pendientes y por qué

| Pendiente | Módulo | Motivo |
|---|---|---|
| Revisión visual de los mensajes de error en pantalla | Todos salvo Caja, Venta (parcial), Cliente (parcial), Documentos config (empresa) y Producto (alta) | No se hizo QA visual con navegador en cada lote |
| Pestañas Cónyuge, Laboral y Referencias | Cliente | Sin sondeo HTTP |
| Edición completa de una venta | Venta | Solo se probó el flujo de alta y la facturación |
| Cheque desde la interfaz | Venta / Caja | El cheque no está habilitado en la configuración de la base de QA; solo se probó por HTTP forjado. Además el módulo de cheques tuvo cambios posteriores (hito 10) |
| Aprobar / rechazar / cancelar / revertir lote de precios por HTTP | CambiosPrecios | Guardas del servicio sin sondeo de flujo completo |
| Edición de producto por el modal AJAX del catálogo en navegador | Catalogo | Solo servidor |
| Configuración de mora (JSON) y edición de plantillas/reglas de documentos | Mora / DocumentosConfig | Sin sondeo de flujo completo |
| Importación de catálogo por ruta, carga de imágenes y URLs, atributos de borrador, escenarios de envío simulado, webhook | MercadoLibre | Superficies de integración externa |
| Pantalla de Seguridad devuelve 500 en el entorno de pruebas HTTP | Seguridad | El rango invertido y la página enorme de auditoría solo se cubrieron como reglas puras |
| Campos generados por JavaScript y parciales compartidos | Shared | Sin análisis propio; se cubren solo cuando el módulo dueño los usa |
| Registro, Olvidé mi contraseña, Reset | Identity | Páginas deshabilitadas (404): no hay campos que validar |

### Decisiones cerradas después del informe

- **CUIT de proveedor:** el dígito verificador (módulo 11) se exige al **crear** y cuando el CUIT **cambia**. Un CUIT antiguo con
  verificador incorrecto (2 de los 5 proveedores de la base de desarrollo) se puede seguir editando mientras no se toque el CUIT.
  Solo se controla el verificador, no el prefijo (los proveedores del exterior usan 50/51/55). El mensaje indica el verificador correcto.
- **Adjuntos de ticket:** además de tamaño y extensión se verifica el contenido real (PDF, JPG, PNG, GIF, WEBP, DOC/XLS, DOCX/XLSX/ZIP;
  los `.txt` no pueden tener bytes nulos). Un ejecutable renombrado se rechaza. La verificación es la misma función que usan los
  documentos de cliente (`DocumentoValidationHelper.ContenidoCoincideConExtension`).

## 5. Cómo mantener esto

- Un campo nuevo con importe usa `MontoArgentino`; con texto libre, `StringLength` igual a la columna
  (o `LongitudTexto.Validar` para parámetros sueltos); con fecha, una regla de rango explícita con
  tolerancia de 1 día por el huso horario.
- Toda regla de campo se verifica contra **datos reales** antes de cerrarse (el caso "caja 01" y los
  roles con espacios aparecieron así).
- Si un campo tiene `data-rule` en la vista (`input-rules.js`), cambiar la regla del servidor exige
  revisar también esa regla del navegador.
- Un valor fuera de regla **se rechaza**; no se recorta ni se corrige en silencio.

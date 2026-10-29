# Matriz de permisos y roles (H2-B2 / P5)

Generada por reflexión sobre `main` 81ca0839 (tests `MatrizPermisosGeneradorTests` y `AutorizacionMatrizPorRolHttpTests`). Regenerar con las variables `BURY_MATRIZ_PERMISOS_OUT` y `BURY_MATRIZ_PERMISOS_ROLES_OUT` (ver el test). Los filtros de clase y de método **se acumulan (AND)**; `SuperAdmin` pasa todo `[PermisoRequerido]`.

## 1. Resultado de la verificación

- 517 acciones MVC inventariadas. Ninguna acción de negocio queda sin `[PermisoRequerido]`/`[Authorize(Roles)]` salvo las de la sección 3 (por diseño).
- Verificación HTTP: para cada rol (Administrador, Gerente, Vendedor, Cajero, Repositor, Tecnico, Contador, un rol sin permisos y un rol con solo `*.view*`) se enviaron TODAS las acciones que sus permisos sembrados no cubren: todas responden 403/AccessDenied (`AutorizacionMatrizPorRolHttpTests`).
- Contrato: toda acción mutante (POST/PUT/PATCH/DELETE) exige un permiso que no sea de lectura, salvo la lista de la sección 3 (`AccionesMutantesTodosLosControllersContractTests`).
- Defecto corregido: `AlertaStock/{Resolver,Ignorar,MarcarEnProceso,GenerarAlertas}` solo exigían `stock.viewalerts` (permiso de lectura). Ahora exigen además `stock.adjust` y los botones se ocultan sin él. Ningún rol de sistema pierde acceso (solo Administrador, Gerente y Repositor tenían `viewalerts`, y los tres tienen `stock.adjust`). Roles personalizados con `viewalerts` sin `adjust` pasan a solo lectura: **decisión pendiente** si negocio prefiere un permiso propio (`stock.managealerts`) en vez de reutilizar `adjust`.

## 2. Roles de sistema (permisos sembrados)

| Módulo | SuperAdmin | Administrador | Gerente | Vendedor | Cajero | Repositor | Tecnico | Contador |
|---|---|---|---|---|---|---|---|---|
| `acciones` (4) | todo | todo | - | - | - | - | - | - |
| `autorizaciones` (4) | todo | todo | todo | - | - | - | - | - |
| `caja` (8) | todo | todo | - | - | close, history, movements, open, view | - | - | - |
| `categorias` (4) | todo | todo | - | view | - | - | - | - |
| `cheques` (6) | todo | todo | - | - | - | - | - | - |
| `clientes` (10) | todo | todo | todo | create, update, uploaddocs, view, viewdocs | view | - | - | view |
| `cobranzas` (6) | todo | todo | todo | - | payinstallment, view, viewalerts, viewarrears | - | - | view, viewalerts, viewarrears |
| `configuracion` (6) | todo | view, viewpunitorio | - | - | - | - | - | - |
| `cotizaciones` (5) | todo | todo | todo | convert, create, update, view | - | - | - | - |
| `creditos` (8) | todo | todo | todo | configure, view, viewinstallments | view | - | - | view, viewinstallments |
| `dashboard` (1) | todo | todo | todo | todo | todo | todo | todo | todo |
| `devoluciones` (5) | todo | todo | todo | - | - | view | todo | - |
| `documentos` (8) | todo | todo | cancel, generate, managerules, managetemplates, reprint, sign, view | generate, reprint, sign, view | reprint, view | - | - | - |
| `evaluacioncredito` (4) | todo | todo | todo | - | - | - | - | - |
| `garantias` (3) | todo | todo | todo | - | - | - | todo | - |
| `marcas` (4) | todo | todo | - | view | - | - | - | - |
| `mercadolibre` (6) | todo | todo | - | - | - | - | - | - |
| `modulos` (4) | todo | todo | - | - | - | - | - | - |
| `mora` (3) | todo | todo | manage, view | - | view | - | - | view |
| `movimientos` (2) | todo | todo | todo | - | - | todo | - | - |
| `notascredito` (3) | todo | todo | todo | - | - | - | view | - |
| `notificaciones` (3) | todo | todo | todo | todo | todo | todo | todo | todo |
| `ordenescompra` (5) | todo | todo | todo | - | - | - | - | view |
| `precios` (9) | todo | todo | apply, approve, history, revert, simulate, view | - | - | - | - | - |
| `productos` (6) | todo | todo | - | view | - | view | view | - |
| `proveedores` (4) | todo | todo | todo | - | - | - | - | view |
| `reportes` (7) | todo | todo | todo | - | - | - | - | todo |
| `rmas` (3) | todo | todo | todo | - | - | - | todo | - |
| `roles` (5) | todo | assignpermissions, create, update, view | - | - | - | - | - | - |
| `stock` (5) | todo | todo | todo | view | - | todo | view, viewkardex | - |
| `tickets` (6) | todo | todo | changestatus, create, update, view | changestatus, create, update, view | changestatus, create, update, view | changestatus, create, update, view | changestatus, create, update, view | changestatus, create, update, view |
| `usuarios` (6) | todo | assignroles, create, resetpassword, update, view | - | - | - | - | - | - |
| `ventas` (11) | todo | todo | authorize, cancel, create, delete, export, invoice, reject, requestexception, update, view | create, invoice, requestexception, view | view | - | - | view |

Administrador recibe todo menos `usuarios.delete, roles.delete, configuracion.update, configuracion.managepunitorio, configuracion.retroactivepunitorio, configuracion.recalcularscoringglobal`. Gerente no recibe `ventas.discount`, `mora.config`, `precios.create/update/delete`. `ventas.discount` solo SuperAdmin hasta que se asigne a mano (decisión previa).

## 3. Acciones sin permiso granular (por diseño)

| Acción | Motivo |
|---|---|
| NotaRapidaApi.* (6) | notas personales: solo el dueño las ve y modifica (filtro por `UsuarioId` en cada consulta) |
| Home.* (Index, Privacy, Error, Estado) | públicas |
| MercadoLibreWebhook.Recibir | webhook externo anónimo; firma/secreto y retención diferidos por el usuario |
| Diagnostico.* (11) | `[Authorize(Roles=SuperAdmin)]` y 404 fuera de Development |
| Ticket.CambiarEstado | exige `tickets.changestatus` o `tickets.resolve` dentro del handler según el estado destino |
| CotizacionApi.Simular, VentaApi.CalcularTotalesVenta, Reporte.Ventas (POST) | solo `*.view`: cálculo/lectura, no persisten |
| Credito.Create/Edit (POST) | retirados: solo redirigen |
| MercadoLibre.ProbarConexion | sonda de cuenta ya vinculada |

## 4. Permisos del catálogo sin ningún uso en servidor ni en vistas

Sembrados y asignables desde Seguridad > Roles, pero ningún `[PermisoRequerido]` ni `TienePermiso` los consulta (otorgarlos no concede nada; el riesgo es de confusión). **Decisión pendiente** (limpiar catálogo o implementar el control):

- `acciones`: create, delete, update
- `autorizaciones`: approve, reject, view
- `caja`: history
- `cheques`: cancel, create, delete, deposit, update, view
- `clientes`: export
- `cobranzas`: view, viewalerts
- `cotizaciones`: update
- `creditos`: create, reschedule, simulate, viewinstallments
- `evaluacioncredito`: approve, evaluate, reject, view
- `garantias`: create, update, view
- `modulos`: create, delete, update
- `notascredito`: cancel
- `precios`: create, delete, history, update
- `productos`: export
- `reportes`: arrears, margins, sales
- `rmas`: updatestatus, view
- `stock`: transfer, view, viewkardex
- `ventas`: export, requestexception

(`autorizaciones.*` y `cheques.*` pertenecen a circuitos retirados: ver `docs/autorizaciones-circuito-generico-retirado.md` y `docs/cheques-modulo-retirado.md`. Nota: `ventas.requestexception` figura asignado a Vendedor y no se consulta; `VentaService` indica que ya no alcanza para autorizar.)

## 5. Matriz acción → permiso efectivo

`C:` = permiso de clase, `M:` = de método (se exigen ambos). `-` = solo autenticación. `GET*` = sin atributo HTTP explícito (acepta cualquier verbo).


### AlertaStockController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Criticos | GET* | C:stock.viewalerts | Authorize() |
| Details | GET* | C:stock.viewalerts | Authorize() |
| Estadisticas | GET* | C:stock.viewalerts | Authorize() |
| GenerarAlertas | POST | C:stock.viewalerts & M:stock.adjust | Authorize() |
| Ignorar | POST | C:stock.viewalerts & M:stock.adjust | Authorize() |
| Index | GET* | C:stock.viewalerts | Authorize() |
| MarcarEnProceso | POST | C:stock.viewalerts & M:stock.adjust | Authorize() |
| Pendientes | GET* | C:stock.viewalerts | Authorize() |
| PorProducto | GET* | C:stock.viewalerts | Authorize() |
| Resolver | POST | C:stock.viewalerts & M:stock.adjust | Authorize() |

### CajaController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Abrir | GET* | C:caja.view & M:caja.open | Authorize() |
| Abrir | POST | C:caja.view & M:caja.open | Authorize() |
| AcreditarMovimiento | POST | C:caja.view & M:caja.edit | Authorize() |
| Cerrar | GET* | C:caja.view & M:caja.close | Authorize() |
| Cerrar | POST | C:caja.view & M:caja.close | Authorize() |
| Create | GET* | C:caja.view & M:caja.create | Authorize() |
| Create | POST | C:caja.view & M:caja.create | Authorize() |
| CreatePartial | GET* | C:caja.view & M:caja.create | Authorize() |
| Delete | POST | C:caja.view & M:caja.delete | Authorize() |
| DetallesApertura | GET* | C:caja.view | Authorize() |
| DetallesCierre | GET* | C:caja.view | Authorize() |
| Edit | GET* | C:caja.view & M:caja.update | Authorize() |
| Edit | POST | C:caja.view & M:caja.update | Authorize() |
| EditPartial | GET* | C:caja.view & M:caja.update | Authorize() |
| Historial | GET* | C:caja.view | Authorize() |
| Index | GET* | C:caja.view | Authorize() |
| RegistrarMovimiento | GET* | C:caja.view & M:caja.movements | Authorize() |
| RegistrarMovimiento | POST | C:caja.view & M:caja.movements | Authorize() |
| UltimoEfectivoCierre | GET | C:caja.view | Authorize() |

### CambiosPreciosController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Aplicar | GET | C:precios.view & M:precios.apply | Authorize() |
| AplicarConfirmed | POST | C:precios.view & M:precios.apply | Authorize() |
| AplicarRapido | POST | C:precios.view & M:precios.apply | Authorize() |
| Aprobar | POST | C:precios.view & M:precios.approve | Authorize() |
| Autorizar | GET | C:precios.view & M:precios.approve | Authorize() |
| Cancelar | POST | C:precios.view & M:precios.approve | Authorize() |
| GetBatchParaRevertirApi | GET | C:precios.view & M:precios.revert | Authorize() |
| Historial | GET | C:precios.view | Authorize() |
| HistorialApi | GET | C:precios.view & M:precios.view | Authorize() |
| Index | GET* | C:precios.view & M:precios.view | Authorize() |
| Preview | GET | C:precios.view | Authorize() |
| Rechazar | POST | C:precios.view & M:precios.approve | Authorize() |
| Revertir | GET | C:precios.view & M:precios.revert | Authorize() |
| RevertirApi | POST | C:precios.view & M:precios.revert | Authorize() |
| RevertirConfirmed | POST | C:precios.view & M:precios.revert | Authorize() |
| RevertirPost | POST | C:precios.view & M:precios.revert | Authorize() |
| Simular | GET | C:precios.view & M:precios.simulate | Authorize() |
| Simular | POST | C:precios.view & M:precios.simulate | Authorize() |
| SimularCambioRapido | POST | C:precios.view & M:precios.simulate | Authorize() |
| SimularDesdeCatalogo | POST | C:precios.view & M:precios.simulate | Authorize() |

### CatalogoController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| AplicarCambioPrecioDirecto | POST | C:productos.view & M:precios.apply | Authorize() |
| AplicarCambioPrecioDirectoJson | POST | C:productos.view & M:precios.apply | Authorize() |
| AplicarCambioPrecios | POST | C:productos.view & M:precios.apply | Authorize() |
| DetalleCambioPrecio | GET | C:productos.view | Authorize() |
| HistorialCambiosPrecio | GET | C:productos.view | Authorize() |
| HistorialPrecioProductoApi | GET | C:productos.view & M:precios.view | Authorize() |
| Index | GET* | C:productos.view | Authorize() |
| Resumen | GET* | C:productos.view | Authorize() |
| RevertirCambioPrecio | POST | C:productos.view & M:precios.revert | Authorize() |
| RevertirCambioPrecioApi | POST | C:productos.view & M:precios.revert | Authorize() |
| SimularCambioPrecios | POST | C:productos.view & M:precios.simulate | Authorize() |
| ToggleDestacado | POST | C:productos.view & M:productos.update | Authorize() |

### CategoriaController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| CreateAjax | POST | C:categorias.view & M:categorias.create | Authorize() |
| DeleteConfirmed | POST | C:categorias.view & M:categorias.delete | Authorize() |
| EditAjax | POST | C:categorias.view & M:categorias.update | Authorize() |
| GetJson | GET | C:categorias.view & M:categorias.update | Authorize() |
| Index | GET* | C:categorias.view | Authorize() |

### ClienteController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| ActualizarBcra | POST | C:clientes.view & M:clientes.consultbcra | Authorize() |
| AsignarGarante | POST | C:clientes.view & M:clientes.edit | Authorize() |
| AsignarLimiteCredito | POST | C:clientes.view & M:clientes.managecreditlimits | Authorize() |
| AsignarNivelCreditoManual | POST | C:clientes.view & M:clientes.managecreditlimits | Authorize() |
| BuscarPosiblesGarantes | GET | C:clientes.view & M:clientes.view | Authorize() |
| Create | GET* | C:clientes.view & M:clientes.create | Authorize() |
| Create | POST | C:clientes.view & M:clientes.create | Authorize() |
| CreateAjax | POST | C:clientes.view & M:clientes.create | Authorize() |
| Delete | GET* | C:clientes.view & M:clientes.delete | Authorize() |
| DeleteConfirmed | POST | C:clientes.view & M:clientes.delete | Authorize() |
| Details | GET* | C:clientes.view | Authorize() |
| Edit | GET* | C:clientes.view & M:clientes.edit | Authorize() |
| Edit | POST | C:clientes.view & M:clientes.edit | Authorize() |
| EditAjax | POST | C:clientes.view & M:clientes.edit | Authorize() |
| Index | GET* | C:clientes.view | Authorize() |
| LimitesPorPuntajeGuardar | POST | C:clientes.view & M:clientes.managecreditlimits | Authorize() |
| LimitesPorPuntajePartial | GET | C:clientes.view | Authorize() |
| LimpiarNivelCreditoManual | POST | C:clientes.view & M:clientes.managecreditlimits | Authorize() |
| ModalCreate | GET | C:clientes.view | Authorize() |
| ModalEdit | GET | C:clientes.view | Authorize() |
| RecalcularAptitud | POST | C:clientes.view & M:clientes.edit | Authorize() |
| RecalcularPuntaje | POST | C:clientes.view & M:clientes.edit | Authorize() |
| RemoverGarante | POST | C:clientes.view & M:clientes.edit | Authorize() |

### ClienteDatosContractualesController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Guardar | POST | M:clientes.edit | Authorize() |
| Obtener | GET | M:clientes.view | Authorize() |

### ConfiguracionClienteController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Index | GET | C:configuracion.view | Authorize() |
| Index | POST | C:configuracion.view & M:configuracion.update | Authorize() |

### ConfiguracionContratoCreditoController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Index | GET | C:configuracion.view | Authorize() |
| Index | POST | C:configuracion.view & M:configuracion.update | Authorize() |

### ConfiguracionMoraController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| GetConfiguracion | GET | C:configuracion.managepunitorio | Authorize() |

### ConfiguracionPagoController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| CalcularCuotas | GET | C:configuracion.view | Authorize() |
| CambiarEstadoPlanGlobal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| CambiarEstadoTarjetaGlobal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| CrearMedioPagoGlobal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| CrearPlanGlobal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| CrearTarjetaGlobal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| CrearVersionPunitorio | POST | C:configuracion.view & M:configuracion.managepunitorio & M:configuracion.viewpunitorio | Authorize() |
| CreditoPersonal | GET | C:configuracion.view | Authorize() |
| CreditoPersonal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| EditarMedioPagoGlobal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| EditarPlanGlobal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| EditarTarjetaGlobal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| EliminarMedioPagoGlobal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| EliminarTarjetaGlobal | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| GetTarjetaConfig | GET | C:configuracion.view | Authorize() |
| Index | GET* | C:configuracion.view | Authorize() |
| ListarTarjetasGlobales | GET | C:configuracion.view | Authorize() |
| MediosPago | GET* | C:configuracion.view | Authorize() |
| ObtenerTarjetaGlobal | GET | C:configuracion.view | Authorize() |
| PreviewPunitorio | GET | C:configuracion.view & M:configuracion.viewpunitorio | Authorize() |
| PreviewRecargoCreditoPersonal | GET | C:configuracion.view | Authorize() |

### ConfiguracionRentabilidadController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Index | GET | C:configuracion.view | Authorize() |
| Index | POST | C:configuracion.view & M:configuracion.update | Authorize() |

### ContratoVentaCreditoController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Generar | POST | C:ventas.view & M:creditos.configure | Authorize() |
| Preparar | GET | C:ventas.view | Authorize() |
| Ver | GET | C:ventas.view | Authorize() |

### CotizacionApiController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| AptitudCredito | GET | C:cotizaciones.view | Authorize() |
| Cancelar | POST | C:cotizaciones.view & M:cotizaciones.cancel | Authorize() |
| ConversionPreflight | POST | C:cotizaciones.view & M:cotizaciones.convert | Authorize() |
| ConversionPreview | POST | C:cotizaciones.view & M:cotizaciones.convert | Authorize() |
| Convertir | POST | C:cotizaciones.view & M:cotizaciones.convert | Authorize() |
| FacturaPreview | GET | C:cotizaciones.view & M:cotizaciones.convert | Authorize() |
| Guardar | POST | C:cotizaciones.view & M:cotizaciones.create | Authorize() |
| Simular | POST | C:cotizaciones.view | Authorize() |
| VencerExpiradas | POST | C:cotizaciones.view & M:cotizaciones.expire | Authorize() |

### CotizacionController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| BuscarClientes | GET | C:cotizaciones.view | Authorize() |
| BuscarProductos | GET | C:cotizaciones.view | Authorize() |
| DescargarPdf | GET | C:cotizaciones.view | Authorize() |
| Detalles | GET | C:cotizaciones.view | Authorize() |
| Imprimir | GET | C:cotizaciones.view | Authorize() |
| Index | GET | C:cotizaciones.view | Authorize() |
| Listado | GET | C:cotizaciones.view | Authorize() |
| ProductoResumen | GET | C:cotizaciones.view | Authorize() |

### CreditoController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| AdelantarCuota | GET | C:creditos.view & M:cobranzas.payinstallment | Authorize() |
| AdelantarCuota | POST | C:creditos.view & M:cobranzas.payinstallment | Authorize() |
| AnularPunitorioCuota | POST | C:creditos.view & M:cobranzas.revertfine | Authorize() |
| AplicarPunitorioCuota | POST | C:creditos.view & M:cobranzas.applyfine | Authorize() |
| Aprobar | POST | C:creditos.view & M:creditos.approve | Authorize() |
| Cancelar | POST | C:creditos.view & M:creditos.approve | Authorize() |
| ConfigurarVenta | GET | C:creditos.view & M:creditos.configure | Authorize() |
| ConfigurarVenta | POST | C:creditos.view & M:creditos.configure | Authorize() |
| Create | GET | C:creditos.view | Authorize() |
| Create | POST | C:creditos.view | Authorize() |
| CuotasVencidas | GET* | C:creditos.view | Authorize() |
| Delete | GET* | C:creditos.view | Authorize() |
| DeleteConfirmed | POST | C:creditos.view & M:creditos.delete | Authorize() |
| Details | GET* | C:creditos.view | Authorize() |
| DetallePunitorioCuota | GET | C:creditos.view | Authorize() |
| Edit | GET | C:creditos.view | Authorize() |
| Edit | POST | C:creditos.view | Authorize() |
| Index | GET* | C:creditos.view | Authorize() |
| PagarCuota | GET | C:creditos.view & M:cobranzas.payinstallment | Authorize() |
| PagarCuota | POST | C:creditos.view & M:cobranzas.payinstallment | Authorize() |
| PanelCliente | GET | C:creditos.view | Authorize() |
| PreviewPagoMultiple | POST | C:creditos.view & M:cobranzas.payinstallment | Authorize() |
| PrevisualizarAdelanto | POST | C:creditos.view & M:cobranzas.payinstallment | Authorize() |
| PrevisualizarPagoCuota | POST | C:creditos.view & M:cobranzas.payinstallment | Authorize() |
| Rechazar | POST | C:creditos.view & M:creditos.approve | Authorize() |
| RegistrarPagoMultiple | POST | C:creditos.view & M:cobranzas.payinstallment | Authorize() |
| Simular | GET | C:creditos.view | Authorize() |
| SimularPlanVenta | GET | C:creditos.view | Authorize() |

### DashboardController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Index | GET* | C:dashboard.view | Authorize() |

### DevolucionController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Aprobar | POST | C:devoluciones.view & M:devoluciones.approve | Authorize() |
| Cancelar | POST | C:devoluciones.view & M:devoluciones.reject | Authorize() |
| Completar | POST | C:devoluciones.view & M:devoluciones.complete | Authorize() |
| CrearRMA | POST | C:devoluciones.view & M:rmas.create | Authorize() |
| Create | GET | C:devoluciones.view | Authorize() |
| Detalles | GET* | C:devoluciones.view | Authorize() |
| Estadisticas | GET* | C:devoluciones.view | Authorize() |
| Garantias | GET* | C:devoluciones.view | Authorize() |
| GetQuickCreateContext | GET | C:devoluciones.view & M:devoluciones.create | Authorize() |
| Index | GET* | C:devoluciones.view | Authorize() |
| NotasCredito | GET* | C:devoluciones.view & M:notascredito.view | Authorize() |
| QuickCreate | POST | C:devoluciones.view & M:devoluciones.create | Authorize() |
| Rechazar | POST | C:devoluciones.view & M:devoluciones.reject | Authorize() |
| RMAs | GET* | C:devoluciones.view | Authorize() |

### DiagnosticoController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| AuditoriaPermisos | GET | - | Authorize(SuperAdmin) |
| CrearUsuarioPrueba | POST | - | Authorize(SuperAdmin) |
| ForcePasswordReset | POST | - | Authorize(SuperAdmin) |
| Index | GET* | - | Authorize(SuperAdmin) |
| MisPermisos | GET | - | Authorize(SuperAdmin) |
| PermisosDelRol | GET | - | Authorize(SuperAdmin) |
| RefreshPermisos | POST | - | Authorize(SuperAdmin) |
| ResetPassword | GET | - | Authorize(SuperAdmin) |
| ResetPassword | POST | - | Authorize(SuperAdmin) |
| TestLogin | POST | - | Authorize(SuperAdmin) |

### DocumentoClienteController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Delete | POST | C:clientes.viewdocs & M:clientes.edit | Authorize() |
| Descargar | GET* | C:clientes.viewdocs | Authorize() |
| Details | GET* | C:clientes.viewdocs | Authorize() |
| GetDocumentosByCliente | GET | C:clientes.viewdocs | Authorize() |
| GetDocumentosPartial | GET | C:clientes.viewdocs | Authorize() |
| Index | GET* | C:clientes.viewdocs | Authorize() |
| Rechazar | POST | C:clientes.viewdocs & M:clientes.edit | Authorize() |
| RechazarBatch | POST | C:clientes.viewdocs & M:clientes.edit | Authorize() |
| Upload | GET* | C:clientes.viewdocs & M:clientes.uploaddocs | Authorize() |
| Upload | POST | C:clientes.viewdocs & M:clientes.uploaddocs | Authorize() |
| Verificar | POST | C:clientes.viewdocs & M:clientes.edit | Authorize() |
| VerificarBatch | POST | C:clientes.viewdocs & M:clientes.edit | Authorize() |
| VerificarTodos | POST | C:clientes.viewdocs & M:clientes.edit | Authorize() |

### DocumentoController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Cancelar | POST | M:documentos.cancel | Authorize() |
| Firmar | POST | M:documentos.sign | Authorize() |
| GenerarPendientes | POST | M:documentos.generate | Authorize() |
| GenerarPresupuesto | POST | M:documentos.generate | Authorize() |
| Index | GET | M:documentos.view | Authorize() |
| Regenerar | POST | M:documentos.generate | Authorize() |
| Reimprimir | GET | M:documentos.reprint | Authorize() |
| Ver | GET | M:documentos.view | Authorize() |
| VerGrupo | GET | M:documentos.view | Authorize() |
| VerPorPago | GET | M:documentos.view | Authorize() |
| VerVarios | GET | M:documentos.view | Authorize() |

### DocumentosConfigController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Empresa | GET | M:documentos.managetemplates | Authorize() |
| Empresa | POST | M:documentos.managetemplates | Authorize() |
| Index | GET | - | Authorize() |
| PaqueteEdit | GET | M:documentos.managerules | Authorize() |
| PaqueteEdit | POST | M:documentos.managerules | Authorize() |
| Paquetes | GET | M:documentos.managerules | Authorize() |
| PaqueteVistaPreviaPdf | GET | M:documentos.managetemplates | Authorize() |
| PlantillaActivar | POST | M:documentos.managetemplates | Authorize() |
| PlantillaEdit | GET | M:documentos.managetemplates | Authorize() |
| PlantillaEdit | POST | M:documentos.managetemplates | Authorize() |
| PlantillaPreview | POST | M:documentos.managetemplates | Authorize() |
| PlantillaPreviewPdf | POST | M:documentos.managetemplates | Authorize() |
| PlantillaRestaurar | POST | M:documentos.managetemplates | Authorize() |
| Plantillas | GET | M:documentos.managetemplates | Authorize() |
| PlantillaVistaPreviaPdf | GET | M:documentos.managetemplates | Authorize() |
| ReglaActivar | POST | M:documentos.managerules | Authorize() |
| ReglaEdit | GET | M:documentos.managerules | Authorize() |
| ReglaEdit | POST | M:documentos.managerules | Authorize() |
| ReglaEliminar | POST | M:documentos.managerules | Authorize() |
| Reglas | GET | M:documentos.managerules | Authorize() |
| ReglaVistaPreviaPdf | GET | M:documentos.managerules | Authorize() |
| TipoEdit | GET | M:documentos.managetypes | Authorize() |
| TipoEdit | POST | M:documentos.managetypes | Authorize() |
| Tipos | GET | M:documentos.managetypes | Authorize() |
| Variables | GET | M:documentos.managetemplates | Authorize() |

### HomeController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Error | GET* | - |  |
| Estado | GET* | - |  |
| Index | GET* | - |  |
| Privacy | GET* | - |  |

### MantenimientoController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| RecalcularScoringGlobal | POST | C:configuracion.recalcularscoringglobal |  |
| ScoringGlobal | GET | C:configuracion.recalcularscoringglobal |  |

### MarcaController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| CreateAjax | POST | C:marcas.view & M:marcas.create | Authorize() |
| DeleteConfirmed | POST | C:marcas.view & M:marcas.delete | Authorize() |
| EditAjax | POST | C:marcas.view & M:marcas.edit | Authorize() |
| GetJson | GET | C:marcas.view | Authorize() |
| Index | GET* | C:marcas.view | Authorize() |

### MercadoLibreController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Aumento | GET* | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| AumentoAplicar | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| AumentoCancelar | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| AumentoNuevo | GET* | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| AumentoRevertir | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| Aumentos | GET* | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| AumentoSimular | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| Borrador | GET* | C:mercadolibre.view | Authorize() |
| BorradorActualizar | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| BorradorCrear | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| BorradorDescartar | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| Borradores | GET* | C:mercadolibre.view | Authorize() |
| BorradorPublicar | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| BorradorSubirImagen | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| BorradorValidar | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| CategoriaAtributos | GET | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| CategoriaHijos | GET | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| CategoriaResolver | GET | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| CategoriaSugerencias | GET | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| Conectar | POST | C:mercadolibre.view & M:mercadolibre.connect | Authorize() |
| Configuracion | GET* | C:mercadolibre.view & M:mercadolibre.config | Authorize() |
| Configuracion | POST | C:mercadolibre.view & M:mercadolibre.config | Authorize() |
| Dashboard | GET* | C:mercadolibre.view | Authorize() |
| Desconectar | POST | C:mercadolibre.view & M:mercadolibre.connect | Authorize() |
| Desvincular | POST | C:mercadolibre.view & M:mercadolibre.link | Authorize() |
| ImportarCatalogo | POST | C:mercadolibre.view & M:mercadolibre.config | Authorize() |
| ImportarListings | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| Index | GET* | C:mercadolibre.view | Authorize() |
| Listing | GET* | C:mercadolibre.view | Authorize() |
| ListingCambiarEstado | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| ListingCrearProducto | GET* | C:mercadolibre.view & M:mercadolibre.link | Authorize() |
| ListingCrearProducto | POST | C:mercadolibre.view & M:mercadolibre.link | Authorize() |
| ListingEditar | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| ListingEditarCategoria | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| ListingEditarDescripcion | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| ListingEditarImagenes | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| ListingOrigenStock | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| Listings | GET* | C:mercadolibre.view | Authorize() |
| ListingSubirImagen | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| ListingVariacionVinculo | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| MensajeResponder | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| Mensajes | GET* | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| MensajeSimular | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OAuthCallback | GET | C:mercadolibre.view | Authorize() |
| Orden | GET* | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenActualizarEnvio | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenAsignarUnidades | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenCrearVenta | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenDevolucion | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| Ordenes | GET* | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenesImportarRecientes | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenGenerarOperativaSimulada | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenGenerarSimulada | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenIgnorar | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenLiquidar | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenResolverClaim | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenSimularClaim | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| OrdenSimularEnvio | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| Pregunta | GET* | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| PreguntaResponder | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| Preguntas | GET* | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| PreguntaSimular | POST | C:mercadolibre.view & M:mercadolibre.orders | Authorize() |
| ProbarConexion | POST | C:mercadolibre.view | Authorize() |
| SyncAplicar | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| SyncPreview | POST | C:mercadolibre.view & M:mercadolibre.sync | Authorize() |
| Vincular | POST | C:mercadolibre.view & M:mercadolibre.link | Authorize() |

### MercadoLibreWebhookController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Recibir | POST | - |  |

### MoraController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Alertas | GET | C:cobranzas.viewarrears & M:mora.view | Authorize() |
| CambiarEstadoAcuerdo | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| ClientesMora | GET* | C:cobranzas.viewarrears & M:mora.view | Authorize() |
| Configuracion | GET | C:cobranzas.viewarrears & M:mora.config | Authorize() |
| ConfiguracionExpandida | GET | C:cobranzas.viewarrears & M:mora.config | Authorize() |
| CrearAcuerdo | GET | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| CrearAcuerdo | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| EjecutarJob | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| FichaCliente | GET* | C:cobranzas.viewarrears & M:mora.view | Authorize() |
| GuardarConfiguracion | POST | C:cobranzas.viewarrears & M:mora.config | Authorize() |
| GuardarConfiguracionExpandida | POST | C:cobranzas.viewarrears & M:mora.config | Authorize() |
| Index | GET* | C:cobranzas.viewarrears & M:mora.view | Authorize() |
| Logs | GET* | C:cobranzas.viewarrears & M:mora.view | Authorize() |
| MarcarLeida | GET | C:cobranzas.viewarrears | Authorize() |
| MarcarLeida | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| MarcarPromesaCumplida | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| MarcarPromesaIncumplida | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| ProcesarMora | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| RegistrarContacto | GET | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| RegistrarContacto | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| RegistrarPagoAcuerdo | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| RegistrarPromesa | GET | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| RegistrarPromesa | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| Resolver | GET | C:cobranzas.viewarrears | Authorize() |
| Resolver | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |
| ResolverAlerta | POST | C:cobranzas.viewarrears & M:mora.manage | Authorize() |

### MovimientoStockController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| BuscarProductos | GET | C:movimientos.view | Authorize() |
| Create | GET* | C:movimientos.view & M:movimientos.create | Authorize() |
| Create | POST | C:movimientos.view & M:movimientos.create | Authorize() |
| GetProductoInfo | GET | C:movimientos.view | Authorize() |
| Index | GET* | C:movimientos.view | Authorize() |
| Kardex | GET* | C:movimientos.view | Authorize() |
| ListJson | GET | C:movimientos.view | Authorize() |

### NotaRapidaApiController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| AgregarItem | POST | - | Authorize() |
| CompletarItem | PATCH | - | Authorize() |
| Crear | POST | - | Authorize() |
| Editar | PUT | - | Authorize() |
| Eliminar | DELETE | - | Authorize() |
| EliminarItem | DELETE | - | Authorize() |
| Listar | GET | - | Authorize() |

### NotificacionController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| EliminarNotificacion | DELETE | C:notificaciones.view & M:notificaciones.delete | Authorize() |
| MarcarComoLeida | POST | C:notificaciones.view & M:notificaciones.update | Authorize() |
| MarcarTodasComoLeidas | POST | C:notificaciones.view & M:notificaciones.update | Authorize() |
| ObtenerCantidadNoLeidas | GET | C:notificaciones.view | Authorize() |
| ObtenerNotificaciones | GET | C:notificaciones.view | Authorize() |

### OrdenCompraController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| CambiarEstado | POST | C:ordenescompra.view & M:ordenescompra.update | Authorize() |
| Create | GET* | C:ordenescompra.view & M:ordenescompra.create | Authorize() |
| Create | POST | C:ordenescompra.view & M:ordenescompra.create | Authorize() |
| Delete | GET* | C:ordenescompra.view & M:ordenescompra.cancel | Authorize() |
| DeleteConfirmed | POST | C:ordenescompra.view & M:ordenescompra.cancel | Authorize() |
| Details | GET* | C:ordenescompra.view & M:ordenescompra.view | Authorize() |
| Edit | GET* | C:ordenescompra.view & M:ordenescompra.update | Authorize() |
| Edit | POST | C:ordenescompra.view & M:ordenescompra.update | Authorize() |
| Index | GET* | C:ordenescompra.view & M:ordenescompra.view | Authorize() |
| Recepcionar | GET* | C:ordenescompra.view & M:ordenescompra.receive | Authorize() |
| Recepcionar | POST | C:ordenescompra.view & M:ordenescompra.receive | Authorize() |

### PlantillaContratoCreditoController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Create | GET | C:configuracion.view & M:configuracion.update | Authorize() |
| Create | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| Edit | GET | C:configuracion.view & M:configuracion.update | Authorize() |
| Edit | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| Index | GET* | C:configuracion.view | Authorize() |
| ToggleActiva | POST | C:configuracion.view & M:configuracion.update | Authorize() |

### PrefijosNumeracionController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Guardar | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| Index | GET | C:configuracion.view | Authorize() |

### ProductoApiController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| GetUnidadesDisponibles | GET | M:ventas.view | Authorize() |

### ProductoController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| ActivarTrazabilidad | POST | C:productos.view & M:productos.edit | Authorize() |
| ActualizarComisionVendedor | POST | C:productos.view & M:productos.edit | Authorize() |
| AgregarUnidadesFisicasInline | POST | C:productos.view & M:productos.edit | Authorize() |
| AjustarStockAgregadoAUnidadesFisicas | POST | C:productos.view & M:productos.edit & M:movimientos.create | Authorize() |
| AjustarStockAgregadoHaciaAbajo | POST | C:productos.view & M:productos.edit & M:movimientos.create | Authorize() |
| ConciliarStockUnidades | POST | C:productos.view & M:productos.edit | Authorize() |
| CrearUnidad | POST | C:productos.view & M:productos.edit | Authorize() |
| CrearUnidadesMasivas | POST | C:productos.view & M:productos.edit | Authorize() |
| CreateAjax | POST | C:productos.view & M:productos.create | Authorize() |
| CreditoPersonalCandidatosJson | GET | C:productos.view | Authorize() |
| DarUnidadBaja | POST | C:productos.view & M:productos.edit | Authorize() |
| DeleteConfirmed | POST | C:productos.view & M:productos.delete | Authorize() |
| DesactivarTrazabilidad | POST | C:productos.view & M:productos.edit | Authorize() |
| EditAjax | POST | C:productos.view & M:productos.edit | Authorize() |
| FinalizarReparacionUnidad | POST | C:productos.view & M:productos.edit | Authorize() |
| GetJson | GET | C:productos.view & M:productos.view | Authorize() |
| GetSubcategorias | GET | C:productos.view | Authorize() |
| GetSubmarcas | GET | C:productos.view | Authorize() |
| Index | GET* | C:productos.view | Authorize() |
| Inventario | GET | C:productos.view | Authorize() |
| MarcarUnidadFaltante | POST | C:productos.view & M:productos.edit | Authorize() |
| PreviewRecargoCreditoPersonal | GET | C:productos.view | Authorize() |
| ReintegrarUnidadAStock | POST | C:productos.view & M:productos.edit | Authorize() |
| Unidades | GET | C:productos.view | Authorize() |
| UnidadesGlobal | GET | C:productos.view | Authorize() |
| UnidadHistorial | GET | C:productos.view | Authorize() |

### ProveedorController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Create | GET* | C:proveedores.view | Authorize() |
| CreateAjax | POST | C:proveedores.view & M:proveedores.create | Authorize() |
| Delete | GET* | C:proveedores.view | Authorize() |
| DeleteConfirmed | POST | C:proveedores.view & M:proveedores.delete | Authorize() |
| Details | GET* | C:proveedores.view | Authorize() |
| Edit | GET* | C:proveedores.view | Authorize() |
| EditAjax | POST | C:proveedores.view & M:proveedores.update | Authorize() |
| GetEditData | GET | C:proveedores.view & M:proveedores.update | Authorize() |
| GetProductos | GET | C:proveedores.view | Authorize() |
| Index | GET* | C:proveedores.view | Authorize() |

### ReporteController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| ComisionesVendedores | GET | C:reportes.view | Authorize() |
| ExportarMargenesExcel | GET* | C:reportes.view & M:reportes.export | Authorize() |
| ExportarMorosidadExcel | GET* | C:reportes.view & M:reportes.export | Authorize() |
| ExportarMorosidadPdf | GET* | C:reportes.view & M:reportes.export | Authorize() |
| ExportarMovimientosValorizadosExcel | GET* | C:reportes.view & M:reportes.export | Authorize() |
| ExportarVentasExcel | GET* | C:reportes.view & M:reportes.export | Authorize() |
| ExportarVentasPdf | GET* | C:reportes.view & M:reportes.export | Authorize() |
| Index | GET* | C:reportes.view | Authorize() |
| Iva | GET* | C:reportes.view | Authorize() |
| Margenes | GET* | C:reportes.view | Authorize() |
| Morosidad | GET* | C:reportes.view | Authorize() |
| MovimientosValorizados | GET | C:reportes.view & M:reportes.stock | Authorize() |
| MovimientosValorizados | POST | C:reportes.view & M:reportes.stock | Authorize() |
| Ventas | GET* | C:reportes.view | Authorize() |
| Ventas | POST | C:reportes.view | Authorize() |
| VentasAgrupadas | GET | C:reportes.view | Authorize() |

### SeguridadController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Auditoria | GET | M:roles.view | Authorize() |
| CopyPermisosRol | GET | M:roles.assignpermissions | Authorize() |
| CopyPermisosRol | POST | M:roles.assignpermissions | Authorize() |
| CreateRol | GET | M:roles.create | Authorize() |
| CreateRol | POST | M:roles.create | Authorize() |
| DeleteRol | POST | M:roles.delete | Authorize() |
| DuplicateRol | GET | M:roles.create | Authorize() |
| DuplicateRol | POST | M:roles.create | Authorize() |
| EditRol | GET | M:roles.update | Authorize() |
| EditRol | POST | M:roles.update | Authorize() |
| EditUsuario | GET | M:usuarios.update | Authorize() |
| EditUsuario | POST | M:usuarios.update | Authorize() |
| Index | GET | - | Authorize() |
| RolDetails | GET | M:roles.view | Authorize() |
| SavePermisosRol | POST | M:roles.assignpermissions | Authorize() |
| ToggleRolActivo | POST | M:roles.update | Authorize() |

### ServiciosVentaController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Guardar | POST | C:configuracion.view & M:configuracion.update | Authorize() |
| Index | GET | C:configuracion.view | Authorize() |

### TicketApiController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Actualizar | PUT | C:tickets.view & M:tickets.update | Authorize() |
| AgregarChecklistItem | POST | C:tickets.view & M:tickets.update | Authorize() |
| CambiarEstado | PATCH | C:tickets.view & M:tickets.changestatus | Authorize() |
| Crear | POST | C:tickets.view & M:tickets.create | Authorize() |
| Detalle | GET | C:tickets.view | Authorize() |
| EliminarAdjunto | DELETE | C:tickets.view & M:tickets.update | Authorize() |
| EliminarChecklistItem | DELETE | C:tickets.view & M:tickets.update | Authorize() |
| Listar | GET | C:tickets.view | Authorize() |
| MarcarChecklistItem | PATCH | C:tickets.view & M:tickets.update | Authorize() |
| RegistrarResolucion | PATCH | C:tickets.view & M:tickets.resolve | Authorize() |
| SubirAdjunto | POST | C:tickets.view & M:tickets.update | Authorize() |

### TicketController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| CambiarEstado | POST | C:tickets.view | Authorize() |
| DescargarAdjunto | GET | C:tickets.view | Authorize() |
| Details | GET* | C:tickets.view | Authorize() |
| Eliminar | POST | C:tickets.view & M:tickets.delete | Authorize() |
| Index | GET* | C:tickets.view | Authorize() |

### UsuariosController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| Bloquear | GET | C:usuarios.view & M:usuarios.update | Authorize() |
| Bloquear | POST | C:usuarios.view & M:usuarios.update | Authorize() |
| BulkAction | POST | C:usuarios.view & M:usuarios.update | Authorize() |
| CambiarPassword | GET | C:usuarios.view & M:usuarios.resetpassword | Authorize() |
| CambiarPassword | POST | C:usuarios.view & M:usuarios.resetpassword | Authorize() |
| ConfirmarEmail | POST | C:usuarios.view & M:usuarios.update | Authorize() |
| Create | GET | C:usuarios.view & M:usuarios.create | Authorize() |
| Create | POST | C:usuarios.view & M:usuarios.create | Authorize() |
| Delete | GET | C:usuarios.view & M:usuarios.delete | Authorize() |
| DeleteConfirmed | POST | C:usuarios.view & M:usuarios.delete | Authorize() |
| Desactivar | POST | C:usuarios.view & M:usuarios.delete | Authorize() |
| Desbloquear | POST | C:usuarios.view & M:usuarios.update | Authorize() |
| Details | GET | C:usuarios.view | Authorize() |
| Reactivar | POST | C:usuarios.view & M:usuarios.delete | Authorize() |

### VentaApiController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| BuscarClientes | GET | C:ventas.view | Authorize() |
| BuscarProductos | GET | C:ventas.view | Authorize() |
| CalcularCuotasTarjeta | GET | C:ventas.view | Authorize() |
| CalcularTotalesVenta | POST | C:ventas.view | Authorize() |
| ConfiguracionPagosGlobal | GET | C:ventas.view | Authorize() |
| EstadoActualizacionDatosCliente | GET | C:ventas.view | Authorize() |
| GetTarjetasActivas | GET | C:ventas.view | Authorize() |
| PrevalidarCredito | GET | C:ventas.view | Authorize() |

### VentaController

| Acción | HTTP | Permisos | Auth |
|---|---|---|---|
| AnularFactura | POST | C:ventas.view & M:ventas.invoice | Authorize() |
| Autorizar | GET* | C:ventas.view & M:ventas.authorize | Authorize() |
| AutorizarConfirmed | POST | C:ventas.view & M:ventas.authorize | Authorize() |
| CambiarEstadoEnvio | POST | C:ventas.view & M:ventas.update | Authorize() |
| Cancelar | GET* | C:ventas.view & M:ventas.cancel | Authorize() |
| CancelarConfirmed | POST | C:ventas.view & M:ventas.cancel | Authorize() |
| ComprobanteFactura | GET | C:ventas.view | Authorize() |
| Confirmar | POST | C:ventas.view & M:ventas.create | Authorize() |
| Create | POST | C:ventas.view & M:ventas.create | Authorize() |
| Create | GET | C:ventas.view & M:ventas.create | Authorize() |
| CreateAjax | POST | C:ventas.view & M:ventas.create | Authorize() |
| Delete | GET* | C:ventas.view | Authorize() |
| DeleteConfirmed | POST | C:ventas.view & M:ventas.delete | Authorize() |
| Details | GET* | C:ventas.view | Authorize() |
| Edit | GET* | C:ventas.view & M:ventas.update | Authorize() |
| Edit | POST | C:ventas.view & M:ventas.update | Authorize() |
| EditAjax | POST | C:ventas.view & M:ventas.update | Authorize() |
| Facturar | GET* | C:ventas.view & M:ventas.invoice | Authorize() |
| Facturar | POST | C:ventas.view & M:ventas.invoice | Authorize() |
| Index | GET* | C:ventas.view | Authorize() |
| PrepararVenta | POST | C:ventas.view & M:ventas.update | Authorize() |
| Rechazar | GET* | C:ventas.view & M:ventas.reject | Authorize() |
| RechazarConfirmed | POST | C:ventas.view & M:ventas.reject | Authorize() |

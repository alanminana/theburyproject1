# KIRA-VENTAS-INDEX-REWORK - HTML objetivo Centro de Ventas

Este archivo es referencia visual. No es Razor ejecutable. No copiar literalmente html/head/body/CDN/CSS inline/JS inline/datos demo/modal Nueva Venta.

## No copiar al Razor

No deben copiarse literalmente al Razor:

- `<!DOCTYPE>`
- `html/head/body`
- CDN Tailwind
- Google Fonts inline
- `style` inline
- `script` inline
- datos demo hardcodeados
- `modal-nueva-venta`
- `btn-abrir-modal-crear-venta`
- `openModal('modal-nueva-venta')`
- `CreateAjax`
- `VentaCrearModal.submit()`
- `modal-confirmar-operacion`

## HTML objetivo completo

```html
<!DOCTYPE html>
<html lang="es">
<head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Centro de Ventas</title>
    <script src="https://cdn.tailwindcss.com"></script>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800;900&family=Material+Symbols+Outlined:opsz,wght,FILL,GRAD@20..48,100..700,0..1,-50..200" rel="stylesheet" />
    <style>
        body {
            font-family: "Inter", system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
            background: #090d12;
            color: #f8fafc;
        }

        .card {
            border: 1px solid #243142;
            border-radius: 8px;
            background: linear-gradient(180deg, rgba(20, 29, 41, 0.98), rgba(12, 18, 27, 0.98));
            box-shadow: 0 18px 48px rgba(0, 0, 0, 0.22);
        }

        .card-sub {
            background: rgba(15, 23, 42, 0.72);
            box-shadow: none;
        }

        .label {
            color: #718096;
            font-size: 0.68rem;
            font-weight: 800;
            letter-spacing: 0.08em;
            text-transform: uppercase;
        }

        .btn {
            display: inline-flex;
            min-height: 2.5rem;
            align-items: center;
            justify-content: center;
            gap: 0.45rem;
            border: 1px solid transparent;
            border-radius: 8px;
            padding: 0.6rem 0.9rem;
            font-size: 0.86rem;
            font-weight: 800;
            line-height: 1;
            white-space: nowrap;
        }

        .btn-primary { background: #38bdf8; color: #03111c; }
        .btn-soft { border-color: rgba(56, 189, 248, 0.24); background: rgba(56, 189, 248, 0.1); color: #bae6fd; }
        .btn-ghost { border-color: rgba(148, 163, 184, 0.18); background: rgba(148, 163, 184, 0.08); color: #f8fafc; }
        .btn-amber { border-color: rgba(251, 191, 36, 0.32); background: rgba(251, 191, 36, 0.14); color: #fde68a; }
        .btn-danger { border-color: rgba(251, 113, 133, 0.32); background: rgba(251, 113, 133, 0.14); color: #fecdd3; }
        .btn-xs { min-height: 2rem; padding: 0.42rem 0.62rem; font-size: 0.76rem; }

        .pill,
        .chip-filter {
            display: inline-flex;
            align-items: center;
            gap: 0.35rem;
            border-radius: 999px;
            padding: 0.32rem 0.58rem;
            font-size: 0.72rem;
            font-weight: 850;
            line-height: 1;
            white-space: nowrap;
        }

        .pill-blue,
        .chip-filter-active { background: rgba(56, 189, 248, 0.12); color: #bae6fd; }
        .pill-green { background: rgba(52, 211, 153, 0.13); color: #bbf7d0; }
        .pill-amber { background: rgba(251, 191, 36, 0.13); color: #fde68a; }
        .pill-red { background: rgba(251, 113, 133, 0.13); color: #fecdd3; }
        .pill-purple { background: rgba(192, 132, 252, 0.13); color: #e9d5ff; }
        .pill-cyan { background: rgba(34, 211, 238, 0.13); color: #cffafe; }
        .pill-slate { background: rgba(148, 163, 184, 0.12); color: #cbd5e1; }

        .tab-btn {
            display: inline-flex;
            min-height: 2.65rem;
            align-items: center;
            gap: 0.45rem;
            border: 1px solid transparent;
            border-radius: 8px;
            padding: 0.65rem 0.8rem;
            color: #a8b3c4;
            font-size: 0.82rem;
            font-weight: 850;
            white-space: nowrap;
        }

        .tab-btn.is-active {
            border-color: rgba(56, 189, 248, 0.26);
            background: rgba(56, 189, 248, 0.12);
            color: #f8fafc;
        }
    </style>
</head>
<body>
    <main id="venta-index-rework" class="grid gap-4 p-4">
        <section id="panel-caja-cerrada" class="card flex items-center gap-4 p-4" role="status">
            <div class="grid h-11 w-11 place-items-center rounded-lg bg-amber-400/10 text-amber-300">
                <span class="material-symbols-outlined">point_of_sale</span>
            </div>
            <div class="flex-1">
                <p class="label">Caja requerida</p>
                <h2 class="font-black">Sin caja abierta</h2>
                <p class="text-sm text-slate-300">No tiene una caja abierta a su nombre. Abra una caja para habilitar las operaciones de venta.</p>
            </div>
            <div class="flex flex-wrap gap-2">
                <a id="btn-abrir-caja" href="/Caja/Abrir" class="btn btn-amber">
                    <span class="material-symbols-outlined">lock_open</span>
                    Abrir caja
                </a>
                <a href="/Caja" class="btn btn-ghost">
                    <span class="material-symbols-outlined">point_of_sale</span>
                    Ver cajas
                </a>
            </div>
        </section>

        <div class="toast-msg card flex items-center gap-2 border-emerald-400/30 p-4" role="status">
            <span class="material-symbols-outlined text-emerald-300">check_circle</span>
            <span>Venta registrada correctamente.</span>
        </div>

        <section class="card grid gap-5 p-5 lg:grid-cols-[minmax(0,1fr)_auto]">
            <div class="grid gap-3">
                <div class="flex items-center gap-2 text-sm font-extrabold text-slate-300">
                    <span class="h-2 w-2 rounded-full bg-emerald-300 shadow-[0_0_0_4px_rgba(52,211,153,0.12)]"></span>
                    Centro operativo
                </div>
                <div>
                    <h1 class="text-4xl font-black leading-none tracking-normal sm:text-6xl">Centro de Ventas</h1>
                    <p class="mt-3 max-w-5xl text-base leading-7 text-slate-300">Seguimiento diario de operaciones, autorizaciones, presupuestos y devoluciones con datos reales del ERP.</p>
                </div>
                <div class="flex flex-wrap gap-2" aria-label="Resumen de filtros">
                    <span class="chip-filter border border-slate-500/20 bg-slate-900/80 text-slate-300">
                        <span class="material-symbols-outlined text-base">today</span>
                        8 hoy
                    </span>
                    <span class="chip-filter border border-slate-500/20 bg-slate-900/80 text-slate-300">
                        <span class="material-symbols-outlined text-base">receipt_long</span>
                        42 registros
                    </span>
                    <span class="chip-filter chip-filter-active">
                        <span class="material-symbols-outlined text-base">filter_alt</span>
                        Filtros activos
                    </span>
                </div>
            </div>
            <div class="flex flex-wrap content-start justify-end gap-2">
                <button type="button" id="btn-configurar-recargo" class="btn btn-soft" onclick="openModal('modal-configuracion-pagos')">
                    <span class="material-symbols-outlined">percent</span>
                    Recargo/Descuento
                </button>
                <a href="/Devolucion?tab=devoluciones" class="btn btn-ghost">
                    <span class="material-symbols-outlined">assignment_return</span>
                    Devoluciones
                </a>
                <button type="button" id="btn-abrir-modal-crear-venta" class="btn btn-primary" onclick="openModal('modal-nueva-venta')">
                    <span class="material-symbols-outlined">add</span>
                    Nueva Venta
                </button>
            </div>
        </section>

        <section class="grid gap-3 md:grid-cols-2 xl:grid-cols-6" aria-label="Indicadores de ventas">
            <article class="card card-sub grid min-h-36 gap-1 p-4">
                <span class="material-symbols-outlined text-sky-300">point_of_sale</span>
                <p class="label">Operaciones hoy</p>
                <strong class="text-2xl font-black">8</strong>
                <span class="text-xs text-slate-300">Ventas no canceladas del dia</span>
            </article>
            <article class="card card-sub grid min-h-36 gap-1 p-4">
                <span class="material-symbols-outlined text-sky-300">payments</span>
                <p class="label">Total vendido hoy</p>
                <strong class="text-2xl font-black">$ 1.284.500,00</strong>
                <span class="text-xs text-slate-300">Importe acumulado real</span>
            </article>
            <article class="card card-sub grid min-h-36 gap-1 p-4">
                <span class="material-symbols-outlined text-sky-300">approval</span>
                <p class="label">Pendientes autorizacion</p>
                <strong class="text-2xl font-black">3</strong>
                <span class="text-xs text-slate-300">Requieren revision operativa</span>
            </article>
            <article class="card card-sub grid min-h-36 gap-1 p-4">
                <span class="material-symbols-outlined text-sky-300">dataset</span>
                <p class="label">Registros totales</p>
                <strong class="text-2xl font-black">42</strong>
                <span class="text-xs text-slate-300">Resultado del filtro actual</span>
            </article>
            <article class="card card-sub grid min-h-36 gap-1 p-4">
                <span class="material-symbols-outlined text-sky-300">monitoring</span>
                <p class="label">Ticket promedio hoy</p>
                <strong class="text-2xl font-black">$ 160.562,50</strong>
                <span class="text-xs text-slate-300">Calculado sobre operaciones de hoy</span>
            </article>
            <article class="card card-sub grid min-h-36 gap-1 p-4">
                <span class="material-symbols-outlined text-sky-300">assignment_return</span>
                <p class="label">Elegibles devolucion</p>
                <strong class="text-2xl font-black">12</strong>
                <span class="text-xs text-slate-300">2 canceladas en vista</span>
            </article>
        </section>

        <section class="card overflow-hidden">
            <div class="border-b border-slate-700/80 bg-slate-950/30">
                <div class="flex gap-1 overflow-x-auto p-3" role="tablist" aria-label="Secciones del Centro de Ventas">
                    <button type="button" id="tab-operaciones" class="tab-btn is-active" role="tab" aria-selected="true" aria-controls="panel-operaciones">
                        <span class="material-symbols-outlined">table_rows</span>
                        Operaciones
                    </button>
                    <button type="button" id="tab-pendientes" class="tab-btn" role="tab" aria-selected="false" aria-controls="panel-pendientes">
                        <span class="material-symbols-outlined">pending_actions</span>
                        Pendientes
                        <span class="grid h-5 min-w-5 place-items-center rounded-full bg-amber-400/20 px-1 text-xs text-amber-100">3</span>
                    </button>
                    <button type="button" id="tab-cotizaciones" class="tab-btn" role="tab" aria-selected="false" aria-controls="panel-cotizaciones">
                        <span class="material-symbols-outlined">request_quote</span>
                        Cotizaciones y presupuestos
                    </button>
                    <button type="button" id="tab-devoluciones" class="tab-btn" role="tab" aria-selected="false" aria-controls="panel-devoluciones">
                        <span class="material-symbols-outlined">assignment_return</span>
                        Devoluciones
                    </button>
                    <button type="button" id="tab-pagos" class="tab-btn" role="tab" aria-selected="false" aria-controls="panel-pagos">
                        <span class="material-symbols-outlined">tune</span>
                        Configuracion de pagos
                    </button>
                </div>
            </div>

            <section id="panel-operaciones" role="tabpanel" aria-labelledby="tab-operaciones" class="p-4">
                <div class="mb-4 flex flex-wrap items-start justify-between gap-4">
                    <div>
                        <p class="label">Listado principal</p>
                        <h2 class="mt-1 text-xl font-black">Operaciones</h2>
                        <p class="text-sm text-slate-300">Consulta estado, autorizacion, medio de pago y acciones disponibles sobre cada venta.</p>
                    </div>
                    <div class="flex flex-wrap justify-end gap-2">
                        <span class="pill pill-slate">42 registros</span>
                        <span class="pill pill-blue">Filtros activos</span>
                    </div>
                </div>

                <form id="form-filtros" action="/Venta" method="get" class="mb-4 grid gap-3 rounded-lg border border-slate-500/20 bg-slate-950/20 p-4 lg:grid-cols-[minmax(14rem,1.5fr)_repeat(5,minmax(9rem,1fr))_auto]">
                    <div class="grid gap-1">
                        <label class="label" for="venta-filtro-numero">Buscar venta</label>
                        <input id="venta-filtro-numero" name="Numero" type="text" value="V-000128" placeholder="Numero de venta" class="min-h-10 rounded-lg border border-slate-700 bg-slate-900 px-3 text-sm text-white" />
                    </div>
                    <div class="grid gap-1">
                        <label class="label" for="venta-filtro-desde">Desde</label>
                        <input id="venta-filtro-desde" name="FechaDesde" type="date" value="2026-05-01" class="min-h-10 rounded-lg border border-slate-700 bg-slate-900 px-3 text-sm text-white" />
                    </div>
                    <div class="grid gap-1">
                        <label class="label" for="venta-filtro-hasta">Hasta</label>
                        <input id="venta-filtro-hasta" name="FechaHasta" type="date" value="2026-05-26" class="min-h-10 rounded-lg border border-slate-700 bg-slate-900 px-3 text-sm text-white" />
                    </div>
                    <div class="grid gap-1">
                        <label class="label" for="venta-filtro-estado">Estado</label>
                        <select id="venta-filtro-estado" name="Estado" class="min-h-10 rounded-lg border border-slate-700 bg-slate-900 px-3 text-sm text-white">
                            <option value="">Todos</option>
                            <option value="Confirmada" selected>Confirmada</option>
                            <option value="Facturada">Facturada</option>
                            <option value="Entregada">Entregada</option>
                            <option value="Cancelada">Cancelada</option>
                        </select>
                    </div>
                    <div class="grid gap-1">
                        <label class="label" for="venta-filtro-pago">Pago</label>
                        <select id="venta-filtro-pago" name="TipoPago" class="min-h-10 rounded-lg border border-slate-700 bg-slate-900 px-3 text-sm text-white">
                            <option value="">Todos</option>
                            <option value="Efectivo">Efectivo</option>
                            <option value="Transferencia" selected>Transferencia</option>
                            <option value="TarjetaCredito">Tarjeta credito</option>
                            <option value="CreditoPersonal">Credito personal</option>
                        </select>
                    </div>
                    <div class="grid gap-1">
                        <label class="label" for="venta-filtro-autorizacion">Autorizacion</label>
                        <select id="venta-filtro-autorizacion" name="EstadoAutorizacion" class="min-h-10 rounded-lg border border-slate-700 bg-slate-900 px-3 text-sm text-white">
                            <option value="">Todas</option>
                            <option value="NoRequiere">No requiere</option>
                            <option value="PendienteAutorizacion" selected>Pendiente</option>
                            <option value="Autorizada">Autorizada</option>
                            <option value="Rechazada">Rechazada</option>
                        </select>
                    </div>
                    <div class="flex items-end gap-2">
                        <button type="submit" class="btn btn-primary">
                            <span class="material-symbols-outlined">filter_list</span>
                            Filtrar
                        </button>
                        <a href="/Venta" class="btn btn-ghost">
                            <span class="material-symbols-outlined">clear_all</span>
                            Limpiar
                        </a>
                    </div>
                </form>

                <div id="ventas-index-scroll" class="overflow-hidden rounded-lg border border-slate-500/20">
                    <div class="overflow-x-auto" tabindex="0" role="region" aria-label="Listado principal de ventas">
                        <table class="w-full min-w-[72rem] border-collapse">
                            <thead>
                                <tr class="bg-slate-900/80 text-left text-xs uppercase tracking-wide text-slate-300">
                                    <th class="p-4" scope="col">Numero</th>
                                    <th class="p-4" scope="col">Fecha</th>
                                    <th class="p-4" scope="col">Cliente</th>
                                    <th class="p-4" scope="col">Estado</th>
                                    <th class="p-4" scope="col">Autorizacion</th>
                                    <th class="p-4" scope="col">Tipo de pago</th>
                                    <th class="p-4 text-right" scope="col">Total</th>
                                    <th class="p-4 text-center" scope="col">Acciones</th>
                                </tr>
                            </thead>
                            <tbody class="text-sm text-slate-300">
                                <tr class="border-t border-slate-500/20">
                                    <td class="p-4"><a href="/Venta/Details/128" class="font-black text-sky-300">V-000128</a></td>
                                    <td class="p-4">26/05/2026</td>
                                    <td class="p-4">Cliente Mostrador</td>
                                    <td class="p-4"><span class="pill pill-green">Confirmada</span></td>
                                    <td class="p-4"><span class="pill pill-amber">Pendiente</span></td>
                                    <td class="p-4">Transferencia</td>
                                    <td class="p-4 text-right font-black text-white">$ 186.400,00</td>
                                    <td class="p-4">
                                        <div class="flex flex-wrap justify-center gap-2">
                                            <a href="/Venta/Details/128" class="btn btn-xs btn-soft">
                                                <span class="material-symbols-outlined">visibility</span>
                                                Ver
                                            </a>
                                            <button type="button" class="btn btn-xs btn-amber" data-open-devolucion-modal data-venta-id="128">
                                                <span class="material-symbols-outlined">assignment_return</span>
                                                Devolver
                                            </button>
                                            <a href="/Venta/Cancelar/128" class="btn btn-xs btn-danger">
                                                <span class="material-symbols-outlined">block</span>
                                                Anular
                                            </a>
                                        </div>
                                    </td>
                                </tr>
                                <tr class="border-t border-slate-500/20">
                                    <td class="p-4"><a href="/Venta/Details/127" class="font-black text-sky-300">V-000127</a></td>
                                    <td class="p-4">26/05/2026</td>
                                    <td class="p-4">Maria Rodriguez</td>
                                    <td class="p-4"><span class="pill pill-purple">Entregada</span></td>
                                    <td class="p-4"><span class="pill pill-green">Autorizada</span></td>
                                    <td class="p-4">Credito personal</td>
                                    <td class="p-4 text-right font-black text-white">$ 412.000,00</td>
                                    <td class="p-4">
                                        <div class="flex flex-wrap justify-center gap-2">
                                            <a href="/Venta/Details/127" class="btn btn-xs btn-soft">
                                                <span class="material-symbols-outlined">visibility</span>
                                                Ver
                                            </a>
                                            <button type="button" class="btn btn-xs btn-amber" data-open-devolucion-modal data-venta-id="127">
                                                <span class="material-symbols-outlined">assignment_return</span>
                                                Devolver
                                            </button>
                                            <span class="btn btn-xs btn-ghost opacity-50">Anular</span>
                                        </div>
                                    </td>
                                </tr>
                                <tr class="border-t border-slate-500/20">
                                    <td class="p-4"><a href="/Venta/Details/126" class="font-black text-sky-300">V-000126</a></td>
                                    <td class="p-4">25/05/2026</td>
                                    <td class="p-4">Comercial Norte SA</td>
                                    <td class="p-4"><span class="pill pill-red">Cancelada</span></td>
                                    <td class="p-4"><span class="pill pill-slate">No requiere</span></td>
                                    <td class="p-4">Efectivo</td>
                                    <td class="p-4 text-right font-black text-slate-500">$ 74.900,00</td>
                                    <td class="p-4">
                                        <div class="flex flex-wrap justify-center gap-2">
                                            <a href="/Venta/Details/126" class="btn btn-xs btn-soft">
                                                <span class="material-symbols-outlined">visibility</span>
                                                Ver
                                            </a>
                                            <span class="btn btn-xs btn-ghost opacity-50">Devolver</span>
                                            <span class="btn btn-xs btn-ghost opacity-50">Anular</span>
                                        </div>
                                    </td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                </div>

                <div class="mt-4 grid gap-3 lg:hidden" aria-label="Ventas en formato mobile">
                    <article class="card card-sub grid gap-3 p-4">
                        <div class="flex items-start justify-between gap-3">
                            <div>
                                <a href="/Venta/Details/128" class="font-black text-sky-300">V-000128</a>
                                <p class="text-sm text-slate-300">26/05/2026</p>
                            </div>
                            <strong class="text-right font-black">$ 186.400,00</strong>
                        </div>
                        <div class="flex items-center gap-2 text-sm text-slate-300">
                            <span class="material-symbols-outlined">person</span>
                            Cliente Mostrador
                        </div>
                        <div class="flex flex-wrap gap-2">
                            <span class="pill pill-green">Confirmada</span>
                            <span class="pill pill-amber">Pendiente</span>
                            <span class="pill pill-slate">Transferencia</span>
                        </div>
                        <div class="flex flex-wrap gap-2">
                            <a href="/Venta/Details/128" class="btn btn-xs btn-soft">Ver</a>
                            <button type="button" class="btn btn-xs btn-amber" data-open-devolucion-modal data-venta-id="128">Devolver</button>
                            <a href="/Venta/Cancelar/128" class="btn btn-xs btn-danger">Anular</a>
                        </div>
                    </article>
                </div>
            </section>

            <section id="panel-pendientes" role="tabpanel" aria-labelledby="tab-pendientes" class="hidden p-4">
                <div class="mb-4">
                    <p class="label">Autorizaciones</p>
                    <h2 class="mt-1 text-xl font-black">Pendientes</h2>
                    <p class="text-sm text-slate-300">Operaciones que necesitan autorizacion antes de continuar.</p>
                </div>
                <div class="grid gap-3">
                    <article class="card card-sub grid gap-3 p-4 md:grid-cols-[minmax(0,1fr)_auto_auto_auto] md:items-center">
                        <div>
                            <a href="/Venta/Details/128" class="font-black text-sky-300">V-000128</a>
                            <p class="text-sm text-slate-300">Cliente Mostrador - 26/05/2026</p>
                        </div>
                        <span class="pill pill-amber">Pendiente</span>
                        <strong>$ 186.400,00</strong>
                        <a href="/Venta/Details/128" class="btn btn-xs btn-soft">Ver</a>
                    </article>
                </div>
            </section>

            <section id="panel-cotizaciones" role="tabpanel" aria-labelledby="tab-cotizaciones" class="hidden p-4">
                <div class="mb-4">
                    <p class="label">Seguimiento comercial</p>
                    <h2 class="mt-1 text-xl font-black">Cotizaciones y presupuestos</h2>
                    <p class="text-sm text-slate-300">Registros reales en estado cotizacion o presupuesto.</p>
                </div>
                <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                    <article class="card card-sub grid gap-3 p-4">
                        <div class="flex items-center justify-between gap-3">
                            <span class="pill pill-cyan">Cotizacion</span>
                            <strong>$ 98.300,00</strong>
                        </div>
                        <a href="/Venta/Details/121" class="font-black text-sky-300">COT-000121</a>
                        <p class="text-sm text-slate-300">Luis Perez</p>
                        <a href="/Venta/Details/121" class="btn btn-xs btn-soft">Ver</a>
                    </article>
                    <article class="card card-sub grid gap-3 p-4">
                        <div class="flex items-center justify-between gap-3">
                            <span class="pill pill-blue">Presupuesto</span>
                            <strong>$ 256.000,00</strong>
                        </div>
                        <a href="/Venta/Details/119" class="font-black text-sky-300">PRE-000119</a>
                        <p class="text-sm text-slate-300">Sofia Alvarez</p>
                        <a href="/Venta/Details/119" class="btn btn-xs btn-soft">Ver</a>
                    </article>
                </div>
            </section>

            <section id="panel-devoluciones" role="tabpanel" aria-labelledby="tab-devoluciones" class="hidden p-4">
                <div class="mb-4 flex flex-wrap items-start justify-between gap-3">
                    <div>
                        <p class="label">Postventa</p>
                        <h2 class="mt-1 text-xl font-black">Devoluciones</h2>
                        <p class="text-sm text-slate-300">Ventas elegibles para abrir el modal real de devolucion.</p>
                    </div>
                    <a href="/Devolucion?tab=devoluciones" class="btn btn-ghost">Ver historial</a>
                </div>
                <div class="grid gap-3">
                    <article class="card card-sub grid gap-3 p-4 md:grid-cols-[minmax(0,1fr)_auto_auto] md:items-center">
                        <div>
                            <a href="/Venta/Details/127" class="font-black text-sky-300">V-000127</a>
                            <p class="text-sm text-slate-300">Maria Rodriguez - Credito personal</p>
                        </div>
                        <strong>$ 412.000,00</strong>
                        <button type="button" class="btn btn-xs btn-amber" data-open-devolucion-modal data-venta-id="127">Devolver</button>
                    </article>
                </div>
            </section>

            <section id="panel-pagos" role="tabpanel" aria-labelledby="tab-pagos" class="hidden p-4">
                <div class="mb-4 flex flex-wrap items-start justify-between gap-3">
                    <div>
                        <p class="label">Reglas de cobro</p>
                        <h2 class="mt-1 text-xl font-black">Configuracion de pagos</h2>
                        <p class="text-sm text-slate-300">El modal real carga recargos y descuentos dinamicamente desde el backend.</p>
                    </div>
                    <button type="button" class="btn btn-primary" onclick="openModal('modal-configuracion-pagos')">
                        <span class="material-symbols-outlined">percent</span>
                        Abrir configuracion
                    </button>
                </div>
                <div class="card card-sub flex items-center gap-4 p-4">
                    <span class="material-symbols-outlined text-3xl text-sky-300">tune</span>
                    <div>
                        <h3 class="font-black">Recargos y descuentos por tipo de pago</h3>
                        <p class="text-sm text-slate-300">No se renderizan datos demo en Razor: el listado productivo se inyecta en #recargo-lista cuando se abre el modal.</p>
                    </div>
                </div>
            </section>
        </section>

        <nav class="fixed bottom-3 left-3 right-3 z-30 grid gap-2 rounded-lg border border-slate-500/20 bg-[#090d12]/95 p-2 shadow-2xl sm:hidden" aria-label="Acciones principales de ventas">
            <button type="button" class="btn btn-primary" onclick="openModal('modal-nueva-venta')">
                <span class="material-symbols-outlined">add</span>
                Nueva Venta
            </button>
            <a href="/Venta" class="btn btn-ghost">
                <span class="material-symbols-outlined">refresh</span>
                Actualizar
            </a>
        </nav>
    </main>

    <div id="modal-configuracion-pagos" class="fixed inset-0 z-50 hidden" aria-hidden="true">
        <div class="absolute inset-0 bg-black/60"></div>
        <div class="relative flex min-h-screen items-center justify-center p-4">
            <div class="card w-full max-w-3xl">
                <div class="flex items-center justify-between border-b border-slate-700 p-6">
                    <h3 class="flex items-center gap-2 text-lg font-black">
                        <span class="material-symbols-outlined text-sky-300">tune</span>
                        Recargos y Descuentos por Tipo de Pago
                    </h3>
                    <button type="button" class="btn btn-ghost" onclick="closeModal('modal-configuracion-pagos')">
                        <span class="material-symbols-outlined">close</span>
                    </button>
                </div>
                <div class="space-y-4 p-6">
                    <div class="card-sub rounded-lg border border-slate-700 p-4">
                        <div class="flex items-center justify-between">
                            <div>
                                <strong>Tarjeta credito</strong>
                                <p class="text-sm text-slate-300">Recargo activo</p>
                            </div>
                            <span class="pill pill-amber">+12%</span>
                        </div>
                    </div>
                    <div class="card-sub rounded-lg border border-slate-700 p-4">
                        <div class="flex items-center justify-between">
                            <div>
                                <strong>Efectivo</strong>
                                <p class="text-sm text-slate-300">Descuento activo</p>
                            </div>
                            <span class="pill pill-green">-8%</span>
                        </div>
                    </div>
                </div>
                <div class="flex justify-end gap-3 border-t border-slate-700 p-6">
                    <button type="button" class="btn btn-ghost" onclick="closeModal('modal-configuracion-pagos')">Cancelar</button>
                    <button type="button" class="btn btn-primary">Guardar Cambios</button>
                </div>
            </div>
        </div>
    </div>

    <div id="modal-nueva-venta" class="fixed inset-0 z-50 hidden" aria-hidden="true">
        <div class="absolute inset-0 bg-black/60"></div>
        <div class="relative flex min-h-screen items-center justify-center p-4">
            <form id="CreateAjax" class="card w-full max-w-4xl" action="/Venta/CreateAjax" method="post">
                <div class="flex items-center justify-between border-b border-slate-700 p-6">
                    <h3 class="flex items-center gap-2 text-lg font-black">
                        <span class="material-symbols-outlined text-sky-300">add_shopping_cart</span>
                        Nueva Venta
                    </h3>
                    <button type="button" class="btn btn-ghost" onclick="closeModal('modal-nueva-venta')">
                        <span class="material-symbols-outlined">close</span>
                    </button>
                </div>
                <div class="grid gap-4 p-6 md:grid-cols-2">
                    <label class="grid gap-1">
                        <span class="label">Cliente</span>
                        <select name="ClienteId" class="min-h-10 rounded-lg border border-slate-700 bg-slate-900 px-3 text-white">
                            <option value="1">Cliente Mostrador</option>
                            <option value="2">Maria Rodriguez</option>
                        </select>
                    </label>
                    <label class="grid gap-1">
                        <span class="label">Tipo de pago</span>
                        <select name="TipoPago" class="min-h-10 rounded-lg border border-slate-700 bg-slate-900 px-3 text-white">
                            <option value="Efectivo">Efectivo</option>
                            <option value="Transferencia">Transferencia</option>
                            <option value="TarjetaCredito">Tarjeta credito</option>
                        </select>
                    </label>
                    <label class="grid gap-1 md:col-span-2">
                        <span class="label">Producto</span>
                        <input name="ProductoBusqueda" type="text" value="Heladera demo 300L" class="min-h-10 rounded-lg border border-slate-700 bg-slate-900 px-3 text-white" />
                    </label>
                </div>
                <div class="flex justify-end gap-3 border-t border-slate-700 p-6">
                    <button type="button" class="btn btn-ghost" onclick="closeModal('modal-nueva-venta')">Cancelar</button>
                    <button type="button" class="btn btn-primary" onclick="VentaCrearModal.submit()">Crear Venta</button>
                </div>
            </form>
        </div>
    </div>

    <div id="modal-confirmar-operacion" class="fixed inset-0 z-[60] hidden" aria-hidden="true">
        <div class="absolute inset-0 bg-black/70"></div>
        <div class="relative flex min-h-screen items-center justify-center p-4">
            <div class="card w-full max-w-md p-6">
                <div class="grid gap-3">
                    <span class="material-symbols-outlined text-4xl text-amber-300">warning</span>
                    <h3 class="text-xl font-black">Confirmar operacion</h3>
                    <p class="text-sm text-slate-300">Esta accion usa datos demo en el HTML objetivo y no debe copiarse al Razor productivo.</p>
                    <div class="flex justify-end gap-3">
                        <button type="button" class="btn btn-ghost" onclick="closeModal('modal-confirmar-operacion')">Cancelar</button>
                        <button type="button" class="btn btn-danger">Confirmar</button>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <script>
        function openModal(id) {
            const modal = document.getElementById(id);
            if (!modal) return;
            modal.classList.remove('hidden');
            modal.setAttribute('aria-hidden', 'false');
            document.body.style.overflow = 'hidden';
        }

        function closeModal(id) {
            const modal = document.getElementById(id);
            if (!modal) return;
            modal.classList.add('hidden');
            modal.setAttribute('aria-hidden', 'true');
            document.body.style.overflow = '';
        }

        const VentaCrearModal = {
            submit() {
                const form = document.getElementById('CreateAjax');
                if (!form) return;
                openModal('modal-confirmar-operacion');
            }
        };

        document.querySelectorAll('[role="tab"]').forEach((tab) => {
            tab.addEventListener('click', () => {
                const panelId = tab.getAttribute('aria-controls');
                document.querySelectorAll('[role="tab"]').forEach((item) => {
                    item.classList.toggle('is-active', item === tab);
                    item.setAttribute('aria-selected', item === tab ? 'true' : 'false');
                });
                document.querySelectorAll('[role="tabpanel"]').forEach((panel) => {
                    panel.classList.toggle('hidden', panel.id !== panelId);
                });
            });
        });
    </script>
</body>
</html>
```

## Adaptacion esperada en Razor productivo

La implementacion Razor debe seguir usando:

- layout compartido del ERP;
- estilos productivos en `wwwroot/css/venta-index-rework.css`;
- JavaScript productivo en `wwwroot/js/venta-index-rework.js`;
- datos reales de `VentaViewModel`;
- links y acciones reales de MVC;
- modal real de recargos/descuentos existente;
- modal real de devolucion existente;
- acceso a nueva venta por `asp-controller="Venta" asp-action="Create"`;
- contratos existentes de filtros, tabs, alertas y hooks JavaScript.

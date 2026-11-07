#!/usr/bin/env python3
"""
Inventario funcional por módulo con evidencia empírica (auditoría funcional integral).

Entrada:
  * Controllers/*.cs  -> acciones HTTP (verbo, nombre, atributos).
  * Logs de la app corrida con
        Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information
        Logging__LogLevel__Microsoft.AspNetCore.Routing.EndpointMiddleware=Information
    (cada petición deja «Request starting/finished» con verbo y estado, y «Executing endpoint 'Controller.Accion'»).
  * Un JSON con evidencia manual de lo ejercitado antes de activar el log (opcional).
Salida: CSV de detalle + Markdown de resumen por módulo.

Definiciones (para que el porcentaje sea auditable):
  * Funcionalidad = (controlador, acción) de un controlador de usuario (no *Api). Si la acción tiene POST, solo cuenta como
    verificada si ese POST se ejercitó con respuesta < 400; si es solo GET, con el GET.
  * Endpoint de soporte = acciones de controladores *Api y utilitarios JSON (buscadores, previews). Se informan aparte y NO
    entran en el denominador del porcentaje.
  * «Verificada» = ejercitada contra la app real con resultado comprobado: (A) circuito de la auditoría con contraste SQL,
    o (E) spec E2E que pasó en un clon restaurado. «Ejercitada» no prueba que sea correcta en todos los casos: ver el informe.
"""
import csv, glob, json, os, re
from collections import defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

ACTION_RE = re.compile(
    r'((?:\s*\[[^\]]+\]\s*)*)\s*public\s+(?:async\s+)?(?:Task<)?'
    r'(?:IActionResult|ActionResult|JsonResult|FileResult|ViewResult|PartialViewResult|RedirectToActionResult|ContentResult|FileContentResult)>?'
    r'\s+(\w+)\s*\(')

FUERA_ALCANCE = {'MercadoLibre', 'MercadoLibreWebhook', 'Diagnostico'}


def modulo_de(controlador, accion):
    c, a = controlador, accion
    if c in FUERA_ALCANCE:
        return 'Fuera de alcance (Mercado Libre / diagnóstico de desarrollo)'
    if c == 'Caja' and re.search(r'cheque|acreditar', a, re.I):
        return 'Cheques'
    if c == 'Devolucion' and re.search(r'notacredito|notascredito', a, re.I):
        return 'Notas de crédito'
    if c == 'Seguridad' and re.search(r'auditoria', a, re.I):
        return 'Seguridad'
    tabla = {
        'Venta': 'Ventas y Cotizaciones', 'VentaApi': 'Ventas y Cotizaciones', 'Cotizacion': 'Ventas y Cotizaciones',
        'CotizacionApi': 'Ventas y Cotizaciones', 'VentaCostoServicios': 'Ventas y Cotizaciones',
        'Caja': 'Caja',
        'Credito': 'Crédito', 'Mantenimiento': 'Crédito', 'ContratoVentaCredito': 'Crédito',
        'Mora': 'Mora y Acuerdos', 'ConfiguracionMora': 'Mora y Acuerdos',
        'Proveedor': 'Compras', 'OrdenCompra': 'Compras',
        'Catalogo': 'Stock', 'Producto': 'Stock', 'ProductoApi': 'Stock', 'Categoria': 'Stock', 'Marca': 'Stock',
        'MovimientoStock': 'Stock', 'AlertaStock': 'Stock', 'CambiosPrecios': 'Stock',
        'Devolucion': 'Devoluciones',
        'Cliente': 'Clientes', 'DocumentoCliente': 'Clientes', 'ClienteDatosContractuales': 'Clientes',
        'Usuarios': 'Usuarios y Roles', 'Seguridad': 'Usuarios y Roles',
        'Documento': 'Documentos', 'DocumentosConfig': 'Documentos',
        'Reporte': 'Reportes',
        'Dashboard': 'Dashboard', 'Home': 'Dashboard',
        'Notificacion': 'Notificaciones',
        'Ticket': 'Tickets', 'TicketApi': 'Tickets', 'NotaRapidaApi': 'Tickets',
        'ConfiguracionPago': 'Configuración', 'PrefijosNumeracion': 'Configuración', 'ServiciosVenta': 'Configuración',
        'ConfiguracionCliente': 'Configuración', 'ConfiguracionRentabilidad': 'Configuración',
        'ConfiguracionContratoCredito': 'Configuración', 'PlantillaContratoCredito': 'Configuración',
    }
    return tabla.get(c, 'Sin asignar')


def es_soporte(controlador, accion, atributos):
    if controlador.endswith('Api'):
        return True
    if re.search(r'^(Get|Buscar|Listar|Obtener|Calcular|Verificar|Preview|Simular)|Json$|Partial$|Preview|Api$', accion):
        return True
    return False


def parsear_acciones():
    acciones = []
    for f in sorted(glob.glob(os.path.join(ROOT, 'Controllers', '*.cs'))):
        src = open(f, encoding='utf-8-sig').read()
        m = re.search(r'class\s+(\w+)Controller', src)
        if not m:
            continue
        c = m.group(1)
        for am in ACTION_RE.finditer(src):
            atrib, nombre = am.group(1), am.group(2)
            if 'NonAction' in atrib:
                continue
            verbo = 'POST' if re.search(r'HttpPost|HttpPut|HttpDelete', atrib) else 'GET'
            perm = re.search(r'Modulo\s*=\s*"(\w+)"\s*,\s*Accion\s*=\s*"(\w+)"', atrib)
            acciones.append({
                'controlador': c, 'accion': nombre, 'verbo': verbo,
                'permiso': f'{perm.group(1)}.{perm.group(2)}' if perm else '',
                'anonimo': 'AllowAnonymous' in atrib,
            })
    return acciones


def parsear_log(path):
    """Devuelve {(controlador, accion, verbo): {estado: n}} a partir de un log de la app."""
    reqs = defaultdict(dict)
    ultimo = None
    ep_re = re.compile(r"Executing endpoint 'TheBuryProject\.Controllers\.(\w+)Controller\.(\w+) \(")
    st_re = re.compile(r'Request starting HTTP/[\d.]+ (\w+) ')
    fin_re = re.compile(r'Request finished HTTP/[\d.]+ (\w+) \S+ - (\d{3})')
    rid_re = re.compile(r'RequestId:(\S+)')
    with open(path, encoding='utf-8', errors='ignore') as fh:
        for linea in fh:
            if '=>' in linea and 'RequestId:' in linea:
                m_id = rid_re.search(linea)
                if m_id:
                    ultimo = m_id.group(1)
                continue
            if ultimo is None:
                continue
            m = st_re.search(linea)
            if m:
                reqs[ultimo]['verbo'] = m.group(1)
            m = ep_re.search(linea)
            if m:
                reqs[ultimo]['ctrl'] = m.group(1)
                reqs[ultimo]['accion'] = m.group(2)
            m = fin_re.search(linea)
            if m:
                reqs[ultimo]['estado'] = int(m.group(2))
    out = defaultdict(lambda: defaultdict(int))
    for r in reqs.values():
        if 'ctrl' in r and 'verbo' in r and 'estado' in r:
            out[(r['ctrl'], r['accion'], r['verbo'])][r['estado']] += 1
    return out


def main():
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument('--log-audit', action='append', default=[], help='logs de los circuitos de la auditoría (A)')
    ap.add_argument('--log-e2e', action='append', default=[], help='logs de la suite E2E (E)')
    ap.add_argument('--manual', help='JSON [{"c":"Proveedor","a":"Create","v":"POST","nota":"..."}] (A, sin log)')
    ap.add_argument('--overrides', help='JSON {"Controlador.Accion": {"estado": "Fallida|Incompleta", "nota": "..."}}')
    ap.add_argument('--base-csv', help='inventario anterior (CSV): conserva lo ya verificado/incompleto/fallido y suma la evidencia nueva')
    ap.add_argument('--csv', required=True)
    ap.add_argument('--md', required=True)
    args = ap.parse_args()

    acciones = parsear_acciones()
    ev = {'A': defaultdict(lambda: defaultdict(int)), 'E': defaultdict(lambda: defaultdict(int))}
    for tag, paths in (('A', args.log_audit), ('E', args.log_e2e)):
        for p in paths:
            for k, estados in parsear_log(p).items():
                for e, n in estados.items():
                    ev[tag][k][e] += n
    if args.manual:
        for m in json.load(open(args.manual, encoding='utf-8')):
            ev['A'][(m['c'], m['a'], m['v'])][200] += 1
    overrides = json.load(open(args.overrides, encoding='utf-8')) if args.overrides else {}

    filas = []
    for a in acciones:
        k = (a['controlador'], a['accion'], a['verbo'])
        ok = lambda tag: sum(n for e, n in ev[tag].get(k, {}).items() if e < 400)
        err = lambda tag: sum(n for e, n in ev[tag].get(k, {}).items() if e >= 400)
        a['ok_A'], a['ok_E'], a['err_A'], a['err_E'] = ok('A'), ok('E'), err('A'), err('E')
        a['modulo'] = modulo_de(a['controlador'], a['accion'])
        a['soporte'] = es_soporte(a['controlador'], a['accion'], '')
        filas.append(a)

    # funcionalidad = (controlador, accion): verificada si cada verbo "efectivo" se ejercitó
    func = defaultdict(list)
    for a in filas:
        func[(a['controlador'], a['accion'])].append(a)
    resultados = []
    for (c, ac), lst in sorted(func.items()):
        tiene_post = any(x['verbo'] == 'POST' for x in lst)
        efectivos = [x for x in lst if x['verbo'] == ('POST' if tiene_post else 'GET')]
        okA = all(x['ok_A'] > 0 for x in efectivos)
        okE = all((x['ok_E'] > 0 or x['ok_A'] > 0) for x in efectivos)
        modulo = lst[0]['modulo']
        soporte = lst[0]['soporte']
        estado = 'Verificada (auditoría)' if okA else ('Verificada (E2E)' if okE else 'No verificada')
        nota = ''
        ov = overrides.get(f'{c}.{ac}')
        if ov:
            estado, nota = ov['estado'], ov.get('nota', '')
        resultados.append({
            'modulo': modulo, 'controlador': c, 'accion': ac,
            'verbos': '+'.join(sorted({x['verbo'] for x in lst})),
            'permiso': next((x['permiso'] for x in lst if x['permiso']), ''),
            'tipo': 'soporte' if soporte else 'funcionalidad',
            'estado': estado, 'nota': nota,
            'ok_auditoria': sum(x['ok_A'] for x in efectivos), 'ok_e2e': sum(x['ok_E'] for x in efectivos),
            'rechazos_4xx_5xx': sum(x['err_A'] + x['err_E'] for x in efectivos),
        })

    if args.base_csv:
        base = {}
        with open(args.base_csv, encoding='utf-8-sig', newline='') as fh:
            for fila in csv.DictReader(fh, delimiter=';'):
                base[(fila['controlador'], fila['accion'])] = fila
        for r in resultados:
            previo = base.get((r['controlador'], r['accion']))
            if previo and r['estado'] == 'No verificada' and previo['estado'] != 'No verificada':
                r['estado'], r['nota'] = previo['estado'], previo.get('nota', '')
                r['ok_auditoria'] = max(r['ok_auditoria'], int(previo.get('ok_auditoria') or 0))
                r['ok_e2e'] = max(r['ok_e2e'], int(previo.get('ok_e2e') or 0))
            elif previo and r['estado'].startswith('Verificada'):
                r['ok_auditoria'] += int(previo.get('ok_auditoria') or 0)
                r['ok_e2e'] += int(previo.get('ok_e2e') or 0)

    with open(args.csv, 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.DictWriter(fh, fieldnames=list(resultados[0].keys()), delimiter=';')
        w.writeheader()
        w.writerows(resultados)

    modulos = defaultdict(lambda: defaultdict(int))
    soporte = defaultdict(lambda: defaultdict(int))
    for r in resultados:
        d = modulos if r['tipo'] == 'funcionalidad' else soporte
        d[r['modulo']]['total'] += 1
        d[r['modulo'][:0] + r['modulo']][r['estado']] += 1
    orden = ['Ventas y Cotizaciones', 'Caja', 'Crédito', 'Mora y Acuerdos', 'Cheques', 'Compras', 'Stock', 'Devoluciones',
             'Notas de crédito', 'Clientes', 'Usuarios y Roles', 'Documentos', 'Reportes', 'Dashboard', 'Notificaciones',
             'Tickets', 'Configuración', 'Seguridad']
    lineas = ['| Módulo | Funcionalidades | Verificadas (auditoría) | Verificadas (E2E) | Fallidas | Incompletas | No verificadas | Cobertura |',
              '|---|---:|---:|---:|---:|---:|---:|---:|']
    tot = defaultdict(int)
    for mname in orden:
        d = modulos.get(mname, {})
        n = d.get('total', 0)
        va, ve = d.get('Verificada (auditoría)', 0), d.get('Verificada (E2E)', 0)
        fa, inc, nv = d.get('Fallida', 0), d.get('Incompleta', 0), d.get('No verificada', 0)
        cob = f'{(va + ve) * 100 / n:.0f} %' if n else 'n/a'
        lineas.append(f'| {mname} | {n} | {va} | {ve} | {fa} | {inc} | {nv} | {cob} |')
        for k, v in (('n', n), ('va', va), ('ve', ve), ('fa', fa), ('inc', inc), ('nv', nv)):
            tot[k] += v
    lineas.append(f"| **Total** | **{tot['n']}** | **{tot['va']}** | **{tot['ve']}** | **{tot['fa']}** | **{tot['inc']}** | **{tot['nv']}** | "
                  f"**{(tot['va'] + tot['ve']) * 100 / tot['n']:.1f} %** |")
    ap_lineas = ['| Módulo | Endpoints de soporte | Ejercitados | No ejercitados |', '|---|---:|---:|---:|']
    for mname in orden:
        d = soporte.get(mname, {})
        n = d.get('total', 0)
        e = d.get('Verificada (auditoría)', 0) + d.get('Verificada (E2E)', 0)
        ap_lineas.append(f'| {mname} | {n} | {e} | {n - e} |')
    with open(args.md, 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(lineas) + '\n\n### Endpoints de soporte (no entran en el denominador)\n\n' + '\n'.join(ap_lineas) + '\n')
    print('\n'.join(lineas))
    print(f"funcionalidades={tot['n']} verificadas={tot['va'] + tot['ve']} (auditoría={tot['va']}, e2e={tot['ve']})")


if __name__ == '__main__':
    main()

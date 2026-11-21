#!/usr/bin/env python3
"""P-05: autenticacion y AUTORIZACION por rol contra el stack Docker real de un servidor de ENSAYO (VM con datos sinteticos).

Para cada rol crea una cuenta sintetica `p05.<rol><sufijo>`, inicia sesion por HTTP real (Identity + antiforgery) y compara, ruta por ruta, lo que el
servidor RESPONDE contra lo que la base de datos dice que ese rol puede (RolPermisos -> ModulosSistema/AccionesModulo). El oraculo es la BD, no el
codigo de los controladores: si un controlador dejara pasar a un rol sin permiso (o bloqueara a uno con permiso) el cruce lo muestra.
Ademas: sin sesion todo redirige al login; un rol sin permiso NO puede crear usuarios, cambiar la clave ni desactivar a otro usuario (intento
de escalada contra una cuenta victima sintetica); un POST sin token antiforgery se rechaza.

  python3 p05_roles_vm.py --env ~/theburyproject1/.env --base https://tbp --ca ~/theburyproject1/caddy-root-ca.crt --confirmo-entorno-de-ensayo

GUARDAS (mismas que P-03): solo contra un servidor de ENSAYO, HTTP y SQL del mismo stack (--resolve a 127.0.0.1), <= 50 usuarios, sin cuentas
p05.* activas previas. NO imprime contrasenas. Al terminar da de baja (y borra) las cuentas p05.* y verifica que `admin` conserve su clave original.
Salida: 0 todo OK | 1 hay FALLA o limpieza no verificada | 2 uso/guarda.
"""
import argparse, os, re, secrets, socket, ssl, subprocess, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "p03"))
from p03_vm_produccion import Session, read_env, new_password  # noqa: E402

# Rutas GET de indice con permiso LITERAL en el atributo del controlador (modulo, accion). Extraidas de Controllers/*.cs.
ROUTES = [
    ("/AlertaStock", "stock", "viewalerts"), ("/Caja", "caja", "view"), ("/Categoria", "categorias", "view"), ("/Cliente", "clientes", "view"),
    ("/ConfiguracionCliente", "configuracion", "view"), ("/ConfiguracionPago", "configuracion", "view"),
    ("/ConfiguracionRentabilidad", "configuracion", "view"), ("/Credito", "creditos", "view"), ("/CuentaPorPagar", "cuentasporpagar", "view"),
    ("/Dashboard", "dashboard", "view"), ("/Devolucion", "devoluciones", "view"), ("/Documento", "documentos", "view"),
    ("/ListaPrecio", "precios", "view"), ("/Marca", "marcas", "view"), ("/Mora", "mora", "view"), ("/MovimientoStock", "movimientos", "view"),
    ("/PrefijosNumeracion", "configuracion", "view"), ("/Producto", "productos", "view"), ("/Proveedor", "proveedores", "view"),
    ("/Reporte", "reportes", "view"), ("/ServiciosVenta", "configuracion", "view"), ("/Tesoreria", "tesoreria", "view"),
    ("/ValoracionStock", "valoracionstock", "view")
]
ROLES = ["Administrador", "Gerente", "Vendedor", "Cajero", "Contador", "Repositor", "Tecnico"]


def classify(status, loc):
    """allowed | denied | login | other (200 o redireccion interna = allowed; 403/AccessDenied = denied; al login = login)"""
    if status == 200:
        return "allowed"
    if status == 403:
        return "denied"
    if status in (301, 302, 303, 307, 308):
        l = loc.lower()
        if "accessdenied" in l or "acceso-denegado" in l or "forbidden" in l:
            return "denied"
        if "/identity/account/login" in l:
            return "login"
        return "allowed"   # redireccion posterior a la autorizacion (p. ej. /Categoria -> /Catalogo): el filtro de permisos ya dejo pasar
    return "other"


def post_with_token(sess, path, fields, pages=("/", "/Dashboard", "/Caja", "/Producto", "/Cliente", "/Seguridad")):
    """POST con token antiforgery tomado de la primera pagina accesible que lo trae (un rol sin acceso a /Seguridad no puede usarla)."""
    tok = None
    for pg in pages:
        body = sess.send("GET", pg)[2]
        m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body) or re.search(r'name="csrf-token"\s+content="([^"]+)"', body)
        if m:
            tok = m.group(1); break
    f = dict(fields)
    if tok:
        f["__RequestVerificationToken"] = tok
    return sess.send("POST", path, f), tok is not None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--env", required=True); ap.add_argument("--base", default="https://tbp"); ap.add_argument("--ca", required=True)
    ap.add_argument("--project", default="theburyproject"); ap.add_argument("--resolve", default="tbp=127.0.0.1")
    ap.add_argument("--confirmo-entorno-de-ensayo", action="store_true", dest="ensayo")
    a = ap.parse_args()
    if not a.ensayo:
        print("guarda: falta --confirmo-entorno-de-ensayo (este script crea y elimina cuentas)"); return 2
    env = read_env(a.env)
    host, ip = a.resolve.split("=")
    if ip not in ("127.0.0.1", "::1") or not re.fullmatch(r"[A-Za-z0-9_-]+", a.project):
        print("guarda: --resolve debe apuntar a 127.0.0.1 y --project ser un nombre simple"); return 2
    orig = socket.getaddrinfo
    socket.getaddrinfo = lambda h, *x, **k: orig(ip if h == host else h, *x, **k)
    ctx = ssl.create_default_context(cafile=os.path.expanduser(a.ca))
    db = f"{a.project}-db-1"
    dbname = env.get("MSSQL_DATABASE", "TheBuryProjectDb")
    if not re.fullmatch(r"[A-Za-z0-9_]+", dbname):
        print("guarda: MSSQL_DATABASE invalido"); return 2
    if subprocess.run(["docker", "inspect", db], capture_output=True).returncode:
        print(f"guarda: no existe el contenedor {db}"); return 2

    def sql(q):
        cmd = ["docker", "exec", "-e", "Q", "-e", "P", db, "bash", "-c",
               f'/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$P" -d {dbname} -h -1 -W -s "|" -b -Q "SET NOCOUNT ON; $Q"']
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=60, env={**os.environ, "Q": q, "P": env["MSSQL_SA_PASSWORD"]})
        if p.returncode:
            raise RuntimeError("sqlcmd fallo")
        return [l.strip() for l in p.stdout.splitlines() if l.strip()]

    user_re = re.compile(r"^[A-Za-z0-9._@+-]{3,50}$")
    def uid(u):
        assert user_re.match(u); return sql(f"SELECT Id FROM AspNetUsers WHERE UserName='{u}'")[0]
    def exists(u):
        assert user_re.match(u); return bool(sql(f"SELECT 1 FROM AspNetUsers WHERE UserName='{u}'"))
    def active(u):
        assert user_re.match(u); return sql(f"SELECT Activo FROM AspNetUsers WHERE UserName='{u}'")[0] == "1"

    if int(sql("SELECT COUNT(*) FROM AspNetUsers")[0]) > 50:
        print("guarda: mas de 50 usuarios, no parece un entorno de ensayo"); return 2
    if int(sql("SELECT COUNT(*) FROM AspNetUsers WHERE UserName LIKE N'p05.%' AND Activo=1")[0]):
        print("guarda: hay cuentas p05.* ACTIVAS; desactivarlas antes"); return 2

    # oraculo: permisos (modulo, accion) por rol, desde la BD
    perms = {}
    for line in sql("SELECT r.Name, m.Clave, a.Clave FROM RolPermisos rp JOIN AspNetRoles r ON r.Id=rp.RoleId JOIN ModulosSistema m ON m.Id=rp.ModuloId "
                    "JOIN AccionesModulo a ON a.Id=rp.AccionId WHERE rp.IsDeleted=0"):
        n, m, ac = line.split("|"); perms.setdefault(n, set()).add((m.lower(), ac.lower()))

    results = []
    def check(i, desc, ok, ev=""):
        results.append(ok); print(f"[{'OK' if ok else 'FALLA'}] {i} {desc} :: {ev}")

    S = lambda n: Session(n, a.base, ctx)
    suf = secrets.token_hex(3)
    pw_admin = env["ADMIN_PASSWORD"]
    created = []
    def create_user(sess, user, pw, role):
        r = sess.action("/Usuarios/Create", {"UserName": user, "Email": f"{user}@ensayo.invalid", "Password": pw, "ConfirmPassword": pw, "Nombre": "Pruebas",
                                             "Apellido": "Ensayo", "RolesSeleccionados": role, "Activo": "true", "EmailConfirmed": "true"})
        ok = r[0] == 200 and re.search(r'"success"\s*:\s*true', r[2]) is not None
        if ok: created.append(user)
        return ok

    restored = False
    try:
        sA = S("admin"); okA = sA.login("admin", pw_admin)
        if okA and sA.terms_pending(): sA.accept_terms("Administrador Ensayo")
        if not (okA and sA.access("/Seguridad") == "OK"):
            print("no se pudo iniciar sesion con admin"); return 2

        # --- 1. sin sesion: todo redirige al login
        anon = S("anonimo")
        bad = [p for p, _, _ in ROUTES if classify(*anon.send("GET", p)[:2]) != "login"]
        check("1.1", f"sin sesion, las {len(ROUTES)} rutas protegidas redirigen al login", not bad, "excepciones: " + (", ".join(bad) or "ninguna"))

        # --- 2. admin (SuperAdmin) accede a todas
        bad = [p for p, _, _ in ROUTES if classify(*sA.send("GET", p)[:2]) != "allowed"]
        check("2.1", "SuperAdmin (admin) accede a todas las rutas", not bad, "sin acceso: " + (", ".join(bad) or "ninguna"))

        # --- 3. cada rol: lo que el servidor responde == lo que la BD dice
        sessions = {}
        for role in ROLES:
            if role not in perms:
                check(f"3.{role}", f"rol {role} existe en la BD", False, "no hay permisos cargados para ese rol"); continue
            user = f"p05.{role.lower()}{suf}"; pw = new_password()
            if not create_user(sA, user, pw, role):
                check(f"3.{role}", f"alta de la cuenta del rol {role}", False, "no se pudo crear"); continue
            s = S(role); okl = s.login(user, pw)
            if okl and s.terms_pending(): s.accept_terms(f"P05 {role}")
            sessions[role] = (s, user, pw)
            mism, nallow, ndeny = [], 0, 0
            for path, m, ac in ROUTES:
                got = classify(*s.send("GET", path)[:2])
                should = (m, ac) in perms[role]
                if should: nallow += 1
                else: ndeny += 1
                if (should and got != "allowed") or ((not should) and got != "denied"):
                    mism.append(f"{path}:{'permitido' if should else 'denegado'}!={got}")
            check(f"3.{role}", f"{role}: login y {nallow} rutas permitidas / {ndeny} denegadas coinciden con la BD", okl and not mism,
                  "discrepancias: " + (", ".join(mism) or "ninguna"))

        # --- 4. escalada: un rol sin permiso de usuarios no puede crear, cambiar clave ni desactivar a otro usuario
        victim, pw_v = f"p05.victima{suf}", new_password()
        check("4.0", "cuenta victima sintetica creada por admin", create_user(sA, victim, pw_v, "Vendedor") and exists(victim), "")
        for role in ("Vendedor", "Cajero", "Contador", "Repositor", "Tecnico", "Gerente"):
            if role not in sessions or ("usuarios", "create") in perms.get(role, set()):
                continue
            s = sessions[role][0]
            intr = f"p05.escala{role.lower()}{suf}"
            r1, t1 = post_with_token(s, "/Usuarios/Create", {"UserName": intr, "Email": f"{intr}@ensayo.invalid", "Password": "Aa1" + secrets.token_hex(8), "ConfirmPassword": "x",
                                               "RolesSeleccionados": "SuperAdmin", "Activo": "true", "EmailConfirmed": "true"})
            created.append(intr)
            r2, t2 = post_with_token(s, "/Usuarios/CambiarPassword", {"UserId": uid(victim), "UserName": victim, "NewPassword": "Zz9" + secrets.token_hex(8), "ConfirmPassword": "x"})
            r3, t3 = post_with_token(s, "/Usuarios/Desactivar", {"id": uid(victim), "returnUrl": "/Seguridad"})
            still = S("v").login(victim, pw_v)
            ok = (not exists(intr)) and active(victim) and still
            check(f"4.{role}", f"{role}: no crea usuarios, ni cambia la clave ni desactiva a otro", ok,
                  f"creado={exists(intr)}; victima_activa={active(victim)}; victima_clave_original={still}; http=({r1[0]},{r2[0]},{r3[0]}); con_token={t1 and t2 and t3}")

        # --- 6. endpoints sensibles: ni siquiera un SuperAdmin llega a diagnostico/swagger/metricas en Production (404), y el rol minimo no llega a mantenimiento
        for path in ("/Diagnostico", "/Diagnostico/ResetPassword", "/swagger", "/swagger/v1/swagger.json", "/metrics", "/hangfire", "/Identity/Account/Register"):
            st = sA.send("GET", path)[0]
            check("6.1", f"SuperAdmin: {path} no esta expuesto en Production (404)", st == 404, f"http={st}")
        if "Tecnico" in sessions:
            for path in ("/Diagnostico", "/Mantenimiento/ScoringGlobal"):
                st, loc, _ = sessions["Tecnico"][0].send("GET", path)
                check("6.2", f"Tecnico (rol minimo): {path} sin acceso", st in (403, 404) or classify(st, loc) == "denied", f"http={st} {loc[:60]}")

        # --- 5. antiforgery: POST sin token se rechaza
        r = sA.send("POST", "/Usuarios/Create", {"UserName": f"p05.csrf{suf}", "Email": f"p05.csrf{suf}@ensayo.invalid", "Password": "Aa1" + secrets.token_hex(8),
                                                  "ConfirmPassword": "x", "RolesSeleccionados": "Vendedor", "Activo": "true"})
        created.append(f"p05.csrf{suf}")
        check("5.1", "POST sin token antiforgery rechazado", r[0] in (400, 403) or (r[0] in (302, 303) and classify(r[0], r[1]) in ("login", "denied")) and not exists(f"p05.csrf{suf}"),
              f"http={r[0]}; creado={exists(f'p05.csrf{suf}')}")
    finally:
        try:
            sAd = S("admin-limpieza"); admin_ok = sAd.login("admin", pw_admin)
            if admin_ok:
                if sAd.terms_pending(): sAd.accept_terms("Administrador Ensayo")
                for u in [x for x in set(created) if exists(x)]:
                    sAd.action("/Usuarios/Desactivar", {"id": uid(u), "returnUrl": "/Seguridad"})
                    sAd.action("/Usuarios/Delete", {"id": uid(u), "motivo": "limpieza ensayo P-05", "returnUrl": "/Seguridad"})
            left = int(sql("SELECT COUNT(*) FROM AspNetUsers WHERE UserName LIKE N'p05.%' AND Activo=1")[0])
            restored = admin_ok and active("admin") and left == 0
            print(f"limpieza: admin activo={active('admin')} login_original={admin_ok}; cuentas p05.* activas={left}; verificada={restored}")
        except Exception as exc:
            print("AVISO limpieza incompleta:", type(exc).__name__)
    if not restored:
        results.append(False); print("[FALLA] limpieza de cuentas p05.* / admin no verificada")
    failed = results.count(False)
    print(f"P-05 roles (Production): {results.count(True)} OK, {failed} FALLA")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

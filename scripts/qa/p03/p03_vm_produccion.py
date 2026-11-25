#!/usr/bin/env python3
"""P-03 en modo PRODUCTION: ensayo de la transicion de cuentas contra el stack Docker real de un servidor de ENSAYO (VM descartable).

Equivale a scripts/qa/p03-ensayo-transicion-cuentas.ps1 (LocalDB/Development), que no puede correr en Production (exige login SQL).
Acciones por HTTP real (Identity, antiforgery, Usuarios/*) con sesiones independientes; verificacion por SELECT con sqlcmd dentro del
contenedor `db`. Sin dependencias externas. NO imprime contrasenas.

  python3 p03_vm_produccion.py --env ~/theburyproject1/.env --base https://<host-ensayo> --ca ~/theburyproject1/caddy-root-ca.crt \
        [--project theburyproject] [--resolve <host-ensayo>=127.0.0.1]

GUARDAS (todas obligatorias, el script aborta con 2 si alguna falla):
  * `--confirmo-entorno-de-ensayo` explicito: solo contra un servidor de ENSAYO descartable con datos sinteticos, NUNCA un ERP real;
  * HTTP y SQL son del MISMO stack: `--resolve` debe apuntar a esta maquina (127.0.0.1) y el contenedor `<proyecto>-db-1` debe existir;
  * nombre de base validado (`^[A-Za-z0-9_]+$`) antes de usarse; <= 50 usuarios; ninguna cuenta `p03.*` ACTIVA preexistente (las bajas son logicas y quedan como filas inactivas; cada corrida usa un sufijo unico).
Al terminar restaura a `admin` (activo, contrasena original, verificado) y da de baja las cuentas p03.* creadas; si la restauracion no se
puede verificar, sale con 1 aunque los pasos hayan pasado.
Salida: 0 todo OK | 1 hay FALLA o restauracion incompleta | 2 uso/guarda.
"""
import argparse, html, http.cookiejar, os, re, secrets, socket, ssl, string, subprocess, sys, urllib.error, urllib.parse, urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


def read_env(path):
    return dict(l.strip().split("=", 1) for l in open(os.path.expanduser(path)) if "=" in l and not l.lstrip().startswith("#"))


def new_password():
    sets = [string.ascii_uppercase, string.ascii_lowercase, string.digits]
    chars = [secrets.choice(s) for s in sets] + [secrets.choice(string.ascii_letters + string.digits) for _ in range(17)]
    secrets.SystemRandom().shuffle(chars)
    return "".join(chars)


class Session:
    def __init__(self, name, base, ctx):
        self.name, self.base = name, base
        self.jar = http.cookiejar.CookieJar()
        self.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar), urllib.request.HTTPSHandler(context=ctx), NoRedirect())

    def send(self, method, path, form=None):
        data = urllib.parse.urlencode(form).encode() if form is not None else None
        req = urllib.request.Request(self.base + path, data, {"Accept": "text/html,application/json"}, method=method)
        try:
            r = self.op.open(req, timeout=60)
            return r.status, r.headers.get("Location", ""), r.read().decode("utf8", "replace")
        except urllib.error.HTTPError as e:
            return e.code, e.headers.get("Location", ""), e.read().decode("utf8", "replace")

    def token(self, path="/Identity/Account/Login"):
        body = self.send("GET", path)[2]
        m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body) or re.search(r'name="csrf-token"\s+content="([^"]+)"', body)
        if not m:
            body = self.send("GET", "/Identity/Account/Login")[2]
            m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body)
        return m.group(1)

    def login(self, user, pw):
        t = self.token()
        s, loc, _ = self.send("POST", "/Identity/Account/Login", {"Input.UserName": user, "Input.Password": pw, "Input.RememberMe": "false", "__RequestVerificationToken": t})
        return any(c.name == ".AspNetCore.Identity.Application" for c in self.jar) and "Lockout" not in loc

    def terms_pending(self):
        s, loc, _ = self.send("GET", "/Seguridad")
        return s == 302 and "Identity/Account/Login" in loc

    def accept_terms(self, name):
        t = self.token()
        s, _, _ = self.send("POST", "/Identity/Account/Login", {"Input.NombreCompleto": name, "Input.AceptaTerminos": "true", "__RequestVerificationToken": t, "ReturnUrl": "/"})
        return s == 302

    def redirects_to_login(self, path="/Seguridad"):
        s, loc, _ = self.send("GET", path)
        return s in (302, 303) and "/Identity/Account/Login" in loc, f"{s} {loc}"

    def access(self, path="/Seguridad"):
        s, loc, _ = self.send("GET", path)
        return "OK" if s == 200 else f"SIN-ACCESO({s} {loc})"

    def action(self, path, fields):
        fields = dict(fields); fields["__RequestVerificationToken"] = self.token("/Seguridad")
        return self.send("POST", path, fields)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--env", required=True); ap.add_argument("--base", default=os.environ.get("BURY_ENSAYO_BASE", "https://tbp")); ap.add_argument("--ca", required=True)
    ap.add_argument("--project", default="theburyproject"); ap.add_argument("--resolve", default=os.environ.get("BURY_ENSAYO_RESOLVE", "tbp=127.0.0.1"))
    ap.add_argument("--confirmo-entorno-de-ensayo", action="store_true", dest="ensayo")
    a = ap.parse_args()
    if not a.ensayo:
        print("guarda: falta --confirmo-entorno-de-ensayo (este script crea, desactiva y elimina cuentas)"); return 2
    env = read_env(a.env)
    host, ip = a.resolve.split("=")
    if ip not in ("127.0.0.1", "::1") or not re.fullmatch(r"[A-Za-z0-9_-]+", a.project):
        print("guarda: --resolve debe apuntar a 127.0.0.1 (HTTP y SQL del mismo stack) y --project ser un nombre simple"); return 2
    orig = socket.getaddrinfo
    socket.getaddrinfo = lambda h, *x, **k: orig(ip if h == host else h, *x, **k)
    ctx = ssl.create_default_context(cafile=os.path.expanduser(a.ca))
    db = f"{a.project}-db-1"
    dbname = env.get("MSSQL_DATABASE", "TheBuryProjectDb")
    if not re.fullmatch(r"[A-Za-z0-9_]+", dbname):
        print("guarda: MSSQL_DATABASE invalido"); return 2
    if subprocess.run(["docker", "inspect", db], capture_output=True).returncode:
        print(f"guarda: no existe el contenedor {db} (HTTP y SQL deben ser del mismo stack)"); return 2

    def sql(q):
        # las variables viajan por el entorno del proceso (no por la linea de comandos: no aparecen en `ps`)
        cmd = ["docker", "exec", "-e", "Q", "-e", "P", db, "bash", "-c",
               f'/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$P" -d {dbname} -h -1 -W -b -Q "SET NOCOUNT ON; $Q"']
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=60, env={**os.environ, "Q": q, "P": env["MSSQL_SA_PASSWORD"]})
        if p.returncode:
            raise RuntimeError("sqlcmd fallo")
        return [l.strip() for l in p.stdout.splitlines() if l.strip()]

    user_re = re.compile(r"^[A-Za-z0-9._@+-]{3,50}$")
    def uid(u):
        assert user_re.match(u); return sql(f"SELECT Id FROM AspNetUsers WHERE UserName='{u}'")[0]
    def active(u):
        assert user_re.match(u); return sql(f"SELECT Activo FROM AspNetUsers WHERE UserName='{u}'")[0] == "1"
    def exists(u):
        assert user_re.match(u); return bool(sql(f"SELECT 1 FROM AspNetUsers WHERE UserName='{u}'"))
    def superadmins():
        return int(sql("SELECT COUNT(DISTINCT u.Id) FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId=u.Id JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name='SuperAdmin' AND u.Activo=1")[0])

    if int(sql("SELECT COUNT(*) FROM AspNetUsers")[0]) > 50:
        print("guarda: mas de 50 usuarios, no parece un entorno de ensayo"); return 2
    if int(sql("SELECT COUNT(*) FROM AspNetUsers WHERE UserName LIKE N'p03.%' AND Activo=1")[0]):
        print("guarda: hay cuentas p03.* ACTIVAS (no se pisan cuentas ajenas); desactivarlas antes"); return 2

    results = []
    def check(i, desc, ok, ev=""):
        results.append(ok); print(f"[{'OK' if ok else 'FALLA'}] {i} {desc} :: {ev}")

    S = lambda n: Session(n, a.base, ctx)
    pw_admin = env["ADMIN_PASSWORD"]; pw_owner, pw_rec, pw_admin_new, pw_admin_rec = (new_password() for _ in range(4))
    suf = secrets.token_hex(3); owner, rec, corta = f"p03.prop{suf}", f"p03.rec{suf}", f"p03.corta{suf}"
    def create_sa(s, user, pw, name):
        r = s.action("/Usuarios/Create", {"UserName": user, "Email": f"{user}@ensayo.invalid", "Password": pw, "ConfirmPassword": pw, "Nombre": name,
                                          "Apellido": "Ensayo", "RolesSeleccionados": "SuperAdmin", "Activo": "true", "EmailConfirmed": "true"})
        return r[0] == 200 and re.search(r'"success"\s*:\s*true', r[2]) is not None
    def change_pw(s, user, pw):
        r = s.action("/Usuarios/CambiarPassword", {"UserId": uid(user), "UserName": user, "NewPassword": pw, "ConfirmPassword": pw})
        return r[0] == 200 and re.search(r'"success"\s*:\s*true', r[2]) is not None

    print(f"entorno: stack={a.project} usuarios={sql('SELECT COUNT(*) FROM AspNetUsers')[0]} superadmin_activos={superadmins()}")
    try:
        sA = S("admin-previa"); okA = sA.login("admin", pw_admin)
        if okA and sA.terms_pending(): sA.accept_terms("Administrador Ensayo")
        check("3.pre", "Sesion previa de admin con acceso a Seguridad", okA and sA.access() == "OK", sA.access())
        if not okA: print("no se pudo iniciar sesion con admin"); return 2

        r = sA.action("/Usuarios/Create", {"UserName": corta, "Email": f"{corta}@ensayo.invalid", "Password": "Ab1", "ConfirmPassword": "Ab1", "RolesSeleccionados": "SuperAdmin", "Activo": "true", "EmailConfirmed": "true"})
        check("1.0", "Contrasena corta rechazada", not re.search(r'"success"\s*:\s*true', r[2]) and not exists(corta), "alta con 3 caracteres no creo usuario")
        check("1.1", "Crear SuperAdmin principal desde Seguridad->Usuarios", create_sa(sA, owner, pw_owner, "Dueno") and exists(owner), "POST /Usuarios/Create")
        sO = S("propietario"); okO = sO.login(owner, pw_owner); pend = sO.terms_pending()
        check("1.2", "Login independiente; Terminos pendientes antes de operar", okO and pend, f"login={okO}; terminosPendientes={pend}")
        acc = sO.accept_terms("Propietario Ensayo")
        check("1.3", "Aceptacion de Terminos y acceso a Seguridad", acc and sO.access() == "OK", f"aceptado={acc}")
        check("1.4", "SuperAdmin de recuperacion creado con la cuenta propia", create_sa(sO, rec, pw_rec, "Resguardo") and exists(rec), "")
        sR = S("recuperacion"); okR = sR.login(rec, pw_rec); acR = okR and sR.terms_pending() and sR.accept_terms("Recuperacion Ensayo")
        check("1.5", "Login independiente + Terminos de la recuperacion", okR and acR and sR.access() == "OK", f"login={okR}")

        check("2.1", "Rotar contrasena de admin con la cuenta propia", change_pw(sO, "admin", pw_admin_new), "POST /Usuarios/CambiarPassword")
        ok, det = sA.redirects_to_login()
        check("3.1", "Sesion previa de admin: siguiente request redirige al login tras rotar", ok, det)
        old = S("x").login("admin", pw_admin); new = S("y").login("admin", pw_admin_new)
        check("2.2", "Clave anterior ya no ingresa; la nueva si", (not old) and new, f"anterior={old}; nueva={new}")
        sA2 = S("admin-2"); sA2.login("admin", pw_admin_new)
        if sA2.terms_pending(): sA2.accept_terms("Administrador Ensayo")
        previo = sA2.access()
        sO.action("/Usuarios/Desactivar", {"id": uid("admin"), "returnUrl": "/Seguridad"})
        ok, det = sA2.redirects_to_login()
        check("3.2", "Desactivar admin: su sesion redirige al login", previo == "OK" and not active("admin") and ok, f"antes={previo}; despues={det}")
        check("3.3", "admin desactivado no puede iniciar sesion", not S("z").login("admin", pw_admin_new), "login rechazado")
        check("2.5", "Quedan >= 2 SuperAdmin activos", superadmins() >= 2, f"SuperAdmin activos={superadmins()}")

        sR.action("/Usuarios/Desactivar", {"id": uid(owner), "returnUrl": "/Seguridad"})
        check("4.1", "Con 2 SuperAdmin se puede desactivar uno", (not active(owner)) and superadmins() == 1, f"SuperAdmin activos={superadmins()}")
        ok, det = sO.redirects_to_login()
        check("3.4", "Propietario desactivado: su sesion redirige al login", ok, det)
        sR.action("/Usuarios/Desactivar", {"id": uid(rec), "returnUrl": "/Seguridad"})
        check("4.2", "Desactivar al ultimo SuperAdmin es rechazado", active(rec) and superadmins() == 1, f"activo={active(rec)}")
        sR.action("/Usuarios/Delete", {"id": uid(rec), "motivo": "ensayo P-03", "returnUrl": "/Seguridad"})
        check("4.3", "Eliminar al ultimo SuperAdmin es rechazado", exists(rec) and active(rec) and superadmins() == 1, f"existe={exists(rec)}")

        sR.action("/Usuarios/Reactivar", {"id": uid(owner), "returnUrl": "/Seguridad"})
        sO2 = S("propietario-2"); okO2 = sO2.login(owner, pw_owner)
        check("5.1", "Recuperacion reactiva al propietario y este vuelve a ingresar", active(owner) and okO2 and sO2.access() == "OK", f"login={okO2}")
        sR.action("/Usuarios/Reactivar", {"id": uid("admin"), "returnUrl": "/Seguridad"})
        rst = change_pw(sR, "admin", pw_admin_rec); okAdm = S("admin-rec").login("admin", pw_admin_rec)
        check("5.2", "Recuperacion reactiva admin y le resetea la contrasena", active("admin") and rst and okAdm, f"reset={rst}; login={okAdm}")
        # sesion de un usuario no administrador: degradacion de rol en la MISMA cookie se cubre en TransicionCuentasCookieHttpTests (CI)
    finally:
        restored = False
        try:
            sRs = S("restauro"); ok_rs = sRs.login(rec, pw_rec) if exists(rec) else False
            if ok_rs:
                if sRs.terms_pending(): sRs.accept_terms("Recuperacion Ensayo")
                if exists(owner) and not active(owner): sRs.action("/Usuarios/Reactivar", {"id": uid(owner), "returnUrl": "/Seguridad"})
                if exists("admin") and not active("admin"): sRs.action("/Usuarios/Reactivar", {"id": uid("admin"), "returnUrl": "/Seguridad"})
                change_pw(sRs, "admin", pw_admin)
                for u in (owner, corta):
                    if exists(u):
                        sRs.action("/Usuarios/Desactivar", {"id": uid(u), "returnUrl": "/Seguridad"})
                        sRs.action("/Usuarios/Delete", {"id": uid(u), "motivo": "limpieza ensayo P-03", "returnUrl": "/Seguridad"})
            sAd = S("admin-final"); admin_ok = sAd.login("admin", pw_admin)
            if admin_ok:
                if sAd.terms_pending(): sAd.accept_terms("Administrador Ensayo")
                if exists(rec):
                    sAd.action("/Usuarios/Desactivar", {"id": uid(rec), "returnUrl": "/Seguridad"})
                    sAd.action("/Usuarios/Delete", {"id": uid(rec), "motivo": "limpieza ensayo P-03", "returnUrl": "/Seguridad"})
            left = int(sql("SELECT COUNT(*) FROM AspNetUsers WHERE UserName LIKE N'p03.%' AND Activo=1")[0])
            restored = admin_ok and active("admin") and left == 0 and superadmins() >= 1
            print(f"restauracion: admin activo={active('admin')} login_original={admin_ok}; cuentas p03.* activas={left}; superadmin_activos={superadmins()}; verificada={restored}")
        except Exception as exc:  # nunca ocultar el resultado, pero tampoco declarar restauracion verificada
            print("AVISO restauracion incompleta:", type(exc).__name__)
    if not restored:
        results.append(False); print("[FALLA] restauracion final de admin / limpieza de cuentas no verificada")
    failed = results.count(False)
    print(f"P-03 Production: {results.count(True)} OK, {failed} FALLA")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

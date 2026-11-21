#!/usr/bin/env python3
"""P-03 COMPLEMENTO: escenarios que p03_vm_produccion.py y TransicionCuentasCookieHttpTests no cubren.

  * bloqueo automatico por intentos fallidos (Lockout 5 intentos) y desbloqueo por un SuperAdmin;
  * bloqueo manual indefinido: la sesion viva cae en el siguiente request y el login se rechaza; desbloqueo restablece el acceso;
  * bloquear al ULTIMO SuperAdmin activo es rechazado;
  * permisos minimos: un Vendedor no entra a Seguridad/Usuarios ni puede resetear claves ni bloquear, pero SI accede a su modulo;
  * restablecimiento de contrasena de una cuenta no administradora: la sesion previa cae, la clave vieja no ingresa, la nueva si;
  * en Production la contrasena de 9 caracteres se rechaza y la de 10 se acepta (minimo de la politica).

Mismas guardas que p03_vm_produccion.py (--confirmo-entorno-de-ensayo, HTTP+SQL del mismo stack, cuentas p03c.* con sufijo unico,
restauracion verificada de `admin`). No imprime contrasenas.

  python3 p03_complemento.py --env ~/theburyproject1/.env --base https://tbp --ca ~/theburyproject1/caddy-root-ca.crt \
        --confirmo-entorno-de-ensayo [--project theburyproject] [--resolve tbp=127.0.0.1]
Salida: 0 todo OK | 1 hay FALLA o restauracion incompleta | 2 uso/guarda.
"""
import argparse, os, re, secrets, socket, ssl, subprocess, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from p03_vm_produccion import Session, new_password, read_env  # noqa: E402

OK_JSON = re.compile(r'"success"\s*:\s*true')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--env", required=True); ap.add_argument("--base", default="https://tbp"); ap.add_argument("--ca", required=True)
    ap.add_argument("--project", default="theburyproject"); ap.add_argument("--resolve", default="tbp=127.0.0.1")
    ap.add_argument("--confirmo-entorno-de-ensayo", action="store_true", dest="ensayo")
    a = ap.parse_args()
    if not a.ensayo:
        print("guarda: falta --confirmo-entorno-de-ensayo"); return 2
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
               f'/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$P" -d {dbname} -h -1 -W -b -Q "SET NOCOUNT ON; $Q"']
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=60, env={**os.environ, "Q": q, "P": env["MSSQL_SA_PASSWORD"]})
        if p.returncode:
            raise RuntimeError("sqlcmd fallo")
        return [l.strip() for l in p.stdout.splitlines() if l.strip()]

    user_re = re.compile(r"^[A-Za-z0-9._@+-]{3,50}$")
    def uid(u):
        assert user_re.match(u); return sql(f"SELECT Id FROM AspNetUsers WHERE UserName='{u}'")[0]
    def one(q): return sql(q)[0]
    def exists(u): assert user_re.match(u); return bool(sql(f"SELECT 1 FROM AspNetUsers WHERE UserName='{u}'"))
    def active(u): assert user_re.match(u); return one(f"SELECT Activo FROM AspNetUsers WHERE UserName='{u}'") == "1"
    def locked(u):
        assert user_re.match(u)
        return one(f"SELECT CASE WHEN LockoutEnd IS NOT NULL AND LockoutEnd>SYSDATETIMEOFFSET() THEN 1 ELSE 0 END FROM AspNetUsers WHERE UserName='{u}'") == "1"
    def superadmins():
        return int(one("SELECT COUNT(DISTINCT u.Id) FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId=u.Id JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name='SuperAdmin' AND u.Activo=1"))

    if int(one("SELECT COUNT(*) FROM AspNetUsers")) > 80:
        print("guarda: demasiados usuarios, no parece un entorno de ensayo"); return 2
    if int(one("SELECT COUNT(*) FROM AspNetUsers WHERE UserName LIKE N'p03c.%' AND Activo=1")):
        print("guarda: hay cuentas p03c.* ACTIVAS; desactivarlas antes"); return 2
    roles = set(sql("SELECT Name FROM AspNetRoles"))
    if "Vendedor" not in roles:
        print("guarda: no existe el rol Vendedor"); return 2

    results = []
    def check(i, desc, ok, ev=""):
        results.append(bool(ok)); print(f"[{'OK' if ok else 'FALLA'}] {i} {desc} :: {ev}")

    S = lambda n: Session(n, a.base, ctx)
    pw_admin = env["ADMIN_PASSWORD"]
    suf = secrets.token_hex(3)
    sa1, sa2, vend, vend2 = f"p03c.sa1{suf}", f"p03c.sa2{suf}", f"p03c.vend{suf}", f"p03c.vtwo{suf}"
    pw_sa1, pw_sa2, pw_v, pw_v_new, pw_v2 = (new_password() for _ in range(5))

    def create(s, user, pw, rol, name="Ensayo"):
        r = s.action("/Usuarios/Create", {"UserName": user, "Email": f"{user}@ensayo.invalid", "Password": pw, "ConfirmPassword": pw, "Nombre": name,
                                          "Apellido": "Ensayo", "RolesSeleccionados": rol, "Activo": "true", "EmailConfirmed": "true"})
        return r[0] == 200 and OK_JSON.search(r[2]) is not None
    def change_pw(s, user, pw):
        r = s.action("/Usuarios/CambiarPassword", {"UserId": uid(user), "UserName": user, "NewPassword": pw, "ConfirmPassword": pw})
        return r[0] == 200 and OK_JSON.search(r[2]) is not None
    def bloquear(s, user, motivo="ensayo P-03 complemento"):
        r = s.action("/Usuarios/Bloquear", {"UserId": uid(user), "UserName": user, "MotivoBloqueo": motivo})
        return r[0] == 200 and OK_JSON.search(r[2]) is not None
    def desbloquear(s, user): return s.action("/Usuarios/Desbloquear", {"id": uid(user), "returnUrl": "/Seguridad"})
    def login_full(user, pw, name="Ensayo Ensayo"):
        s = S(user); ok = s.login(user, pw)
        if ok and s.terms_pending(): s.accept_terms(name)
        return s, ok

    print(f"entorno: stack={a.project} usuarios={one('SELECT COUNT(*) FROM AspNetUsers')} superadmin_activos={superadmins()}")
    try:
        sA, okA = login_full("admin", pw_admin, "Administrador Ensayo")
        check("0", "admin ingresa con acceso a Seguridad", okA and sA.access() == "OK")
        if not okA: return 2

        # --- politica de contrasena en Production -------------------------------------------------------------------------------
        nueve = f"p03c.nue{suf}"
        r = sA.action("/Usuarios/Create", {"UserName": nueve, "Email": f"{nueve}@ensayo.invalid", "Password": "Abcdefg12", "ConfirmPassword": "Abcdefg12",
                                           "Nombre": "Ensayo", "Apellido": "Ensayo", "RolesSeleccionados": "Vendedor", "Activo": "true", "EmailConfirmed": "true"})
        check("P.1", "Contrasena de 9 caracteres rechazada (minimo 10 en Production)", not OK_JSON.search(r[2]) and not exists(nueve), f"HTTP {r[0]}")
        check("P.2", "Contrasena de 10+ caracteres aceptada (cuenta Vendedor)", create(sA, vend, pw_v, "Vendedor") and exists(vend), "")

        # --- bloqueo automatico por intentos fallidos ---------------------------------------------------------------------------
        for _ in range(5): S("mal").login(vend, "Incorrecta1x")
        sL = S("lock"); entro = sL.login(vend, pw_v)
        check("B.1", "Tras 5 intentos fallidos la clave correcta tambien se rechaza (Lockout)", locked(vend) and not entro, f"bloqueado_en_BD={locked(vend)}; login={entro}")
        desbloquear(sA, vend)
        sV, okV = login_full(vend, pw_v)
        check("B.2", "SuperAdmin desbloquea y la cuenta vuelve a ingresar", (not locked(vend)) and okV, f"login={okV}")

        # --- bloqueo manual con sesion viva -------------------------------------------------------------------------------------
        previo = sV.send("GET", "/Venta")[0]
        check("B.3", "Control positivo: el Vendedor accede a su modulo (Venta)", previo == 200, f"GET /Venta={previo}")
        check("B.4", "SuperAdmin bloquea manualmente (indefinido)", bloquear(sA, vend) and locked(vend), "")
        ok, det = sV.redirects_to_login("/Venta")
        check("B.5", "Sesion viva del bloqueado cae en el siguiente request", ok, det)
        check("B.6", "El bloqueado no puede iniciar sesion", not S("b").login(vend, pw_v), "login rechazado")
        desbloquear(sA, vend)
        sV2, okV2 = login_full(vend, pw_v)
        check("B.7", "Desbloqueo manual restablece el acceso", okV2 and sV2.send("GET", "/Venta")[0] == 200, f"login={okV2}")

        # --- permisos minimos ---------------------------------------------------------------------------------------------------
        deny = lambda c: c in (302, 303, 401, 403, 404)
        def act(s, path, fields):
            f = dict(fields); f["__RequestVerificationToken"] = s.token("/Venta"); return s.send("POST", path, f)
        c_seg = sV2.send("GET", "/Seguridad")[0]; c_usr = sV2.send("GET", "/Usuarios")[0]
        check("M.1", "Vendedor sin acceso a Seguridad ni a Usuarios", deny(c_seg) and deny(c_usr), f"/Seguridad={c_seg}; /Usuarios={c_usr}")
        create(sA, vend2, pw_v2, "Vendedor")
        stamp0 = one(f"SELECT SecurityStamp FROM AspNetUsers WHERE UserName='{vend2}'")
        r1 = act(sV2, "/Usuarios/CambiarPassword", {"UserId": uid(vend2), "UserName": vend2, "NewPassword": "Hacker12345x", "ConfirmPassword": "Hacker12345x"})
        r2 = act(sV2, "/Usuarios/Bloquear", {"UserId": uid(vend2), "UserName": vend2, "MotivoBloqueo": "intento"})
        r3 = act(sV2, "/Usuarios/Desactivar", {"id": uid(vend2), "returnUrl": "/Seguridad"})
        intacto = (one(f"SELECT SecurityStamp FROM AspNetUsers WHERE UserName='{vend2}'") == stamp0 and not locked(vend2) and active(vend2))
        check("M.2", "Vendedor no puede resetear clave, bloquear ni desactivar a otro", intacto and not OK_JSON.search(r1[2]) and not OK_JSON.search(r2[2]),
              f"cambiarPw={r1[0]}; bloquear={r2[0]}; desactivar={r3[0]}; cuenta intacta={intacto}")
        r4 = act(sV2, "/Usuarios/Create", {"UserName": f"p03c.esc{suf}", "Email": "e@ensayo.invalid", "Password": "Escalada12345", "ConfirmPassword": "Escalada12345",
                                             "Nombre": "E", "Apellido": "E", "RolesSeleccionados": "SuperAdmin", "Activo": "true", "EmailConfirmed": "true"})
        check("M.3", "Vendedor no puede crear un SuperAdmin (escalada)", not exists(f"p03c.esc{suf}"), f"HTTP {r4[0]}")

        # --- restablecimiento de clave de una cuenta no administradora ----------------------------------------------------------
        sV3, _ = login_full(vend, pw_v)
        antes = sV3.send("GET", "/Venta")[0]
        check("R.1", "SuperAdmin restablece la clave del Vendedor", change_pw(sA, vend, pw_v_new), "")
        ok, det = sV3.redirects_to_login("/Venta")
        check("R.2", "Sesion previa del Vendedor cae tras el reseteo", antes == 200 and ok, f"antes={antes}; despues={det}")
        check("R.3", "Clave vieja no ingresa; la nueva si", (not S("o").login(vend, pw_v)) and S("n").login(vend, pw_v_new), "")

        # --- dos SuperAdmin recuperables + bloqueo del ultimo -------------------------------------------------------------------
        check("S.1", "Alta de dos SuperAdmin con la cuenta admin", create(sA, sa1, pw_sa1, "SuperAdmin") and create(sA, sa2, pw_sa2, "SuperAdmin"), "")
        s1, ok1 = login_full(sa1, pw_sa1); s2, ok2 = login_full(sa2, pw_sa2)
        check("S.2", "Ambos ingresan y acceden a Seguridad", ok1 and ok2 and s1.access() == "OK" and s2.access() == "OK", "")
        activos_antes = superadmins()
        # dejar a sa2 como unico SuperAdmin activo: baja de admin y sa1 (otros SuperAdmin preexistentes inactivos por el ensayo)
        s2.action("/Usuarios/Desactivar", {"id": uid("admin"), "returnUrl": "/Seguridad"})
        s2.action("/Usuarios/Desactivar", {"id": uid(sa1), "returnUrl": "/Seguridad"})
        solo = superadmins()
        check("S.3", "Un solo SuperAdmin activo (precondicion)", solo == 1 and active(sa2), f"activos {activos_antes} -> {solo}")
        s2.action("/Usuarios/Bloquear", {"UserId": uid(sa2), "UserName": sa2, "MotivoBloqueo": "ensayo ultimo SA"})
        check("S.4", "Bloquear al ultimo SuperAdmin es rechazado", not locked(sa2) and s2.access() == "OK", f"bloqueado={locked(sa2)}")
        s2.action("/Usuarios/Reactivar", {"id": uid(sa1), "returnUrl": "/Seguridad"})
        s2.action("/Usuarios/Reactivar", {"id": uid("admin"), "returnUrl": "/Seguridad"})
        check("S.5", "Recuperacion: sa2 reactiva a sa1 y a admin", active(sa1) and active("admin") and superadmins() >= 3, f"activos={superadmins()}")
    finally:
        restored = False
        try:
            sAd = S("final"); ok_ad = sAd.login("admin", pw_admin)
            if not ok_ad and exists(sa2):  # admin pudo quedar desactivado: reactivarlo con la cuenta de recuperacion
                sx, okx = login_full(sa2, pw_sa2)
                if okx:
                    sx.action("/Usuarios/Reactivar", {"id": uid("admin"), "returnUrl": "/Seguridad"})
                    sAd = S("final2"); ok_ad = sAd.login("admin", pw_admin)
            if ok_ad:
                if sAd.terms_pending(): sAd.accept_terms("Administrador Ensayo")
                for u in (sa1, sa2, vend, vend2, f"p03c.nue{suf}", f"p03c.esc{suf}"):
                    if exists(u):
                        if locked(u): desbloquear(sAd, u)
                        sAd.action("/Usuarios/Desactivar", {"id": uid(u), "returnUrl": "/Seguridad"})
                        sAd.action("/Usuarios/Delete", {"id": uid(u), "motivo": "limpieza ensayo P-03 complemento", "returnUrl": "/Seguridad"})
            left = int(one("SELECT COUNT(*) FROM AspNetUsers WHERE UserName LIKE N'p03c.%' AND Activo=1"))
            restored = ok_ad and active("admin") and left == 0 and superadmins() >= 1
            print(f"restauracion: admin activo={active('admin')} login_original={ok_ad}; cuentas p03c.* activas={left}; superadmin_activos={superadmins()}; verificada={restored}")
        except Exception as exc:
            print("AVISO restauracion incompleta:", type(exc).__name__)
    if not restored:
        results.append(False); print("[FALLA] restauracion final no verificada")
    failed = results.count(False)
    print(f"P-03 complemento: {results.count(True)} OK, {failed} FALLA")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

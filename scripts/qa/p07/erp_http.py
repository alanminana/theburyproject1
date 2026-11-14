"""Cliente HTTP minimo (solo stdlib) que se comporta como un navegador contra el ERP:
cookies, token antifalsificacion y envio de formularios reales (se parsean los inputs de la pagina
y solo se sobrescriben los campos que el recorrido quiere cambiar)."""
import http.cookiejar, re, urllib.request, urllib.parse, urllib.error, html
from html.parser import HTMLParser


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


class Resp:
    def __init__(self, status, headers, body, url):
        self.status, self.headers, self.body, self.url = status, headers, body, url

    @property
    def text(self):
        return self.body.decode("utf-8", "replace")

    @property
    def location(self):
        return self.headers.get("Location", "")


class _FormParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.forms, self.cur, self.sel, self.ta = [], None, None, None

    def handle_starttag(self, tag, a):
        a = {k: (v if v is not None else "") for k, v in a}
        if tag == "form":
            self.cur = {"attrs": a, "fields": []}
            self.forms.append(self.cur)
        elif self.cur is None:
            return
        elif tag == "input":
            t = a.get("type", "text").lower()
            n = a.get("name")
            if not n or t in ("submit", "button", "image", "reset", "file") or "disabled" in a:
                return
            if t in ("checkbox", "radio") and "checked" not in a:
                return
            self.cur["fields"].append([n, html.unescape(a.get("value", "on" if t == "checkbox" else ""))])
        elif tag == "select":
            self.sel = {"name": a.get("name"), "val": None, "first": None, "disabled": "disabled" in a}
        elif tag == "option" and self.sel is not None:
            self._close_option()
            self.opt = {"value": html.unescape(a["value"]) if "value" in a else None, "selected": "selected" in a, "text": []}
        elif tag == "textarea":
            self.ta = {"name": a.get("name"), "buf": []}

    def _close_option(self):
        o = getattr(self, "opt", None)
        if o is not None and self.sel is not None:
            v = o["value"] if o["value"] is not None else html.unescape("".join(o["text"])).strip()
            if self.sel["first"] is None:
                self.sel["first"] = v
            if o["selected"]:
                self.sel["val"] = v
        self.opt = None

    def handle_data(self, d):
        if getattr(self, "opt", None) is not None:
            self.opt["text"].append(d)
        if self.ta is not None:
            self.ta["buf"].append(d)

    def handle_endtag(self, tag):
        if tag == "option":
            self._close_option()
        if tag == "select":
            self._close_option()
        if tag == "form":
            self.cur = None
        elif tag == "select" and self.sel is not None and self.cur is not None:
            if self.sel["name"] and not self.sel["disabled"]:
                v = self.sel["val"] if self.sel["val"] is not None else (self.sel["first"] or "")
                self.cur["fields"].append([self.sel["name"], v])
            self.sel = None
        elif tag == "textarea" and self.ta is not None and self.cur is not None:
            if self.ta["name"]:
                self.cur["fields"].append([self.ta["name"], "".join(self.ta["buf"]).lstrip("\n")])
            self.ta = None


class Erp:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.jar = http.cookiejar.CookieJar()
        self.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar), _NoRedirect)

    def _do(self, req):
        try:
            r = self.op.open(req, timeout=120)
        except urllib.error.HTTPError as e:
            r = e
        return Resp(r.status if hasattr(r, "status") else r.code, r.headers, r.read(), r.geturl())

    def get(self, path, follow=True, headers=None):
        url = path if path.startswith("http") else self.base + path
        for _ in range(8):
            r = self._do(urllib.request.Request(url, headers=headers or {}))
            if follow and r.status in (301, 302, 303, 307) and r.location:
                url = urllib.parse.urljoin(url, r.location)
                continue
            return r
        return r

    def post(self, path, fields, follow=True, headers=None):
        url = path if path.startswith("http") else self.base + path
        data = urllib.parse.urlencode([tuple(x) for x in (fields if isinstance(fields, list) else fields.items())]).encode()
        h = {"Content-Type": "application/x-www-form-urlencoded", "Referer": self.base + "/"}
        h.update(headers or {})
        r = self._do(urllib.request.Request(url, data=data, headers=h, method="POST"))
        if follow and r.status in (301, 302, 303, 307) and r.location:
            return self.get(urllib.parse.urljoin(url, r.location))
        return r

    def post_json(self, path, obj, token_page="/Cotizacion"):
        import json as _j
        tok = self.token(self.get(token_page).text)
        h = {"Content-Type": "application/json", "RequestVerificationToken": tok or "", "Referer": self.base + "/"}
        r = self._do(urllib.request.Request(self.base + path, data=_j.dumps(obj).encode(), headers=h, method="POST"))
        try:
            r.json = _j.loads(r.text) if r.body else None
        except ValueError:
            r.json = None
        return r

    def post_multipart(self, path, fields, files, referer=None):
        import uuid
        b = "----p07" + uuid.uuid4().hex
        crlf = b"\r\n"
        parts = []
        for k, v in fields:
            parts.append(b"--" + b.encode() + crlf + ('Content-Disposition: form-data; name="%s"' % k).encode() + crlf + crlf + str(v).encode() + crlf)
        for k, (fn, ctype, data) in files.items():
            parts.append(b"--" + b.encode() + crlf + ('Content-Disposition: form-data; name="%s"; filename="%s"' % (k, fn)).encode() + crlf
                         + ("Content-Type: %s" % ctype).encode() + crlf + crlf + data + crlf)
        parts.append(b"--" + b.encode() + b"--" + crlf)
        h = {"Content-Type": "multipart/form-data; boundary=" + b, "Referer": self.base + (referer or "/")}
        return self._do(urllib.request.Request(self.base + path, data=b"".join(parts), headers=h, method="POST"))

    def forms(self, page_html):
        p = _FormParser()
        p.feed(page_html)
        return p.forms

    def token(self, page_html):
        m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page_html) or \
            re.search(r'value="([^"]+)"[^>]*name="__RequestVerificationToken"', page_html)
        return m.group(1) if m else None

    def submit(self, page_url, overrides=None, form_index=None, action_contains=None, extra=None, follow=True, fields_marker=None, post_to=None):
        """GET de la pagina, toma el formulario (por indice o fragmento de action) y lo envia con overrides."""
        pg = self.get(page_url)
        fs = self.forms(pg.text)
        if action_contains is not None:
            fs = [f for f in fs if action_contains in f["attrs"].get("action", "")]
        if fields_marker is not None:
            fs = [f for f in fs if any(x[0] == fields_marker for x in f["fields"])]
        f = fs[form_index or 0]
        fields = [list(x) for x in f["fields"]]
        for k, v in (overrides or {}).items():
            fields = [x for x in fields if x[0] != k]
            if isinstance(v, (list, tuple)):
                fields += [[k, i] for i in v]
            else:
                fields.append([k, v])
        for k, v in (extra or []):
            fields.append([k, v])
        action = post_to or f["attrs"].get("action") or page_url
        return self.post(urllib.parse.urljoin(self.base + page_url, action), fields, follow=follow), pg

    def login(self, user, pwd):
        """Login en dos fases (credenciales y, la primera vez, Terminos y Condiciones). Devuelve la pagina final."""
        pg = self.get("/Identity/Account/Login")
        f = self.forms(pg.text)[0]
        fields = [x for x in f["fields"] if x[0] not in ("Input.UserName", "Input.Password")]
        fields += [["Input.UserName", user], ["Input.Password", pwd]]
        r = self.post(urllib.parse.urljoin(pg.url, f["attrs"].get("action") or pg.url), fields, follow=True)
        if "Input.NombreCompleto" in r.text:
            f = self.forms(r.text)[0]
            fields = [x for x in f["fields"] if x[0] not in ("Input.NombreCompleto", "Input.AceptaTerminos")]
            fields += [["Input.NombreCompleto", "Usuario de prueba P07"], ["Input.AceptaTerminos", "true"]]
            r = self.post(urllib.parse.urljoin(r.url, f["attrs"].get("action") or r.url), fields, follow=True)
        return r


def msgs(page_html):
    """Mensajes visibles de alerta (TempData / validacion): bloques role="alert" no ocultos y errores de campo."""
    out = []
    for m in re.finditer(r'<(div|span|p)([^>]*role="alert"[^>]*)>(.*?)</\1>', page_html, re.S):
        if re.search(r'class="[^"]*\bhidden\b', m.group(2)):
            continue
        t = re.sub(r"<[^>]+>", " ", m.group(3))
        t = re.sub(r"\s+", " ", html.unescape(t)).strip()
        t = re.sub(r"^(check_circle|error|warning|info|cancel)\s+", "", t)
        if t:
            out.append(t)
    for m in re.finditer(r'class="[^"]*field-validation-error[^"]*"[^>]*>(.*?)</span>', page_html, re.S):
        t = re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", m.group(1)))).strip()
        if t:
            out.append(t)
    return out

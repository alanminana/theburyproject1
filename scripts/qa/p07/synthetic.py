"""Datos sinteticos y utilidades de validacion usadas por los recorridos P-07."""

def cuit_digit(base10):
    w = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2]
    d = 11 - sum(int(a) * b for a, b in zip(base10, w)) % 11
    return {11: 0, 10: 9}.get(d, d)

def cuit(base10):
    return base10 + str(cuit_digit(base10))

def pdf(texto="P07 documento sintetico"):
    """PDF minimo valido (estructura completa) para las cargas de documentos de cliente."""
    objs = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
        None,
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ]
    stream = ("BT /F1 12 Tf 10 50 Td (%s) Tj ET" % texto).encode()
    objs[3] = b"<< /Length %d >>\nstream\n" % len(stream) + stream + b"\nendstream"
    out = b"%PDF-1.4\n"; offs = []
    for i, o in enumerate(objs, 1):
        offs.append(len(out)); out += b"%d 0 obj\n" % i + o + b"\nendobj\n"
    x = len(out)
    out += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objs) + 1)
    for o in offs: out += b"%010d 00000 n \n" % o
    out += b"trailer\n<< /Size %d /Root 1 0 R >>\nstartxref\n%d\n%%%%EOF\n" % (len(objs) + 1, x)
    return out

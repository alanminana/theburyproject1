// Descargas legitimas en el recorrido de roles (crawl.js).
//
// Algunas pantallas "Ver"/"Imprimir" responden con un archivo adjunto (p. ej. /ContratoVentaCredito/Ver devuelve un PDF con
// Content-Disposition: attachment). Para el navegador eso no es una navegacion: page.goto lanza "Download is starting". No es una
// falla de la aplicacion ni de la CSP, pero tampoco se puede aceptar a ciegas: una descarga solo vale si
//   1) la respuesta es 200 (sin redireccion a Login/AccesoDenegado, que seria un rol sin permiso),
//   2) es realmente un adjunto (Content-Disposition: attachment, o un tipo de archivo conocido),
//   3) el archivo no esta vacio y su firma coincide con el tipo declarado (%PDF-, PK.. para xlsx/zip).
// Los permisos se validan con la misma sesion del rol: un rol sin permiso no recibe un 200 con archivo, y el caso queda como error.
'use strict';

const FIRMAS = [
  { tipo: /pdf/i, ext: /\.pdf$/i, firma: Buffer.from('%PDF-'), nombre: 'PDF' },
  { tipo: /spreadsheetml|zip|officedocument/i, ext: /\.(xlsx|docx|zip)$/i, firma: Buffer.from([0x50, 0x4b]), nombre: 'OOXML/ZIP' },
];

/** true si el error de page.goto corresponde a una descarga iniciada por el servidor (no a una falla de red). */
function esInicioDeDescarga(err) {
  return /download is starting/i.test(String((err && err.message) || err || ''));
}

/**
 * Valida una descarga pidiendo la misma ruta con la sesion (cookies) del rol.
 * @param {import('playwright').APIRequestContext} request  contexto de peticiones de la sesion del rol (page.request / ctx.request)
 * @param {string} url  URL absoluta
 * @returns {Promise<{ok: boolean, detalle: string, status: number, tipo: string, bytes: number, archivo: string}>}
 */
async function validarDescarga(request, url) {
  const ruta = new URL(url).pathname + new URL(url).search;
  let resp;
  try {
    resp = await request.get(url, { maxRedirects: 0, timeout: 30000 });
  } catch (e) {
    return { ok: false, detalle: `${ruta}: no se pudo pedir el archivo (${String(e.message).slice(0, 80)})`, status: 0, tipo: '', bytes: 0, archivo: '' };
  }
  const status = resp.status();
  const h = resp.headers();
  const tipo = (h['content-type'] || '').split(';')[0].trim();
  const disp = h['content-disposition'] || '';
  const m = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disp);
  const archivo = m ? decodeURIComponent(m[1]) : '';
  const base = { status, tipo, archivo };
  if (status !== 200) return { ...base, ok: false, bytes: 0, detalle: `${ruta}: la descarga respondio ${status} (¿rol sin permiso o sesion invalida?)` };
  const cuerpo = await resp.body();
  const bytes = cuerpo.length;
  if (!/attachment/i.test(disp) && !FIRMAS.some((f) => f.tipo.test(tipo))) {
    return { ...base, ok: false, bytes, detalle: `${ruta}: no es un adjunto (Content-Type=${tipo || '?'}, sin Content-Disposition: attachment)` };
  }
  if (bytes === 0) return { ...base, ok: false, bytes, detalle: `${ruta}: archivo vacio` };
  const esperada = FIRMAS.find((f) => f.tipo.test(tipo) || f.ext.test(archivo));
  if (esperada && !cuerpo.subarray(0, esperada.firma.length).equals(esperada.firma)) {
    return { ...base, ok: false, bytes, detalle: `${ruta}: el archivo no es un ${esperada.nombre} valido (firma incorrecta)` };
  }
  return { ...base, ok: true, bytes, detalle: `${ruta}: ${archivo || tipo} ${bytes} bytes` };
}

module.exports = { esInicioDeDescarga, validarDescarga };

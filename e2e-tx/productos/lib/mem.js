// @ts-check
// Memoria compartida entre tests (persistida en archivo): sobrevive al reinicio de worker tras un test fallido.
const fs = require('fs');
const path = require('path');
const FILE = path.join(__dirname, '..', 'evidence', 'mem.json');
const read = () => { try { return JSON.parse(fs.readFileSync(FILE, 'utf8')); } catch { return {}; } };
const mem = new Proxy({}, {
    get: (_, k) => read()[String(k)],
    set: (_, k, v) => { const m = read(); m[String(k)] = v; fs.writeFileSync(FILE, JSON.stringify(m)); return true; },
});
const reset = () => { try { fs.unlinkSync(FILE); } catch {} };
module.exports = { mem, reset };

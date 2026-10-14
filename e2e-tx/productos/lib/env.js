// @ts-check
const path = require('path');
const fs = require('fs');

const AUTHORIZED_URL = 'http://127.0.0.1:18092';
const EVIDENCE_DIR = path.join(__dirname, '..', 'evidence');
const AUTH_DIR = path.join(__dirname, '..', '.auth');
const STATE_FILE = path.join(EVIDENCE_DIR, 'state.json');

fs.mkdirSync(EVIDENCE_DIR, { recursive: true });
fs.mkdirSync(AUTH_DIR, { recursive: true });

/** Aborta si el destino no es el stack QA autorizado. */
function assertAuthorized() {
    const base = process.env.E2E_BASE_URL || AUTHORIZED_URL;
    if (base !== AUTHORIZED_URL) {
        throw new Error(`[QA-TX] URL no autorizada: ${base}. Solo ${AUTHORIZED_URL} (bury-qa-prd).`);
    }
}

function readState() {
    try { return JSON.parse(fs.readFileSync(STATE_FILE, 'utf8')); } catch { return {}; }
}
function writeState(s) { fs.writeFileSync(STATE_FILE, JSON.stringify(s, null, 2)); }

module.exports = { AUTHORIZED_URL, EVIDENCE_DIR, AUTH_DIR, STATE_FILE, assertAuthorized, readState, writeState };

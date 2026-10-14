// @ts-check
/**
 * Resultados ESPERADOS, derivados de las reglas documentadas (no del output del sistema).
 *
 * Fuentes:
 *  - Plan de cuotas: Services/FinancialCalculationService.SimularPlanCredito
 *      interesTotal   = round2(montoFinanciado * recargo% / 100)         (recargo TOTAL, no francés)
 *      totalFinanciado= montoFinanciado + interesTotal
 *      Total e Interés se reparten en centavos enteros; el RESIDUO cae en la ÚLTIMA cuota;
 *      Capital_i = Total_i - Interes_i.
 *  - Punitorio: Services/PunitorioCalculator
 *      por tramo: saldoBase * porcentaje/100 * díasTramo / periodoDías, sin redondear por tramo,
 *      suma de tramos redondeada UNA vez; devenga solo si los días globales desde el vencimiento
 *      superan la gracia de la configuración vigente en ese tramo.
 *  - Mora de alertas: MoraService.CalcularMora = monto * (TasaMoraBase/100/30) * días, round2.
 */

const r2 = n => Math.round((n + Number.EPSILON) * 100) / 100;
/** Redondeo "away from zero" a centavos enteros (MidpointRounding.AwayFromZero). */
const aCent = n => Math.sign(n) * Math.round(Math.abs(n) * 100 + 1e-9);

/** @returns {number[]} importes (en pesos) */
function distribuir(importe, n) {
    const c = aCent(importe);
    const base = Math.trunc(c / n);
    const res = c % n;
    const v = Array(n).fill(base);
    v[n - 1] = base + res;
    return v.map(x => x / 100);
}

/** Plan de recargo total. */
function planCuotas(monto, recargoPct, n) {
    const interesTotal = aCent(monto * recargoPct / 100) / 100;
    const total = r2(monto + interesTotal);
    const totales = distribuir(total, n);
    const intereses = distribuir(interesTotal, n);
    const capitales = totales.map((t, i) => r2(t - intereses[i]));
    return { monto, interesTotal, total, totales, intereses, capitales };
}

/** Fecha mensual (mismo día, recortado a fin de mes) como hace un calendario comercial. */
function fechaMensual(base, k) {
    const d = new Date(Date.UTC(base.getUTCFullYear(), base.getUTCMonth() + k, 1));
    const ultimo = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 0)).getUTCDate();
    d.setUTCDate(Math.min(base.getUTCDate(), ultimo));
    return d.toISOString().slice(0, 10);
}

/**
 * Punitorio de un único tramo (config constante).
 * @param {{saldo:number,dias:number,pct:number,periodo:number,gracia:number}} p
 */
function punitorioTramo({ saldo, dias, pct, periodo, gracia }) {
    if (dias <= gracia) return 0;
    return r2(saldo * pct / 100 * dias / periodo);
}

/**
 * Punitorio con varios tramos de configuración (vigencias distintas).
 * @param {number} saldo
 * @param {number} diasTotales días globales desde el vencimiento
 * @param {{dias:number,pct:number,periodo:number,gracia:number}[]} tramos
 */
function punitorioTramos(saldo, diasTotales, tramos) {
    let exacto = 0;
    for (const t of tramos) if (diasTotales > t.gracia) exacto += saldo * t.pct / 100 * t.dias / t.periodo;
    return r2(exacto);
}

const moraAlerta = (monto, dias, tasaBase) => (dias <= 0 || monto <= 0 ? 0 : r2(monto * (tasaBase / 100 / 30) * dias));

const iso = d => d.toISOString().slice(0, 10);
const hoyIso = () => {
    // fecha comercial Argentina (UTC-3), sin tocar el reloj del SO
    const d = new Date(Date.now() - 3 * 3600 * 1000);
    return iso(d);
};
const sumarDias = (isoDate, n) => { const d = new Date(isoDate + 'T00:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return iso(d); };

module.exports = { r2, aCent, distribuir, planCuotas, fechaMensual, punitorioTramo, punitorioTramos, moraAlerta, hoyIso, sumarDias };

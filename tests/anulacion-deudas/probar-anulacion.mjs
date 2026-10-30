// Pruebas de regresión de la anulación de deudas, de punta a punta contra
// una API LOCAL (base mercadex_pruebas). Crea sus propias casetas y deudas
// (bloque Z…), así que se puede repetir sin gastar los datos de prueba.
//
//   node probar-anulacion.mjs
//
// Termina con código 1 si alguna prueba falla.

import { api, loginAdmin, CLAVE_APP, ErrorApi } from './comun.mjs';

const resultados = [];

async function prueba(nombre, fn) {
  try {
    await fn();
    resultados.push({ nombre, ok: true });
    console.log(`  ✔ ${nombre}`);
  } catch (e) {
    resultados.push({ nombre, ok: false, error: e });
    console.log(`  ✘ ${nombre}\n      ${e.message}`);
  }
}

function igual(real, esperado, que) {
  if (JSON.stringify(real) !== JSON.stringify(esperado))
    throw new Error(`${que}: se esperaba ${JSON.stringify(esperado)} y llegó ${JSON.stringify(real)}`);
}

function verdad(cond, que) {
  if (!cond) throw new Error(que);
}

async function estado(promesa) {
  try {
    await promesa;
    return 200;
  } catch (e) {
    if (e instanceof ErrorApi) return e.estado;
    throw e;
  }
}

const admin = await loginAdmin();
const A = (metodo, ruta, cuerpo) => api(metodo, ruta, { cuerpo, token: admin });
const PANEL = '/api/panel/obligaciones-anulaciones';
const ESCRITORIO = '/api/obligaciones-anulaciones';
const P = (metodo, ruta, cuerpo, token = admin) => api(metodo, PANEL + ruta, { cuerpo, token, sinClave: true });

const marca = Date.now().toString(36).toUpperCase().slice(-5);
const bloque = 'Z' + marca;
const nuevaClave = () => crypto.randomUUID();
const MOTIVO = 'Prueba automática de anulación de deudas';

console.log(`Preparando datos propios (bloque ${bloque})…`);

const monedas = await A('GET', '/api/monedas');
const bob = monedas.find(m => m.codigo === 'BOB');
await A('POST', '/api/ingresos/catalogos/conceptos', { codigo: 'P' + marca, nombre: 'Prueba alquiler ' + marca });
const concepto = (await A('GET', '/api/conceptos-cobro')).find(c => c.codigo === 'P' + marca).id;

for (let i = 1; i <= 8; i++)
  await A('POST', '/api/unidades-comerciales', { codigo: `Z${i}-${marca}`, tipo: 1, pasilloId: null, bloque });

const unidades = (await A('GET', '/api/unidades-comerciales')).filter(u => u.bloque === bloque);
const unidad = n => unidades.find(u => u.codigo === `Z${n}-${marca}`).id;

const cargoMes = mes => A('POST', '/api/cobranza/cargo-masivo', {
  conceptoCobroId: concepto, anio: 2026, mes, alcance: 'bloque', bloque, modoMonto: 'fijo', monto: mes === 11 ? 100 : 50,
  monedaId: bob.id, fechaEmision: `2026-${mes}-01`, fechaVencimiento: `2026-${mes}-28`
});
await cargoMes(11);
await cargoMes(12);
// Un cargo eventual (sin período) por caseta, para las pruebas de concurrencia.
for (let n = 1; n <= 8; n++) {
  await A('POST', '/api/cobranza/cargo', {
    unidadComercialId: unidad(n), conceptoCobroId: concepto, monedaId: bob.id, monto: 30,
    fechaEmision: '2026-10-20', fechaVencimiento: null, anio: null, mes: null
  });
}

const deudasDe = async (n, extra = '') =>
  (await P('GET', `/deudas?caseta=Z${n}-${marca}&conceptoCobroId=${concepto}${extra}`)).deudas;
const deuda = async (n, mes) => (await deudasDe(n)).find(d => (mes ? d.mes === mes : d.mes === null));

// Socio titular de Z1 y Z2, con acceso a la app.
const ci = String(9000000 + Math.floor(Math.random() * 999999));
const socio = await A('POST', '/api/socios', { nombres: 'Socio', apellidos: 'Prueba ' + marca, documentoIdentidad: ci, telefono: null });
await A('POST', '/api/asignaciones-socio-caseta', { socioId: socio.id, unidadComercialId: unidad(1) });
await A('POST', '/api/asignaciones-socio-caseta', { socioId: socio.id, unidadComercialId: unidad(2) });
const acceso = await api('POST', `/api/panel/cuentas/${socio.id}/codigo`, { token: admin, sinClave: true });
const app = (await api('POST', '/api/app/activar', {
  sinClave: true, cuerpo: { documento: ci, codigo: acceso.codigo, contrasena: 'SocioPrueba-2026' }
})).token;
const deudasApp = async () =>
  (await api('GET', '/api/app/deudas', { token: app, sinClave: true })).flatMap(u => u.deudas);

// Usuario cajero (no puede anular).
const cajeroNombre = 'cajero' + marca.toLowerCase();
const cajeroNuevo = await A('POST', '/api/usuarios', { nombreUsuario: cajeroNombre, nombreCompleto: 'Cajera Prueba', rol: 'Cajero' });
let cajero = (await api('POST', '/api/auth/login', { cuerpo: { usuario: cajeroNombre, password: cajeroNuevo.contrasenaTemporal } })).token;
await api('POST', '/api/auth/contrasena', { token: cajero, cuerpo: { actual: cajeroNuevo.contrasenaTemporal, nueva: 'CajeraPrueba-2026' } });
cajero = (await api('POST', '/api/auth/login', { cuerpo: { usuario: cajeroNombre, password: 'CajeraPrueba-2026' } })).token;

const cobrar = async (ids, n) => {
  const filas = (await deudasDe(n)).filter(d => ids.includes(d.id));
  return A('POST', '/api/caja/cobrar', {
    numeroRecibo: 0, unidadComercialId: unidad(n), monedaId: bob.id,
    monto: filas.reduce((s, d) => s + d.monto, 0), formaPago: 1, nombrePagador: 'Pagador Prueba',
    tipoCambio: 1, referencia: null, items: filas.map(d => ({ obligacionId: d.id, nombreConcepto: d.concepto }))
  });
};

const anular = async (filtro, { motivo = MOTIVO, clave = nuevaClave(), ruta = P } = {}) => {
  const previa = await ruta('POST', '/vista-previa', filtro);
  const r = await ruta('POST', '', { ...filtro, motivo, confirmo: true, claveOperacion: clave, huella: previa.huella, cantidadEsperada: previa.cantidad });
  return { previa, r };
};

const saldoCaseta = async n => {
  const c = await A('GET', `/api/casetas/${unidad(n)}`);
  return c.deuda.reduce((s, d) => s + d.monto, 0);
};
const totalDeudores = async () => (await A('GET', `/api/reportes/deudores?bloque=${bloque}`)).totales.reduce((s, t) => s + t.monto, 0);

console.log('\nPruebas:');

// ---------------------------------------------------------------- AUTORIZACIÓN
await prueba('Sin sesión: 401', async () => {
  igual(await estado(api('GET', PANEL + '/deudas?bloque=' + bloque, { sinClave: true })), 401, 'estado');
  igual(await estado(api('POST', PANEL + '/vista-previa', { cuerpo: { bloque }, sinClave: true })), 401, 'estado');
});

await prueba('Cajero puede consultar pero no anular: 403', async () => {
  igual(await estado(P('GET', `/deudas?bloque=${bloque}`, undefined, cajero)), 200, 'consulta');
  igual(await estado(P('POST', '/vista-previa', { bloque }, cajero)), 403, 'vista previa');
  igual(await estado(P('POST', '', { bloque, motivo: MOTIVO, confirmo: true, claveOperacion: nuevaClave(), huella: 'x', cantidadEsperada: 1 }, cajero)), 403, 'confirmar');
});

await prueba('Sesión de socio (app): 403', async () => {
  igual(await estado(P('GET', `/deudas?bloque=${bloque}`, undefined, app)), 403, 'estado');
});

if (CLAVE_APP) {
  await prueba('Ruta del escritorio sin la clave de la app: 401; con la clave: 200', async () => {
    igual(await estado(api('GET', ESCRITORIO + '/deudas?bloque=' + bloque, { token: admin, sinClave: true })), 401, 'sin clave');
    igual(await estado(api('GET', ESCRITORIO + '/deudas?bloque=' + bloque, { token: admin })), 200, 'con clave');
  });
}

// ---------------------------------------------------------------- VALIDACIONES
await prueba('Validaciones: largo del motivo, confirmación, vista previa y filtros obligatorios', async () => {
  const id = (await deuda(1, 11)).id;
  const previa = await P('POST', '/vista-previa', { obligacionIds: [id] });
  const base = { obligacionIds: [id], motivo: MOTIVO, confirmo: true, claveOperacion: nuevaClave(), huella: previa.huella, cantidadEsperada: 1 };
  igual(await estado(P('POST', '', { ...base, motivo: 'x'.repeat(501) })), 400, 'motivo de más de 500 caracteres');
  igual(await estado(P('POST', '', { ...base, confirmo: false })), 400, 'sin confirmar');
  igual(await estado(P('POST', '', { ...base, huella: '' })), 400, 'sin huella');
  igual(await estado(P('POST', '', { ...base, claveOperacion: '00000000-0000-0000-0000-000000000000' })), 400, 'sin clave');
  igual(await estado(P('POST', '/vista-previa', {})), 400, 'masiva sin filtros');
  igual(await estado(P('POST', '/vista-previa', { obligacionIds: [crypto.randomUUID()] })), 404, 'deuda inexistente');
  igual((await deuda(1, 11)).estado, 'Pendiente', 'la deuda sigue pendiente');
});

// ---------------------------------------------------------------- INDIVIDUAL
let claveIndividual, anulacionIndividual;
await prueba('Anulación individual: estado Anulada, monto intacto, saldos recalculados, historial', async () => {
  const d = await deuda(1, 11);
  const saldoAntes = await saldoCaseta(1);
  const appAntes = (await deudasApp()).length;
  const deudoresAntes = await totalDeudores();

  claveIndividual = nuevaClave();
  const { previa, r } = await anular({ obligacionIds: [d.id] }, { clave: claveIndividual });
  igual(previa.tipo, 'Individual', 'tipo');
  igual(previa.cantidad, 1, 'cantidad en la vista previa');
  igual(previa.totales, [{ moneda: 'BOB', cantidad: 1, total: 100 }], 'total en la vista previa');
  igual(r.repetida, false, 'repetida');
  igual(r.cantidad, 1, 'anuladas');
  anulacionIndividual = r.anulacionId;

  const despues = await deuda(1, 11);
  igual(despues.estado, 'Anulada', 'estado');
  igual(despues.monto, 100, 'monto conservado');
  igual(despues.anulable, false, 'anulable');
  igual(despues.anulacion.motivo, MOTIVO, 'motivo');
  igual(despues.anulacion.responsable, 'Administrador Local', 'responsable');

  igual(await saldoCaseta(1), saldoAntes - 100, 'saldo de la caseta');
  igual((await deudasApp()).length, appAntes - 1, 'deudas en la app del socio');
  verdad(!(await deudasApp()).some(x => x.id === d.id), 'la app no muestra la deuda anulada');
  igual(await totalDeudores(), deudoresAntes - 100, 'reporte de deudores');

  const detalle = await P('GET', `/${r.anulacionId}`);
  igual(detalle.deudas.length, 1, 'detalle');
  igual(detalle.deudas[0].estadoAnterior, 'Pendiente', 'estado anterior');
  igual(detalle.deudas[0].estadoActual, 'Anulada', 'estado actual');

  const historial = await P('GET', '/historial');
  verdad(historial.some(h => h.id === r.anulacionId && h.motivo === MOTIVO && h.cantidad === 1), 'historial');

  const porDeuda = await api('GET', `${ESCRITORIO}/obligacion/${d.id}`, { token: admin });
  igual(porDeuda.anulacionId, r.anulacionId, 'consulta por deuda (ruta del escritorio)');

  const registro = await A('GET', '/api/registro-cambios?modulo=Obligaciones');
  const linea = registro.filas.find(f => f.accion === 'Deudas anuladas' && f.detalle.includes(`Caseta Z1-${marca}`));
  verdad(linea && linea.usuario === 'Administrador Local' && linea.detalle.includes(MOTIVO), 'registro de cambios');

  // Sigue visible en el listado que usa el escritorio, con su estado.
  const escritorio = await A('GET', `/api/cobranza/deudas?caseta=Z1-${marca}&conceptoId=${concepto}&estado=Anulada`);
  igual(escritorio.deudas.map(x => x.id), [d.id], 'listado del escritorio');
});

await prueba('Sin motivo: se anula igual y el historial dice «Sin motivo indicado»', async () => {
  await A('POST', '/api/cobranza/cargo', {
    unidadComercialId: unidad(3), conceptoCobroId: concepto, monedaId: bob.id, monto: 40,
    fechaEmision: '2027-01-01', fechaVencimiento: null, anio: 2027, mes: 1
  });
  const d = (await deudasDe(3)).find(x => x.anio === 2027 && x.mes === 1);
  const { r } = await anular({ obligacionIds: [d.id] }, { motivo: '' });
  igual(r.cantidad, 1, 'anuladas');
  const despues = (await deudasDe(3)).find(x => x.id === d.id);
  igual(despues.estado, 'Anulada', 'estado');
  igual(despues.anulacion.motivo, 'Sin motivo indicado', 'motivo');
  igual(despues.anulacion.responsable, 'Administrador Local', 'responsable');
});

// ---------------------------------------------------------------- DUPLICADOS
await prueba('Doble envío (misma clave): no anula nada más', async () => {
  const d = await deuda(1, 11);
  const r = await P('POST', '', { obligacionIds: [d.id], motivo: MOTIVO, confirmo: true, claveOperacion: claveIndividual, huella: 'cualquiera', cantidadEsperada: 1 });
  igual(r.repetida, true, 'repetida');
  igual(r.anulacionId, anulacionIndividual, 'misma anulación');
});

await prueba('Anular otra vez una deuda anulada: bloqueada y 409', async () => {
  const d = await deuda(1, 11);
  const previa = await P('POST', '/vista-previa', { obligacionIds: [d.id] });
  igual(previa.cantidad, 0, 'anulables');
  igual(previa.bloqueadas[0].motivo, 'Ya está anulada.', 'motivo');
  igual(await estado(P('POST', '', { obligacionIds: [d.id], motivo: MOTIVO, confirmo: true, claveOperacion: nuevaClave(), huella: previa.huella, cantidadEsperada: 1 })), 409, 'confirmar');
});

// ---------------------------------------------------------------- PAGADAS Y COMPROBANTES
let reciboZ3;
await prueba('Deuda pagada: no se puede anular; el recibo y el pago no cambian', async () => {
  const d = await deuda(3, 11);
  reciboZ3 = (await cobrar([d.id], 3)).recibo;
  const previa = await P('POST', '/vista-previa', { obligacionIds: [d.id] });
  igual(previa.cantidad, 0, 'anulables');
  verdad(previa.bloqueadas[0].motivo.startsWith('Está pagada'), 'motivo: ' + previa.bloqueadas[0].motivo);
  igual(await estado(P('POST', '', { obligacionIds: [d.id], motivo: MOTIVO, confirmo: true, claveOperacion: nuevaClave(), huella: previa.huella, cantidadEsperada: 1 })), 409, 'confirmar');
  igual((await deuda(3, 11)).estado, 'Pagada', 'estado');
  const recibo = await A('GET', `/api/caja/recibos/${reciboZ3.id}`);
  igual(recibo.estado, reciboZ3.estado, 'estado del recibo');
});

await prueba('Deuda con su recibo anulado: se puede anular y el recibo anulado no cambia', async () => {
  const d = await deuda(4, 11);
  const cobro = await cobrar([d.id], 4);
  await A('PUT', `/api/caja/recibos/${cobro.recibo.id}/anular`, { motivo: 'Prueba: recibo anulado' });
  igual((await deuda(4, 11)).estado, 'Pendiente', 'vuelve a pendiente');
  const reciboAntes = await A('GET', `/api/caja/recibos/${cobro.recibo.id}`);
  igual(reciboAntes.estado, 'Anulado', 'recibo anulado');

  const { previa, r } = await anular({ obligacionIds: [d.id] });
  igual([previa.cantidad, previa.bloqueadas.length], [1, 0], 'se puede anular');
  igual(r.cantidad, 1, 'anulada');
  igual((await deuda(4, 11)).estado, 'Anulada', 'estado');

  const reciboDespues = await A('GET', `/api/caja/recibos/${cobro.recibo.id}`);
  igual(reciboDespues, reciboAntes, 'el recibo anulado queda idéntico');
});

await prueba('Deuda con un pago QR en revisión: bloqueada', async () => {
  const d = await deuda(2, 11);
  const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
  await api('POST', '/api/app/pagos', { token: app, sinClave: true, cuerpo: { obligacionIds: [d.id], referencia: 'QR-PRUEBA', comprobanteBase64: png, comprobanteTipo: 'image/png' } });
  const previa = await P('POST', '/vista-previa', { obligacionIds: [d.id] });
  igual(previa.cantidad, 0, 'anulables');
  verdad(previa.bloqueadas[0].motivo.includes('pago QR en revisión'), 'motivo: ' + previa.bloqueadas[0].motivo);
});

await prueba('Deuda con un pago QR rechazado: se puede anular y el comprobante no cambia', async () => {
  await A('POST', '/api/cobranza/cargo', {
    unidadComercialId: unidad(1), conceptoCobroId: concepto, monedaId: bob.id, monto: 35,
    fechaEmision: '2027-05-01', fechaVencimiento: null, anio: 2027, mes: 5
  });
  const d = (await deudasDe(1)).find(x => x.anio === 2027 && x.mes === 5);
  const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
  const qr = await api('POST', '/api/app/pagos', { token: app, sinClave: true, cuerpo: { obligacionIds: [d.id], referencia: 'QR-RECHAZO', comprobanteBase64: png, comprobanteTipo: 'image/png' } });

  // En revisión: bloqueada.
  igual((await P('POST', '/vista-previa', { obligacionIds: [d.id] })).cantidad, 0, 'bloqueada mientras está en revisión');

  await api('POST', `/api/panel/pagos/${qr.id}/rechazar`, { token: admin, sinClave: true, cuerpo: { revisadoPor: 'Administrador Local', motivo: 'El monto no coincide' } });
  const pagosAntes = (await api('GET', '/api/panel/pagos?estado=todos', { token: admin, sinClave: true })).find(x => x.id === qr.id);
  igual(pagosAntes.estado, 'Rechazado', 'rechazado');

  const { previa, r } = await anular({ obligacionIds: [d.id] });
  igual([previa.cantidad, previa.bloqueadas.length], [1, 0], 'se puede anular');
  igual(r.cantidad, 1, 'anulada');

  const pagosDespues = (await api('GET', '/api/panel/pagos?estado=todos', { token: admin, sinClave: true })).find(x => x.id === qr.id);
  igual([pagosDespues.estado, pagosDespues.motivoRechazo, pagosDespues.referencia], ['Rechazado', 'El monto no coincide', 'QR-RECHAZO'], 'comprobante sin cambios');
});

// ---------------------------------------------------------------- MASIVA
const filtroNoviembre = { conceptoCobroId: concepto, anio: 2026, mes: 11, bloque };

await prueba('Vista previa desactualizada (monto corregido): 409 y no se anula nada', async () => {
  const previa = await P('POST', '/vista-previa', filtroNoviembre);
  igual(previa.cantidad, 4, 'anulables (Z5–Z8)');
  igual(previa.noAnulables, 1, 'no anulables (Z2 con pago QR en revisión; Z4 ya anulada no entra)');
  const z5 = await deuda(5, 11);
  await A('PUT', `/api/obligaciones-correcciones/${z5.id}/monto`, { montoAnteriorEsperado: 100, montoNuevo: 120, motivo: 'Prueba: corrección de monto' });
  igual(await estado(P('POST', '', { ...filtroNoviembre, motivo: MOTIVO, confirmo: true, claveOperacion: nuevaClave(), huella: previa.huella, cantidadEsperada: previa.cantidad })), 409, 'confirmar');
  for (const n of [5, 6, 7, 8]) igual((await deuda(n, 11)).estado, 'Pendiente', `Z${n} sigue pendiente`);
});

await prueba('Anulación masiva por concepto, período y bloque: solo las pendientes sin cobros', async () => {
  const { previa, r } = await anular(filtroNoviembre);
  igual(previa.tipo, 'Masiva', 'tipo');
  igual(previa.totales, [{ moneda: 'BOB', cantidad: 4, total: 420 }], 'total (con Z5 corregida a 120)');
  verdad(previa.filtros.includes('Bloque: ' + bloque) && previa.filtros.includes('Período: 11/2026'), 'filtros: ' + previa.filtros);
  igual(r.cantidad, 4, 'anuladas');
  for (const n of [5, 6, 7, 8]) igual((await deuda(n, 11)).estado, 'Anulada', `Z${n}`);
  igual((await deuda(5, 11)).monto, 120, 'monto de Z5 conservado');
  igual((await deuda(2, 11)).estado, 'Pendiente', 'Z2 (QR) sin tocar');
  igual((await deuda(3, 11)).estado, 'Pagada', 'Z3 (pagada) sin tocar');
  igual((await deuda(4, 11)).estado, 'Anulada', 'Z4 (anulada antes) sin cambios');
  igual((await deuda(5, 12)).estado, 'Pendiente', 'diciembre sin tocar');
  const detalle = await P('GET', `/${r.anulacionId}`);
  igual(detalle.deudas.length, 4, 'detalle');
  igual(detalle.totales, [{ moneda: 'BOB', cantidad: 4, total: 420 }], 'totales del detalle');
});

await prueba('Masiva con desmarcadas: solo se anulan las marcadas', async () => {
  const datos = {
    conceptoCobroId: concepto, anio: 2027, mes: 6, alcance: 'bloque', bloque, modoMonto: 'fijo', monto: 45,
    monedaId: bob.id, fechaEmision: '2027-06-01', fechaVencimiento: '2027-06-28'
  };
  await A('POST', '/api/cobranza/cargo-masivo', datos);
  const filtro = { conceptoCobroId: concepto, anio: 2027, mes: 6, bloque };
  const junio = async n => (await deudasDe(n)).find(x => x.anio === 2027 && x.mes === 6);
  const excluir = [(await junio(1)).id, (await junio(2)).id];

  const completa = await P('POST', '/vista-previa', filtro);
  igual(completa.cantidad, 8, 'vista previa completa');

  // La huella de la vista previa completa no sirve para confirmar con desmarcadas.
  igual(await estado(P('POST', '', { ...filtro, excluirObligacionIds: excluir, motivo: MOTIVO, confirmo: true, claveOperacion: nuevaClave(), huella: completa.huella, cantidadEsperada: 8 })), 409, 'huella sin descontar');

  const { previa, r } = await anular({ ...filtro, excluirObligacionIds: excluir });
  igual(previa.cantidad, 6, 'vista previa sin las desmarcadas');
  igual(previa.totales, [{ moneda: 'BOB', cantidad: 6, total: 270 }], 'total');
  verdad(previa.filtros.includes('2 desmarcadas'), 'filtros: ' + previa.filtros);
  igual(r.cantidad, 6, 'anuladas');
  for (const n of [1, 2]) igual((await junio(n)).estado, 'Pendiente', `Z${n} desmarcada sigue pendiente`);
  for (const n of [3, 4, 5, 6, 7, 8]) igual((await junio(n)).estado, 'Anulada', `Z${n} anulada`);
  const historial = await P('GET', '/historial');
  verdad(historial.some(h => h.id === r.anulacionId && h.filtros.includes('2 desmarcadas')), 'historial con desmarcadas');
});

await prueba('Por socio: solo las casetas de las que es titular', async () => {
  const previa = await P('POST', '/vista-previa', { socioId: socio.id, conceptoCobroId: concepto, anio: 2026, mes: 12 });
  igual(previa.deudas.map(d => d.caseta).sort(), [`Z1-${marca}`, `Z2-${marca}`], 'casetas');
  verdad(previa.filtros.includes('Socio: Socio Prueba ' + marca), 'filtros: ' + previa.filtros);
});

// ---------------------------------------------------------------- NO SE VUELVE A GENERAR
await prueba('Repetir la generación no recrea deudas anuladas', async () => {
  const q = `conceptoCobroId=${concepto}&anio=2026&mes=11&alcance=bloque&bloque=${bloque}&modoMonto=fijo&monto=100&monedaId=${bob.id}&fechaEmision=2026-11-01`;
  const previa = await A('GET', `/api/cobranza/cargo-masivo/vista-previa?${q}`);
  igual(previa.listas, 0, 'casetas listas para generar');
  igual(previa.yaTenian, 8, 'casetas que ya tienen (incluye anuladas)');
  igual(await estado(cargoMes(11)), 409, 'cargo masivo');
  await A('POST', '/api/cobranza/tarifas-masivo', { conceptoCobroId: concepto, monedaId: bob.id, monto: 100, alcance: 'bloque', bloque, pasilloId: null, soloSinTarifa: false });
  const tarifas = await A('GET', `/api/tarifas-unidad/vista-previa?conceptoCobroId=${concepto}&anio=2026&mes=11`);
  const z = tarifas.detalle.filter(x => x.caseta.endsWith(marca));
  igual(z.length, 8, 'casetas en la generación por tarifas');
  verdad(z.every(x => x.estado === 'DeudaExistente'), 'generación por tarifas: todas figuran con deuda existente (' + z.map(x => x.estado) + ')');
  const todas = await P('GET', `/deudas?bloque=${bloque}&conceptoCobroId=${concepto}&anio=2026&mes=11`);
  igual(todas.total, 8, 'siguen siendo 8 deudas de noviembre');
});

await prueba('Volver a registrar a mano una deuda anulada: se crea una nueva y la anulada se conserva', async () => {
  const antes = await P('GET', `/deudas?bloque=${bloque}&conceptoCobroId=${concepto}&anio=2026&mes=11`);
  const anulada = antes.deudas.find(d => d.caseta === `Z1-${marca}`);
  igual(anulada?.estado, 'Anulada', 'la de noviembre de Z1 está anulada');
  const cargo = { unidadComercialId: unidad(1), conceptoCobroId: concepto, monedaId: bob.id, monto: 125,
    fechaEmision: '2026-11-01', fechaVencimiento: null, anio: 2026, mes: 11 };
  const r = await A('POST', '/api/cobranza/cargo', cargo);
  verdad(r.mensaje.includes('se conserva'), 'mensaje: ' + r.mensaje);
  igual(await estado(A('POST', '/api/cobranza/cargo', cargo)), 409, 'una segunda vez (ya tiene una vigente)');
  const despues = await P('GET', `/deudas?bloque=${bloque}&conceptoCobroId=${concepto}&anio=2026&mes=11`);
  const deZ1 = despues.deudas.filter(d => d.caseta === `Z1-${marca}`);
  igual(deZ1.length, 2, 'Z1 tiene la anulada y la nueva');
  verdad(deZ1.some(d => d.id === anulada.id && d.estado === 'Anulada' && d.monto === anulada.monto), 'la anulada sigue igual');
  verdad(deZ1.some(d => d.id === r.id && d.estado === 'Pendiente' && d.monto === 125), 'la nueva está pendiente');
  // El cargo mensual masivo sigue sin regenerar las demás anuladas.
  const q = `conceptoCobroId=${concepto}&anio=2026&mes=11&alcance=bloque&bloque=${bloque}&modoMonto=fijo&monto=100&monedaId=${bob.id}&fechaEmision=2026-11-01`;
  igual((await A('GET', `/api/cobranza/cargo-masivo/vista-previa?${q}`)).listas, 0, 'cargo masivo: ninguna lista');
  igual(await estado(cargoMes(11)), 409, 'cargo masivo');
  igual((await P('GET', `/deudas?bloque=${bloque}&conceptoCobroId=${concepto}&anio=2026&mes=11`)).total, 9, 'noviembre: 8 + la nueva');
});

// ---------------------------------------------------------------- PAGOS PARCIALES
await prueba('Pago parcial: abono, saldo en todas partes, no anulable, y anular recibos devuelve solo su parte', async () => {
  await A('POST', '/api/cobranza/cargo', {
    unidadComercialId: unidad(2), conceptoCobroId: concepto, monedaId: bob.id, monto: 750,
    fechaEmision: '2027-01-01', fechaVencimiento: null, anio: 2027, mes: 1
  });
  const d = await deuda(2, 1);
  const pendienteDe = async () => (await A('GET', `/api/Obligaciones/unidad/${unidad(2)}/pendientes`)).find(o => o.id === d.id);
  const cobro = (monto, item) => A('POST', '/api/caja/cobrar', {
    numeroRecibo: 0, unidadComercialId: unidad(2), monedaId: bob.id, monto, formaPago: 1, nombrePagador: 'Pagador Prueba',
    tipoCambio: 1, referencia: null, items: [{ obligacionId: d.id, nombreConcepto: d.concepto, ...item }]
  });

  // Más que el saldo, o un total que no coincide: no se cobra nada.
  verdad(await estado(cobro(751, { monto: 751 })) >= 400, 'más que el saldo');
  verdad(await estado(cobro(750, { monto: 300 })) >= 400, 'total distinto al abono');
  igual((await pendienteDe()).saldo, 750, 'sin cobros: saldo completo');

  const primero = await cobro(300, { monto: 300 });
  igual(primero.recibo.total, 300, 'recibo del abono');
  igual(primero.recibo.detalles[0].monto, 300, 'detalle del abono');
  let o = await pendienteDe();
  igual([o.estado, o.monto, o.montoPagado, o.saldo], ['Pendiente', 750, 300, 450], 'deuda con abono');

  // El saldo se ve igual en la app del socio y en la lista de deudores.
  const enApp = (await deudasApp()).find(x => x.id === d.id);
  igual([enApp.monto, enApp.montoOriginal, enApp.pagado], [450, 750, 300], 'app del socio');
  const deudores = await A('GET', `/api/reportes/deudores?caseta=Z2-${marca}&conceptos=${concepto}`);
  const fila = deudores.filas.find(f => f.codigo === `Z2-${marca}`);
  verdad(fila.deudas.some(x => x.periodo === '01/2027' && x.monto === 450), 'deudores: saldo 450');

  // Con un abono no se puede anular la deuda.
  const previa = await P('POST', '/vista-previa', { obligacionIds: [d.id] });
  igual([previa.cantidad, previa.bloqueadas.length], [0, 1], 'no anulable con abono');

  // Pagar el resto sin indicar monto: todo el saldo.
  const segundo = await cobro(450, {});
  igual(segundo.recibo.total, 450, 'recibo del resto');
  verdad(!(await A('GET', `/api/Obligaciones/unidad/${unidad(2)}/pendientes`)).some(x => x.id === d.id), 'pagada: ya no está pendiente');

  // Anular el primer recibo devuelve solo los 300.
  await A('PUT', `/api/caja/recibos/${primero.recibo.id}/anular`, { motivo: 'Prueba: anular abono' });
  o = await pendienteDe();
  igual([o.estado, o.montoPagado, o.saldo], ['Pendiente', 450, 300], 'tras anular el abono');

  await A('PUT', `/api/caja/recibos/${segundo.recibo.id}/anular`, { motivo: 'Prueba: anular el resto' });
  o = await pendienteDe();
  igual([o.estado, o.montoPagado, o.saldo, o.monto], ['Pendiente', 0, 750, 750], 'tras anular los dos');
});

await prueba('Pago parcial: anular un abono y cobrar otro a la vez no pierde ninguno', async () => {
  const resultados = [];
  for (let mes = 2; mes <= 6; mes++) {
    await A('POST', '/api/cobranza/cargo', {
      unidadComercialId: unidad(2), conceptoCobroId: concepto, monedaId: bob.id, monto: 600,
      fechaEmision: '2028-01-01', fechaVencimiento: null, anio: 2028, mes
    });
    const d = (await deudasDe(2, '&anio=2028')).find(x => x.mes === mes);
    const cobro = monto => A('POST', '/api/caja/cobrar', {
      numeroRecibo: 0, unidadComercialId: unidad(2), monedaId: bob.id, monto, formaPago: 1, nombrePagador: 'Pagador Prueba',
      tipoCambio: 1, referencia: null, items: [{ obligacionId: d.id, nombreConcepto: d.concepto, monto }]
    });
    const primero = await cobro(200);
    await Promise.all([
      A('PUT', `/api/caja/recibos/${primero.recibo.id}/anular`, { motivo: 'Prueba: carrera de abonos' }),
      cobro(100)
    ]);
    const o = (await A('GET', `/api/Obligaciones/unidad/${unidad(2)}/pendientes`)).find(x => x.id === d.id);
    resultados.push(`${o.montoPagado}/${o.saldo}`);
    igual([o.montoPagado, o.saldo], [100, 500], `mes ${mes}: queda solo el abono de 100`);
  }
  console.log('      (pagado/saldo en cada carrera: ' + resultados.join(', ') + ')');
});

await prueba('Cargo mensual: las casetas desmarcadas no reciben la deuda (y se pueden cobrar después)', async () => {
  const datos = {
    conceptoCobroId: concepto, anio: 2027, mes: 3, alcance: 'bloque', bloque, modoMonto: 'fijo', monto: 70,
    monedaId: bob.id, fechaEmision: '2027-03-01', fechaVencimiento: '2027-03-28'
  };
  const previa = await A('GET', `/api/cobranza/cargo-masivo/vista-previa?${new URLSearchParams(datos)}`);
  igual(previa.listas, 8, 'listas en la vista previa');
  const excluir = [unidad(2), unidad(3)];

  // Con la cantidad de la vista previa sin descontar las desmarcadas: 409 y nada se genera.
  igual(await estado(A('POST', '/api/cobranza/cargo-masivo', { ...datos, listasEsperadas: 8, excluirUnidadIds: excluir })), 409, 'cantidad sin descontar');

  const r = await A('POST', '/api/cobranza/cargo-masivo', { ...datos, listasEsperadas: 6, excluirUnidadIds: excluir });
  igual([r.generadas, r.desmarcadas], [6, 2], 'generadas y desmarcadas');
  igual(r.totales, [{ moneda: 'BOB', cantidad: 6, total: 420 }], 'total solo de las generadas');

  const marzo = n => deudasDe(n).then(l => l.filter(x => x.anio === 2027 && x.mes === 3));
  for (const n of [1, 4, 5, 6, 7, 8]) igual((await marzo(n)).length, 1, `Z${n} recibió la deuda`);
  for (const n of [2, 3]) igual((await marzo(n)).length, 0, `Z${n} desmarcada no la recibió`);

  // La desmarcada no queda bloqueada: se le puede cargar después.
  await A('POST', '/api/cobranza/cargo', {
    unidadComercialId: unidad(2), conceptoCobroId: concepto, monedaId: bob.id, monto: 70,
    fechaEmision: '2027-03-01', fechaVencimiento: null, anio: 2027, mes: 3
  });
  igual((await marzo(2)).length, 1, 'Z2 cobrada después');

  const registro = await A('GET', '/api/registro-cambios?modulo=Obligaciones');
  const linea = registro.filas.find(f => f.accion === 'Cargo mensual generado' && f.detalle.includes('03/2027') && f.detalle.includes(marca));
  verdad(linea && linea.detalle.includes('Desmarcadas (2)'), 'registro de cambios: ' + (linea?.detalle ?? 'sin línea'));
});

await prueba('Recibo con observaciones (opcionales): se guardan, se devuelven y quedan en el registro', async () => {
  for (const n of [4, 5, 6]) {
    await A('POST', '/api/cobranza/cargo', {
      unidadComercialId: unidad(n), conceptoCobroId: concepto, monedaId: bob.id, monto: 25,
      fechaEmision: '2027-04-01', fechaVencimiento: null, anio: 2027, mes: 4
    });
  }
  const abril = async n => (await deudasDe(n)).find(x => x.anio === 2027 && x.mes === 4);
  const cuerpo = (n, d, extra = {}) => ({
    numeroRecibo: 0, unidadComercialId: unidad(n), monedaId: bob.id, monto: d.monto, formaPago: 1,
    nombrePagador: 'Pagador Prueba', tipoCambio: 1, referencia: null,
    items: [{ obligacionId: d.id, nombreConcepto: d.concepto }], ...extra
  });

  const obs = 'Pagó con billetes de 200; se le devolvió el cambio';
  const conObs = await A('POST', '/api/caja/cobrar', cuerpo(4, await abril(4), { observaciones: `  ${obs}  ` }));
  igual(conObs.recibo.observaciones, obs, 'respuesta del cobro');
  igual((await A('GET', `/api/caja/recibos/${conObs.recibo.id}`)).observaciones, obs, 'recibo consultado');

  // Como lo envía el instalador actual (sin el campo): sin observaciones.
  const sinObs = await A('POST', '/api/caja/cobrar', cuerpo(5, await abril(5)));
  igual((await A('GET', `/api/caja/recibos/${sinObs.recibo.id}`)).observaciones, null, 'sin observaciones');

  // Más de 500 caracteres: no se cobra nada.
  const d6 = await abril(6);
  igual(await estado(A('POST', '/api/caja/cobrar', cuerpo(6, d6, { observaciones: 'x'.repeat(501) }))), 400, 'demasiado largas');
  igual((await abril(6)).estado, 'Pendiente', 'la deuda sigue pendiente');

  const registro = await A('GET', '/api/registro-cambios?modulo=Caja');
  verdad(registro.filas.some(f => f.accion === 'Cobro registrado' && f.detalle.includes(`Recibo N.º ${conObs.recibo.numero}`) && f.detalle.includes(`Obs.: ${obs}`)),
    'registro de cambios con las observaciones');
});

// ---------------------------------------------------------------- CONCURRENCIA
await prueba('Concurrencia: 5 confirmaciones simultáneas de la misma anulación → una sola', async () => {
  const filtro = { conceptoCobroId: concepto, anio: 2026, mes: 12, bloque };
  const previa = await P('POST', '/vista-previa', filtro);
  igual(previa.cantidad, 8, 'anulables');
  const estados = await Promise.all(Array.from({ length: 5 }, () =>
    estado(P('POST', '', { ...filtro, motivo: MOTIVO, confirmo: true, claveOperacion: nuevaClave(), huella: previa.huella, cantidadEsperada: previa.cantidad }))));
  igual(estados.filter(e => e === 200).length, 1, `éxitos (${estados})`);
  igual(estados.filter(e => e === 409).length, 4, `conflictos (${estados})`);
  const historial = await P('GET', '/historial');
  igual(historial.filter(h => h.filtros?.includes(bloque) && h.filtros.includes('12/2026')).length, 1, 'anulaciones registradas');
  for (let n = 1; n <= 8; n++) igual((await deuda(n, 12)).estado, 'Anulada', `Z${n} diciembre`);
});

await prueba('Concurrencia: doble clic (misma clave, 3 envíos a la vez) → una sola anulación', async () => {
  const d = await deuda(5);
  const previa = await P('POST', '/vista-previa', { obligacionIds: [d.id] });
  const clave = nuevaClave();
  const cuerpo = { obligacionIds: [d.id], motivo: MOTIVO, confirmo: true, claveOperacion: clave, huella: previa.huella, cantidadEsperada: 1 };
  const respuestas = await Promise.all([1, 2, 3].map(() => P('POST', '', cuerpo).catch(e => e)));
  const ok = respuestas.filter(x => !(x instanceof Error));
  igual(ok.filter(x => x.repetida === false).length, 1, 'una sola anulación nueva');
  verdad(respuestas.every(x => !(x instanceof Error) || x.estado === 409), 'el resto: repetida o 409');
  verdad(new Set(ok.map(x => x.anulacionId)).size === 1, 'misma anulación');
});

await prueba('Concurrencia: anular y cobrar la misma deuda a la vez → solo una de las dos', async () => {
  const ganadores = [];
  for (const [i, n] of [1, 2, 3, 4, 6, 7, 8].entries()) {
    const d = await deuda(n);
    const previa = await P('POST', '/vista-previa', { obligacionIds: [d.id] });
    // Los dos cuerpos se preparan antes para que las dos peticiones salgan juntas.
    const cuerpoAnular = { obligacionIds: [d.id], motivo: MOTIVO, confirmo: true, claveOperacion: nuevaClave(), huella: previa.huella, cantidadEsperada: 1 };
    const cuerpoCobro = {
      numeroRecibo: 0, unidadComercialId: unidad(n), monedaId: bob.id, monto: d.monto, formaPago: 1,
      nombrePagador: 'Pagador Prueba', tipoCambio: 1, referencia: null, items: [{ obligacionId: d.id, nombreConcepto: d.concepto }]
    };
    const anularYa = () => estado(P('POST', '', cuerpoAnular));
    const cobrarYa = () => estado(A('POST', '/api/caja/cobrar', cuerpoCobro));
    const [eAnular, eCobrar] = i % 2 === 0
      ? await Promise.all([anularYa(), cobrarYa()])
      : (await Promise.all([cobrarYa(), anularYa()])).reverse();
    const final = await deuda(n);
    igual([eAnular, eCobrar].filter(e => e === 200).length, 1, `Z${n}: una sola operación (anular ${eAnular}, cobrar ${eCobrar})`);
    igual(final.estado, eAnular === 200 ? 'Anulada' : 'Pagada', `Z${n}: estado final`);
    igual(final.monto, 30, `Z${n}: monto intacto`);
    if (final.estado === 'Anulada') {
      // Anulada: no puede tener recibo.
      const r = await P('GET', `/obligacion/${d.id}`);
      verdad(r.anulacionId, `Z${n}: anulación registrada`);
    } else {
      // Pagada: no puede tener anulación.
      igual(await estado(P('GET', `/obligacion/${d.id}`)), 404, `Z${n}: sin anulación`);
    }
    ganadores.push(final.estado);
  }
  console.log(`      (resultado de cada carrera: ${ganadores.join(', ')})`);
});

// ---------------------------------------------------------------- RESUMEN
const fallidas = resultados.filter(r => !r.ok);
console.log(`\n${resultados.length - fallidas.length} de ${resultados.length} pruebas correctas.`);
process.exit(fallidas.length === 0 ? 0 : 1);

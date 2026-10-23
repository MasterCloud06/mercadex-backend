// Datos de prueba para la anulación de deudas. SOLO contra una API local
// conectada a la base local mercadex_pruebas (ver README.md).
//
//   node sembrar-datos-prueba.mjs antes     ← con la API anterior (sin anulación)
//   node sembrar-datos-prueba.mjs despues   ← con la API nueva: agrega una deuda anulada
//
// "antes" crea un mercado pequeño con deudas pendientes, pagadas, una caseta
// con parte de sus deudas pagadas, un recibo anulado y un pago QR en revisión.

import { api, loginAdmin, hoy } from './comun.mjs';

const fase = process.argv[2] ?? 'antes';

const token = await loginAdmin();
const conSesion = (metodo, ruta, cuerpo) => api(metodo, ruta, { cuerpo, token });

async function catalogos() {
  const monedas = await conSesion('GET', '/api/monedas');
  const bob = monedas.find(m => m.codigo === 'BOB');
  const usd = monedas.find(m => m.codigo === 'USD');
  if (!bob || !usd) throw new Error('Faltan las monedas BOB y USD: ejecuta antes 01-monedas.sql en mercadex_pruebas.');

  const conceptos = await conSesion('GET', '/api/conceptos-cobro');
  const unidades = await conSesion('GET', '/api/unidades-comerciales');
  return { bob, usd, conceptos, unidades };
}

if (fase === 'antes') {
  let { bob, usd, conceptos, unidades } = await catalogos();

  if (unidades.length > 0) {
    console.log('La base ya tiene casetas: no se vuelve a sembrar.');
    process.exit(0);
  }

  // ---------- Conceptos ----------
  for (const c of [
    { codigo: 'ALQ', nombre: 'Alquiler de caseta', descripcion: 'Mensualidad por el uso de la caseta' },
    { codigo: 'LIM', nombre: 'Servicio de limpieza', descripcion: 'Aporte mensual de limpieza' },
    { codigo: 'MUL', nombre: 'Multa por inasistencia', descripcion: 'Cargo eventual' }
  ]) {
    await conSesion('POST', '/api/ingresos/catalogos/conceptos', c);
  }
  conceptos = await conSesion('GET', '/api/conceptos-cobro');
  const concepto = codigo => conceptos.find(c => c.codigo === codigo).id;

  // ---------- Casetas ----------
  const codigos = [
    ['A-01', 'A'], ['A-02', 'A'], ['A-03', 'A'], ['A-04', 'A'], ['A-05', 'A'], ['A-06', 'A'],
    ['B-01', 'B'], ['B-02', 'B'], ['B-03', 'B'], ['B-04', 'B']
  ];
  for (const [codigo, bloque] of codigos) {
    await conSesion('POST', '/api/unidades-comerciales', { codigo, tipo: 1, pasilloId: null, bloque });
  }
  await conSesion('POST', '/api/unidades-comerciales', { codigo: 'C-01', tipo: 2, pasilloId: null, bloque: 'C' });
  unidades = await conSesion('GET', '/api/unidades-comerciales');
  const unidad = codigo => unidades.find(u => u.codigo === codigo).id;

  // ---------- Socios y titulares ----------
  const socios = [
    { nombres: 'Juan', apellidos: 'Mamani Choque', documentoIdentidad: '4567890', telefono: '70000001', casetas: ['A-01', 'A-02'] },
    { nombres: 'María', apellidos: 'Quispe Limachi', documentoIdentidad: '5678901', telefono: '70000002', casetas: ['A-03'] },
    { nombres: 'Pedro', apellidos: 'Condori Huanca', documentoIdentidad: '6789012', telefono: '70000003', casetas: ['B-01'] },
    { nombres: 'Rosa', apellidos: 'Flores Apaza', documentoIdentidad: '7890123', telefono: '70000004', casetas: ['B-02', 'B-03'] }
  ];
  for (const s of socios) {
    const creado = await conSesion('POST', '/api/socios', {
      nombres: s.nombres, apellidos: s.apellidos, documentoIdentidad: s.documentoIdentidad, telefono: s.telefono
    });
    s.id = creado.id;
    for (const c of s.casetas) {
      await conSesion('POST', '/api/asignaciones-socio-caseta', { socioId: s.id, unidadComercialId: unidad(c) });
    }
  }

  // ---------- Deudas (con el cargo masivo actual) ----------
  const cargo = (conceptoCobroId, anio, mes, monto, extra = {}) =>
    conSesion('POST', '/api/cobranza/cargo-masivo', {
      conceptoCobroId, anio, mes, alcance: 'todas', modoMonto: 'fijo', monto, monedaId: bob.id,
      fechaEmision: `${anio}-${String(mes).padStart(2, '0')}-01`,
      fechaVencimiento: `${anio}-${String(mes).padStart(2, '0')}-28`,
      ...extra
    });

  await cargo(concepto('ALQ'), 2026, 9, 150, { incluirComedores: true });
  await cargo(concepto('ALQ'), 2026, 10, 150, { incluirComedores: true });
  await cargo(concepto('LIM'), 2026, 10, 20, { alcance: 'bloque', bloque: 'A' });

  // Un cargo eventual en dólares.
  await conSesion('POST', '/api/cobranza/cargo', {
    unidadComercialId: unidad('B-03'), conceptoCobroId: concepto('MUL'), monedaId: usd.id,
    monto: 10, fechaEmision: hoy(), fechaVencimiento: null, anio: null, mes: null
  });

  const deudas = async (caseta) =>
    (await conSesion('GET', `/api/cobranza/deudas?caseta=${caseta}&estado=Pendiente`)).deudas;

  const cobrar = async (caseta, filtro, pagador) => {
    const items = (await deudas(caseta)).filter(filtro);
    return conSesion('POST', '/api/caja/cobrar', {
      numeroRecibo: 0,
      unidadComercialId: unidad(caseta),
      monedaId: bob.id,
      monto: items.reduce((s, d) => s + d.monto, 0),
      formaPago: 1,
      nombrePagador: pagador,
      tipoCambio: 1,
      referencia: null,
      items: items.map(d => ({ obligacionId: d.id, nombreConcepto: d.concepto }))
    });
  };

  // A-01: pagó septiembre y octubre (al día en alquiler).
  await cobrar('A-01', d => d.conceptoId === concepto('ALQ'), 'Juan Mamani Choque');
  // A-02: pagó solo septiembre: parte de sus deudas pagadas y parte pendientes.
  await cobrar('A-02', d => d.conceptoId === concepto('ALQ') && d.mes === 9, 'Juan Mamani Choque');
  // A-03: pagó septiembre y después se anuló el recibo (la deuda vuelve a pendiente con historial de cobro).
  const cobroA03 = await cobrar('A-03', d => d.conceptoId === concepto('ALQ') && d.mes === 9, 'María Quispe Limachi');
  await conSesion('PUT', `/api/caja/recibos/${cobroA03.recibo.id}/anular`, { motivo: 'Recibo emitido a la caseta equivocada' });

  // B-01: el socio envía un comprobante QR por octubre (queda en revisión).
  const pedro = socios.find(s => s.documentoIdentidad === '6789012');
  const acceso = await api('POST', `/api/panel/cuentas/${pedro.id}/codigo`, { token, sinClave: true });
  const sesionApp = await api('POST', '/api/app/activar', {
    sinClave: true,
    cuerpo: { documento: pedro.documentoIdentidad, codigo: acceso.codigo, contrasena: 'SocioPrueba-2026' }
  });
  const octubreB01 = (await deudas('B-01')).find(d => d.conceptoId === concepto('ALQ') && d.anio === 2026 && d.mes === 10);
  // Foto mínima válida (PNG de 1x1) como comprobante.
  const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
  await api('POST', '/api/app/pagos', {
    token: sesionApp.token,
    sinClave: true,
    cuerpo: { obligacionIds: [octubreB01.id], referencia: 'QR-000123', comprobanteBase64: png, comprobanteTipo: 'image/png' }
  });

  const resumen = await conSesion('GET', '/api/cobranza/deudas?estado=');
  console.log('Datos de prueba creados.');
  console.log(resumen.resumen.map(r => `  ${r.estado}: ${r.cantidad} deudas, ${r.moneda} ${r.monto}`).join('\n'));
} else if (fase === 'despues') {
  // Requiere la API nueva: una deuda ya anulada para ver su estado e historial.
  const { conceptos } = await catalogos();
  const alq = conceptos.find(c => c.codigo === 'ALQ').id;
  const ruta = '/api/obligaciones-anulaciones';

  const lista = await conSesion('GET', `${ruta}/deudas?caseta=B-04&conceptoCobroId=${alq}&anio=2026&mes=9`);
  const deuda = lista.deudas[0];

  if (deuda.estado === 'Anulada') {
    console.log('B-04 septiembre ya estaba anulada.');
    process.exit(0);
  }

  const previa = await conSesion('POST', `${ruta}/vista-previa`, { obligacionIds: [deuda.id] });
  const r = await conSesion('POST', ruta, {
    obligacionIds: [deuda.id],
    motivo: 'Caseta B-04 cerrada en septiembre por obras del mercado',
    confirmo: true,
    claveOperacion: crypto.randomUUID(),
    huella: previa.huella,
    cantidadEsperada: previa.cantidad
  });
  console.log(r.mensaje);
} else {
  console.error('Uso: node sembrar-datos-prueba.mjs antes|despues');
  process.exit(2);
}

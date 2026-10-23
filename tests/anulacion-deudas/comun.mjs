// Funciones comunes de los scripts de prueba de anulación de deudas.
// Solo trabajan contra una API local: se niegan a usar cualquier otra dirección.

export const API = (process.env.MERCADEX_API_URL ?? 'http://127.0.0.1:5189').replace(/\/$/, '');

const host = new URL(API).hostname;
if (!['127.0.0.1', 'localhost', '::1'].includes(host)) {
  console.error(`MERCADEX_API_URL=${API} no es local. Estos scripts solo se ejecutan contra 127.0.0.1 o localhost.`);
  process.exit(2);
}

export const CLAVE_APP = process.env.MERCADEX_API_KEY ?? '';
export const ADMIN_USER = process.env.MERCADEX_ADMIN_USER ?? 'admin';
export const ADMIN_PASSWORD = process.env.MERCADEX_ADMIN_PASSWORD ?? '';

export class ErrorApi extends Error {
  constructor(metodo, ruta, estado, cuerpo) {
    super(`${metodo} ${ruta} → ${estado}: ${typeof cuerpo === 'string' ? cuerpo : JSON.stringify(cuerpo)}`);
    this.estado = estado;
    this.cuerpo = cuerpo;
  }
}

/** Llama a la API. `token` es la sesión; la clave de la app se envía salvo en /api/panel y /api/app. */
export async function api(metodo, ruta, { cuerpo, token, sinClave = false, esperar } = {}) {
  const encabezados = { 'Content-Type': 'application/json' };
  if (token) encabezados.Authorization = `Bearer ${token}`;
  if (CLAVE_APP && !sinClave) encabezados['X-Mercadex-Key'] = CLAVE_APP;

  const r = await fetch(API + ruta, {
    method: metodo,
    headers: encabezados,
    body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo)
  });

  const texto = await r.text();
  let datos = texto;
  try { datos = texto ? JSON.parse(texto) : null; } catch { /* texto plano */ }

  if (esperar !== undefined) {
    if (r.status !== esperar) throw new ErrorApi(metodo, ruta, r.status, datos);
    return datos;
  }

  if (!r.ok) throw new ErrorApi(metodo, ruta, r.status, datos);
  return datos;
}

export async function loginAdmin() {
  if (!ADMIN_PASSWORD) {
    console.error('Falta MERCADEX_ADMIN_PASSWORD (la misma que usa la API local).');
    process.exit(2);
  }
  const r = await api('POST', '/api/auth/login', { cuerpo: { usuario: ADMIN_USER, password: ADMIN_PASSWORD } });
  return r.token;
}

export const hoy = () => new Date().toISOString().slice(0, 10);

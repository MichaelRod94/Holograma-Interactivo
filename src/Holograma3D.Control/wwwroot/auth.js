// Sesión de usuario: tokens bearer de la Api y fetch autenticado.
// API estable: la reutilizan generate.js (F7) y reconstruct.js (F8).

const KEY = 'holograma3d.control.auth';
const RENEW_BEFORE_MS = 60_000;

export const apiBaseUrl = (window.APP_CONFIG?.apiBaseUrl || `${location.protocol}//${location.hostname}:5080`).replace(/\/$/, '');

/** Error con mensaje para mostrar y, en registro, errores por campo ({ email: [...], password: [...] }). */
export class AuthError extends Error {
  constructor(message, fields = {}) {
    super(message);
    this.fields = fields;
  }
}

// Los códigos de Identity vienen en inglés: los de uso común, en español.
const IDENTITY_MESSAGES = {
  PasswordTooShort: 'Mínimo 8 caracteres.',
  PasswordRequiresDigit: 'Incluye al menos un número.',
  PasswordRequiresLower: 'Incluye al menos una minúscula.',
  PasswordRequiresUpper: 'Incluye al menos una mayúscula.',
  PasswordRequiresNonAlphanumeric: 'Incluye al menos un símbolo (por ejemplo - o !).',
  PasswordRequiresUniqueChars: 'Usa más caracteres distintos.',
  DuplicateUserName: 'Ya existe una cuenta con ese correo.',
  DuplicateEmail: 'Ya existe una cuenta con ese correo.',
  InvalidEmail: 'El correo no es válido.',
  InvalidUserName: 'El correo no es válido.',
};

// ponytail: sessionStorage (dura lo que la pestaña); "recordarme" con localStorage queda para después.
let tokens = read();
let refreshing = null;

function read() {
  try {
    return JSON.parse(sessionStorage.getItem(KEY));
  } catch {
    return null;
  }
}

function store(response) {
  tokens = {
    accessToken: response.accessToken,
    refreshToken: response.refreshToken,
    expiresAt: Date.now() + response.expiresIn * 1000,
  };
  try {
    sessionStorage.setItem(KEY, JSON.stringify(tokens));
  } catch { /* sin almacenamiento: la sesión dura hasta recargar */ }
}

const post = (path, body) => fetch(apiBaseUrl + path, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
});

const tooMany = () => new AuthError('Demasiados intentos, espera un minuto.');

export const isLoggedIn = () => tokens !== null;

export async function login(email, password) {
  const response = await post('/api/auth/login', { email, password });
  if (response.status === 429)
    throw tooMany();
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new AuthError(problem.detail === 'LockedOut'
      ? 'Cuenta bloqueada 5 minutos por intentos fallidos.'
      : 'Correo o contraseña incorrectos.');
  }
  store(await response.json());
}

/** Crea la cuenta e inicia sesión. */
export async function register(email, password) {
  const response = await post('/api/auth/register', { email, password });
  if (response.status === 429)
    throw tooMany();
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const fields = {};
    for (const [code, messages] of Object.entries(problem.errors ?? {})) {
      const field = code.startsWith('Password') ? 'password' : 'email';
      (fields[field] ??= []).push(IDENTITY_MESSAGES[code] ?? [messages].flat().join(' '));
    }
    throw new AuthError('Revisa los datos.', fields);
  }
  await login(email, password);
}

/** Token vigente; lo renueva si vence en menos de un minuto. null si ya no hay sesión. */
export async function getValidToken() {
  if (!tokens)
    return null;
  if (tokens.expiresAt - Date.now() < RENEW_BEFORE_MS)
    await renew();
  return tokens?.accessToken ?? null;
}

/** Una sola renovación a la vez aunque la pidan varias llamadas. */
function renew() {
  refreshing ??= (async () => {
    try {
      const response = await post('/api/auth/refresh', { refreshToken: tokens.refreshToken });
      if (!response.ok)
        throw new Error(`refresh ${response.status}`);
      store(await response.json());
    } catch {
      logout();
    } finally {
      refreshing = null;
    }
  })();
  return refreshing;
}

/** fetch con Authorization. Ante un 401 renueva una vez; si tampoco sirve, cierra la sesión. */
export async function authFetch(url, options = {}) {
  const send = async () => {
    const token = await getValidToken();
    if (!token)
      throw new AuthError('La sesión terminó.');
    const headers = new Headers(options.headers);
    headers.set('Authorization', `Bearer ${token}`);
    return fetch(url.startsWith('http') ? url : apiBaseUrl + url, { ...options, headers });
  };
  let response = await send();
  if (response.status === 401) {
    await renew();
    response = await send();
    if (response.status === 401) {
      logout();
      throw new AuthError('La sesión terminó.');
    }
  }
  return response;
}

/** Borra los tokens y avisa (evento "auth:logout") para que la app detenga el hub y muestre el acceso. */
export function logout() {
  const wasLoggedIn = tokens !== null;
  tokens = null;
  try {
    sessionStorage.removeItem(KEY);
  } catch { /* nada que borrar */ }
  if (wasLoggedIn)
    dispatchEvent(new Event('auth:logout'));
}

/** Invalida los refresh tokens de todos los dispositivos y cierra la sesión aquí. */
export async function logoutAll() {
  try {
    await authFetch('/api/auth/logout-all', { method: 'POST' });
  } finally {
    logout();
  }
}

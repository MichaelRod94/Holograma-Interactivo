import { apiBaseUrl, AuthError, authFetch, getValidToken, isLoggedIn, login, logout, logoutAll, register } from './auth.js';
import { Library } from './library.js';
import { Preview } from './preview.js';
import { reportRtt, startGestures } from './gestures.js';

const $ = id => document.getElementById(id);
const SESSION_KEY = 'holograma3d.control.session';
const GESTURES = { idle: 'Libre', grab: 'Agarrando', zoom: 'Zoom', rotate: 'Rotando' };

// ---------- Acceso ----------
let registering = false;

function showLogin(message = '') {
  $('app').hidden = true;
  $('login').hidden = false;
  $('login-error').textContent = message;
  $('email').focus();
}

function setRegistering(on) {
  registering = on;
  $('login-title').textContent = on ? 'Crea tu cuenta.' : 'Inicia sesión para controlar tus hologramas.';
  $('login-submit').textContent = on ? 'Crear cuenta' : 'Entrar';
  $('login-toggle').textContent = on ? 'Ya tengo cuenta' : 'Crear cuenta';
  $('password').autocomplete = on ? 'new-password' : 'current-password';
  $('password-hint').hidden = !on;
  for (const id of ['email-error', 'password-error', 'login-error'])
    $(id).textContent = '';
}

$('login-toggle').addEventListener('click', () => setRegistering(!registering));

$('login-form').addEventListener('submit', async event => {
  event.preventDefault();
  for (const id of ['email-error', 'password-error', 'login-error'])
    $(id).textContent = '';
  const email = $('email').value.trim(), password = $('password').value;
  if (!email || !password) {
    $('login-error').textContent = 'Escribe tu correo y tu contraseña.';
    return;
  }
  $('login-submit').disabled = true;
  try {
    await (registering ? register(email, password) : login(email, password));
    $('password').value = '';
    await startApp();
  } catch (error) {
    if (!(error instanceof AuthError)) {
      $('login-error').textContent = `La Api no respondió (${apiBaseUrl}).`;
      return;
    }
    $('login-error').textContent = error.message;
    $('email-error').textContent = error.fields.email?.join(' ') ?? '';
    $('password-error').textContent = error.fields.password?.join(' ') ?? '';
  } finally {
    $('login-submit').disabled = false;
  }
});

$('logout').addEventListener('click', () => logout());
$('logout-all').addEventListener('click', () => logoutAll());

// La sesión terminó (botón, refresh vencido o 401): se detiene todo lo que usa el token.
addEventListener('auth:logout', async () => {
  const conn = connection;
  connection = null;
  await conn?.stop();
  showLogin('Tu sesión se cerró.');
});

// ---------- Sesión de holograma ----------
let connection = null;
let session = null;
let started = false;
let preview, library;

function setGesture({ state, hands }) {
  const chip = $('gesture-chip');
  chip.dataset.state = state;
  chip.textContent = `${GESTURES[state] ?? state} · ${hands} mano${hands === 1 ? '' : 's'}`;
}

function applySnapshot(snapshot) {
  session = snapshot;
  try {
    localStorage.setItem(SESSION_KEY, snapshot.code);
  } catch { /* sin almacenamiento: al recargar se crea otra sesión */ }
  $('session-code').textContent = `${snapshot.code.slice(0, 3)} ${snapshot.code.slice(3)}`;
  $('session-code').setAttribute('aria-label', `Código ${[...snapshot.code].join(' ')}`);
  $('qr').src = `${apiBaseUrl}/api/sessions/${snapshot.code}/qr.svg`;
  $('qr').hidden = false;
  $('display-url').value = snapshot.displayUrl;
  $('displays').textContent = snapshot.displays;
  setGesture(snapshot.gesture);
  preview.applyTransform(snapshot.transform, true);
  preview.loadModel(snapshot.model);
  library.setActive(snapshot.model?.id);
}

const hubMessage = error => String(error?.message ?? error).split('HubException: ').pop();

/** Retoma la sesión guardada; si expiró o la Api se reinició, crea otra. */
async function openSession(forceNew = false) {
  $('session-error').textContent = '';
  let saved = null;
  try {
    saved = localStorage.getItem(SESSION_KEY);
  } catch { /* sin almacenamiento */ }
  if (saved && !forceNew) {
    try {
      applySnapshot(await connection.invoke('ResumeSession', saved));
      return;
    } catch { /* expiró: se crea otra */ }
  }
  applySnapshot(await connection.invoke('CreateSession'));
}

async function connect() {
  const conn = new signalR.HubConnectionBuilder()
    .withUrl(`${apiBaseUrl}/hubs/hologram`, { accessTokenFactory: async () => (await getValidToken()) ?? '' })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();
  conn.on('TransformChanged', t => {
    preview.applyTransform(t);
    if (t.srcT > 0)
      reportRtt(performance.now() - t.srcT);
  });
  conn.on('ModelChanged', model => {
    preview.loadModel(model);
    library.setActive(model?.id);
  });
  conn.on('GestureChanged', setGesture);
  conn.on('DisplaysChanged', n => { $('displays').textContent = n; });
  conn.onreconnecting(() => { $('session-error').textContent = 'Reconectando con la Api…'; });
  conn.onreconnected(async () => {
    try {
      await openSession(); // la conexión es nueva: vuelve a ser la de control
    } catch (error) {
      $('session-error').textContent = hubMessage(error);
    }
  });
  conn.onclose(() => {
    if (conn === connection)
      $('session-error').textContent = 'Se perdió la conexión con la Api. Recarga la página.';
  });
  connection = conn;
  await conn.start();
  await openSession();

  // ?select={modelId}: lo usan las páginas de generación y reconstrucción al terminar.
  const select = new URLSearchParams(location.search).get('select');
  if (select) {
    await project(select);
    history.replaceState(null, '', location.pathname);
  }
}

async function project(modelId) {
  try {
    await connection?.invoke('SelectModel', modelId);
  } catch (error) {
    $('session-error').textContent = hubMessage(error);
  }
}

async function reset() {
  try {
    await connection?.invoke('ResetTransform');
  } catch (error) {
    $('session-error').textContent = hubMessage(error);
  }
}

$('reset').addEventListener('click', reset);
addEventListener('keydown', e => {
  if ((e.key === 'r' || e.key === 'R') && !e.ctrlKey && !e.metaKey && !e.altKey && !e.target.closest('input, textarea'))
    reset();
});
$('new-session').addEventListener('click', async () => {
  try {
    await openSession(true);
  } catch (error) {
    $('session-error').textContent = hubMessage(error);
  }
});
$('copy-url').addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText($('display-url').value);
    $('copy-url').textContent = 'Copiado';
  } catch {
    $('display-url').select(); // sin portapapeles: queda seleccionada para Ctrl+C
  }
  setTimeout(() => { $('copy-url').textContent = 'Copiar'; }, 1500);
});

// ---------- Grabador: 30 s de HandFrames en un JSON (tests/Holograma3D.Tests/Recordings y calibración) ----------
const RECORD_SECONDS = 30;
let recording = null;

$('record').addEventListener('click', () => {
  if (recording)
    return;
  recording = { frames: [], started: Date.now() };
  $('record').disabled = true;
  $('record-status').textContent = '';
  const timer = setInterval(() => {
    const left = RECORD_SECONDS - Math.floor((Date.now() - recording.started) / 1000);
    if (left > 0) {
      $('record').textContent = `Grabando… ${left} s`;
      return;
    }
    clearInterval(timer);
    const { frames } = recording;
    recording = null;
    $('record').disabled = false;
    $('record').textContent = `Grabar ${RECORD_SECONDS} s`;
    if (!frames.length) {
      $('record-status').textContent = 'No se grabó nada: la cámara no está enviando frames.';
      return;
    }
    // 4 decimales bastan para los landmarks y el archivo queda en una fracción del tamaño.
    const rounded = frames.map(f => ({ ...f, hands: f.hands.map(h => ({ ...h, lm: h.lm.map(v => Math.round(v * 1e4) / 1e4) })) }));
    const url = URL.createObjectURL(new Blob([JSON.stringify(rounded)], { type: 'application/json' }));
    const link = Object.assign(document.createElement('a'), { href: url, download: `grabacion-${new Date().toISOString().slice(0, 19).replace(/[T:]/g, '-')}.json` });
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
    $('record-status').textContent = `Grabación lista: ${frames.length} frames.`;
  }, 250);
  $('record').textContent = `Grabando… ${RECORD_SECONDS} s`;
});

// ---------- Arranque ----------
async function startApp() {
  const me = await authFetch('/api/auth/me').then(r => r.json());
  $('user-email').textContent = me.email;
  $('login').hidden = true;
  $('app').hidden = false;

  if (!started) {
    started = true;
    preview = new Preview($('preview'), async model => {
      const response = await authFetch(model.fileUrl);
      return response.ok ? response.arrayBuffer() : null;
    });
    library = new Library({
      list: $('models'),
      dropzone: $('dropzone'),
      fileInput: $('file'),
      progress: $('upload-progress'),
      error: $('upload-error'),
      defaultButton: $('default-model'),
      onProject: project,
    });
    startGestures({
      video: $('video'),
      canvas: $('overlay'),
      meters: $('meters'),
      message: $('camera-message'),
      onFrame: frame => {
        recording?.frames.push(frame);
        if (connection?.state === signalR.HubConnectionState.Connected && session)
          connection.send('SendHandFrame', frame).catch(() => { });
      },
    });
  }

  await library.refresh();
  try {
    await connect();
  } catch (error) {
    $('session-error').textContent = `No se pudo abrir la sesión: ${hubMessage(error)}`;
  }
}

if (isLoggedIn())
  startApp().catch(error => showLogin(error instanceof AuthError ? 'Tu sesión se cerró.' : `La Api no respondió (${apiBaseUrl}).`));
else
  showLogin();

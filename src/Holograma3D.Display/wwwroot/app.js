import * as THREE from 'three';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { HologramRenderer } from './hologram.js';
import { Calibration, createTestCube, loadSettings } from './calibration.js';

const ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
const SMOOTHING_SECONDS = 0.08;
const DEMO_SPEED = THREE.MathUtils.degToRad(20); // rad/s
const Y_AXIS = new THREE.Vector3(0, 1, 0);
const LONG_PRESS_MS = 800;
const TRIPLE_TAP_MS = 600;

const apiBaseUrl = (window.APP_CONFIG?.apiBaseUrl || `${location.protocol}//${location.hostname}:5080`).replace(/\/$/, '');
const params = new URLSearchParams(location.search);
const demo = params.get('demo') === '1';
const $ = id => document.getElementById(id);

// ---------- Escena ----------
// ponytail: color post-MVP; respetar materiales del GLB detrás de un interruptor.
const holoMaterial = new THREE.MeshStandardMaterial({ color: 0x7fe6ff, emissive: 0x0a3a4a, metalness: 0.2, roughness: 0.4 });
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x000000);
scene.add(new THREE.HemisphereLight(0xffffff, 0x1a2a33, 0.9));

const pivot = new THREE.Group();     // la transformación de la sesión se aplica solo aquí
const modelSlot = new THREE.Group(); // el modelo normalizado (radio 1, centrado)
const testCube = createTestCube();
testCube.visible = false;
pivot.add(modelSlot, testCube);
scene.add(pivot);

const hologram = new HologramRenderer($('view'), scene, loadSettings());
const calibration = new Calibration($('calibration'), hologram.settings, {
  onChange: settings => hologram.configure(settings),
  onTestCube: on => {
    testCube.visible = on;
    modelSlot.visible = !on;
  },
});
if (params.get('calib') === '1')
  calibration.toggle(true);let mixer = null;
setModel(new THREE.Mesh(new THREE.TorusKnotGeometry(0.7, 0.22, 200, 32), holoMaterial));

// ---------- Modelos ----------
/** Centra por bounding box y escala para que el radio de la esfera envolvente sea 1. */
function normalize(object) {
  const box = new THREE.Box3().setFromObject(object);
  const holder = new THREE.Group();
  holder.add(object);
  if (box.isEmpty())
    return holder;
  const sphere = box.getBoundingSphere(new THREE.Sphere());
  object.position.sub(sphere.center);
  holder.scale.setScalar(sphere.radius > 0 ? 1 / sphere.radius : 1);
  return holder;
}

function disposeMaterial(material) {
  if (material === holoMaterial)
    return;
  for (const value of Object.values(material))
    if (value?.isTexture)
      value.dispose();
  material.dispose();
}

function dispose(root) {
  root.traverse(o => {
    o.geometry?.dispose();
    if (o.material)
      [o.material].flat().forEach(disposeMaterial);
  });
}

function setModel(object, newMixer = null) {
  for (const old of [...modelSlot.children]) {
    modelSlot.remove(old);
    dispose(old);
  }
  mixer?.stopAllAction();
  mixer = newMixer;
  modelSlot.add(normalize(object));
}

function fromStl(buffer) {
  const geometry = new STLLoader().parse(buffer);
  geometry.computeVertexNormals(); // muchos STL traen normales en cero
  const mesh = new THREE.Mesh(geometry, holoMaterial);
  mesh.rotation.x = -Math.PI / 2; // los STL vienen con Z arriba
  return { object: mesh, mixer: null };
}

// ponytail: sin DRACOLoader; agrégalo si un proveedor entrega GLB comprimidos con Draco.
async function fromGlb(buffer) {
  const gltf = await new GLTFLoader().parseAsync(buffer, '');
  gltf.scene.traverse(o => {
    if (!o.isMesh)
      return;
    [o.material].flat().forEach(disposeMaterial);
    o.material = holoMaterial; // MVP monocromo, también para los GLB con color
  });
  let animations = null;
  if (gltf.animations.length) {
    animations = new THREE.AnimationMixer(gltf.scene);
    for (const clip of gltf.animations)
      animations.clipAction(clip).play(); // LoopRepeat por defecto
  }
  return { object: gltf.scene, mixer: animations };
}

let modelRequest = 0;

/** Mientras carga el nuevo se queda el anterior; si la descarga falla, también. */
async function loadModel(model) {
  const request = ++modelRequest;
  if (!model) {
    setModel(new THREE.Mesh(new THREE.TorusKnotGeometry(0.7, 0.22, 200, 32), holoMaterial));
    return;
  }
  try {
    // Ruta anónima de la sesión: fileUrl exige token.
    const response = await fetch(`${apiBaseUrl}/api/sessions/${code}/model?v=${model.id}`);
    if (!response.ok)
      return;
    const buffer = await response.arrayBuffer();
    if (request !== modelRequest)
      return;
    const loaded = model.format === 'stl' ? fromStl(buffer) : await fromGlb(buffer);
    if (request !== modelRequest) {
      dispose(loaded.object);
      return;
    }
    setModel(loaded.object, loaded.mixer);
  } catch (error) {
    console.warn('No se pudo cargar el modelo', error);
  }
}

// ---------- Transformación con suavizado ----------
const target = { position: new THREE.Vector3(), quaternion: new THREE.Quaternion(), scale: 1 };
let lastSeq = -Infinity;

function applyTransform(t, force = false) {
  if (!force && t.seq <= lastSeq)
    return; // llegó tarde
  lastSeq = t.seq;
  target.position.set(t.px, t.py, t.pz);
  target.quaternion.set(t.qx, t.qy, t.qz, t.qw).normalize();
  target.scale = t.scale;
}

let demoAngle = 0;
let previous = performance.now();

function frame(now) {
  const dt = Math.min((now - previous) / 1000, 0.1);
  previous = now;
  if (demo) {
    demoAngle += DEMO_SPEED * dt;
    target.quaternion.setFromAxisAngle(Y_AXIS, demoAngle);
  }
  const k = 1 - Math.exp(-dt / SMOOTHING_SECONDS);
  pivot.position.lerp(target.position, k);
  pivot.quaternion.slerp(target.quaternion, k);
  pivot.scale.setScalar(THREE.MathUtils.lerp(pivot.scale.x, target.scale, k));
  mixer?.update(dt);
  hologram.render();
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

// ---------- HUD (?hud=1) ----------
$('hud').hidden = params.get('hud') !== '1';
const setStatus = status => { $('dot').className = `dot ${status}`; };
const setGesture = g => { $('gesture').textContent = `${g.state} · ${g.hands} mano${g.hands === 1 ? '' : 's'}`; };
if (demo)
  $('gesture').textContent = 'demo (sin conexión)';

// ---------- Conexión (sin token) ----------
let code = '';
let connection = null;

/** SignalR antepone su propio texto; el mensaje útil va después de "HubException: ". */
const hubMessage = error => String(error?.message ?? error).split('HubException: ').pop();

async function join(conn) {
  const snapshot = await conn.invoke('JoinSession', code);
  applyTransform(snapshot.transform, true);
  setGesture(snapshot.gesture);
  await loadModel(snapshot.model);
}

async function connect(newCode) {
  const old = connection;
  connection = null;
  await old?.stop();

  code = newCode;
  lastSeq = -Infinity;
  const conn = new signalR.HubConnectionBuilder()
    .withUrl(`${apiBaseUrl}/hubs/hologram`)
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();
  conn.on('TransformChanged', t => applyTransform(t));
  conn.on('ModelChanged', m => loadModel(m));
  conn.on('GestureChanged', setGesture);
  conn.onreconnecting(() => setStatus('connecting'));
  conn.onreconnected(async () => {
    try {
      await join(conn); // conexión nueva: hay que volver a entrar
      setStatus('connected');
    } catch (error) {
      fail(hubMessage(error));
    }
  });
  conn.onclose(() => {
    if (conn === connection)
      fail('Se perdió la conexión con la Api.');
  });
  connection = conn;

  setStatus('connecting');
  try {
    await conn.start();
  } catch {
    fail(`No se pudo conectar con la Api (${apiBaseUrl}).`);
    return;
  }
  try {
    await join(conn);
    setStatus('connected');
  } catch (error) {
    fail(hubMessage(error)); // p. ej. "La sesión ABC123 no existe o expiró."
  }
}

function fail(message) {
  setStatus('');
  const conn = connection;
  connection = null;
  conn?.stop();
  showUi(message);
}

// ---------- Pantalla inicial ----------
const normalizeCode = value => [...value.toUpperCase()].filter(c => ALPHABET.includes(c)).join('').slice(0, 6);
const input = $('code');
input.value = normalizeCode(params.get('s') ?? '');
input.addEventListener('input', () => { input.value = normalizeCode(input.value); });
$('code-field').hidden = demo;

function showUi(message = '') {
  $('message').textContent = message;
  $('start').hidden = false;
  quick.hidden = true;
}

// ---------- Vista: holograma de 4 lados o plana ----------
const quick = $('mode-quick');
const modeButtons = document.querySelectorAll('.modes [data-mode]');
let quickTimer = 0;

function setMode(mode) {
  calibration.set('mode', mode); // la guarda en el navegador y reconfigura el render
  showMode();
}

function showMode() {
  const mode = calibration.settings.mode;
  for (const button of modeButtons)
    button.setAttribute('aria-pressed', String(button.dataset.mode === mode));
  quick.textContent = mode === 'flat' ? 'Ver holograma' : 'Ver plana';
}

/** El botón rápido aparece unos segundos tras un toque: no se queda encima del holograma. */
function flashQuick() {
  quick.hidden = false;
  clearTimeout(quickTimer);
  quickTimer = setTimeout(() => { quick.hidden = true; }, 3000);
}

for (const button of modeButtons)
  button.addEventListener('click', () => setMode(button.dataset.mode));
quick.addEventListener('click', () => {
  setMode(calibration.settings.mode === 'flat' ? 'hologram' : 'flat');
  flashQuick();
});
showMode();

$('join').addEventListener('submit', async event => {
  event.preventDefault();
  const newCode = normalizeCode(input.value);
  if (!demo && newCode.length !== 6) {
    $('message').textContent = 'Escribe los 6 caracteres del código.';
    return;
  }
  // Ambos piden un toque del usuario: por eso van aquí.
  document.documentElement.requestFullscreen?.().catch(() => { });
  keepAwake();
  $('start').hidden = true;
  $('message').textContent = '';
  flashQuick();
  if (!demo && (newCode !== code || !connection))
    await connect(newCode);
});

// ---------- Pantalla encendida ----------
let started = false;

async function keepAwake() {
  started = true;
  try {
    await navigator.wakeLock.request('screen'); // solo en HTTPS
  } catch {
    notice('Mantén la pantalla encendida desde los ajustes del teléfono.');
  }
}

document.addEventListener('visibilitychange', () => {
  if (started && document.visibilityState === 'visible')
    keepAwake(); // el navegador suelta el wake lock al ocultarse la página
});

let noticeTimer = 0;
function notice(text) {
  $('notice').textContent = text;
  $('notice').hidden = false;
  clearTimeout(noticeTimer);
  noticeTimer = setTimeout(() => { $('notice').hidden = true; }, 6000);
}

// ---------- Un toque: botón de vista · toque largo: pantalla inicial · tres toques: calibración ----------
const canvas = $('view');
let pressTimer = 0;
let taps = [];
canvas.addEventListener('pointerdown', () => {
  flashQuick();
  pressTimer = setTimeout(() => showUi(), LONG_PRESS_MS);
  const now = performance.now();
  taps = [...taps.filter(t => now - t < TRIPLE_TAP_MS), now];
  if (taps.length >= 3) {
    taps = [];
    calibration.toggle();
  }
});
for (const type of ['pointerup', 'pointercancel', 'pointerleave'])
  canvas.addEventListener(type, () => clearTimeout(pressTimer));

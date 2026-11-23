import * as THREE from 'three';

const KEY = 'holograma3d.display.calibration';

// mode: "hologram" (4 lados para la pirámide) | "flat" (una sola vista, proyección plana).
export const DEFAULTS = Object.freeze({ mode: 'hologram', panel: 0.34, gap: 0.08, distance: 4, resolution: 1024, mirror: true, upInward: false });

const SLIDERS = [
  ['panel', 'Tamaño del panel', 0.2, 0.45, 0.01],
  ['gap', 'Separación del centro', 0.02, 0.2, 0.005],
  ['distance', 'Distancia de cámara', 2.5, 8, 0.1],
  ['resolution', 'Resolución (px)', 256, 1024, 64],
];
const CHECKS = [
  ['mirror', 'Espejo'],
  ['upInward', 'Arriba hacia dentro (pirámide al revés)'],
];

export function loadSettings() {
  try {
    return { ...DEFAULTS, ...JSON.parse(localStorage.getItem(KEY) ?? '{}') };
  } catch {
    return { ...DEFAULTS };
  }
}

function save(settings) {
  try {
    localStorage.setItem(KEY, JSON.stringify(settings));
  } catch { /* sin almacenamiento: se usan los valores de esta visita */ }
}

/**
 * Cubo de prueba: frente (+Z) rojo, derecha (+X) verde, arriba (+Y) azul, el resto gris.
 * Es la única excepción al monocromo. Desde el lado sur debe verse la cara roja de frente y la verde a la derecha.
 */
export function createTestCube() {
  const gray = 0x555555;
  const colors = [0x22cc44, gray, 0x2266ff, gray, 0xff2222, gray]; // orden de BoxGeometry: +X, −X, +Y, −Y, +Z, −Z
  const cube = new THREE.Mesh(new THREE.BoxGeometry(1.2, 1.2, 1.2),
    colors.map(color => new THREE.MeshStandardMaterial({ color, roughness: 0.6 })));
  cube.add(new THREE.AxesHelper(1.5));
  return cube;
}

/** Panel de calibración. onChange recibe los valores nuevos; onTestCube, si se muestra el cubo. */
export class Calibration {
  constructor(root, settings, { onChange, onTestCube }) {
    this.root = root;
    this.settings = { ...settings };
    this.onChange = onChange;
    this.onTestCube = onTestCube;
    this.build();
  }

  toggle(open = this.root.hidden) {
    this.root.hidden = !open;
  }

  build() {
    this.root.innerHTML = `
      <h2>Calibración</h2>
      ${SLIDERS.map(([key, text, min, max, step]) => `
        <label for="cal-${key}">${text}<output id="cal-${key}-out"></output></label>
        <input id="cal-${key}" type="range" min="${min}" max="${max}" step="${step}">`).join('')}
      ${CHECKS.map(([key, text]) => `
        <label class="check"><input id="cal-${key}" type="checkbox"> ${text}</label>`).join('')}
      <div class="buttons">
        <button type="button" id="cal-cube" aria-pressed="false">Cubo de prueba</button>
        <button type="button" id="cal-reset">Restablecer</button>
        <button type="button" id="cal-close">Cerrar</button>
      </div>`;

    for (const [key] of SLIDERS)
      this.root.querySelector(`#cal-${key}`).addEventListener('input', e => this.set(key, Number(e.target.value)));
    for (const [key] of CHECKS)
      this.root.querySelector(`#cal-${key}`).addEventListener('change', e => this.set(key, e.target.checked));

    const cube = this.root.querySelector('#cal-cube');
    cube.addEventListener('click', () => {
      const on = cube.getAttribute('aria-pressed') !== 'true';
      cube.setAttribute('aria-pressed', String(on));
      this.onTestCube(on);
    });
    this.root.querySelector('#cal-reset').addEventListener('click', () => {
      this.settings = { ...DEFAULTS, mode: this.settings.mode }; // restablecer no cambia de vista
      this.changed();
    });
    this.root.querySelector('#cal-close').addEventListener('click', () => this.toggle(false));
    this.show();
  }

  set(key, value) {
    this.settings[key] = value;
    this.changed();
  }

  changed() {
    save(this.settings);
    this.show();
    this.onChange({ ...this.settings });
  }

  show() {
    for (const [key] of SLIDERS) {
      this.root.querySelector(`#cal-${key}`).value = this.settings[key];
      this.root.querySelector(`#cal-${key}-out`).textContent = this.settings[key];
    }
    for (const [key] of CHECKS)
      this.root.querySelector(`#cal-${key}`).checked = this.settings[key];
  }
}

import * as THREE from 'three';

// Una cámara por lado del modelo, cada una con su panel en la cruz.
// `at` es la posición del panel respecto al centro y `angle` lo gira para que el "arriba" de su imagen
// apunte hacia fuera: en la pirámide, lo exterior se ve más alto.
const VIEWS = [
  { name: 'sur', dir: new THREE.Vector3(0, 0, 1), at: [0, -1], angle: Math.PI },       // ve el frente
  { name: 'este', dir: new THREE.Vector3(1, 0, 0), at: [1, 0], angle: -Math.PI / 2 },
  { name: 'norte', dir: new THREE.Vector3(0, 0, -1), at: [0, 1], angle: 0 },
  { name: 'oeste', dir: new THREE.Vector3(-1, 0, 0), at: [-1, 0], angle: Math.PI / 2 },
];
const LIGHT_LIFT = new THREE.Vector3(0, 2, 0);
const FOV = 35;

/**
 * Modo "hologram": dibuja la escena desde 4 lados y compone las 4 imágenes en cruz sobre negro.
 * Modo "flat": una sola vista de frente a pantalla completa, para proyectar en plano.
 */
export class HologramRenderer {
  constructor(canvas, scene, settings) {
    this.scene = scene;
    this.settings = settings;
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    this.renderer.setClearColor(0x000000, 1);

    // Sigue a la cámara de cada vista: las 4 caras se ven igual de iluminadas.
    this.light = new THREE.DirectionalLight(0xffffff, 1.6);
    scene.add(this.light);

    this.flatCamera = new THREE.PerspectiveCamera(FOV, 1, 0.1, 100);
    this.composition = new THREE.Scene();
    this.ortho = new THREE.OrthographicCamera();
    this.views = VIEWS.map(view => {
      const camera = new THREE.PerspectiveCamera(FOV, 1, 0.1, 100);
      const target = new THREE.WebGLRenderTarget(1, 1, { samples: 4 });
      // Las esquinas de paneles vecinos se enciman: con mezcla aditiva el negro de uno no tapa al otro.
      const panel = new THREE.Mesh(new THREE.PlaneGeometry(1, 1),
        new THREE.MeshBasicMaterial({ map: target.texture, blending: THREE.AdditiveBlending, depthTest: false }));
      this.composition.add(panel);
      return { ...view, camera, target, panel };
    });

    addEventListener('resize', () => this.layout());
    this.layout();
  }

  configure(settings) {
    this.settings = settings;
    this.layout();
  }

  layout() {
    const { panel, gap, distance, resolution, mirror, upInward } = this.settings;
    const w = innerWidth, h = innerHeight, s = Math.min(w, h);
    this.renderer.setSize(w, h);
    Object.assign(this.ortho, { left: -w / 2, right: w / 2, top: h / 2, bottom: -h / 2, near: -1, far: 1 });
    this.ortho.updateProjectionMatrix();

    // Plana: en vertical se abre el FOV para que el modelo quepa igual de ancho que de alto.
    const aspect = w / h;
    this.flatCamera.aspect = aspect;
    this.flatCamera.fov = aspect >= 1 ? FOV
      : THREE.MathUtils.radToDeg(2 * Math.atan(Math.tan(THREE.MathUtils.degToRad(FOV / 2)) / aspect));
    this.flatCamera.position.set(0, 0, distance);
    this.flatCamera.lookAt(0, 0, 0);
    this.flatCamera.updateProjectionMatrix();

    const g = s * gap;
    const p = Math.min(s * panel, s / 2 - g); // la cruz entra en el cuadrado de lado S
    const r = g + p / 2;
    const pixels = Math.max(64, Math.min(resolution, Math.round(p * this.renderer.getPixelRatio())));
    for (const v of this.views) {
      v.camera.position.copy(v.dir).multiplyScalar(distance);
      v.camera.lookAt(0, 0, 0);
      v.target.setSize(pixels, pixels);
      v.panel.position.set(v.at[0] * r, v.at[1] * r, 0);
      v.panel.rotation.z = v.angle + (upInward ? Math.PI : 0);
      v.panel.scale.set(mirror ? -p : p, p, 1); // cada cara de la pirámide refleja la imagen
    }
  }

  render() {
    if (this.settings.mode === 'flat') {
      this.light.position.copy(this.flatCamera.position).add(LIGHT_LIFT);
      this.renderer.setRenderTarget(null);
      this.renderer.render(this.scene, this.flatCamera);
      return;
    }
    for (const v of this.views) {
      this.light.position.copy(v.camera.position).add(LIGHT_LIFT);
      this.renderer.setRenderTarget(v.target);
      this.renderer.render(this.scene, v.camera);
    }
    this.renderer.setRenderTarget(null);
    this.renderer.render(this.composition, this.ortho);
  }
}

import * as THREE from 'three';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const SMOOTHING_SECONDS = 0.08;

// ponytail: duplicado consciente de ~40 líneas del Display (material, normalización, loaders, dispose);
// extraer a un módulo compartido si crece.
const holoMaterial = new THREE.MeshStandardMaterial({ color: 0x7fe6ff, emissive: 0x0a3a4a, metalness: 0.2, roughness: 0.4 });

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

function fromStl(buffer) {
  const geometry = new STLLoader().parse(buffer);
  geometry.computeVertexNormals();
  const mesh = new THREE.Mesh(geometry, holoMaterial);
  mesh.rotation.x = -Math.PI / 2; // los STL vienen con Z arriba
  return { object: mesh, mixer: null };
}

async function fromGlb(buffer) {
  const gltf = await new GLTFLoader().parseAsync(buffer, '');
  gltf.scene.traverse(o => {
    if (!o.isMesh)
      return;
    [o.material].flat().forEach(disposeMaterial);
    o.material = holoMaterial;
  });
  let mixer = null;
  if (gltf.animations.length) {
    mixer = new THREE.AnimationMixer(gltf.scene);
    for (const clip of gltf.animations)
      mixer.clipAction(clip).play();
  }
  return { object: gltf.scene, mixer };
}

const defaultModel = () => new THREE.Mesh(new THREE.TorusKnotGeometry(0.7, 0.22, 200, 32), holoMaterial);

/** Vista de frente del modelo activo con la transformación de la sesión: operar sin mirar el proyector. */
export class Preview {
  constructor(canvas, fetchModel) {
    this.fetchModel = fetchModel; // (model) => Promise<ArrayBuffer | null>
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x000000);
    this.scene.add(new THREE.HemisphereLight(0xffffff, 0x1a2a33, 0.9));
    const light = new THREE.DirectionalLight(0xffffff, 1.6);
    light.position.set(0, 2, 4);
    this.scene.add(light);
    this.camera = new THREE.PerspectiveCamera(35, 4 / 3, 0.1, 100);
    this.camera.position.set(0, 0, 4.5);
    this.pivot = new THREE.Group();
    this.slot = new THREE.Group();
    this.pivot.add(this.slot);
    this.scene.add(this.pivot);
    this.mixer = null;
    this.request = 0;
    this.target = { position: new THREE.Vector3(), quaternion: new THREE.Quaternion(), scale: 1 };
    this.lastSeq = -Infinity;
    this.setModel(defaultModel());

    new ResizeObserver(() => this.resize()).observe(canvas);
    let previous = performance.now();
    const frame = now => {
      const dt = Math.min((now - previous) / 1000, 0.1);
      previous = now;
      const k = 1 - Math.exp(-dt / SMOOTHING_SECONDS);
      this.pivot.position.lerp(this.target.position, k);
      this.pivot.quaternion.slerp(this.target.quaternion, k);
      this.pivot.scale.setScalar(THREE.MathUtils.lerp(this.pivot.scale.x, this.target.scale, k));
      this.mixer?.update(dt);
      this.renderer.render(this.scene, this.camera);
      requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);
  }

  resize() {
    const { clientWidth: w, clientHeight: h } = this.renderer.domElement;
    if (!w || !h)
      return;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  applyTransform(t, force = false) {
    if (!force && t.seq <= this.lastSeq)
      return;
    this.lastSeq = t.seq;
    this.target.position.set(t.px, t.py, t.pz);
    this.target.quaternion.set(t.qx, t.qy, t.qz, t.qw).normalize();
    this.target.scale = t.scale;
  }

  setModel(object, mixer = null) {
    for (const old of [...this.slot.children]) {
      this.slot.remove(old);
      dispose(old);
    }
    this.mixer?.stopAllAction();
    this.mixer = mixer;
    this.slot.add(normalize(object));
  }

  /** Mientras carga, se queda el anterior; si falla, también. */
  async loadModel(model) {
    const request = ++this.request;
    if (!model) {
      this.setModel(defaultModel());
      return;
    }
    try {
      const buffer = await this.fetchModel(model);
      if (!buffer || request !== this.request)
        return;
      const loaded = model.format === 'stl' ? fromStl(buffer) : await fromGlb(buffer);
      if (request !== this.request) {
        dispose(loaded.object);
        return;
      }
      this.setModel(loaded.object, loaded.mixer);
    } catch (error) {
      console.warn('No se pudo cargar el modelo en la vista previa', error);
    }
  }
}

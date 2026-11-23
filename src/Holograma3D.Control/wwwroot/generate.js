import { authFetch, isLoggedIn } from './auth.js';
import { downloadModel, el } from './library.js';
import { shrink } from './images.js';

const LABELS = ['Frente', 'Derecha', 'Atrás', 'Izquierda'];
const MAX_SIDE = 2048;
const REFRESH_MS = 3000;
const STATES = { queued: 'En cola', running: 'Generando', succeeded: 'Lista', failed: 'Falló' };
const $ = id => document.getElementById(id);

if (!isLoggedIn())
  location.replace('index.html');
addEventListener('auth:logout', () => location.replace('index.html'));

let provider = { enabled: false, maxImages: 0 };
let photos = []; // { blob, url }
let refreshTimer = 0;

const problemText = problem => [problem?.title, problem?.detail].filter(Boolean).join(': ') || 'No se pudo completar.';

function renderPhotos() {
  $('photos-hint').textContent = photos.length < provider.maxImages
    ? `Agrega fotos del objeto (${photos.length} de ${provider.maxImages}). Sugerido: ${LABELS.slice(0, provider.maxImages).join(', ').toLowerCase()}.`
    : `Ya tienes ${provider.maxImages} fotos.`;
  $('thumbs').replaceChildren(...photos.map((photo, i) => el('li', {},
    el('img', { src: photo.url, alt: `Foto ${i + 1}` }),
    el('span', { textContent: LABELS[i] ?? `Foto ${i + 1}` }),
    el('button', {
      type: 'button',
      textContent: 'Quitar',
      ariaLabel: `Quitar la foto ${i + 1}`,
      onclick: () => {
        URL.revokeObjectURL(photo.url);
        photos.splice(i, 1);
        renderPhotos();
      },
    }))));
}

$('photos').addEventListener('change', async () => {
  $('form-error').textContent = '';
  const files = [...$('photos').files].slice(0, provider.maxImages - photos.length);
  $('photos').value = '';
  for (const file of files) {
    try {
      const blob = await shrink(file, MAX_SIDE);
      photos.push({ blob, url: URL.createObjectURL(blob) });
    } catch {
      $('form-error').textContent = `No se pudo leer ${file.name}: usa JPG, PNG o WEBP.`;
    }
  }
  renderPhotos();
});

$('form').addEventListener('submit', async event => {
  event.preventDefault();
  $('form-error').textContent = '';
  if (!photos.length) {
    $('form-error').textContent = 'Agrega al menos una foto.';
    return;
  }
  const form = new FormData();
  form.append('name', $('name').value.trim());
  photos.forEach((photo, i) => form.append('images', photo.blob, `${(LABELS[i] ?? `foto-${i + 1}`).toLowerCase()}.jpg`));

  $('submit').disabled = true;
  $('submit').textContent = 'Subiendo…';
  try {
    const response = await authFetch('/api/generations', { method: 'POST', body: form });
    if (response.status !== 202) {
      $('form-error').textContent = problemText(await response.json().catch(() => null));
      return;
    }
    photos.forEach(photo => URL.revokeObjectURL(photo.url));
    photos = [];
    $('name').value = '';
    renderPhotos();
    await refreshJobs();
  } catch (error) {
    $('form-error').textContent = `No se pudo enviar: ${error.message}`;
  } finally {
    $('submit').disabled = false;
    $('submit').textContent = 'Generar con IA';
  }
});

function jobItem(job) {
  const date = new Date(job.createdAt).toLocaleString('es');
  const children = [
    el('div', { className: 'name', textContent: job.name }),
    el('div', { className: 'meta', textContent: `${STATES[job.status] ?? job.status} · ${job.provider} · ${date}` }),
  ];
  if (job.status === 'queued' || job.status === 'running')
    children.push(el('progress', { max: 100, value: job.progress, ariaLabel: `Progreso de ${job.name}: ${job.progress}%` }));
  if (job.error)
    children.push(el('p', { className: 'form-error', textContent: job.error }));
  if (job.status === 'succeeded')
    children.push(job.modelId
      ? el('div', { className: 'actions' },
        el('a', { className: 'button', href: `index.html?select=${job.modelId}`, textContent: 'Proyectar' }),
        el('button', { type: 'button', textContent: 'Descargar', ariaLabel: `Descargar ${job.name}`, onclick: () => download(job) }))
      : el('p', { className: 'muted small', textContent: 'El modelo se eliminó del catálogo.' }));
  return el('li', {}, ...children);
}

/** El formato y la URL del archivo los da el modelo del catálogo, no la generación. */
async function download(job) {
  try {
    const response = await authFetch(`/api/models/${job.modelId}`);
    if (!response.ok)
      throw new Error(response.status === 404 ? 'El modelo ya no existe.' : `La Api respondió ${response.status}.`);
    await downloadModel(await response.json());
  } catch (error) {
    $('form-error').textContent = `No se pudo descargar "${job.name}": ${error.message}`;
  }
}

/** Mientras haya trabajos activos, vuelve a consultar cada 3 s; luego se detiene. */
async function refreshJobs() {
  clearTimeout(refreshTimer);
  const response = await authFetch('/api/generations');
  if (!response.ok)
    return;
  const jobs = await response.json();
  $('jobs').replaceChildren(...(jobs.length
    ? jobs.map(jobItem)
    : [el('li', { className: 'muted', textContent: 'Aún no has generado modelos.' })]));
  if (jobs.some(j => j.status === 'queued' || j.status === 'running'))
    refreshTimer = setTimeout(refreshJobs, REFRESH_MS);
}

async function init() {
  provider = await (await authFetch('/api/generations/provider')).json();
  if (provider.enabled) {
    $('notice').textContent = `Las fotos se envían a ${provider.provider} y cada generación puede consumir créditos de tu cuenta.`;
    $('form').hidden = false;
    renderPhotos();
  } else {
    $('disabled').hidden = false;
  }
  await refreshJobs();
}

init().catch(error => { $('form-error').textContent = error.message; });

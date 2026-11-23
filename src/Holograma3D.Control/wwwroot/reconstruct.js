import { apiBaseUrl, getValidToken, isLoggedIn } from './auth.js';
import { downloadModel, el } from './library.js';
import { shrink } from './images.js';

const MAX_SIDE = 1600; // la Api las reduce a 1024 px de todos modos
const $ = id => document.getElementById(id);

if (!isLoggedIn())
  location.replace('index.html');
addEventListener('auth:logout', () => location.replace('index.html'));

// Visto desde arriba con el frente abajo: dónde se para la cámara en cada toma.
const VIEWS = [
  { key: 'front', label: 'Frontal', hint: 'Frente al objeto.', camera: [50, 92] },
  { key: 'left', label: 'Izquierda', hint: 'Desde el frente, muévete a TU izquierda.', camera: [8, 50] },
  { key: 'right', label: 'Derecha', hint: 'Desde el frente, muévete a TU derecha.', camera: [92, 50] },
  { key: 'back', label: 'Trasera', hint: 'Detrás del objeto.', camera: [50, 8] },
  { key: 'top', label: 'Superior', hint: 'Desde arriba, con el frente del objeto en el borde inferior de la foto.', camera: [50, 50] },
];
const photos = {}; // key → { blob, url }

/** Esquema: el objeto (su frente es el borde grueso) y la cámara apuntándole. */
function guide({ key, camera: [cx, cy] }) {
  const ns = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(ns, 'svg');
  svg.setAttribute('viewBox', '0 0 100 100');
  svg.setAttribute('aria-hidden', 'true');
  svg.innerHTML = `
    <rect x="30" y="30" width="40" height="40" rx="3" fill="#17303b" stroke="#7fe6ff" stroke-width="2"/>
    <line x1="30" y1="70" x2="70" y2="70" stroke="#ffb84d" stroke-width="5"/>
    <text x="50" y="84" font-size="9" text-anchor="middle" fill="#ffb84d">frente</text>
    ${key === 'top'
      ? '<circle cx="50" cy="50" r="8" fill="#e6f4f7"/><text x="50" y="54" font-size="11" text-anchor="middle" fill="#0b1418">↓</text>'
      : `<line x1="${cx}" y1="${cy}" x2="50" y2="50" stroke="#e6f4f7" stroke-width="1.5" stroke-dasharray="3 3"/>
         <circle cx="${cx}" cy="${cy}" r="6" fill="#e6f4f7"/>`}`;
  return svg;
}

function viewCard(view) {
  const input = el('input', { type: 'file', accept: 'image/jpeg,image/png,image/webp', id: `photo-${view.key}` });
  input.setAttribute('capture', 'environment'); // en el móvil abre la cámara trasera
  const preview = el('img', { alt: `Foto ${view.label.toLowerCase()}`, hidden: true });
  input.addEventListener('change', async () => {
    const file = input.files[0];
    if (!file)
      return;
    $('form-error').textContent = '';
    try {
      const blob = await shrink(file, MAX_SIDE);
      if (photos[view.key])
        URL.revokeObjectURL(photos[view.key].url);
      photos[view.key] = { blob, url: URL.createObjectURL(blob) };
      preview.src = photos[view.key].url;
      preview.hidden = false;
    } catch {
      $('form-error').textContent = `No se pudo leer la foto ${view.label.toLowerCase()}: usa JPG, PNG o WEBP.`;
    }
  });
  return el('li', {},
    el('h3', { textContent: view.label }),
    guide(view),
    el('p', { className: 'muted small', textContent: view.hint }),
    preview,
    el('label', { className: 'button', htmlFor: input.id, textContent: 'Elegir foto' }),
    input);
}

$('views').replaceChildren(...VIEWS.map(viewCard));

const problemText = problem => [problem?.title, problem?.detail].filter(Boolean).join(': ') || 'No se pudo completar.';

$('form').addEventListener('submit', async event => {
  event.preventDefault();
  $('form-error').textContent = '';
  $('result').hidden = true;
  const missing = VIEWS.filter(v => !photos[v.key]).map(v => v.label.toLowerCase());
  if (missing.length) {
    $('form-error').textContent = `Falta la foto ${missing.join(', ')}.`;
    return;
  }
  const token = await getValidToken();
  if (!token)
    return;

  const form = new FormData();
  form.append('name', $('name').value.trim());
  form.append('resolution', $('resolution').value);
  for (const v of VIEWS)
    form.append(v.key, photos[v.key].blob, `${v.key}.jpg`);

  // XMLHttpRequest: da el progreso de la subida y avisa cuando empieza el procesamiento.
  const xhr = new XMLHttpRequest();
  xhr.open('POST', `${apiBaseUrl}/api/reconstructions`);
  xhr.setRequestHeader('Authorization', `Bearer ${token}`);
  xhr.responseType = 'json';
  xhr.upload.onprogress = e => {
    if (e.lengthComputable)
      $('status').textContent = `Subiendo… ${Math.round(e.loaded / e.total * 100)} %`;
  };
  xhr.upload.onload = () => { $('status').textContent = 'Procesando: siluetas, volumen y malla…'; };
  xhr.onload = () => {
    $('submit').disabled = false;
    if (xhr.status !== 201) {
      $('status').textContent = '';
      $('form-error').textContent = xhr.status === 413 ? 'Las fotos pesan demasiado (máx. 10 MB cada una).' : problemText(xhr.response);
      return;
    }
    const model = xhr.response;
    $('status').textContent = `Listo: "${model.name}" con ${model.triangleCount.toLocaleString('es')} triángulos está en tu catálogo.`;
    $('project').href = `index.html?select=${model.id}`;
    $('download').onclick = () => downloadModel(model).catch(e => { $('form-error').textContent = `No se pudo descargar: ${e.message}`; });
    $('result').hidden = false;
  };
  xhr.onerror = () => {
    $('submit').disabled = false;
    $('status').textContent = '';
    $('form-error').textContent = 'No se pudo enviar: la Api no respondió.';
  };
  $('submit').disabled = true;
  $('status').textContent = 'Subiendo…';
  xhr.send(form);
});

import { apiBaseUrl, authFetch, getValidToken } from './auth.js';

const MAX_BYTES = 50 * 1024 * 1024;
const SOURCES = { upload: 'Subido', generated: 'Generado', reconstruction: 'Reconstruido' };

/** Crea un elemento; los textos van como textContent (los nombres de modelo los escribe el usuario). */
export function el(tag, props = {}, ...children) {
  const node = Object.assign(document.createElement(tag), props);
  node.append(...children);
  return node;
}

function readableSize(bytes) {
  if (bytes < 1024)
    return `${bytes} B`;
  if (bytes < 1024 * 1024)
    return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

const problemText = problem => [problem?.title, problem?.detail].filter(Boolean).join(': ') || 'No se pudo completar.';

/**
 * Catálogo del usuario: lista, subida y borrado.
 * onProject(modelId | null) proyecta un modelo (null = el modelo por defecto).
 */
export class Library {
  constructor({ list, dropzone, fileInput, progress, error, defaultButton, onProject }) {
    Object.assign(this, { list, progress, error, onProject });
    this.models = [];
    this.activeId = null;

    fileInput.addEventListener('change', () => {
      if (fileInput.files[0])
        this.upload(fileInput.files[0]);
      fileInput.value = '';
    });
    dropzone.addEventListener('dragover', e => {
      e.preventDefault();
      dropzone.classList.add('over');
    });
    dropzone.addEventListener('dragleave', () => dropzone.classList.remove('over'));
    dropzone.addEventListener('drop', e => {
      e.preventDefault();
      dropzone.classList.remove('over');
      if (e.dataTransfer.files[0])
        this.upload(e.dataTransfer.files[0]);
    });
    defaultButton.addEventListener('click', () => onProject(null));
  }

  async refresh() {
    try {
      const response = await authFetch('/api/models');
      if (!response.ok)
        throw new Error(problemText(await response.json().catch(() => null)));
      this.models = await response.json();
      this.render();
    } catch (e) {
      this.error.textContent = `No se pudo cargar la lista de modelos. ${e.message}`;
    }
  }

  setActive(id) {
    this.activeId = id ?? null;
    this.render();
  }

  render() {
    if (!this.models.length) {
      this.list.replaceChildren(el('li', { className: 'muted', textContent: 'Aún no tienes modelos. Sube un .stl o un .glb.' }));
      return;
    }
    this.list.replaceChildren(...this.models.map(m => this.item(m)));
  }

  item(model) {
    const active = model.id === this.activeId;
    const meta = [
      model.format.toUpperCase(),
      SOURCES[model.source] ?? model.source,
      readableSize(model.sizeBytes),
      model.triangleCount === null ? null : `${model.triangleCount.toLocaleString('es')} triángulos`,
      new Date(model.createdAt).toLocaleDateString('es'),
    ].filter(Boolean).join(' · ');

    const actions = el('div', { className: 'actions' });
    const showButtons = () => actions.replaceChildren(
      active
        ? el('span', { className: 'badge', textContent: 'Proyectando' })
        : el('button', { type: 'button', textContent: 'Proyectar', onclick: () => this.onProject(model.id) }),
      el('button', { type: 'button', className: 'danger', textContent: 'Eliminar', onclick: confirmDelete }),
    );
    // Confirmación dentro de la tarjeta, sin confirm().
    const confirmDelete = () => {
      const yes = el('button', { type: 'button', className: 'danger', textContent: 'Sí, eliminar', onclick: () => this.remove(model) });
      actions.replaceChildren(
        el('span', { textContent: `¿Eliminar "${model.name}"?` }),
        yes,
        el('button', { type: 'button', textContent: 'Cancelar', onclick: showButtons }),
      );
      yes.focus();
    };
    showButtons();

    const li = el('li', {},
      el('div', { className: 'name', textContent: model.name }),
      el('div', { className: 'meta', textContent: meta }),
      actions);
    if (active)
      li.setAttribute('aria-current', 'true');
    return li;
  }

  async remove(model) {
    const response = await authFetch(`/api/models/${model.id}`, { method: 'DELETE' });
    if (!response.ok && response.status !== 404) {
      this.error.textContent = `No se pudo eliminar "${model.name}".`;
      return;
    }
    if (model.id === this.activeId)
      this.onProject(null); // la sesión no se queda apuntando a un modelo borrado
    await this.refresh();
  }

  /** XMLHttpRequest en vez de fetch: da el progreso de la subida. */
  async upload(file) {
    this.error.textContent = '';
    if (!/\.(stl|glb)$/i.test(file.name)) {
      this.error.textContent = 'Solo se aceptan archivos .stl o .glb.';
      return;
    }
    if (file.size > MAX_BYTES) {
      this.error.textContent = 'El archivo supera el máximo de 50 MB.';
      return;
    }
    const token = await getValidToken();
    if (!token)
      return;

    const form = new FormData();
    form.append('file', file);
    const xhr = new XMLHttpRequest();
    xhr.open('POST', `${apiBaseUrl}/api/models`);
    xhr.setRequestHeader('Authorization', `Bearer ${token}`);
    xhr.responseType = 'json';
    this.progress.hidden = false;
    this.progress.value = 0;
    xhr.upload.onprogress = e => {
      if (e.lengthComputable)
        this.progress.value = e.loaded / e.total * 100;
    };
    xhr.onload = async () => {
      this.progress.hidden = true;
      if (xhr.status === 201)
        await this.refresh();
      else
        this.error.textContent = xhr.status === 413 ? 'El archivo supera el máximo de 50 MB.' : problemText(xhr.response);
    };
    xhr.onerror = () => {
      this.progress.hidden = true;
      this.error.textContent = 'No se pudo subir el archivo: la Api no respondió.';
    };
    xhr.send(form);
  }
}

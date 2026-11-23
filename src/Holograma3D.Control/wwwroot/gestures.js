import { DrawingUtils, FilesetResolver, GestureRecognizer } from '@mediapipe/tasks-vision';

// ponytail: MediaPipe, three.js y SignalR vienen de CDN; sin internet no hay gestos. Servirlos desde /vendor si hace falta.
const WASM = 'https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.35/wasm';
const MODEL = 'https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task';
const SEND_INTERVAL_MS = 1000 / 30; // la Api descarta lo que llegue a menos de 15 ms

/** Explica por qué no abrió la cámara. */
function cameraProblem(error) {
  if (!isSecureContext)
    return 'La cámara solo funciona en contexto seguro: abre http://localhost:5081.';
  switch (error?.name) {
    case 'NotAllowedError': return 'Permiso de cámara denegado. Permítelo en la barra de direcciones y recarga la página.';
    case 'NotReadableError': return 'La cámara está ocupada por otra aplicación (Teams, Zoom…). Ciérrala y recarga.';
    case 'NotFoundError':
    case 'OverconstrainedError': return 'No se encontró una cámara.';
    default: return `No se pudo abrir la cámara: ${error?.message ?? error}`;
  }
}

async function createRecognizer() {
  const vision = await FilesetResolver.forVisionTasks(WASM);
  const options = delegate => ({
    baseOptions: { modelAssetPath: MODEL, delegate },
    runningMode: 'VIDEO',
    numHands: 2,
    minHandDetectionConfidence: 0.6,
    minHandPresenceConfidence: 0.6,
    minTrackingConfidence: 0.5,
  });
  try {
    return await GestureRecognizer.createFromOptions(vision, options('GPU'));
  } catch {
    return await GestureRecognizer.createFromOptions(vision, options('CPU'));
  }
}

/**
 * Abre la cámara, reconoce manos y llama onFrame({ t, hands }) a 30 Hz como máximo, también sin manos
 * (el dominio usa esos frames para soltar). Las coordenadas van sin espejo; el espejo es solo visual (CSS).
 */
export async function startGestures({ video, canvas, meters, message, onFrame }) {
  message.textContent = 'Abriendo la cámara…';
  try {
    video.srcObject = await navigator.mediaDevices.getUserMedia({ video: { width: 1280, height: 720, facingMode: 'user' } });
    await video.play();
  } catch (error) {
    message.textContent = cameraProblem(error);
    return;
  }

  message.textContent = 'Cargando el reconocedor de gestos…';
  let recognizer;
  try {
    recognizer = await createRecognizer();
  } catch (error) {
    message.textContent = `No se pudo cargar MediaPipe: ${error?.message ?? error}`;
    return;
  }
  message.textContent = '';

  const ctx = canvas.getContext('2d');
  const drawing = new DrawingUtils(ctx);
  let lastVideoTime = -1, lastSent = 0;
  let frames = 0, inferenceMs = 0, fps = 0, windowStart = performance.now();

  const loop = () => {
    if (document.visibilityState !== 'visible')
      return; // se reanuda en visibilitychange
    schedule();
    if (video.currentTime === lastVideoTime)
      return;
    lastVideoTime = video.currentTime;

    const start = performance.now();
    const result = recognizer.recognizeForVideo(video, start);
    const now = performance.now();
    inferenceMs = inferenceMs * 0.9 + (now - start) * 0.1;
    frames++;
    if (now - windowStart >= 1000) {
      fps = frames * 1000 / (now - windowStart);
      frames = 0;
      windowStart = now;
      meters.textContent = `${fps.toFixed(0)} fps · ${inferenceMs.toFixed(1)} ms${rtt === null ? '' : ` · RTT ${rtt.toFixed(0)} ms`}`;
    }

    const hands = result.landmarks.map((landmarks, i) => ({
      gesture: result.gestures[i]?.[0]?.categoryName || 'None',
      score: result.gestures[i]?.[0]?.score ?? 0,
      lm: landmarks.flatMap(p => [p.x, p.y, p.z]),
    }));
    draw(ctx, drawing, result, hands, video);
    if (now - lastSent >= SEND_INTERVAL_MS) {
      lastSent = now;
      onFrame({ t: now, hands });
    }
  };
  // Un solo callback pendiente: al volver a la pestaña no se duplica el bucle.
  let pending = false;
  const schedule = () => {
    if (pending)
      return;
    pending = true;
    const next = () => {
      pending = false;
      loop();
    };
    if (video.requestVideoFrameCallback)
      video.requestVideoFrameCallback(next);
    else
      requestAnimationFrame(next);
  };

  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible')
      schedule();
  });
  schedule();
}

let rtt = null;

/** Tiempo desde que salió un frame hasta que volvió su TransformChanged. */
export function reportRtt(ms) {
  rtt = ms;
}

function draw(ctx, drawing, result, hands, video) {
  const { videoWidth: w, videoHeight: h } = video;
  if (ctx.canvas.width !== w || ctx.canvas.height !== h) {
    ctx.canvas.width = w;
    ctx.canvas.height = h;
  }
  ctx.clearRect(0, 0, w, h);
  result.landmarks.forEach((landmarks, i) => {
    drawing.drawConnectors(landmarks, GestureRecognizer.HAND_CONNECTIONS, { color: '#7fe6ff', lineWidth: 3 });
    drawing.drawLandmarks(landmarks, { color: '#ffb84d', lineWidth: 1, radius: 3 });

    // El canvas se ve en espejo por CSS: el texto se escribe invertido para que se lea bien.
    const label = `${hands[i].gesture} ${(hands[i].score * 100).toFixed(0)}%`;
    ctx.save();
    ctx.scale(-1, 1);
    ctx.font = '600 22px system-ui, sans-serif';
    ctx.textAlign = 'center';
    ctx.fillStyle = '#ffffff';
    ctx.strokeStyle = '#000000';
    ctx.lineWidth = 4;
    const x = -landmarks[0].x * w, y = landmarks[0].y * h + 34;
    ctx.strokeText(label, x, y);
    ctx.fillText(label, x, y);
    ctx.restore();
  });
}

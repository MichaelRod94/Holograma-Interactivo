/**
 * Reduce una foto a <maxSide> px de lado mayor y la recodifica en JPEG (calidad 0.9): menos datos que subir.
 * createImageBitmap ya la endereza según su EXIF, así que la foto llega derecha.
 */
export async function shrink(file, maxSide = 2048) {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height));
  const canvas = Object.assign(document.createElement('canvas'), {
    width: Math.round(bitmap.width * scale),
    height: Math.round(bitmap.height * scale),
  });
  canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  return new Promise((resolve, reject) =>
    canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('No se pudo convertir la foto.')), 'image/jpeg', 0.9));
}

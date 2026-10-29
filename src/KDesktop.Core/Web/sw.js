// K-Desktop service worker: the app opens even when the PC is off (with the last commands it saw),
// new versions arrive at once (network first), and command icons are kept (their names are content hashes).
const SHELL = 'kd-shell-v9';
const ICONS = 'kd-icons-v1';
const FILES = ['./', 'kd.css', 'app.css', 'app.js', 'fuzzy.js', 'pad.js', 'voice.js', 'chat.js', 'inbox.js', 'manifest.webmanifest',
  'icons/favicon.png', 'icons/icon-192.png', 'icons/apple-touch-icon.png'];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(SHELL).then(c => c.addAll(FILES)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
  event.waitUntil((async () => {
    for (const key of await caches.keys())
      if (key !== SHELL && key !== ICONS) await caches.delete(key);
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET') return;
  const url = new URL(request.url);
  if (url.origin !== location.origin) return;

  if (url.pathname.includes('/api/icon/')) {
    event.respondWith(iconFirst(request));
    return;
  }
  if (url.pathname.includes('/api/')) return; // always live

  event.respondWith(networkFirst(request));
});

async function iconFirst(request) {
  const cache = await caches.open(ICONS);
  const hit = await cache.match(request);
  if (hit) return hit;
  const response = await fetch(request);
  if (response.ok) cache.put(request, response.clone());
  return response;
}

async function networkFirst(request) {
  const cache = await caches.open(SHELL);
  const cached = async () => (await cache.match(request, { ignoreSearch: true }))
    ?? (request.mode === 'navigate' ? await cache.match('./') : undefined);
  try {
    const response = await fetch(request);
    if (response.ok) {
      cache.put(request, response.clone());
      return response;
    }
    // The proxy answers 502/504 while K-Desktop is not running: the saved copy is better.
    return response.status >= 500 ? (await cached()) ?? response : response;
  } catch {
    return (await cached()) ?? Response.error();
  }
}

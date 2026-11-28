// Admin PWA service worker: network-only so admin data is always live and nothing
// sensitive is cached on the device; presence of a fetch handler makes the app installable.
self.addEventListener('install', event => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => {});

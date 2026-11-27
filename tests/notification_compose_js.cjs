const fs = require('node:fs');
const assert = require('node:assert/strict');

const js = fs.readFileSync('wwwroot/assets/js/notifications.js', 'utf8');
const view = fs.readFileSync('Views/Notifications/Manage.cshtml', 'utf8');

// Native confirm() is suppressed in in-app browsers (WhatsApp, Facebook), which silently
// cancelled the broadcast submit and made "Send notification" look dead. It must never return.
assert.ok(!/\bconfirm\s*\(/.test(js), 'notifications.js must not use native confirm()');
assert.ok(js.includes('broadcastArmed'), 'broadcast submit must require a second tap (double-tap arming)');
assert.ok(js.includes('Tap again to confirm'), 'armed send button must say a second tap confirms');

// CSP (script-src-attr 'none') blocks inline event handlers, so forms must not carry any.
assert.ok(!/\son\w+\s*=/.test(view), 'Manage.cshtml must not use inline event handlers');
assert.ok(view.includes('data-confirm-delete'), 'delete forms must use the CSP-safe double-tap attribute');

// The character counter mixes digits and a slash; without an explicit direction it renders
// reversed ("2000 / 27") inside the Arabic RTL page.
assert.ok(/data-message-count[^>]*dir="ltr"/.test(view), 'character counter must pin dir="ltr"');

console.log('PASS notification compose: no native confirm, CSP-safe double-tap, counter direction pinned');

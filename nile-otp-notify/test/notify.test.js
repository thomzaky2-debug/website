import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

process.env.DATA_DIR = fs.mkdtempSync(path.join(os.tmpdir(), 'otp-notify-'));
process.env.SENDER = 'mock';
process.env.RESEND_COOLDOWN_SECONDS = '60';

const { config } = await import('../src/config.js');
const { createStore } = await import('../src/store.js');
const { hashApiKey } = await import('../src/auth.js');
const { createMockSender } = await import('../src/senders/mock.js');
const { createApp } = await import('../src/server.js');
const { createNotifyLimiter } = await import('../src/notify.js');

const KEY = 'notify-key-123';
const PHONE = '+201012345678';
const store = await createStore(config.dataDir);
store.addClient('chic-unique', 'Chic Unique', hashApiKey(KEY));
const sender = createMockSender();
const app = createApp({ store, sender });
let base;
const srv = await new Promise((resolve) => { const s = app.listen(0, () => { base = `http://127.0.0.1:${s.address().port}`; resolve(s); }); });
test.after(() => { srv.close(); store.close(); });
const post = (url, body, key = KEY) => fetch(base + url, { method: 'POST', headers: { 'content-type': 'application/json', 'x-api-key': key }, body: JSON.stringify(body) });

test('notify requires a valid API key', async () => {
  assert.equal((await post('/api/notify', { phoneNumber: PHONE, message: 'hi' }, '')).status, 401);
  assert.equal((await post('/api/notify', { phoneNumber: PHONE, message: 'hi' }, 'bad')).status, 401);
});

test('notify validates phone and message', async () => {
  assert.equal((await post('/api/notify', { phoneNumber: '0101', message: 'hi' })).status, 400);
  assert.equal((await post('/api/notify', { phoneNumber: PHONE, message: '' })).status, 400);
  assert.equal((await post('/api/notify', { phoneNumber: PHONE, message: 'x'.repeat(1001) })).status, 400);
});

test('notify delivers text and does not touch OTP limits', async () => {
  const r = await post('/api/notify', { phoneNumber: PHONE, message: 'تم دفع طلبك' });
  assert.equal(r.status, 200);
  assert.deepEqual(await r.json(), { delivered: true });
  assert.equal(sender.notices().at(-1).text, 'تم دفع طلبك');
  // an OTP right after a notice is still allowed (no shared cooldown)
  assert.equal((await post('/api/otp/send', { phoneNumber: PHONE })).status, 200);
});

test('notify has its own conservative per-number limit', () => {
  let t = 1000;
  const lim = createNotifyLimiter({ perPhoneHour: 2, perClientHour: 5, now: () => t });
  assert.equal(lim.check('c', PHONE), null); lim.record('c', PHONE);
  assert.equal(lim.check('c', PHONE), null); lim.record('c', PHONE);
  assert.equal(lim.check('c', PHONE).reason, 'number_hourly_limit');
  t += 3600 * 1000 + 1;
  assert.equal(lim.check('c', PHONE), null);
});

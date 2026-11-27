import { maskPhone } from './config.js';

const E164 = /^\+[1-9]\d{6,14}$/;
const MAX_MESSAGE_LENGTH = 1000;
const HOUR_MS = 60 * 60 * 1000;

// Separate from the OTP limits on purpose: notices never touch the OTP send log,
// so a payment notice can not lock a customer out of a login code (or the reverse).
// In memory only: a restart resets the counters, which is fine because the website
// sends each notice once.
export function createNotifyLimiter({ perPhoneHour = 3, perClientHour = 60, now = Date.now } = {}) {
  const byPhone = new Map();
  const byClient = new Map();
  const prune = (list, t) => list.filter((x) => x > t - HOUR_MS);
  return {
    check(clientId, phone) {
      const t = now();
      const p = prune(byPhone.get(`${clientId}:${phone}`) ?? [], t);
      const c = prune(byClient.get(clientId) ?? [], t);
      if (p.length >= perPhoneHour) return { reason: 'number_hourly_limit', retryAfterSeconds: Math.ceil((p[0] + HOUR_MS - t) / 1000) };
      if (c.length >= perClientHour) return { reason: 'client_hourly_limit', retryAfterSeconds: Math.ceil((c[0] + HOUR_MS - t) / 1000) };
      return null;
    },
    record(clientId, phone) {
      const t = now();
      byPhone.set(`${clientId}:${phone}`, [...prune(byPhone.get(`${clientId}:${phone}`) ?? [], t), t]);
      byClient.set(clientId, [...prune(byClient.get(clientId) ?? [], t), t]);
    },
  };
}

// POST /api/notify { phoneNumber: "+20...", message: "text" }  (x-api-key, same as OTP)
export function registerNotify(app, { auth, sender, limiter = createNotifyLimiter() }) {
  app.post('/api/notify', auth, async (req, res) => {
    const { phoneNumber, message } = req.body ?? {};
    if (typeof phoneNumber !== 'string' || !E164.test(phoneNumber)) {
      return res.status(400).json({ error: 'phoneNumber must be E.164, e.g. +2010XXXXXXXX' });
    }
    const text = typeof message === 'string' ? message.trim() : '';
    if (!text || text.length > MAX_MESSAGE_LENGTH) {
      return res.status(400).json({ error: `message must be 1-${MAX_MESSAGE_LENGTH} characters` });
    }
    if (typeof sender.sendText !== 'function') {
      return res.status(501).json({ error: 'sender_cannot_send_text' });
    }
    if (!sender.isReady()) {
      return res.status(503).json({ error: 'sender_not_ready' });
    }
    const blocked = limiter.check(req.client.id, phoneNumber);
    if (blocked) {
      return res.status(429).json({ error: 'rate_limited', reason: blocked.reason, retryAfterSeconds: blocked.retryAfterSeconds });
    }
    try {
      await sender.sendText(phoneNumber, text);
      limiter.record(req.client.id, phoneNumber);
      return res.json({ delivered: true });
    } catch (err) {
      console.error(`[notify] failed for ${maskPhone(phoneNumber)}: ${err.message}`);
      if (err.message === 'recipient_not_on_whatsapp') return res.status(422).json({ error: 'recipient_not_on_whatsapp' });
      return res.status(502).json({ error: 'delivery_failed' });
    }
  });
}

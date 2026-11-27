import path from 'node:path';
import { config, maskPhone } from '../config.js';

// Serializes sends and enforces minimum spacing to reduce anti-abuse triggers.
// The chain self-heals: a failed send rejects to its caller but does NOT poison
// later sends.
export function createSendChain() {
  let queue = Promise.resolve();
  let lastSendAt = 0;
  return (minIntervalMs, work) => {
    queue = queue.catch(() => {}).then(async () => {
      const wait = minIntervalMs - (Date.now() - lastSendAt);
      if (wait > 0) await new Promise((r) => setTimeout(r, wait));
      await work();
      lastSendAt = Date.now();
    });
    return queue;
  };
}

const PAIRING_COOLDOWN_MS = 60_000;
const RECONNECT_BASE_MS = 2_000;
const RECONNECT_MAX_MS = 60_000;

// Decide what to do when the WhatsApp connection closes. Known real-world cases
// (see README "Known upstream problems"):
// - 515 restartRequired is NORMAL after a successful pairing: WhatsApp forces a
//   reconnect with the fresh credentials. Reconnect IMMEDIATELY; delaying risks a
//   stale session (https://github.com/WhiskeySockets/Baileys/issues/2512).
// - 401 loggedOut, 403 forbidden, 411 multideviceMismatch, 440 connectionReplaced
//   are terminal for this session: stop, re-pair manually. Reconnecting on 440
//   fights whichever session took over and loops forever.
// - Everything else (408 timedOut/connectionLost, 428 connectionClosed, 500
//   badSession, 503 unavailableService, unknown) is transient: reconnect with
//   capped exponential backoff.
export function classifyDisconnect(DisconnectReason, code) {
  const D = DisconnectReason || {};
  if (code === D.loggedOut || code === D.forbidden ||
      code === D.multideviceMismatch || code === D.connectionReplaced) {
    return 'stop';
  }
  if (code === D.restartRequired) return 'immediate';
  return 'backoff';
}

// Pure close-event planner, wired to real state in init(). 'stale' means the event
// came from a socket we already replaced (init() ends the old socket, whose close
// event then reaches its old handler): those events must be ignored, or every
// deliberate replacement would schedule a duplicate reconnect.
export function planCloseAction(DisconnectReason, { code, stale = false, stopped = false, attempts = 0 } = {}) {
  if (stale || stopped) return { action: 'ignore' };
  const cls = classifyDisconnect(DisconnectReason, code);
  if (cls === 'stop') return { action: 'stop' };
  if (cls === 'immediate') return { action: 'reconnect', delayMs: 0 };
  return { action: 'reconnect', delayMs: Math.min(RECONNECT_MAX_MS, RECONNECT_BASE_MS * 2 ** attempts) };
}

// Resolve the WhatsApp Web client version. The version bundled with Baileys (and
// returned by fetchLatestBaileysVersion) can be stale and then WhatsApp refuses
// registration AND login with 405 / "Couldn't link device"
// (https://github.com/WhiskeySockets/Baileys/issues/2679). The only live source is
// fetchLatestWaWebVersion(), read from web.whatsapp.com. If the fetch fails we
// fall back to the bundled default and warn: connecting may then fail until the
// live fetch succeeds, but the service still starts and retries.
export async function resolveWaVersion(fetchers) {
  const { fetchLatestWaWebVersion, fetchLatestBaileysVersion } = fetchers;
  if (fetchLatestWaWebVersion) {
    try {
      // NOTE (rc14): on fetch/parse failure this does NOT throw; it returns the
      // bundled version with isLatest:false and an error field. Only a true
      // isLatest counts as live, otherwise we would log "(live-web)" for the
      // stale bundled version - exactly the failure this check exists to catch.
      const r = await fetchLatestWaWebVersion();
      if (r && r.isLatest === true && !r.error && Array.isArray(r.version)) {
        return { version: r.version, source: 'live-web' };
      }
    } catch { /* fall through */ }
  }
  if (fetchLatestBaileysVersion) {
    try {
      const r = await fetchLatestBaileysVersion();
      if (r && Array.isArray(r.version)) {
        return { version: r.version, source: 'github-defaults', warning: 'live WhatsApp Web version fetch failed; using the Baileys defaults version, which may be stale and rejected' };
      }
    } catch { /* fall through */ }
  }
  return { version: undefined, source: 'bundled', warning: 'could not fetch a current WhatsApp Web version; using the version bundled with Baileys, which may be rejected' };
}

// Unofficial WhatsApp sender built on Baileys (MIT licensed,
// https://github.com/WhiskeySockets/Baileys). It connects the dedicated number as a
// linked device. WhatsApp's terms do not allow unofficial clients: the number can be
// restricted or permanently banned, and sessions can drop. Use only a number you can
// afford to lose, keep send volume low, and keep BAILEYS_MIN_INTERVAL_MS spacing.
// Map a pairing failure to a small allowlisted category for the admin
// diagnostic surface. Raw err.message is NEVER exposed: Baileys errors can
// carry upstream detail. Numeric WhatsApp/Boom status codes are not secrets
// and pass through; everything else collapses to 'unexpected'.
export function classifyPairingError(err) {
  const msg = String(err?.message || '');
  const status = err?.output?.statusCode;
  if (status === 428 || msg === 'Connection Closed') return { result: 'socket_not_open', status: 428 };
  if (msg === 'transport_wait_timeout') return { result: 'socket_not_open', status: null };
  if (msg === 'whatsapp_sender_not_started') return { result: 'sender_not_started', status: null };
  if (msg === 'pairing_cooldown') return { result: 'cooldown', status: null };
  if (msg === 'already_paired') return { result: 'already_paired', status: null };
  return { result: 'unexpected', status: Number.isFinite(status) ? status : null };
}

// Bounded wait for the actual WebSocket transport to be open. A QR event or
// connection 'connecting' is NOT proof of an open transport: sendRawMessage
// still throws Boom 428 while ws.isOpen is false (socket.js:58-61). Uses
// Baileys' own waitForSocketOpen (socket.js:509-527: instant return when open,
// instant Boom 428 when closed/closing, event wait when connecting, listener
// cleanup in finally — but NO internal timeout, so the deadline here is
// required). race() subscribes both promises up front, so a late rejection
// after the deadline is already handled: no unhandled rejection. When the
// socket shape is unknown (test seam without ws), proceeds without waiting.
export async function waitTransportOpen(sockLike, timeoutMs) {
  if (sockLike?.ws?.isOpen) return;
  if (!sockLike?.ws || typeof sockLike.waitForSocketOpen !== 'function') return;
  let timer;
  try {
    await Promise.race([
      sockLike.waitForSocketOpen(),
      new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('transport_wait_timeout')), timeoutMs);
      }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}

export function createBaileysSender() {
  let sock = null;
  let ready = false;
  let lastQr = null;
  let authStateRef = null;
  let pairingTransportWaitMs = 15_000;
  let pairing = null; // { phone, code, at }
  let stopped = false;
  let reconnectTimer = null;
  let reconnectAttempts = 0;
  const chain = createSendChain();

  // In-memory diagnostic snapshot for GET /api/sender/status. Allowlisted
  // fields only: connection state, last close status, last QR time, last
  // pairing outcome category. No messages, no phone numbers, no credentials.
  // Lost on process restart by design (nothing sensitive persists).
  const diag = {
    connection: 'init', // init | connecting | open | closed | stopped
    lastCloseStatus: null,
    lastCloseAt: null,
    lastQrAt: null,
    lastPairingResult: null, // code_sent | socket_not_open | sender_not_started | cooldown | already_paired | unexpected
    lastPairingStatus: null,
    lastPairingAt: null,
  };

  function clearReconnect() {
    if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null; }
  }

  async function init() {
    let makeWASocket, useMultiFileAuthState, DisconnectReason, Browsers,
        makeCacheableSignalKeyStore, fetchLatestWaWebVersion, fetchLatestBaileysVersion,
        qrcode, pino;
    try {
      ({ default: makeWASocket, useMultiFileAuthState, DisconnectReason, Browsers,
         makeCacheableSignalKeyStore, fetchLatestWaWebVersion, fetchLatestBaileysVersion } =
        await import('@whiskeysockets/baileys'));
      qrcode = (await import('qrcode-terminal')).default;
      pino = (await import('pino')).default;
    } catch {
      throw new Error(
        'Baileys is not installed. Run: npm install @whiskeysockets/baileys qrcode-terminal'
      );
    }

    stopped = false;
    clearReconnect();
    diag.connection = 'connecting';

    // Close any previous socket before creating a new one, so reconnects never
    // stack sockets or duplicate listeners.
    if (sock) {
      try { sock.end?.(); } catch { /* already closed */ }
      sock = null;
      ready = false;
    }

    const resolved = await resolveWaVersion({ fetchLatestWaWebVersion, fetchLatestBaileysVersion });
    if (resolved.warning) console.warn(`[baileys] ${resolved.warning}`);
    console.log(`[baileys] WhatsApp Web version ${resolved.version ? resolved.version.join('.') : 'bundled'} (${resolved.source})`);

    const mySock = (() => { let s; return (v) => (v === undefined ? s : (s = v)); })();
    const authDir = path.join(config.dataDir, 'baileys-auth');
    const { state, saveCreds } = await useMultiFileAuthState(authDir);
    authStateRef = state;
    // Quiet the library's verbose info-level pino logs (handshake/registration
    // payloads); warnings and errors still surface.
    const logger = pino({ level: process.env.BAILEYS_LOG_LEVEL || 'warn' });
    sock = makeWASocket({
      version: resolved.version,
      auth: {
        creds: state.creds,
        keys: makeCacheableSignalKeyStore(state.keys, logger),
      },
      logger,
      printQRInTerminal: false,
      // WhatsApp rejects the 'Desktop' device descriptor with 428 before any QR
      // (https://github.com/WhiskeySockets/Baileys/issues/2671). 'Chrome' works.
      browser: Browsers.ubuntu('Chrome'),
    });
    mySock(sock);

    // ev.process serializes event batches; awaiting saveCreds BEFORE the close
    // handler runs avoids the race where a 515 reconnect reads credentials that
    // were not persisted yet.
    sock.ev.process(async (events) => {
      if (events['creds.update']) {
        try { await saveCreds(); } catch (e) { console.error('[baileys] failed to save credentials:', e.message); }
      }
      const update = events['connection.update'];
      if (!update) return;
      const { connection, lastDisconnect, qr } = update;
      if (qr) {
        // Never print the QR (a session credential) into routine logs by default.
        // It is served over the admin endpoint; opt into terminal output with
        // BAILEYS_LOG_QR=true on a private machine only.
        lastQr = qr;
        diag.lastQrAt = new Date().toISOString();
        if (diag.connection !== 'open') diag.connection = 'connecting';
        console.log('[baileys] QR ready: fetch it via GET /api/sender/qr with the admin token.');
        if (process.env.BAILEYS_LOG_QR === 'true') qrcode.generate(qr, { small: true });
      }
      if (connection === 'open') {
        ready = true;
        diag.connection = 'open';
        lastQr = null;
        pairing = null;
        reconnectAttempts = 0;
        console.log('[baileys] connected');
      }
      if (connection === 'close') {
        const code = lastDisconnect?.error?.output?.statusCode;
        diag.lastCloseStatus = code ?? null;
        diag.lastCloseAt = new Date().toISOString();
        // Events from a socket we deliberately replaced (init() ends the old one,
        // whose close then fires into this old handler) must not act.
        const plan = planCloseAction(DisconnectReason, {
          code, stale: mySock() !== sock || sock === null, stopped, attempts: reconnectAttempts,
        });
        if (plan.action === 'ignore') return;
        ready = false;
        diag.connection = plan.action === 'stop' ? 'stopped' : 'closed';
        console.log(`[baileys] connection closed (status ${code ?? 'unknown'}, action ${plan.action})`);
        if (plan.action === 'stop') {
          stopped = true;
          if (code === DisconnectReason.connectionReplaced) {
            console.log('[baileys] another session took over this number. NOT reconnecting; re-pair manually if this was you.');
          } else {
            console.log('[baileys] session is over (logged out / forbidden / device mismatch). Delete the baileys-auth directory and re-pair.');
          }
          return;
        }
        if (plan.delayMs > 0) reconnectAttempts++;
        clearReconnect();
        reconnectTimer = setTimeout(() => {
          init().catch((e) => {
            console.error('[baileys] re-init failed:', e.message);
            reconnectAttempts++;
            reconnectTimer = setTimeout(() => init().catch(() => {}),
              Math.min(RECONNECT_MAX_MS, RECONNECT_BASE_MS * 2 ** reconnectAttempts));
            if (reconnectTimer.unref) reconnectTimer.unref();
          });
        }, plan.delayMs);
        if (reconnectTimer.unref) reconnectTimer.unref();
      }
    });
  }

  async function deliverText(phone, text) {
    if (!ready || !sock) {
      throw new Error('whatsapp_sender_not_ready');
    }
    const digits = String(phone).replace(/\D/g, '');
    const jid = `${digits}@s.whatsapp.net`;
    await chain(config.baileysMinIntervalMs, async () => {
      // Fail fast with a clear reason when the recipient has no WhatsApp account,
      // instead of the message silently never arriving.
      let targetJid = jid;
      try {
        // A successful probe returning [] (or exists:false) means the number has no
        // WhatsApp account: fail fast. Only a THROWN probe (network hiccup) falls
        // back to the plain JID.
        const probe = await sock.onWhatsApp(jid);
        if (!probe || probe.length === 0 || probe[0]?.exists === false) {
          throw new Error('recipient_not_on_whatsapp');
        }
        if (probe[0]?.jid) targetJid = probe[0].jid;
      } catch (e) {
        if (e.message === 'recipient_not_on_whatsapp') throw e;
        // Probe itself failed (network hiccup): try the plain JID rather than fail the send.
      }
      await sock.sendMessage(targetJid, { text });
    });
  }

  async function send(phone, code) {
    await deliverText(phone, `Your verification code is: ${code}\nIt expires in 10 minutes. Do not share it.`);
    console.log(`[baileys] OTP sent to ${maskPhone(phone)}`);
    return { delivered: true, provider: 'baileys' };
  }

  // Plain notice (not a code). Same serialized, spaced chain as OTP sends.
  async function sendText(phone, text) {
    await deliverText(phone, text);
    console.log(`[baileys] notice sent to ${maskPhone(phone)}`);
    return { delivered: true, provider: 'baileys' };
  }

  // Phone-number pairing for hosts with no terminal: returns an 8-character code
  // that the owner enters in WhatsApp > Linked devices > Link with phone number.
  // One code per 60 seconds; nothing is issued automatically.
  async function requestPairingCode(phone) {
    try {
      if (!sock) throw new Error('whatsapp_sender_not_started');
      if (ready) throw new Error('already_paired');
      if (pairing && Date.now() - pairing.at < PAIRING_COOLDOWN_MS) {
        throw new Error('pairing_cooldown');
      }
      // The transport must actually be OPEN before the pairing send. Without
      // this, a tap into a dead/closed socket fails 428 AFTER Baileys has
      // already persisted creds.me + pairingCode (socket.js:601-607).
      await waitTransportOpen(sock, pairingTransportWaitMs);
      const code = await sock.requestPairingCode(String(phone).replace(/\D/g, ''));
      pairing = { phone, code, at: Date.now() };
      diag.lastPairingResult = 'code_sent';
      diag.lastPairingStatus = null;
      diag.lastPairingAt = new Date().toISOString();
      return code;
    } catch (err) {
      const c = classifyPairingError(err);
      diag.lastPairingResult = c.result;
      diag.lastPairingStatus = c.status;
      diag.lastPairingAt = new Date().toISOString();
      throw err;
    }
  }

  return {
    name: 'baileys',
    init,
    isReady: () => ready,
    send,
    sendText,
    getQr: () => lastQr,
    getPairing: () => pairing,
    // registered/partialCreds are derived live from the auth state: they are
    // the poisoned-auth diagnostic (registered=false + partialCreds=true means
    // creds.me was persisted without the link ever completing). Booleans only,
    // no credential material. null when auth state was never loaded (tests).
    getDiag: () => ({
      ...diag,
      registered: authStateRef ? authStateRef.creds.registered === true : null,
      partialCreds: authStateRef
        ? authStateRef.creds.me !== undefined && authStateRef.creds.registered !== true
        : null,
    }),
    requestPairingCode,
    // Test-only seam: lets unit tests exercise send/pairing paths without a real
    // WhatsApp connection. opts.ready marks the sender connected; opts.authState
    // stands in for the loaded auth state (diag flags).
    _injectSockForTest: (fake, opts = {}) => {
      sock = fake;
      if (opts.ready) ready = true;
      if (opts.authState) authStateRef = opts.authState;
      if (Number.isFinite(opts.transportWaitMs)) pairingTransportWaitMs = opts.transportWaitMs;
    },
  };
}

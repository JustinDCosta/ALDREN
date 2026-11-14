// ALDREN Companion: relays commands from ALDREN's native host to Chrome. Page content travels back as data only;
// nothing here acts on what a page says. Focus rules carry their own end time and expire even if ALDREN isn't running.
const HOST = 'com.aldren.companion';
const FOCUS_RULE_BASE = 1000;
let native = null;

function reply(id, ok, result, error) {
  native?.postMessage({ id, ok, result: result ?? {}, error: error ?? null });
}

async function activeTab() {
  const [tab] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
  if (!tab) throw new Error('No browser tab is active.');
  return tab;
}

function isWebPage(url) {
  return /^https?:\/\//i.test(url || '');
}

// The page helpers live in the extension's isolated world, injected on demand; the page itself can't reach them.
async function inPage(tab, fn, args) {
  if (!isWebPage(tab.url)) throw new Error('ALDREN only works on web pages (http or https).');
  await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ['content.js'] });
  const [result] = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (name, a) => window.__aldren[name](a),
    args: [fn, args ?? {}],
  });
  if (result?.result?.error) throw new Error(result.result.error);
  return result?.result;
}

async function acting(tab, work) {
  await chrome.action.setBadgeText({ tabId: tab.id, text: 'ACT' });
  await chrome.action.setBadgeBackgroundColor({ tabId: tab.id, color: '#00B7C3' });
  try {
    return await work();
  } finally {
    setTimeout(() => chrome.action.setBadgeText({ tabId: tab.id, text: '' }), 1500);
  }
}

async function focusBlock({ domains, until, goal }) {
  const rules = (domains || []).slice(0, 200).map((domain, i) => ({
    id: FOCUS_RULE_BASE + i,
    priority: 1,
    action: { type: 'redirect', redirect: { extensionPath: '/blocked.html' } },
    condition: { requestDomains: [String(domain).toLowerCase()], resourceTypes: ['main_frame'] },
  }));
  await focusClear();
  await chrome.declarativeNetRequest.updateDynamicRules({ addRules: rules });
  await chrome.storage.local.set({ focus: { until, goal, domains } });
  await chrome.alarms.create('focus-end', { when: until });
  return { blocked: rules.length, until };
}

async function focusClear() {
  const existing = await chrome.declarativeNetRequest.getDynamicRules();
  const ids = existing.filter((r) => r.id >= FOCUS_RULE_BASE).map((r) => r.id);
  if (ids.length) await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds: ids });
  await chrome.storage.local.remove('focus');
  await chrome.alarms.clear('focus-end');
  return { cleared: ids.length };
}

// The lease: rules never outlive their end time, whether or not ALDREN is around to clear them.
async function expireFocusIfDue() {
  const { focus } = await chrome.storage.local.get('focus');
  if (focus && Date.now() >= focus.until) await focusClear();
}

chrome.alarms.onAlarm.addListener((alarm) => alarm.name === 'focus-end' && focusClear());
chrome.runtime.onStartup.addListener(expireFocusIfDue);
expireFocusIfDue();

const commands = {
  ping: async () => ({ pong: Date.now(), version: chrome.runtime.getManifest().version }),
  tabs: async () => (await chrome.tabs.query({})).map((t, i) => ({ index: i + 1, id: t.id, title: t.title, url: t.url, active: t.active })),
  open: async ({ url }) => {
    if (!isWebPage(url)) throw new Error('Only http and https addresses can be opened.');
    const tab = await chrome.tabs.create({ url });
    return { id: tab.id };
  },
  activate: async ({ tabId }) => {
    const tab = await chrome.tabs.update(tabId, { active: true });
    await chrome.windows.update(tab.windowId, { focused: true });
    return { id: tab.id, title: tab.title };
  },
  close: async ({ tabId }) => {
    await chrome.tabs.remove(tabId);
    return { closed: tabId };
  },
  back: async () => {
    const tab = await activeTab();
    await chrome.tabs.goBack(tab.id);
    return { back: true };
  },
  read: async () => inPage(await activeTab(), 'read'),
  click: async (args) => {
    const tab = await activeTab();
    return acting(tab, () => inPage(tab, 'click', args));
  },
  fill: async (args) => {
    const tab = await activeTab();
    return acting(tab, () => inPage(tab, 'fill', args));
  },
  scroll: async (args) => inPage(await activeTab(), 'scroll', args),
  download: async ({ url }) => {
    if (!isWebPage(url)) throw new Error('Only http and https files can be downloaded.');
    return { downloadId: await chrome.downloads.download({ url }) };
  },
  focusBlock,
  focusClear,
};

function connectNative() {
  native = chrome.runtime.connectNative(HOST); // an open port also keeps this service worker alive
  native.onMessage.addListener(async (msg) => {
    const handler = commands[msg?.cmd];
    if (!handler) return reply(msg?.id, false, null, 'Unknown command.');
    try {
      reply(msg.id, true, await handler(msg.args || {}), null);
    } catch (e) {
      reply(msg.id, false, null, String(e?.message || e));
    }
  });
  native.onDisconnect.addListener(() => {
    // Reading lastError marks it handled: "host not found" just means Settings > Browser > Connect hasn't been clicked yet.
    void chrome.runtime.lastError;
    native = null;
    setTimeout(connectNative, 5000);
  });
}

connectNative();

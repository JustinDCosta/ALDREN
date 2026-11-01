// Runs in the test page's isolated world. Page text goes back as data: nothing here reads it for instructions.
const MAX_TEXT = 50000;
const MAX_ELEMENTS = 500;
const elements = new Map(); // id → element, rebuilt by every read

function isVisible(el) {
  const style = getComputedStyle(el);
  return el.getClientRects().length > 0 && style.visibility !== 'hidden' && style.display !== 'none';
}

function labelOf(el) {
  return (el.getAttribute('aria-label') || el.labels?.[0]?.innerText || el.placeholder || el.innerText || el.value || el.name || '')
    .trim().replace(/\s+/g, ' ').slice(0, 200);
}

function read() {
  elements.clear();
  const items = [];
  for (const el of document.querySelectorAll('a[href], button, input, select, textarea, [role="button"]')) {
    if (items.length === MAX_ELEMENTS || !isVisible(el)) {
      continue;
    }

    const id = items.length + 1;
    elements.set(id, el);
    const r = el.getBoundingClientRect();
    items.push({
      id,
      tag: el.tagName.toLowerCase(),
      type: el.type || null,
      name: labelOf(el),
      value: 'value' in el && el.type !== 'password' ? el.value : null, // passwords never leave the page
      rect: [r.x, r.y, r.width, r.height].map(Math.round),
    });
  }

  return { title: document.title, url: location.href, text: document.body.innerText.slice(0, MAX_TEXT), elements: items };
}

function target(args) {
  const el = elements.get(args.id);
  if (!el || !el.isConnected) {
    throw new Error(`No element ${args.id}: read the page first.`);
  }
  return el;
}

function click(args) {
  target(args).click();
  return { clicked: args.id };
}

function fill(args) {
  const el = target(args);
  if (!('value' in el) || el.type === 'password') {
    throw new Error(`Element ${args.id} can't be filled.`);
  }

  el.focus();
  el.value = String(args.value ?? '');
  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
  return { filled: args.id };
}

const handlers = { read, click, fill };

if (globalThis.chrome?.runtime?.id) {
  const port = chrome.runtime.connect({ name: 'page' });
  port.onMessage.addListener((msg) => {
    try {
      const handler = handlers[msg.cmd];
      if (!handler) {
        throw new Error(`Unknown command: ${msg.cmd}`);
      }
      port.postMessage({ id: msg.id, ok: true, result: handler(msg.args || {}), error: null });
    } catch (e) {
      port.postMessage({ id: msg.id, ok: false, result: {}, error: String(e?.message ?? e) });
    }
  });
}

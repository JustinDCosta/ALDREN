// Injected on demand into the active tab's isolated world (from S6). Page text goes back as data; passwords never leave the
// page, and password or payment-card fields are never filled.
(() => {
  if (window.__aldren) return;
  const MAX_TEXT = 40000;
  const MAX_ELEMENTS = 300;
  const elements = new Map();

  const isVisible = (el) => {
    const style = getComputedStyle(el);
    return el.getClientRects().length > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };

  const labelOf = (el) =>
    (el.getAttribute('aria-label') || el.labels?.[0]?.innerText || el.placeholder || el.innerText || el.value || el.name || el.title || '')
      .trim().replace(/\s+/g, ' ').slice(0, 160);

  const isSensitiveField = (el) => {
    const hints = `${el.type || ''} ${el.name || ''} ${el.id || ''} ${el.getAttribute('autocomplete') || ''}`.toLowerCase();
    return el.type === 'password' || /cc-|card|cvv|cvc|iban|security.?code|one-time-code|otp/.test(hints);
  };

  const read = () => {
    elements.clear();
    const items = [];
    for (const el of document.querySelectorAll('a[href], button, input, select, textarea, [role="button"], [role="link"], [role="tab"]')) {
      if (items.length === MAX_ELEMENTS || !isVisible(el)) continue;
      const id = items.length + 1;
      elements.set(id, el);
      items.push({
        id,
        tag: el.tagName.toLowerCase(),
        type: el.type || el.getAttribute('role') || null,
        name: labelOf(el),
        href: el.href ? String(el.href).slice(0, 300) : null,
        value: 'value' in el && !isSensitiveField(el) && el.tagName !== 'BUTTON' ? String(el.value).slice(0, 200) : null,
        sensitive: isSensitiveField(el),
        inForm: !!el.closest('form'),
      });
    }

    return { title: document.title, url: location.href, text: (document.body?.innerText || '').slice(0, MAX_TEXT), elements: items };
  };

  const target = (id) => {
    const el = elements.get(id);
    if (!el || !el.isConnected) throw new Error(`No element ${id}: read the page again first.`);
    return el;
  };

  const click = ({ id }) => {
    try {
      const el = target(id);
      el.scrollIntoView({ block: 'center' });
      el.click();
      return { clicked: id, name: labelOf(el) };
    } catch (e) {
      return { error: String(e.message || e) };
    }
  };

  const fill = ({ id, value }) => {
    try {
      const el = target(id);
      if (!('value' in el) || isSensitiveField(el)) throw new Error(`Element ${id} can't be filled by ALDREN.`);
      el.focus();
      el.value = String(value ?? '');
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
      return { filled: id };
    } catch (e) {
      return { error: String(e.message || e) };
    }
  };

  const scroll = ({ direction }) => {
    window.scrollBy({ top: (direction === 'up' ? -1 : 1) * window.innerHeight * 0.8, behavior: 'smooth' });
    return { scrolled: direction || 'down' };
  };

  window.__aldren = { read, click, fill, scroll };
})();

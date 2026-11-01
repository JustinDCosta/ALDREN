// Relays commands between ALDREN's native host and the test page. Answers ping itself, so ping measures the bridge alone.
const HOST = 'com.aldren.bridge';
let native = null;
let page = null; // port from the content script on the local test page

function reply(id, ok, result, error) {
  native?.postMessage({ id, ok, result, error });
}

function connectNative() {
  native = chrome.runtime.connectNative(HOST); // an open native port also keeps this service worker alive
  native.onMessage.addListener((msg) => {
    if (msg.cmd === 'ping') {
      reply(msg.id, true, { pong: Date.now() }, null);
    } else if (page) {
      page.postMessage(msg);
    } else {
      reply(msg.id, false, {}, 'No test page is open.');
    }
  });
  native.onDisconnect.addListener(() => {
    console.warn('Native host disconnected:', chrome.runtime.lastError?.message);
    native = null;
    setTimeout(connectNative, 2000);
  });
}

chrome.runtime.onConnect.addListener((port) => {
  if (port.name !== 'page' || !port.sender?.url?.startsWith('http://127.0.0.1')) {
    port.disconnect();
    return;
  }

  page = port;
  port.onMessage.addListener((msg) => native?.postMessage(msg));
  port.onDisconnect.addListener(() => {
    if (page === port) {
      page = null;
    }
  });
});

connectNative();

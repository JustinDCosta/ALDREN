// Shows the session's goal and end time from the extension's own storage.
chrome.storage.local.get('focus').then(({ focus }) => {
  if (!focus) return;
  if (focus.goal) document.getElementById('goal').textContent = `Back to it: ${focus.goal}.`;
  document.getElementById('until').textContent = `This site opens again at ${new Date(focus.until).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}.`;
});

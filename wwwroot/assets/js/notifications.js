(() => {
  async function refresh() {
    const badge = document.querySelector('[data-notification-count]');
    if (!badge || document.hidden) return;
    try {
      const response = await fetch('/Account/Notifications/Unread', { headers: { Accept: 'application/json' } });
      if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return;
      const { count } = await response.json();
      badge.textContent = count > 99 ? '99+' : String(count);
      badge.hidden = count === 0;
    } catch { /* Keep the last known unread count during connection loss. */ }
  }
  refresh(); setInterval(refresh, 60000); document.addEventListener('visibilitychange', refresh);
  document.addEventListener('submit', async event => {
      const form = event.target;
      if (!form.matches('[data-notification-read]') || event.defaultPrevented) return;
      event.preventDefault();
      const button = form.querySelector('button'); button.disabled = true;
      try {
        const response = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' } });
        if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error();
        const card = form.closest('article'); card.classList.remove('is-unread'); card.classList.add('is-read'); form.remove(); refresh();
      } catch {
        form.querySelector('[role=status]').textContent = document.documentElement.lang === 'ar' ? 'تعذر الحفظ. حاول مرة أخرى.' : 'Could not save. Please try again.';
        button.disabled = false;
      }
  });
})();

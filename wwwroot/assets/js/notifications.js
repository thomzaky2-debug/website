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

(() => {
  const form = document.querySelector('[data-notification-compose]');
  if (!form) return;
  const ar = document.documentElement.lang === 'ar';
  const email = form.querySelector('[name=RecipientEmail]');
  const search = form.querySelector('#customer-search');
  const results = form.querySelector('#customer-results');
  const title = form.querySelector('[name=Title]');
  const body = form.querySelector('[name=Body]');
  let timer, request, sequence = 0;
  const all = () => form.querySelector('[name=Audience]:checked')?.value === 'everyone';
  function preview() {
    form.querySelector('[data-customer-picker]').hidden = all();
    form.querySelector('[data-broadcast-note]').hidden = !all();
    email.disabled = all(); email.required = !all();
    document.querySelector('[data-preview-recipient]').textContent = all() ? (ar ? 'إلى جميع العملاء' : 'To everyone') : email.value || (ar ? 'اختر المستلم' : 'Choose a recipient');
    document.querySelector('[data-preview-title]').textContent = title.value || (ar ? 'عنوان الإشعار' : 'Your notification title');
    document.querySelector('[data-preview-body]').textContent = body.value || (ar ? 'ستظهر رسالتك هنا أثناء الكتابة.' : 'Your message will appear here as you type.');
    form.querySelector('[data-message-count]').textContent = `${body.value.length} / 2000`;
  }
  async function lookup() {
    const version = ++sequence;
    request?.abort(); request = new AbortController();
    results.textContent = ar ? 'جارٍ البحث…' : 'Finding customers…';
    try {
      const response = await fetch('/Admin/Notifications/Customers?q=' + encodeURIComponent(search.value), { signal: request.signal, headers: { Accept: 'application/json' } });
      if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error();
      const customers = await response.json();
      if (version !== sequence) return;
      results.replaceChildren();
      for (const customer of customers) {
        const button = document.createElement('button'); button.type = 'button';
        const name = document.createElement('strong'); name.textContent = customer.name;
        const detail = document.createElement('small'); detail.textContent = customer.email;
        button.append(name, detail);
        button.addEventListener('click', () => { email.value = customer.email; results.replaceChildren(); search.value = ''; preview(); title.focus(); });
        results.append(button);
      }
      if (!customers.length) results.textContent = ar ? 'لا توجد نتائج. جرّب اسماً أو بريداً آخر.' : 'No customers found. Try another name or email.';
    } catch (error) {
      if (error.name !== 'AbortError' && version === sequence) results.textContent = ar ? 'تعذر تحميل العملاء. يمكنك إدخال البريد أدناه.' : 'Could not load customers. You can enter their email below.';
    }
  }
  search.addEventListener('input', () => { clearTimeout(timer); ++sequence; request?.abort(); timer = setTimeout(lookup, 250); });
  search.addEventListener('focus', () => { if (!results.children.length) lookup(); });
  form.addEventListener('input', preview); form.addEventListener('change', preview);
  form.addEventListener('submit', event => {
    if (all() && !confirm(ar ? 'إرسال هذا الإشعار إلى جميع العملاء؟' : 'Send this notification to all customers?')) { event.preventDefault(); return; }
    if (form.checkValidity()) form.querySelector('[type=submit]').disabled = true;
  });
  preview();
})();

'use strict';
// Progressive enhancement: controllers still return their normal server-rendered views.
(() => {
  let busy = false;
  // Same-document fragment navigations (in-page anchors such as the settings
  // tabs) also fire popstate in current browsers; only real location changes
  // (path or query) should trigger a content swap.
  let lastAsyncPath = location.pathname + location.search;
  const dirtyForms = new WeakSet();
  const formKey = form => {
    const url = new URL(form.action, location.href);
    const identity = [...form.querySelectorAll('input[type="hidden"]')]
      .filter(input => ['id', 'Id', 'productId', 'orderId', 'cartKey'].includes(input.name))
      .map(input => input.name + '=' + input.value).join('&');
    return url.pathname + '?' + identity;
  };
  document.addEventListener('submit', event => {
    if (busy) { event.preventDefault(); event.stopImmediatePropagation(); }
  }, true);
  document.addEventListener('input', event => {
    if (event.target.form && event.target.name !== '__RequestVerificationToken') dirtyForms.add(event.target.form);
  });
  document.addEventListener('change', event => {
    if (event.target.form) dirtyForms.add(event.target.form);
  });
  function reportError(form) {
    if (window.NileShopToast) {
      window.NileShopToast(document.body.dataset.asyncError, 'error');
      return;
    }
    let message = document.querySelector('#async-update-error');
    if (!message) {
      message = document.createElement('p');
      message.id = 'async-update-error';
      message.className = 'notice';
      message.setAttribute('role', 'alert');
      (form || document.querySelector('#main')).prepend(message);
    }
    message.textContent = document.body.dataset.asyncError;
  }
  async function update(url, { method = 'GET', body, form, historyMode = 'replace' } = {}) {
    if (busy) return false;
    if (new URL(url, location.href).origin !== location.origin) return false;
    busy = true;
    const main = document.querySelector('#main');
    const position = { x: scrollX, y: scrollY };
    const active = document.activeElement;
    const activeId = active?.id;
    const activeName = active?.name;
    const sourceKey = form ? formKey(form) : null;
    const openSections = [...main.querySelectorAll('details')].map((node, index) => node.open ? index : -1);
    const buttons = form ? [...form.querySelectorAll('button[type="submit"],button:not([type])')] : [];
    const disabled = buttons.map(button => button.disabled);
    // Serialize before disabling controls so submitted values are retained.
    buttons.forEach(button => button.disabled = true);
    form?.setAttribute('aria-busy', 'true');
    main.setAttribute('aria-busy', 'true');
    try {
      const response = await window.fetch(url, {
        method, body, credentials: 'same-origin',
        headers: { Accept: 'text/html', 'X-NileShop-Async': 'true' }
      });
      const destination = new URL(response.url);
      if (destination.origin !== location.origin) throw new Error('Unexpected destination');
      if (/\/(?:Identity\/Account|Account)\/Login/i.test(destination.pathname)) {
        // Signing in remains an intentional navigation, including for guest saved items.
        location.assign(destination.href);
        return false;
      }
      if (!response.ok || !response.headers.get('Content-Type')?.includes('text/html')) throw new Error('Update failed');
      const page = new DOMParser().parseFromString(await response.text(), 'text/html');
      const nextMain = page.querySelector('#main');
      if (!nextMain) throw new Error('Missing content');
      // Retain actual draft forms (including selected files) unrelated to the submitted action.
      const drafts = [...main.querySelectorAll('form')].filter(node => node !== form && node.method.toUpperCase() !== 'GET' && dirtyForms.has(node));
      for (const draft of drafts) {
        const match = [...nextMain.querySelectorAll('form')].find(node => formKey(node) === formKey(draft));
        if (match) {
          const token = match.querySelector('[name="__RequestVerificationToken"]')?.value;
          if (token && draft.elements.namedItem('__RequestVerificationToken')) draft.elements.namedItem('__RequestVerificationToken').value = token;
          match.replaceWith(draft);
        }
      }
      window.disposeNileShopPage?.();
      main.replaceChildren(...nextMain.childNodes);
      const headerCatalog = document.getElementById('header-catalog');
      const nextHeaderCatalog = page.getElementById('header-catalog');
      if (headerCatalog && nextHeaderCatalog) {
        nextHeaderCatalog.open = headerCatalog.open;
        headerCatalog.replaceWith(nextHeaderCatalog);
      }
      document.title = page.title;
      document.querySelectorAll('#site-nav a').forEach(link => {
        const next = [...page.querySelectorAll('#site-nav a')].find(node => node.getAttribute('href') === link.getAttribute('href'));
        if (next?.hasAttribute('aria-current')) link.setAttribute('aria-current', next.getAttribute('aria-current'));
        else link.removeAttribute('aria-current');
      });
      const samePage = destination.pathname.toLowerCase() === location.pathname.toLowerCase();
      if (samePage) main.querySelectorAll('details').forEach((node, index) => { if (openSections.includes(index)) node.open = true; });
      const counts = page.querySelectorAll('.cart-count');
      if (counts.length) document.querySelectorAll('.cart-count').forEach(node => node.textContent = counts[0].textContent);
      const finalUrl = destination.pathname + destination.search + (destination.hash || (samePage ? location.hash : ''));
      if (finalUrl !== location.pathname + location.search + location.hash) history[historyMode === 'push' ? 'pushState' : 'replaceState']({}, '', finalUrl);
      lastAsyncPath = location.pathname + location.search;
      window.initializeNileShopPage?.();
      const source = sourceKey && [...main.querySelectorAll('form')].find(node => formKey(node) === sourceKey);
      const focus = (activeId && document.getElementById(activeId)) || (activeName && source?.elements.namedItem(activeName)) || source?.querySelector('button[type="submit"],button:not([type])');
      focus?.focus({ preventScroll: true });
      const validation = main.querySelector('.validation-summary-errors, .field-validation-error');
      if (validation) {
        validation.setAttribute('tabindex', '-1');
        validation.focus({ preventScroll: true });
      }
      window.scrollTo(position.x, position.y);
      if (validation) {
        const message = validation.querySelector('li') || validation;
        if (message.textContent.trim()) window.NileShopToast?.(message.textContent.trim(), 'error');
      }
      return true;
    } catch {
      // Never retry a mutation automatically: the server may already have saved it.
      reportError(form?.isConnected ? form : null);
      return false;
    } finally {
      buttons.forEach((button, index) => { button.disabled = disabled[index]; });
      form?.removeAttribute('aria-busy');
      main.removeAttribute('aria-busy');
      busy = false;
      document.querySelector('#shopSkeletons')?.setAttribute('hidden', '');
      document.querySelectorAll('#collection > .category-product-groups, #collection > .product-grid').forEach(node => node.style.removeProperty('display'));
    }
  }
  document.addEventListener('submit', event => {
    const form = event.target;
    if (event.defaultPrevented || !form.matches('form[data-async-form]')) return;
    event.preventDefault();
    if (busy || !form.reportValidity()) return;
    const method = form.method.toUpperCase();
    const url = new URL(form.action || location.href, location.href);
    const data = new FormData(form);
    if (event.submitter?.name) data.append(event.submitter.name, event.submitter.value);
    if (method === 'GET') {
      url.search = new URLSearchParams(data).toString();
      update(url.href, { form, historyMode: 'push' });
    } else update(url.href, { method, body: data, form });
  });
  document.addEventListener('click', event => {
    const link = event.target.closest('.pagination a, .category-browse-nav a, .category-product-group .section-head > a, .search-results-meta > a');
    if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || link.target || link.hasAttribute('download')) return;
    if (new URL(link.href).origin !== location.origin) return;
    event.preventDefault();
    update(link.href, { historyMode: 'push' });
  });
  document.addEventListener('change', event => {
    const form = event.target.form;
    if (event.target.matches('select') && form?.matches('[data-async-form][method="get"]:not(.catalog-filters)') && !busy) form.requestSubmit();
  });
  window.addEventListener('popstate', () => {
    const path = location.pathname + location.search;
    if (path === lastAsyncPath) return; // hash-only navigation: let the browser scroll
    lastAsyncPath = path;
    update(location.href);
  });
  window.NileShopAsync = { refresh: () => update(location.href), update };
})();

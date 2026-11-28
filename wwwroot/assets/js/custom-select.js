'use strict';
// Storefront option picker (size / colour). Replaces the native popup with a listbox anchored under its own field.
// The real <select> stays in the form (visually hidden), so validation, submit values and cart logic are unchanged.
(() => {
  const SELECTOR = '.quick-add-form select, .product-buy-form select';
  let uid = 0;
  let openInstance = null;

  function labelText(select) {
    const label = select.closest('label') || (select.id && document.querySelector('label[for="' + CSS.escape(select.id) + '"]'));
    if (!label) return '';
    return [...label.childNodes].filter(n => n.nodeType === 3 || (n.nodeType === 1 && !n.contains(select) && n !== select)).map(n => n.textContent).join(' ').replace(/\s+/g, ' ').trim();
  }

  function enhance(select) {
    if (select.dataset.customSelect || select.multiple) return;
    select.dataset.customSelect = 'true';
    const id = 'cs' + (++uid);
    const wrap = document.createElement('div');
    wrap.className = 'cs';
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'cs-button';
    button.setAttribute('role', 'combobox');
    button.setAttribute('aria-haspopup', 'listbox');
    button.setAttribute('aria-expanded', 'false');
    button.setAttribute('aria-controls', id + '-list');
    const name = labelText(select);
    if (name) button.setAttribute('aria-label', name);
    const value = document.createElement('span');
    value.className = 'cs-value';
    const caret = document.createElement('span');
    caret.className = 'cs-caret';
    caret.setAttribute('aria-hidden', 'true');
    button.append(value, caret);
    const list = document.createElement('ul');
    list.className = 'cs-list';
    list.id = id + '-list';
    list.setAttribute('role', 'listbox');
    list.tabIndex = -1;
    list.hidden = true;
    if (name) list.setAttribute('aria-label', name);
    select.before(wrap);
    wrap.append(select, button, list);
    select.classList.add('cs-native');
    select.tabIndex = -1;
    select.setAttribute('aria-hidden', 'true');

    let active = -1;
    let typed = '';
    let typedTimer = 0;
    const items = () => [...list.children];

    function build() {
      list.replaceChildren();
      [...select.options].forEach((option, index) => {
        if (option.value === '' && select.required) return; // the placeholder is not a choice for a required field
        const li = document.createElement('li');
        li.id = id + '-o' + index;
        li.setAttribute('role', 'option');
        li.className = 'cs-option' + (option.value === '' ? ' is-placeholder' : '');
        li.dataset.index = String(index);
        li.textContent = option.textContent;
        li.setAttribute('aria-selected', String(option.selected));
        if (option.disabled) li.setAttribute('aria-disabled', 'true');
        list.append(li);
      });
    }
    function sync() {
      const option = select.options[select.selectedIndex];
      value.textContent = option ? option.textContent : '';
      wrap.classList.toggle('has-placeholder', !option || option.value === '');
      items().forEach(li => li.setAttribute('aria-selected', String(Number(li.dataset.index) === select.selectedIndex)));
    }
    function setActive(index, scroll = true) {
      const all = items();
      if (!all.length) return;
      active = Math.max(0, Math.min(all.length - 1, index));
      all.forEach((li, i) => li.classList.toggle('is-active', i === active));
      button.setAttribute('aria-activedescendant', all[active].id);
      if (scroll) {
        const el = all[active];
        if (el.offsetTop < list.scrollTop) list.scrollTop = el.offsetTop - 6;
        else if (el.offsetTop + el.offsetHeight > list.scrollTop + list.clientHeight) list.scrollTop = el.offsetTop + el.offsetHeight - list.clientHeight + 6;
      }
    }
    function place() {
      list.classList.remove('is-up');
      const rect = button.getBoundingClientRect();
      const below = innerHeight - rect.bottom;
      const above = rect.top;
      const needed = Math.min(list.scrollHeight, 240) + 8;
      if (below < needed && above > below) list.classList.add('is-up');
      list.style.maxHeight = Math.max(120, Math.min(240, (list.classList.contains('is-up') ? above : below) - 12)) + 'px';
    }
    function open() {
      if (!list.hidden) return;
      if (openInstance && openInstance !== api) openInstance.close();
      build();
      list.hidden = false;
      button.setAttribute('aria-expanded', 'true');
      wrap.classList.add('is-open');
      place();
      setActive(Math.max(0, items().findIndex(li => Number(li.dataset.index) === select.selectedIndex)));
      openInstance = api;
    }
    function close(restoreFocus = false) {
      if (list.hidden) return;
      list.hidden = true;
      button.setAttribute('aria-expanded', 'false');
      button.removeAttribute('aria-activedescendant');
      wrap.classList.remove('is-open');
      if (openInstance === api) openInstance = null;
      if (restoreFocus) button.focus();
    }
    function choose(index) {
      const option = select.options[index];
      if (!option || option.disabled) return;
      const changed = select.selectedIndex !== index;
      select.selectedIndex = index;
      sync();
      wrap.classList.remove('is-invalid');
      close(true);
      if (changed) {
        select.dispatchEvent(new Event('input', { bubbles: true }));
        select.dispatchEvent(new Event('change', { bubbles: true }));
      }
    }
    const api = { close, wrap };

    button.addEventListener('click', () => (list.hidden ? open() : close()));
    button.addEventListener('keydown', event => {
      const key = event.key;
      if (list.hidden) {
        if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(key)) { event.preventDefault(); open(); }
        return;
      }
      if (key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(true); }
      else if (key === 'ArrowDown') { event.preventDefault(); setActive(active + 1); }
      else if (key === 'ArrowUp') { event.preventDefault(); setActive(active - 1); }
      else if (key === 'Home') { event.preventDefault(); setActive(0); }
      else if (key === 'End') { event.preventDefault(); setActive(items().length - 1); }
      else if (key === 'Enter' || key === ' ') { event.preventDefault(); const li = items()[active]; if (li) choose(Number(li.dataset.index)); }
      else if (key === 'Tab') close();
      else if (key.length === 1 && !event.ctrlKey && !event.metaKey) {
        clearTimeout(typedTimer);
        typed += key.toLowerCase();
        typedTimer = setTimeout(() => { typed = ''; }, 600);
        const hit = items().findIndex(li => li.textContent.trim().toLowerCase().startsWith(typed));
        if (hit >= 0) setActive(hit);
      }
    });
    list.addEventListener('pointerdown', event => event.preventDefault()); // keep focus on the button
    list.addEventListener('click', event => {
      const li = event.target.closest('.cs-option');
      if (li) choose(Number(li.dataset.index));
    });
    list.addEventListener('pointermove', event => {
      const li = event.target.closest('.cs-option');
      if (li) setActive(Number(li.dataset.index), false);
    });
    select.addEventListener('focus', () => button.focus());
    select.addEventListener('invalid', () => { wrap.classList.add('is-invalid'); button.focus(); });
    select.addEventListener('change', sync);
    select.form?.addEventListener('reset', () => setTimeout(sync, 0));
    // Re-sync if something else (cart script, form restore) changes the value programmatically.
    new MutationObserver(sync).observe(select, { childList: true, attributes: true, subtree: true });
    sync();
  }

  document.addEventListener('pointerdown', event => {
    if (openInstance && !openInstance.wrap.contains(event.target)) openInstance.close();
  }, true);
  document.addEventListener('keydown', event => { if (event.key === 'Escape' && openInstance) openInstance.close(true); });
  addEventListener('resize', () => openInstance?.close());
  const scan = root => (root.matches?.(SELECTOR) ? [root] : []).concat([...(root.querySelectorAll?.(SELECTOR) || [])]).forEach(enhance);
  scan(document);
  new MutationObserver(records => records.forEach(r => r.addedNodes.forEach(n => n.nodeType === 1 && scan(n)))).observe(document.body, { childList: true, subtree: true });
})();

'use strict';
window.initializeNileShopPage = function () {
 window.disposeNileShopPage?.();
 const lifecycle = new AbortController();
 const timeouts = new Set(), intervals = new Set(), observers = [], cleanups = [];
 const setTimeout = (callback, delay) => { const id = window.setTimeout(() => { timeouts.delete(id); callback(); }, delay); timeouts.add(id); return id; };
 const clearTimeout = id => { timeouts.delete(id); window.clearTimeout(id); };
 const setInterval = (callback, delay) => { const id = window.setInterval(callback, delay); intervals.add(id); return id; };
 const fetch = (url, options = {}) => {
   // Join page disposal and optional per-request cancellation without requiring AbortSignal.any.
   const request = new AbortController();
   const abort = () => request.abort();
   lifecycle.signal.addEventListener('abort', abort, {once:true});
   options.signal?.addEventListener('abort', abort, {once:true});
   if (lifecycle.signal.aborted || options.signal?.aborted) request.abort();
   return window.fetch(url, {...options, signal:request.signal}).finally(() => {
     lifecycle.signal.removeEventListener('abort', abort);
     options.signal?.removeEventListener('abort', abort);
   });
 };
 window.disposeNileShopPage = () => {
   cleanups.forEach(cleanup => cleanup());
   lifecycle.abort(); timeouts.forEach(window.clearTimeout); intervals.forEach(window.clearInterval);
   observers.forEach(observer => observer.disconnect());
   document.querySelectorAll('[data-object-url]').forEach(node => { URL.revokeObjectURL(node.dataset.objectUrl); delete node.dataset.objectUrl; });
 };
'use strict';
document.documentElement.classList.remove('no-js');
document.querySelector('#audit-search')?.addEventListener('input',function(){const q=this.value.toLocaleLowerCase();document.querySelectorAll('#audit-table tbody tr').forEach(r=>r.hidden=!r.textContent.toLocaleLowerCase().includes(q));}, { signal: lifecycle.signal });
document.querySelectorAll('[data-preview]').forEach(b=>b.addEventListener('click',()=>{document.querySelector('.detail-image').src=b.dataset.preview;document.querySelectorAll('[data-preview]').forEach(node=>node.setAttribute('aria-pressed',String(node===b)));const zoom=document.querySelector('[data-product-zoom]');if(zoom)zoom.href=b.dataset.preview;}, { signal: lifecycle.signal }));
document.querySelectorAll('[data-confirm]').forEach(f=>f.addEventListener('submit',e=>{if(!confirm(document.documentElement.lang==='ar'?'تأكيد هذا الإجراء؟':'Confirm this action?'))e.preventDefault();}, { signal: lifecycle.signal }));

const shipping=document.querySelector('#shipping-method');
const useWalletToggle=document.querySelector('#use-wallet');
function updateShipping(){
 if(!shipping)return;
 const isFreeQualified = document.querySelector('.checkout-summary[data-free-delivery-qualified="true"]') !== null;
 const originalFee = Number(shipping.selectedOptions[0]?.dataset.fee||0);
 const fee = isFreeQualified ? 0 : originalFee;
 const totalEl=document.querySelector('#checkout-total');
 const feeEl=document.querySelector('#shipping-fee');
 const origFeeEl=document.querySelector('#shipping-orig-fee');
 if(feeEl)feeEl.textContent=fee.toFixed(2);
 if(origFeeEl)origFeeEl.textContent=originalFee.toFixed(2);
 if(!totalEl)return;
 const grand=Number(totalEl.dataset.subtotal||0)+fee;
 totalEl.textContent=grand.toFixed(2);
 const walletBox=document.querySelector('[data-wallet-box]');
 if(!walletBox)return;
 const balance=Number(walletBox.dataset.walletBalance||0);
 const applied=useWalletToggle?.checked?Math.min(balance,grand):0;
 const payable=Math.max(0,grand-applied);
 const creditRow=document.querySelector('#wallet-credit-row');
 const appliedEl=document.querySelector('#wallet-applied');
 const payableRow=document.querySelector('#checkout-payable-row');
 const payableEl=document.querySelector('#checkout-payable');
 const fullBanner=document.querySelector('#walletFullBanner');
 const instapayWrap=document.querySelector('#instapayFieldWrap');
 const instapayInput=instapayWrap?.querySelector('input[name="InstaPayUsername"]');
 const submitBtn=document.querySelector('.checkout-submit');
 if(creditRow)creditRow.hidden=applied<=0;
 if(appliedEl)appliedEl.textContent=applied.toFixed(2);
 if(payableRow)payableRow.hidden=applied<=0;
 if(payableEl)payableEl.textContent=payable.toFixed(2);
 const fullCovered=applied>0&&payable===0;
 if(fullBanner)fullBanner.hidden=!fullCovered;
 if(instapayWrap)instapayWrap.hidden=fullCovered;
 if(instapayInput)instapayInput.required=!fullCovered;
 if(submitBtn&&submitBtn.dataset.labelWallet&&submitBtn.dataset.labelNilepay){
  submitBtn.textContent=fullCovered?submitBtn.dataset.labelWallet:submitBtn.dataset.labelNilepay;
 }
}
shipping?.addEventListener('change',updateShipping, { signal: lifecycle.signal });
useWalletToggle?.addEventListener('change',updateShipping, { signal: lifecycle.signal });
updateShipping();
const paymentForm=document.querySelector('#payment-status-form');
if(paymentForm){let checks=0;let active=false;async function refreshPayment(){if(document.hidden||active||checks++>=60)return;active=true;try{const response=await fetch(paymentForm.action,{method:'POST',body:new FormData(paymentForm),credentials:'same-origin',headers:{'Accept':'application/json'}});if(response.ok){const state=await response.json();if(state.paymentStatus!=='Unpaid'||['EXPIRED','CANCELLED','FAILED'].includes(state.gatewayStatus)){window.NileShopAsync.refresh();}}}catch{}finally{active=false}}setInterval(refreshPayment,10000);document.addEventListener('visibilitychange',()=>{if(!document.hidden)refreshPayment()}, { signal: lifecycle.signal });}
const menuToggle=document.querySelector('.menu-toggle');
// Reset disclosure state when rotating a device or moving to the desktop header.
const compactNavigation=window.matchMedia('(max-width: 1280px)');
compactNavigation.addEventListener('change',closeSiteMenu, { signal: lifecycle.signal });
menuToggle?.addEventListener('click',()=>{const open=menuToggle.getAttribute('aria-expanded')==='true';menuToggle.setAttribute('aria-expanded',String(!open));menuToggle.setAttribute('aria-label',open?menuToggle.dataset.openLabel:menuToggle.dataset.closeLabel);document.querySelector('#site-nav')?.classList.toggle('is-open',!open);}, { signal: lifecycle.signal });
document.querySelectorAll('#site-nav a').forEach(link=>link.addEventListener('click',closeSiteMenu, { signal: lifecycle.signal }));
const editorUpload=document.querySelector('#Uploads');
const editorPreview=document.querySelector('#product-editor-preview');
const replacePrimary=document.querySelector('#ReplacePrimaryImage');
const productSlug=document.querySelector('.product-editor-form #Slug');
const productName=document.querySelector('.product-editor-form #NameEn');
productSlug?.addEventListener('input',()=>{productSlug.dataset.autoSlug=productSlug.value.trim()?'false':'true';}, { signal: lifecycle.signal });
productName?.addEventListener('input',()=>{
 if(productSlug?.dataset.autoSlug==='true'){
  productSlug.placeholder=productName.value.toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-+|-+$/g,'').slice(0,170).replace(/-+$/g,'');
 }
}, { signal: lifecycle.signal });
const uploadThumbnails=document.querySelector('#entry-upload-thumbnails');
function updateUploadThumbnails(){
 if(!uploadThumbnails||!editorUpload)return;
 uploadThumbnails.querySelectorAll('[data-object-url]').forEach(node=>URL.revokeObjectURL(node.dataset.objectUrl));
 uploadThumbnails.replaceChildren();
 const files=[...editorUpload.files];
 const overLimit=files.length>6||files.some(file=>file.size>4*1024*1024)||files.reduce((sum,file)=>sum+file.size,0)>16*1024*1024;
 editorUpload.setCustomValidity(overLimit?(document.querySelector('#entry-upload-help')?.textContent||'Too many or oversized photos.'): '');
 const status=document.querySelector('#entry-upload-status');
 if(status)status.textContent=files.length?`${status.dataset.selectedLabel}: ${files.length} · ${files.map(file=>file.name).join(', ')}`:'';
 for(const file of files.slice(0,6)){
  if(!['image/jpeg','image/png','image/webp'].includes(file.type))continue;
  const thumbnail=document.createElement('img');
  thumbnail.src=URL.createObjectURL(file);thumbnail.dataset.objectUrl=thumbnail.src;
  thumbnail.alt=file.name;uploadThumbnails.append(thumbnail);
 }
}
document.querySelector('.product-editor-form')?.addEventListener('invalid',event=>{
 for(let parent=event.target.parentElement;parent;parent=parent.parentElement){if(parent.tagName==='DETAILS')parent.open=true;}
}, { capture: true, signal: lifecycle.signal });
function updateEditorPreview(){
 if(!editorPreview)return;
 if(editorPreview.dataset.objectUrl){URL.revokeObjectURL(editorPreview.dataset.objectUrl);delete editorPreview.dataset.objectUrl;}
 const saved=editorPreview.dataset.savedSrc||'',file=editorUpload?.files?.[0];
 let source=saved;
 if(file&&(!saved||replacePrimary?.checked)&&['image/jpeg','image/png','image/webp'].includes(file.type)){
  source=URL.createObjectURL(file);editorPreview.dataset.objectUrl=source;
 }
 if(source)editorPreview.src=source;else editorPreview.removeAttribute('src');
 editorPreview.hidden=!source;
 const empty=document.querySelector('#product-editor-empty');if(empty)empty.hidden=Boolean(source);
}
editorUpload?.addEventListener('change',updateEditorPreview, { signal: lifecycle.signal });
editorUpload?.addEventListener('change',updateUploadThumbnails, { signal: lifecycle.signal });
updateEditorPreview();
updateUploadThumbnails();
replacePrimary?.addEventListener('change',updateEditorPreview, { signal: lifecycle.signal });
document.querySelector('#ImageUrl')?.addEventListener('change',function(){
 if(!editorPreview)return;
 const path=this.value.trim();
 editorPreview.dataset.savedSrc=/^\/assets\/[a-zA-Z0-9_./-]+\.(jpg|jpeg|png|webp|gif)$/.test(path)&&!path.includes('..')?path:'';
 updateEditorPreview();
}, { signal: lifecycle.signal });
window.addEventListener('pagehide',()=>{if(editorPreview?.dataset.objectUrl)URL.revokeObjectURL(editorPreview.dataset.objectUrl);}, { signal: lifecycle.signal });
// Quantity updates are serialized so rapid changes cannot overwrite a newer cart.
const quantityForms=document.querySelectorAll('[data-auto-quantity]');
if(quantityForms.length){
 const pendingQuantities=new Map();let cartBusy=false,quantityTimer;
 const checkoutLink=document.querySelector('#cart-checkout');
 const money=new Intl.NumberFormat(document.documentElement.lang,{minimumFractionDigits:2,maximumFractionDigits:2});
 const setCartBusy=busy=>{document.querySelectorAll('.cart-item .remove-button').forEach(button=>button.disabled=busy);checkoutLink?.setAttribute('aria-disabled',String(busy));document.querySelector('.cart-layout')?.setAttribute('aria-busy',String(busy));};
 checkoutLink?.addEventListener('click',event=>{if(cartBusy||pendingQuantities.size)event.preventDefault();}, { signal: lifecycle.signal });
 async function flushQuantities(){
  if(cartBusy)return;
  cartBusy=true;setCartBusy(true);
  try{
   while(pendingQuantities.size){
    const [form]=pendingQuantities.keys();pendingQuantities.delete(form);
    const response=await fetch(form.action,{method:'POST',body:new FormData(form),headers:{Accept:'application/json'},credentials:'same-origin'});
    if(!response.ok)throw new Error('cart-update');
    const cart=await response.json();
    if(!Array.isArray(cart.lines))throw new Error('cart-update');
    for(const row of document.querySelectorAll('[data-cart-key]')){
     const line=cart.lines.find(item=>item.key===row.dataset.cartKey);
     if(!line){await window.NileShopAsync.refresh();return;}
     row.querySelector('[data-line-total]').textContent=money.format(line.total)+' EGP';
     row.querySelector('[data-unit-price]').textContent=money.format(line.unitPrice)+' EGP';
     const rowForm=row.querySelector('[data-auto-quantity]');
     if(!pendingQuantities.has(rowForm))rowForm.elements.quantity.value=line.quantity;
    }
    document.querySelector('#cart-grand-total').textContent=money.format(cart.total)+' EGP';
    document.querySelector('#cart-item-count').textContent=cart.count;
    document.querySelectorAll('.cart-count').forEach(node=>node.textContent=cart.count);
    if (cart.error) showToast(cart.error, 'error');
   }
  }catch{showToast(document.body.dataset.asyncError, 'error');}
  finally{cartBusy=false;setCartBusy(pendingQuantities.size>0);}
 }
 for(const form of quantityForms){
  const input=form.elements.quantity;
  const queue=()=>{if(!input.checkValidity()||input.value==='')return;pendingQuantities.set(form,true);setCartBusy(true);clearTimeout(quantityTimer);quantityTimer=setTimeout(flushQuantities,250);};
  input.addEventListener('input',queue, { signal: lifecycle.signal });input.addEventListener('change',queue, { signal: lifecycle.signal });
  form.addEventListener('submit',event=>{event.preventDefault();if(form.reportValidity()){pendingQuantities.set(form,true);clearTimeout(quantityTimer);flushQuantities();}}, { signal: lifecycle.signal });
  form.querySelector('button[type="submit"]')?.setAttribute('hidden','');
 }
}

document.querySelectorAll('[data-quantity-step]').forEach(button=>button.addEventListener('click',()=>{
 const input=button.closest('form').elements.quantity;
 const next=Math.min(Number(input.max)||99,Math.max(Number(input.min)||1,(Number(input.value)||1)+Number(button.dataset.quantityStep)));
 if(next!==Number(input.value)){input.value=next;input.dispatchEvent(new Event('input',{bubbles:true}));}
}, { signal: lifecycle.signal }));
// Apply catalog selections immediately while keeping all other filter values.
const catalogFilters=document.querySelector('.catalog-filters');
const headerCatalog=document.getElementById('header-catalog');
headerCatalog?.addEventListener('toggle', () => {
 if (headerCatalog.open) closeSiteMenu();
}, { signal: lifecycle.signal });
menuToggle?.addEventListener('click', () => {
 if (headerCatalog) headerCatalog.open = false;
}, { signal: lifecycle.signal });
document.addEventListener('click', event => {
 if (headerCatalog?.open && !headerCatalog.contains(event.target)) headerCatalog.open = false;
}, { signal: lifecycle.signal });
headerCatalog?.addEventListener('keydown', event => {
 if (event.key === 'Escape' && !event.defaultPrevented && headerCatalog.open) {
  headerCatalog.open = false;
  headerCatalog.querySelector('summary')?.focus();
 }
}, { signal: lifecycle.signal });
catalogFilters?.querySelectorAll('select,input[type="checkbox"]').forEach(control=>{
 control.addEventListener('change',()=>submitCatalogFilters(), { signal: lifecycle.signal });
});
// Accessible, debounced suggestions. Superseded requests are cancelled.
const searchInput=document.querySelector('#catalog-search');
if(searchInput){
 const list=document.querySelector('#search-suggestions'),feedback=document.querySelector('#search-feedback'),clear=document.querySelector('.search-clear');
 let suggestionTimer,request,revision=0,active=-1;
 function closeSuggestions(){list.hidden=true;searchInput.setAttribute('aria-expanded','false');searchInput.removeAttribute('aria-activedescendant');active=-1;}
 function highlight(index){const options=[...list.querySelectorAll('[role="option"]')];if(!options.length)return;active=(index+options.length)%options.length;options.forEach((node,i)=>node.setAttribute('aria-selected',String(i===active)));searchInput.setAttribute('aria-activedescendant',options[active].id);options[active].scrollIntoView({block:'nearest'});}
 async function suggest(version){
  const q=searchInput.value.trim();if(q.length<2)return;
  request=new AbortController();
  try{
   const url=new URL(searchInput.dataset.suggestUrl,location.origin);url.search=new URLSearchParams(new FormData(searchInput.form)).toString();
   const response=await fetch(url,{signal:request.signal,headers:{Accept:'application/json'}});if(!response.ok)return;
   const data=await response.json();if(version!==revision||document.activeElement!==searchInput)return;
   list.replaceChildren();active=-1;
   const products=data.products||[];
   products.forEach((product,index)=>{
    const item=document.createElement('li'),link=document.createElement('a'),img=document.createElement('img'),body=document.createElement('span'),name=document.createElement('strong'),price=document.createElement('small');
    link.id='search-option-'+index;link.href=product.url;link.setAttribute('role','option');link.setAttribute('aria-selected','false');link.tabIndex=-1;
    img.src=product.image;img.alt='';name.textContent=product.name;price.textContent=new Intl.NumberFormat(document.documentElement.lang,{minimumFractionDigits:2,maximumFractionDigits:2}).format(product.price)+' EGP';
    body.append(name,price);link.append(img,body);item.append(link);list.append(item);
   });
   feedback.textContent=products.length?products.length+' '+feedback.dataset.found:feedback.dataset.none;
   list.hidden=products.length===0;searchInput.setAttribute('aria-expanded',String(products.length>0));
  }catch(error){if(error.name!=='AbortError')closeSuggestions();}
 }
 function queueSuggestions(){clear.hidden=!searchInput.value;clearTimeout(suggestionTimer);request?.abort();revision++;closeSuggestions();if(searchInput.value.trim().length>=2){const version=revision;suggestionTimer=setTimeout(()=>suggest(version),220);}}
 clear.hidden=!searchInput.value;
 searchInput.addEventListener('input',event=>{if(!event.isComposing)queueSuggestions();}, { signal: lifecycle.signal });searchInput.addEventListener('compositionend',queueSuggestions, { signal: lifecycle.signal });searchInput.addEventListener('focus',queueSuggestions, { signal: lifecycle.signal });
 searchInput.addEventListener('keydown',event=>{
  if(event.key==='Escape'){revision++;request?.abort();closeSuggestions();}
  if(!list.hidden&&['ArrowDown','ArrowUp'].includes(event.key)){event.preventDefault();highlight(active<0?(event.key==='ArrowDown'?0:list.querySelectorAll('[role="option"]').length-1):active+(event.key==='ArrowDown'?1:-1));}
  if(event.key==='Enter'&&!list.hidden&&active>=0){event.preventDefault();list.querySelectorAll('[role="option"]')[active].click();}
 }, { signal: lifecycle.signal });
 clear.addEventListener('click',()=>{searchInput.value='';queueSuggestions();searchInput.focus();submitCatalogFilters();}, { signal: lifecycle.signal });
 document.addEventListener('click',event=>{if(!searchInput.closest('.search-field').contains(event.target)){revision++;request?.abort();closeSuggestions();}}, { signal: lifecycle.signal });
 searchInput.addEventListener('blur',()=>{setTimeout(()=>{if(!list.contains(document.activeElement)){revision++;request?.abort();closeSuggestions();}},150);}, { signal: lifecycle.signal });
}

const productBuyForm=document.querySelector('.product-buy-form[data-unit-amount]');
if(productBuyForm){
 const quantity=productBuyForm.elements.quantity,subtotal=document.querySelector('#product-subtotal');
 quantity.addEventListener('input',()=>{if(quantity.checkValidity()&&quantity.value!=='')subtotal.textContent=new Intl.NumberFormat(document.documentElement.lang,{minimumFractionDigits:2,maximumFractionDigits:2}).format(Number(productBuyForm.dataset.unitAmount)*Number(quantity.value))+' EGP';}, { signal: lifecycle.signal });
}
const productZoom=document.querySelector('[data-product-zoom]'),imageDialog=document.querySelector('.product-image-dialog');
productZoom?.addEventListener('click',event=>{if(typeof imageDialog?.showModal!=='function')return;event.preventDefault();imageDialog.querySelector('img').src=productZoom.href;imageDialog.showModal();}, { signal: lifecycle.signal });
imageDialog?.querySelector('[data-close-zoom]')?.addEventListener('click',()=>imageDialog.close(), { signal: lifecycle.signal });
imageDialog?.addEventListener('click',event=>{if(event.target===imageDialog){const bounds=imageDialog.getBoundingClientRect();if(event.clientX<bounds.left||event.clientX>bounds.right||event.clientY<bounds.top||event.clientY>bounds.bottom)imageDialog.close();}}, { signal: lifecycle.signal });

function submitCatalogFilters(){
 if(!catalogFilters || !catalogFilters.reportValidity())return;
 const skel=document.querySelector('#shopSkeletons');
 if(skel){
  skel.hidden=false;
  document.querySelectorAll('#collection > .category-product-groups, #collection > .product-grid').forEach(el=>{el.style.display='none';});
 }
 if(typeof catalogFilters.requestSubmit==='function')catalogFilters.requestSubmit();
 else if(catalogFilters.reportValidity())HTMLFormElement.prototype.submit.call(catalogFilters);
}
function closeSiteMenu(){
 menuToggle?.setAttribute('aria-expanded','false');
 if(menuToggle)menuToggle.setAttribute('aria-label',menuToggle.dataset.openLabel);
 document.querySelector('#site-nav')?.classList.remove('is-open');
}
document.addEventListener('keydown',event=>{if(event.key==='Escape'&&menuToggle?.getAttribute('aria-expanded')==='true'){closeSiteMenu();menuToggle.focus();}}, { signal: lifecycle.signal });
document.addEventListener('click',event=>{if(menuToggle?.getAttribute('aria-expanded')==='true'&&!event.target.closest('.site-header'))closeSiteMenu();}, { signal: lifecycle.signal });

document.querySelector('.site-header')?.addEventListener('focusout',()=>setTimeout(()=>{if(!document.activeElement?.closest('.site-header'))closeSiteMenu();},0), { signal: lifecycle.signal });

/* ==========================================================================
   Enhancement JS — Toast, AJAX Cart, Mini-Cart, Dark Mode, Recently Viewed,
   Scroll Animations, Cookie Consent, Checkout Spinner, Copy Link
   ========================================================================== */

// --- HTML Escaping Helper ---
function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, character => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[character]));
}

// --- A1. Toast Notification System ---
function showToast(message, type = 'success') {
  let container = document.querySelector('.toast-container');
  if (!container) {
    container = document.createElement('div');
    container.className = 'toast-container';
    document.body.appendChild(container);
  }
  const toast = document.createElement('div');
  toast.className = 'toast-notification' + (type === 'error' ? ' error' : '');
  toast.setAttribute('role', type === 'error' ? 'alert' : 'status');
  toast.innerHTML =
    '<span class="toast-icon" aria-hidden="true">' + (type === 'error' ? '✕' : '✓') + '</span>' +
    '<span class="toast-message">' + escapeHtml(message) + '</span>';
  container.appendChild(toast);
  requestAnimationFrame(() => requestAnimationFrame(() => toast.classList.add('show')));
  window.setTimeout(() => {
    toast.classList.remove('show');
    window.setTimeout(() => toast.remove(), 400);
  }, type === 'error' ? 6500 : 4000);
}

// --- A1. AJAX Add-to-Cart Interception ---
let pendingCartAdd = Promise.resolve();
document.querySelectorAll('form[data-ajax-cart]').forEach(form => {
  form.addEventListener('submit', async e => {
    e.preventDefault();
    if (!form.reportValidity()) return;
    const btn = form.querySelector('button[type="submit"], button:not([type])');
    if (btn?.disabled) return;
    const body = new FormData(form);
    if (btn) btn.disabled = true;
    const previousAdd = pendingCartAdd;
    let finishAdd;
    pendingCartAdd = new Promise(resolve => { finishAdd = resolve; });
    try {
      await previousAdd;
      if (lifecycle.signal.aborted) return;
      const response = await fetch('/Cart/AddAjax', {
        method: 'POST',
        body,
        headers: { 'Accept': 'application/json' },
        credentials: 'same-origin'
      });
      const data = await response.json();
      if (response.ok && data.success) {
        showToast(data.message || document.body.dataset.cartAdded);
        document.querySelectorAll('.cart-count').forEach(el => { el.textContent = data.cartCount; });
        const options = form.closest('.quick-add details');
        if (options?.open) {
          const restoreFocus = options.contains(document.activeElement);
          options.open = false;
          if (restoreFocus) options.querySelector('summary')?.focus();
        }
        if (miniCartOverlay?.classList.contains('open')) refreshMiniCart();
      } else {
        showToast(data.message || document.body.dataset.asyncError, 'error');
      }
    } catch {
      showToast(document.body.dataset.asyncError, 'error');
    } finally {
      if (btn) btn.disabled = false;
      finishAdd();
    }
  }, { signal: lifecycle.signal });
});

// --- A2. Mini-Cart Drawer ---
const miniCartOverlay = document.querySelector('#miniCartOverlay');
const miniCartDrawer = document.querySelector('#miniCartDrawer');
let miniCartOrigin, miniCartOverflow='';
cleanups.push(()=>closeMiniCart(false));

function openMiniCart() {
  if (!miniCartOverlay || miniCartOverlay.classList.contains('open')) return;
  miniCartOrigin=document.activeElement;
  miniCartOverflow=document.body.style.overflow;
  miniCartOverlay.hidden=false;
  miniCartOverlay.classList.add('open');
  document.body.style.overflow = 'hidden';
  miniCartDrawer?.querySelector('.mini-cart-close')?.focus();
  refreshMiniCart();
}

function closeMiniCart(restoreFocus=true) {
  if (!miniCartOverlay?.classList.contains('open')) return;
  miniCartOverlay.classList.remove('open');
  miniCartOverlay.hidden=true;
  document.body.style.overflow = miniCartOverflow;
  if(restoreFocus){
    if(miniCartOrigin?.isConnected && miniCartOrigin.getClientRects().length)miniCartOrigin.focus();
    else menuToggle?.focus();
  }
}

async function refreshMiniCart() {
  if (!miniCartOverlay) return;
  const itemsEl = document.querySelector('#miniCartItems');
  const footerEl = document.querySelector('#miniCartFooter');
  const totalEl = document.querySelector('#miniCartTotal');
  itemsEl.setAttribute('aria-busy','true');
  itemsEl.textContent=miniCartOverlay.dataset.loadingText;
  footerEl.style.display='none';
  try {
    const res = await fetch('/Cart/MiniCart', { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' });
    if (!res.ok) throw new Error('Cart unavailable');
    const data = await res.json();
    const emptyText = miniCartOverlay.dataset.emptyText || 'Your cart is empty';
    const shopText = miniCartOverlay.dataset.shopText || 'Browse Shop';
    const qtyText = miniCartOverlay.dataset.qtyText || 'Qty';
    const money = new Intl.NumberFormat(document.documentElement.lang, {minimumFractionDigits:2, maximumFractionDigits:2});
    if (!data.items || data.items.length === 0) {
      itemsEl.innerHTML = '<div class="mini-cart-empty"><p>' + escapeHtml(emptyText) + '</p><a href="/shop">' + escapeHtml(shopText) + ' →</a></div>';
      footerEl.style.display = 'none';
    } else {
      itemsEl.innerHTML = data.items.map(item =>
        '<div class="mini-cart-item">' +
        '<img class="mini-cart-item-image" src="' + escapeHtml(item.image) + '" alt="" loading="lazy"/>' +
        '<div class="mini-cart-item-details">' +
        '<strong>' + escapeHtml(item.name) + '</strong>' +
        (item.size || item.color ? '<small>' + escapeHtml(((item.size || '') + ' ' + (item.color || '')).trim()) + '</small>' : '') +
        '<small>' + escapeHtml(qtyText) + ': ' + Number(item.quantity) + '</small>' +
        '</div>' +
        '<span class="mini-cart-item-price">' + money.format(Number(item.lineTotal)) + ' EGP</span>' +
        '</div>'
      ).join('');
      footerEl.style.display = '';
      totalEl.textContent = money.format(Number(data.total)) + ' EGP';
    }
    // Also update header cart count
    document.querySelectorAll('.cart-count').forEach(el => { el.textContent = data.count; });
  } catch {
    itemsEl.textContent=miniCartOverlay.dataset.errorText;
  } finally { itemsEl.removeAttribute('aria-busy'); }
}

// Cart icon click → open mini-cart instead of navigating
const cartLink = document.querySelector('.cart-link');
if (cartLink) {
  cartLink.addEventListener('click', e => {
    e.preventDefault();
    openMiniCart();
  }, { signal: lifecycle.signal });
}

// Close mini-cart
document.querySelector('.mini-cart-close')?.addEventListener('click', closeMiniCart, { signal: lifecycle.signal });
miniCartOverlay?.addEventListener('click', e => {
  if (e.target === miniCartOverlay) closeMiniCart();
}, { signal: lifecycle.signal });
document.addEventListener('keydown', e => {
  if (e.key === 'Escape' && miniCartOverlay?.classList.contains('open')) closeMiniCart();
  if(e.key==='Tab' && miniCartOverlay?.classList.contains('open')){
    const controls=[...miniCartDrawer.querySelectorAll('a[href],button:not(:disabled),input:not(:disabled),[tabindex="0"]')].filter(node=>node.getClientRects().length);
    const first=controls[0],last=controls.at(-1);
    if(e.shiftKey && document.activeElement===first){e.preventDefault();last?.focus();}
    else if(!e.shiftKey && document.activeElement===last){e.preventDefault();first?.focus();}
  }
}, { signal: lifecycle.signal });

// --- A3. Scroll Animations ---
if ('IntersectionObserver' in window) {
  const scrollObserver = new IntersectionObserver((entries) => {
    entries.forEach(entry => {
      if (entry.isIntersecting) {
        entry.target.classList.add('visible');
        scrollObserver.unobserve(entry.target);
      }
    });
  }, { threshold: 0.01 });
  observers.push(scrollObserver);
  document.querySelectorAll('.animate-on-scroll').forEach(el => {
    const rect = el.getBoundingClientRect();
    if (rect.top < window.innerHeight && rect.bottom > 0) {
      el.classList.add('visible');
    } else {
      scrollObserver.observe(el);
    }
  });
} else {
  document.querySelectorAll('.animate-on-scroll').forEach(el => el.classList.add('visible'));
}

// --- Copy Phone to WhatsApp Helper ---
document.querySelector('[data-copy-phone]')?.addEventListener('click', () => {
  const phoneInput = document.querySelector('#Phone');
  const waInput = document.querySelector('#WhatsAppPhone');
  if (phoneInput && waInput) {
    waInput.value = phoneInput.value.trim();
    waInput.focus();
  }
}, { signal: lifecycle.signal });

// --- A5. Checkout Spinner & Saved Delivery Address Switcher ---
const checkoutAddressBox = document.querySelector('[data-checkout-address-box]');
if (checkoutAddressBox) {
  const savedCard = document.querySelector('#savedAddressCard');
  const editor = document.querySelector('#checkoutAddressEditor');
  const addressInput = editor?.querySelector('input[name="ShippingAddress"]');
  const saveCheck = editor?.querySelector('input[name="SaveAddressToProfile"]');
  const savedValue = checkoutAddressBox.dataset.savedAddress || '';

  checkoutAddressBox.querySelector('[data-change-address]')?.addEventListener('click', () => {
    if (savedCard) savedCard.hidden = true;
    if (editor) editor.hidden = false;
    if (addressInput) {
      addressInput.value = '';
      addressInput.focus();
    }
  }, { signal: lifecycle.signal });

  checkoutAddressBox.querySelector('[data-use-saved-address]')?.addEventListener('click', () => {
    if (addressInput) addressInput.value = savedValue;
    if (saveCheck) saveCheck.checked = false;
    if (editor) editor.hidden = true;
    if (savedCard) savedCard.hidden = false;
  }, { signal: lifecycle.signal });
}

const checkoutForm = document.querySelector('.checkout-form');
const checkoutOverlayEl = document.querySelector('.checkout-overlay');
if (checkoutForm && checkoutOverlayEl) {
  checkoutForm.addEventListener('submit', () => {
    checkoutOverlayEl.classList.add('active');
    const submitBtn = checkoutForm.querySelector('.checkout-submit');
    if (submitBtn) submitBtn.disabled = true;
  }, { signal: lifecycle.signal });
}

// --- B3. Recently Viewed Products ---
(function() {
  // Save current product to recently viewed
  const detailPage = document.querySelector('.product-detail-page');
  if (detailPage) {
    const productData = detailPage.dataset;
    if (productData.productId) {
      const item = {
        id: Number(productData.productId),
        name: productData.productName || '',
        slug: productData.productSlug || '',
        image: productData.productImage || '',
        price: productData.productPrice || ''
      };
      let recent = [];
      try { recent = JSON.parse(localStorage.getItem('recentlyViewed') || '[]'); } catch {}
      recent = recent.filter(r => r.id !== item.id);
      recent.unshift(item);
      recent = recent.slice(0, 8);
      try { localStorage.setItem('recentlyViewed', JSON.stringify(recent)); } catch {}
    }
  }

  // Render recently viewed section on home and shop pages
  const rvContainer = document.querySelector('#recentlyViewed');
  if (rvContainer) {
    let recent = [];
    try { recent = JSON.parse(localStorage.getItem('recentlyViewed') || '[]'); } catch {}
    if (recent.length > 0) {
      rvContainer.style.display = '';
      const money = new Intl.NumberFormat(document.documentElement.lang, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
      rvContainer.innerHTML =
        '<h2>Recently Viewed</h2>' +
        '<div class="recently-viewed-scroll">' +
        recent.map(item =>
          '<a class="recently-viewed-card" href="/products/' + encodeURIComponent(item.slug) + '">' +
          '<img src="' + escapeHtml(item.image) + '" alt="" loading="lazy"/>' +
          '<div class="rv-info">' +
          '<div class="rv-name">' + escapeHtml(item.name) + '</div>' +
          '<div class="rv-price">' + money.format(Number(item.price)) + ' EGP</div>' +
          '</div></a>'
        ).join('') +
        '</div>';
    }
  }
})();

// --- B4. Copy Link ---
document.querySelector('.copy-link')?.addEventListener('click', function() {
  const url = this.dataset.url || window.location.href;
  if(!navigator.clipboard?.writeText){showToast(document.body.dataset.copyError,'error');return;}
  navigator.clipboard.writeText(url).then(() => showToast(document.body.dataset.linkCopied)).catch(() => showToast(document.body.dataset.copyError, 'error'));
}, { signal: lifecycle.signal });

// --- D1. Dark Mode ---
(function() {
  const html = document.documentElement;
  const toggle = document.querySelector('.dark-mode-toggle');
  const sunSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="5"/><path d="M12 1v2M12 21v2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M1 12h2M21 12h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42"/></svg>';
  const moonSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/></svg>';

  function applyDark(isDark) {
    html.classList.toggle('dark-mode', isDark);
    if (toggle) toggle.innerHTML = isDark ? sunSvg : moonSvg;
    toggle?.setAttribute('aria-pressed',String(isDark));
  }

  // Check preference
  let stored=null;try { stored=localStorage.getItem('darkMode'); } catch {}
  if (stored !== null) {
    applyDark(stored === 'true');
  } else if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
    applyDark(true);
  }

  toggle?.addEventListener('click', () => {
    const isDark = !html.classList.contains('dark-mode');
    applyDark(isDark);
    try { localStorage.setItem('darkMode', String(isDark)); } catch {}
  }, { signal: lifecycle.signal });
})();

// --- E1. Cookie Consent ---
(function() {
  const banner = document.querySelector('#cookieConsent');
  if (!banner) return;
  let acknowledged=false;try { acknowledged=Boolean(localStorage.getItem('cookieConsent')); } catch {}
  if (acknowledged) {
    banner.hidden=true;
    return;
  }
  const btn = document.querySelector('#acceptCookies');
  btn?.addEventListener('click', () => {
    try { localStorage.setItem('cookieConsent', 'accepted'); } catch {}
    banner.hidden=true;
  }, { signal: lifecycle.signal });
})();

// Password visibility controls are added only when JavaScript is available.
(function () {
  document.querySelectorAll('input[type="password"], .password-field input').forEach(input => {
    const field = input.closest('.password-field') || document.createElement('div');
    if (!input.closest('.password-field')) {
      field.className = 'password-field'; input.before(field); field.append(input);
    }
    field.querySelector('.password-visibility')?.remove();
    const toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'password-visibility';
    const eye = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/><path class="password-eye-slash" d="m3 3 18 18"/></svg>';
    toggle.innerHTML = eye;
    const update = () => {
      const visible = input.type === 'text';
      const label = visible ? document.body.dataset.hidePassword : document.body.dataset.showPassword;
      toggle.setAttribute('aria-label', label);
      toggle.title = label;
      toggle.setAttribute('aria-pressed', String(visible));
    };
    toggle.addEventListener('click', () => {
      input.type = input.type === 'password' ? 'text' : 'password';
      update();
    }, { signal: lifecycle.signal });
    field.append(toggle);
    update();
  });
})();

// Save category visibility without navigating away from the dashboard.
document.querySelectorAll('[data-category-visibility]').forEach(form => {
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (form.getAttribute('aria-busy') === 'true') return;
    const button = form.querySelector('button[type="submit"]');
    const error = form.querySelector('[data-visibility-error]');
    button.disabled = true;
    form.setAttribute('aria-busy', 'true');
    error.hidden = true;
    try {
      const response = await fetch(form.action, {
        method: 'POST', body: new FormData(form), credentials: 'same-origin',
        headers: { Accept: 'application/json' }
      });
      if (!response.ok || response.redirected) throw new Error('Visibility update failed');
      const state = await response.json();
      if (typeof state.hidden !== 'boolean') throw new Error('Invalid visibility response');
      form.elements.namedItem('hidden').value = String(!state.hidden);
      button.textContent = state.buttonText;
      form.closest('.admin-category-card').querySelector('.category-visibility-status').textContent = state.statusText;
    } catch {
      showToast(error.textContent.trim() || document.body.dataset.asyncError, 'error');
    } finally {
      button.disabled = false;
      form.removeAttribute('aria-busy');
    }
  }, { signal: lifecycle.signal });
});

document.querySelectorAll('[data-language-preference]').forEach(form => {
  form.querySelector('select').addEventListener('change', () => form.requestSubmit(), {signal:lifecycle.signal});
  form.querySelector('button[type="submit"]').hidden = true;
});

// --- Store Settings: Copy text buttons & sidebar scrollspy ---
document.querySelectorAll('[data-copy-text]').forEach(btn => {
  btn.addEventListener('click', () => {
    const text = btn.dataset.copyText || '';
    if(!navigator.clipboard?.writeText){showToast(document.body.dataset.copyError,'error');return;}
    navigator.clipboard.writeText(text)
      .then(() => showToast(document.body.dataset.linkCopied))
      .catch(() => showToast(document.body.dataset.copyError, 'error'));
  }, { signal: lifecycle.signal });
});

const returnDays = document.querySelector('#ReturnDays');
returnDays?.addEventListener('input', () => {
  if (!returnDays.value || !returnDays.validity.valid) return;
  const days = String(returnDays.valueAsNumber);
  returnDays.form.querySelectorAll('[data-return-window-pattern]').forEach(field => {
    const pattern = new RegExp(field.dataset.returnWindowPattern, 'gi');
    field.value = field.value.replace(pattern, (_, prefix) => prefix + days);
  });
}, { signal: lifecycle.signal });

const emailOtpForm = document.querySelector('[data-email-otp]');
const emailOtpCode = emailOtpForm?.querySelector('#otp-code');
emailOtpCode?.addEventListener('input', () => {
  emailOtpCode.value = emailOtpCode.value.replace(/[٠-٩۰-۹]/g, digit =>
    String(digit.charCodeAt(0) - (digit <= '٩' ? 0x660 : 0x6f0))
  ).replace(/[^0-9]/g, '');
  if (emailOtpCode.value.length === 6) emailOtpForm.requestSubmit();
}, { signal: lifecycle.signal });

const settingsNavLinks = [...document.querySelectorAll('.settings-page .settings-nav a[href^="#"]')];
if (settingsNavLinks.length > 0 && 'IntersectionObserver' in window) {
  const sections = settingsNavLinks
    .map(link => document.querySelector(link.getAttribute('href')))
    .filter(Boolean);
  const activateLink = id => {
    settingsNavLinks.forEach(a => a.classList.toggle('is-active', a.getAttribute('href') === '#' + id));
  };
  const spy = new IntersectionObserver(entries => {
    const visible = entries.filter(e => e.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
    if (visible.length > 0) activateLink(visible[0].target.id);
  }, { rootMargin: '-18% 0px -65% 0px', threshold: 0.05 });
  sections.forEach(sec => spy.observe(sec));
  lifecycle.signal.addEventListener('abort', () => spy.disconnect());
  settingsNavLinks.forEach(link => {
    link.addEventListener('click', () => activateLink(link.getAttribute('href').slice(1)), { signal: lifecycle.signal });
  });
}

// --- Silent Wishlist / Saved Items Heart Toggle ---
function getLocalSaved() {
  try { return JSON.parse(localStorage.getItem('chic_saved_items') || '[]'); }
  catch { return []; }
}
function setLocalSaved(ids) {
  try { localStorage.setItem('chic_saved_items', JSON.stringify(ids)); }
  catch {}
}
function setHeartVisual(btn, saved) {
  btn.classList.toggle('is-saved', saved);
  btn.setAttribute('aria-pressed', String(saved));
  const svg = btn.querySelector('svg');
  if (svg) {
    if (saved) {
      svg.setAttribute('fill', '#e11d48');
      svg.setAttribute('stroke', '#e11d48');
    } else {
      svg.setAttribute('fill', 'none');
      svg.setAttribute('stroke', 'currentColor');
    }
  }
  if (btn.classList.contains('action-link')) {
    btn.textContent = (saved ? '♥ ' : '♡ ') + (btn.dataset.label || btn.textContent.replace(/^[♥♡]\s*/, ''));
  }
}
function syncAllHearts() {
  const savedSet = new Set(getLocalSaved().map(Number));
  document.querySelectorAll('.save-product-form[data-silent-save], .product-save-detail[data-silent-save]').forEach(f => {
    const pid = parseInt(f.querySelector('input[name="productId"]')?.value, 10);
    const btn = f.querySelector('button');
    if (!btn || !pid) return;
    if (btn.classList.contains('is-saved')) {
      savedSet.add(pid);
    } else if (savedSet.has(pid)) {
      setHeartVisual(btn, true);
    }
  });
  setLocalSaved([...savedSet]);
}

document.querySelectorAll('.save-product-form[data-silent-save], .product-save-detail[data-silent-save]').forEach(form => {
  form.addEventListener('submit', async event => {
    event.preventDefault();
    event.stopImmediatePropagation();

    const button = form.querySelector('button');
    if (!button || button.disabled) return;

    const pid = parseInt(form.querySelector('input[name="productId"]')?.value, 10);
    const wasSaved = button.classList.contains('is-saved');
    const nextSaved = !wasSaved;

    // Instant optimistic update: fill with red color!
    setHeartVisual(button, nextSaved);

    // Update localStorage
    const savedSet = new Set(getLocalSaved().map(Number));
    if (nextSaved) savedSet.add(pid);
    else savedSet.delete(pid);
    setLocalSaved([...savedSet]);

    // Sync any other card of same product on screen
    document.querySelectorAll(`.save-product-form input[name="productId"][value="${pid}"], .product-save-detail input[name="productId"][value="${pid}"]`).forEach(inp => {
      const otherBtn = inp.form?.querySelector('button');
      if (otherBtn && otherBtn !== button) setHeartVisual(otherBtn, nextSaved);
    });

    // Silent background AJAX request (no page refresh, no notice banner):
    try {
      const response = await window.fetch(form.action, {
        method: 'POST',
        body: new FormData(form),
        credentials: 'same-origin',
        headers: {
          'Accept': 'application/json',
          'X-Requested-With': 'XMLHttpRequest'
        }
      });
      if (response.ok) {
        const data = await response.json().catch(() => null);
        if (data && typeof data.saved === 'boolean') {
          setHeartVisual(button, data.saved);
        }
      }
    } catch (err) {
      // Silently keep optimistic UI
    }
  }, { signal: lifecycle.signal });
});

syncAllHearts();
window.NileShopToast = showToast;
document.querySelectorAll('[data-page-toast]:not([hidden])').forEach(notice => {
  showToast(notice.textContent.trim(), notice.dataset.pageToast);
  notice.hidden = true;
});

};
window.initializeNileShopPage();

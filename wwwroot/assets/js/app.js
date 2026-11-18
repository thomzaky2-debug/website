'use strict';
document.documentElement.classList.remove('no-js');
document.querySelector('#audit-search')?.addEventListener('input',function(){const q=this.value.toLocaleLowerCase();document.querySelectorAll('#audit-table tbody tr').forEach(r=>r.hidden=!r.textContent.toLocaleLowerCase().includes(q));});
document.querySelectorAll('[data-preview]').forEach(b=>b.addEventListener('click',()=>{document.querySelector('.detail-image').src=b.dataset.preview;document.querySelectorAll('[data-preview]').forEach(node=>node.setAttribute('aria-pressed',String(node===b)));const zoom=document.querySelector('[data-product-zoom]');if(zoom)zoom.href=b.dataset.preview;}));
document.querySelectorAll('[data-confirm]').forEach(f=>f.addEventListener('submit',e=>{if(!confirm(document.documentElement.lang==='ar'?'تأكيد هذا الإجراء؟':'Confirm this action?'))e.preventDefault();}));

const shipping=document.querySelector('#shipping-method');
function updateShipping(){if(!shipping)return;const fee=Number(shipping.selectedOptions[0]?.dataset.fee||0),total=document.querySelector('#checkout-total');document.querySelector('#shipping-fee').textContent=fee.toFixed(2);if(total)total.textContent=(Number(total.dataset.subtotal)+fee).toFixed(2);}
shipping?.addEventListener('change',updateShipping);updateShipping();
const paymentForm=document.querySelector('#payment-status-form');
if(paymentForm){let checks=0;let active=false;async function refreshPayment(){if(document.hidden||active||checks++>=60)return;active=true;try{const response=await fetch(paymentForm.action,{method:'POST',body:new FormData(paymentForm),credentials:'same-origin',headers:{'Accept':'application/json'}});if(response.ok){const state=await response.json();if(state.paymentStatus!=='Unpaid'||['EXPIRED','CANCELLED','FAILED'].includes(state.gatewayStatus)){location.reload();}}}catch{}finally{active=false}}setInterval(refreshPayment,10000);document.addEventListener('visibilitychange',()=>{if(!document.hidden)refreshPayment()});}
const menuToggle=document.querySelector('.menu-toggle');
menuToggle?.addEventListener('click',()=>{const open=menuToggle.getAttribute('aria-expanded')==='true';menuToggle.setAttribute('aria-expanded',String(!open));menuToggle.setAttribute('aria-label',open?menuToggle.dataset.openLabel:menuToggle.dataset.closeLabel);document.querySelector('#site-nav')?.classList.toggle('is-open',!open);});
document.querySelectorAll('#site-nav a').forEach(link=>link.addEventListener('click',closeSiteMenu));
const editorUpload=document.querySelector('#Uploads');
const editorPreview=document.querySelector('#product-editor-preview');
const replacePrimary=document.querySelector('#ReplacePrimaryImage');
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
editorUpload?.addEventListener('change',updateEditorPreview);
replacePrimary?.addEventListener('change',updateEditorPreview);
document.querySelector('#ImageUrl')?.addEventListener('change',function(){
 if(!editorPreview)return;
 const path=this.value.trim();
 editorPreview.dataset.savedSrc=/^\/assets\/[a-zA-Z0-9_./-]+\.(jpg|jpeg|png|webp|gif)$/.test(path)&&!path.includes('..')?path:'';
 updateEditorPreview();
});
window.addEventListener('pagehide',()=>{if(editorPreview?.dataset.objectUrl)URL.revokeObjectURL(editorPreview.dataset.objectUrl);});
// Quantity updates are serialized so rapid changes cannot overwrite a newer cart.
const quantityForms=document.querySelectorAll('[data-auto-quantity]');
if(quantityForms.length){
 const pendingQuantities=new Map();let cartBusy=false,quantityTimer;
 const checkoutLink=document.querySelector('#cart-checkout');
 const money=new Intl.NumberFormat(document.documentElement.lang,{minimumFractionDigits:2,maximumFractionDigits:2});
 const setCartBusy=busy=>{document.querySelectorAll('.cart-item .remove-button').forEach(button=>button.disabled=busy);checkoutLink?.setAttribute('aria-disabled',String(busy));document.querySelector('.cart-layout')?.setAttribute('aria-busy',String(busy));};
 checkoutLink?.addEventListener('click',event=>{if(cartBusy||pendingQuantities.size)event.preventDefault();});
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
     if(!line){location.reload();return;}
     row.querySelector('[data-line-total]').textContent=money.format(line.total)+' EGP';
     row.querySelector('[data-unit-price]').textContent=money.format(line.unitPrice)+' EGP';
     const rowForm=row.querySelector('[data-auto-quantity]');
     if(!pendingQuantities.has(rowForm))rowForm.elements.quantity.value=line.quantity;
    }
    document.querySelector('#cart-grand-total').textContent=money.format(cart.total)+' EGP';
    document.querySelector('#cart-item-count').textContent=cart.count;
    document.querySelectorAll('.cart-count').forEach(node=>node.textContent=cart.count);
    const message=document.querySelector('#cart-update-message');message.textContent=cart.error||'';message.hidden=!cart.error;
   }
  }catch{location.reload();}
  finally{cartBusy=false;setCartBusy(pendingQuantities.size>0);}
 }
 for(const form of quantityForms){
  const input=form.elements.quantity;
  const queue=()=>{if(!input.checkValidity()||input.value==='')return;pendingQuantities.set(form,true);setCartBusy(true);clearTimeout(quantityTimer);quantityTimer=setTimeout(flushQuantities,250);};
  input.addEventListener('input',queue);input.addEventListener('change',queue);
  form.addEventListener('submit',event=>{event.preventDefault();if(form.reportValidity()){pendingQuantities.set(form,true);clearTimeout(quantityTimer);flushQuantities();}});
  form.querySelector('button[type="submit"]')?.setAttribute('hidden','');
 }
}

document.querySelectorAll('[data-quantity-step]').forEach(button=>button.addEventListener('click',()=>{
 const input=button.closest('form').elements.quantity;
 const next=Math.min(Number(input.max)||99,Math.max(Number(input.min)||1,(Number(input.value)||1)+Number(button.dataset.quantityStep)));
 if(next!==Number(input.value)){input.value=next;input.dispatchEvent(new Event('input',{bubbles:true}));}
}));
// Apply catalog selections immediately while keeping all other filter values.
const catalogFilters=document.querySelector('.catalog-filters');
catalogFilters?.querySelectorAll('select,input[type="checkbox"]').forEach(control=>{
 control.addEventListener('change',()=>submitCatalogFilters());
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
 searchInput.addEventListener('input',event=>{if(!event.isComposing)queueSuggestions();});searchInput.addEventListener('compositionend',queueSuggestions);searchInput.addEventListener('focus',queueSuggestions);
 searchInput.addEventListener('keydown',event=>{
  if(event.key==='Escape'){revision++;request?.abort();closeSuggestions();}
  if(!list.hidden&&['ArrowDown','ArrowUp'].includes(event.key)){event.preventDefault();highlight(active<0?(event.key==='ArrowDown'?0:list.querySelectorAll('[role="option"]').length-1):active+(event.key==='ArrowDown'?1:-1));}
  if(event.key==='Enter'&&!list.hidden&&active>=0){event.preventDefault();list.querySelectorAll('[role="option"]')[active].click();}
 });
 clear.addEventListener('click',()=>{searchInput.value='';queueSuggestions();searchInput.focus();submitCatalogFilters();});
 document.addEventListener('click',event=>{if(!searchInput.closest('.search-field').contains(event.target)){revision++;request?.abort();closeSuggestions();}});
 searchInput.addEventListener('blur',()=>{setTimeout(()=>{if(!list.contains(document.activeElement)){revision++;request?.abort();closeSuggestions();}},150);});
}

const productBuyForm=document.querySelector('.product-buy-form[data-unit-amount]');
if(productBuyForm){
 const quantity=productBuyForm.elements.quantity,subtotal=document.querySelector('#product-subtotal');
 quantity.addEventListener('input',()=>{if(quantity.checkValidity()&&quantity.value!=='')subtotal.textContent=new Intl.NumberFormat(document.documentElement.lang,{minimumFractionDigits:2,maximumFractionDigits:2}).format(Number(productBuyForm.dataset.unitAmount)*Number(quantity.value))+' EGP';});
}
const productZoom=document.querySelector('[data-product-zoom]'),imageDialog=document.querySelector('.product-image-dialog');
productZoom?.addEventListener('click',event=>{if(typeof imageDialog?.showModal!=='function')return;event.preventDefault();imageDialog.querySelector('img').src=productZoom.href;imageDialog.showModal();});
imageDialog?.querySelector('[data-close-zoom]')?.addEventListener('click',()=>imageDialog.close());
imageDialog?.addEventListener('click',event=>{if(event.target===imageDialog){const bounds=imageDialog.getBoundingClientRect();if(event.clientX<bounds.left||event.clientX>bounds.right||event.clientY<bounds.top||event.clientY>bounds.bottom)imageDialog.close();}});

function submitCatalogFilters(){
 if(!catalogFilters)return;
 if(typeof catalogFilters.requestSubmit==='function')catalogFilters.requestSubmit();
 else if(catalogFilters.reportValidity())HTMLFormElement.prototype.submit.call(catalogFilters);
}
function closeSiteMenu(){
 menuToggle?.setAttribute('aria-expanded','false');
 if(menuToggle)menuToggle.setAttribute('aria-label',menuToggle.dataset.openLabel);
 document.querySelector('#site-nav')?.classList.remove('is-open');
}
document.addEventListener('keydown',event=>{if(event.key==='Escape'&&menuToggle?.getAttribute('aria-expanded')==='true'){closeSiteMenu();menuToggle.focus();}});
document.addEventListener('click',event=>{if(menuToggle?.getAttribute('aria-expanded')==='true'&&!event.target.closest('.site-header'))closeSiteMenu();});

document.querySelector('.site-header')?.addEventListener('focusout',()=>setTimeout(()=>{if(!document.activeElement?.closest('.site-header'))closeSiteMenu();},0));

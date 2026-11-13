'use strict';
document.querySelector('#audit-search')?.addEventListener('input',function(){const q=this.value.toLocaleLowerCase();document.querySelectorAll('#audit-table tbody tr').forEach(r=>r.hidden=!r.textContent.toLocaleLowerCase().includes(q));});
document.querySelectorAll('[data-preview]').forEach(b=>b.addEventListener('click',()=>{document.querySelector('.detail-image').src=b.dataset.preview;}));
document.querySelectorAll('[data-confirm]').forEach(f=>f.addEventListener('submit',e=>{if(!confirm(document.documentElement.lang==='ar'?'تأكيد هذا الإجراء؟':'Confirm this action?'))e.preventDefault();}));

(function () {
  var form = document.querySelector('[data-offer-form]');
  if (!form) return;
  var kind = form.querySelector('[data-offer-kind]');
  var scope = form.querySelector('[data-offer-scope]');
  function has(list, value) { return (list || '').split(' ').indexOf(value) !== -1; }
  function apply() {
    var k = kind.value, s = scope ? scope.value : 'All';
    form.querySelectorAll('[data-kinds]').forEach(function (el) { el.hidden = !has(el.getAttribute('data-kinds'), k); });
    form.querySelectorAll('[data-kinds-inline]').forEach(function (el) { el.hidden = !has(el.getAttribute('data-kinds-inline'), k); });
    var productKind = k === 'Percent' || k === 'Fixed';
    form.querySelectorAll('[data-scopes]').forEach(function (el) { el.hidden = !productKind || el.getAttribute('data-scopes') !== s; });
  }
  kind.addEventListener('change', apply);
  if (scope) scope.addEventListener('change', apply);
  apply();
})();

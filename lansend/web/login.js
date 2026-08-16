const params = new URLSearchParams(location.search);
const next = params.get('next');
if (next && next.startsWith('/') && !next.startsWith('//')) {
  document.querySelector('#next').value = next;
}
if (params.get('error') === '1') {
  document.querySelector('#login-error').hidden = false;
}

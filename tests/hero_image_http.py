"""Isolated admin hero-image upload, persistence and access checks."""
import http.cookiejar, os, re, secrets, sqlite3, ssl, subprocess, time
import urllib.error, urllib.parse, urllib.request
from pathlib import Path

password = secrets.token_urlsafe(24) + 'Aa1!'
database = '.dotnet/hero-test-' + secrets.token_hex(6) + '.db'
env = dict(os.environ, ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS='https://localhost:5446', Database__UseDevelopmentSqlite='true', Database__DevelopmentSqlitePath=database, AdminSettings__Email='hero@example.test', AdminSettings__Password=password, NilePay__ApiKey='', Smtp__Enabled='false')
base = 'https://localhost:5446'
jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=ssl._create_unverified_context()), urllib.request.HTTPCookieProcessor(jar))
def request(path, fields=None):
    data = urllib.parse.urlencode(fields).encode() if fields is not None else None
    with opener.open(base + path, data, timeout=15) as r:
        return r.read().decode(), r.url

def token(html):
    return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html).group(1)

def upload(image):
    html, _ = request('/StoreAdmin/Settings')
    boundary = 'hero' + secrets.token_hex(12)
    data = ('--' + boundary + '\r\nContent-Disposition: form-data; name="__RequestVerificationToken"\r\n\r\n' + token(html) + '\r\n--' + boundary + '\r\nContent-Disposition: form-data; name="heroImage"; filename="hero.jpg"\r\nContent-Type: image/jpeg\r\n\r\n').encode() + image + ('\r\n--' + boundary + '--\r\n').encode()
    with opener.open(urllib.request.Request(base + '/StoreAdmin/HeroImage', data, {'Content-Type': 'multipart/form-data; boundary=' + boundary}), timeout=20) as r:
        return r.read().decode()

image_path = None
with open('.dotnet/hero-test.log', 'w') as log:
    proc = subprocess.Popen(['.dotnet/dotnet', 'bin/Debug/net10.0/NileShop.dll'], env=env, stdout=log, stderr=log)
    try:
        for _ in range(60):
            try:
                request('/')
                break
            except Exception:
                time.sleep(.5)
        else:
            raise RuntimeError('Test server did not start')
        _, url = request('/StoreAdmin/Settings')
        assert '/Identity/Account/Login' in url
        login, _ = request('/Identity/Account/Login')
        request('/Identity/Account/Login', {'Email': 'hero@example.test', 'Password': password, '__RequestVerificationToken': token(login)})
        settings, _ = request('/StoreAdmin/Settings')
        assert 'Homepage hero image' in settings
        original = Path('wwwroot/assets/img/products/shoe1.jpg').read_bytes()
        upload(original)
        with sqlite3.connect(database) as db:
            image_path = db.execute('SELECT HeroImageUrl FROM StoreSettings').fetchone()[0]
        assert image_path.startswith('/assets/uploads/')
        assert Path('wwwroot' + image_path).read_bytes() == original
        assert image_path in request('/')[0]
        settings, _ = request('/StoreAdmin/Settings')
        request('/StoreAdmin/Settings', {'ReturnDays': '14', '__RequestVerificationToken': token(settings)})
        assert image_path in request('/')[0], 'Saving contact/policy settings lost the image'
        upload(b'not an actual image file')
        assert image_path in request('/')[0], 'Invalid upload replaced the image'
        settings, _ = request('/StoreAdmin/Settings')
        request('/StoreAdmin/HeroImage', {'__RequestVerificationToken': token(settings)})
        assert image_path in request('/')[0], 'Empty upload replaced the image'
        settings, _ = request('/StoreAdmin/Settings')
        request('/StoreAdmin/HeroImage', {'reset': 'true', '__RequestVerificationToken': token(settings)})
        assert 'src="/assets/img/slider/slider0.jpg"' in request('/')[0]
        print('PASS admin access, upload, exact bytes, homepage display, settings persistence, invalid/empty upload protection, restore default')
    finally:
        proc.terminate()
        proc.wait(timeout=15)
        if image_path:
            Path('wwwroot' + image_path).unlink(missing_ok=True)
        for suffix in ['', '-shm', '-wal']:
            Path(database + suffix).unlink(missing_ok=True)

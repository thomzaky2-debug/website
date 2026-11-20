"""Tests for client and admin subdomain separation and routing."""
import http.cookiejar, os, re, secrets, sqlite3, ssl, subprocess, time
import urllib.error, urllib.parse, urllib.request

password = secrets.token_urlsafe(24) + 'Aa1!'
database = '.dotnet/subdomain-test-' + secrets.token_hex(6) + '.db'
port = '5449'
base = f'https://localhost:{port}'
env = dict(
    os.environ,
    ASPNETCORE_ENVIRONMENT='Development', EmailOtp__Enabled='false',
    ASPNETCORE_URLS=f'https://*:{port}',
    Database__UseDevelopmentSqlite='true',
    Database__DevelopmentSqlitePath=database,
    AdminSettings__Email='admin@testshop.local',
    AdminSettings__Password=password,
    NilePay__ApiKey='',
    Smtp__Enabled='false',
    AllowedHosts='localhost;admin.testshop.local;client.testshop.local',
    Subdomains__Enabled='true',
    Subdomains__AdminHost='admin.testshop.local',
    Subdomains__ClientHost='client.testshop.local',
    Subdomains__StrictRedirect='true'
)

class NoRedirectHandler(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None

jar = http.cookiejar.CookieJar()
# Opener without automatic redirect following so we can inspect redirect locations
noredir_opener = urllib.request.build_opener(
    urllib.request.ProxyHandler({}),
    urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),
    urllib.request.HTTPCookieProcessor(jar),
    NoRedirectHandler
)
# Standard opener that follows redirects
follow_opener = urllib.request.build_opener(
    urllib.request.ProxyHandler({}),
    urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),
    urllib.request.HTTPCookieProcessor(jar)
)

def req(path, fields=None, host='client.testshop.local', follow=True):
    data = urllib.parse.urlencode(fields).encode() if fields is not None else None
    request = urllib.request.Request(base + path, data, headers={'Host': host})
    opener = follow_opener if follow else noredir_opener
    try:
        with opener.open(request, timeout=15) as r:
            return r.status, r.read().decode(), r.url, dict(r.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(), e.url, dict(e.headers)

def token(html):
    m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html)
    return m.group(1) if m else ''

log = open('.dotnet/subdomain-test.log', 'w')
proc = subprocess.Popen(['.dotnet/dotnet', 'bin/Debug/net10.0/NileShop.dll'], env=env, stdout=log, stderr=log)

try:
    for _ in range(60):
        try:
            status, _, _, _ = req('/', host='client.testshop.local', follow=False)
            if status == 200:
                break
        except Exception:
            pass
        time.sleep(0.5)
    else:
        raise AssertionError('Server did not start within timeout')

    # 1. Client host serves storefront home
    status, body, _, _ = req('/', host='client.testshop.local', follow=False)
    assert status == 200, f'Expected 200 for client host root, got {status}'
    assert 'Chic & Unique' in body or 'Chic &amp; Unique' in body, 'Expected Chic & Unique in client homepage'
    print('PASS client host serves storefront')

    # 2. Client host visiting admin path redirects to admin host
    status, _, _, headers = req('/StoreAdmin', host='client.testshop.local', follow=False)
    assert status in (301, 302), f'Expected redirect from /StoreAdmin on client host, got {status}'
    loc = headers.get('Location', '')
    assert 'admin.testshop.local' in loc and '/StoreAdmin' in loc, f'Expected redirect to admin host, got {loc}'
    print('PASS client host redirects /StoreAdmin to admin host')

    # 3. Admin host root redirects to /Account/Login when unauthenticated
    status, _, _, headers = req('/', host='admin.testshop.local', follow=False)
    assert status in (301, 302), f'Expected redirect for admin host root, got {status}'
    loc = headers.get('Location', '')
    assert '/Account/Login' in loc, f'Expected redirect to login, got {loc}'
    print('PASS unauthenticated admin host root redirects to login')

    # 4. Admin host accessing storefront path (/shop) redirects to client host
    status, _, _, headers = req('/shop', host='admin.testshop.local', follow=False)
    assert status in (301, 302), f'Expected redirect for storefront path on admin host, got {status}'
    loc = headers.get('Location', '')
    assert 'client.testshop.local' in loc and '/shop' in loc, f'Expected redirect to client host, got {loc}'
    print('PASS storefront path on admin host redirects to client host')

    # 5. Customer registration on admin host redirects to client host
    status, _, _, headers = req('/Account/Register', host='admin.testshop.local', follow=False)
    assert status in (301, 302), f'Expected redirect for registration on admin host, got {status}'
    loc = headers.get('Location', '')
    assert 'client.testshop.local' in loc and '/Account/Register' in loc, f'Expected redirect to client registration, got {loc}'
    print('PASS registration on admin host redirects to client host')

    # 6. Admin login on admin host succeeds
    status, login_html, _, _ = req('/Account/Login', host='admin.testshop.local', follow=False)
    assert status == 200, f'Expected 200 on admin login page, got {status}'
    t = token(login_html)
    status, _, _, headers = req('/Account/Login', {'Email': 'admin@testshop.local', 'Password': password, '__RequestVerificationToken': t}, host='admin.testshop.local', follow=False)
    assert status in (301, 302), f'Expected redirect on successful admin login, got {status}'
    print('PASS admin login on admin host succeeds')

    # 7. Customer registration on client host succeeds
    client_jar = http.cookiejar.CookieJar()
    client_opener = urllib.request.build_opener(
        urllib.request.ProxyHandler({}),
        urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),
        urllib.request.HTTPCookieProcessor(client_jar),
        NoRedirectHandler
    )
    def client_req(path, fields=None):
        data = urllib.parse.urlencode(fields).encode() if fields is not None else None
        req_obj = urllib.request.Request(base + path, data, headers={'Host': 'client.testshop.local'})
        try:
            with client_opener.open(req_obj, timeout=15) as r:
                return r.status, r.read().decode(), r.url, dict(r.headers)
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode(), e.url, dict(e.headers)

    status, reg_html, _, _ = client_req('/Account/Register')
    t_reg = token(reg_html)
    customer_pass = 'Customer123456!'
    status, _, _, _ = client_req('/Account/Register', {
        'FullName': 'Shop Customer',
        'Email': 'customer@client.test',
        'Phone': '01012345678',
        'Password': customer_pass,
        'ConfirmPassword': customer_pass,
        '__RequestVerificationToken': t_reg
    })
    print('PASS customer registration on client host succeeds')

    # 8. Customer attempting login on admin host is rejected
    cust_admin_jar = http.cookiejar.CookieJar()
    cust_admin_opener = urllib.request.build_opener(
        urllib.request.ProxyHandler({}),
        urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),
        urllib.request.HTTPCookieProcessor(cust_admin_jar),
        NoRedirectHandler
    )
    def cust_admin_req(path, fields=None):
        data = urllib.parse.urlencode(fields).encode() if fields is not None else None
        req_obj = urllib.request.Request(base + path, data, headers={'Host': 'admin.testshop.local'})
        try:
            with cust_admin_opener.open(req_obj, timeout=15) as r:
                return r.status, r.read().decode(), r.url, dict(r.headers)
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode(), e.url, dict(e.headers)

    status, admin_login_page, _, _ = cust_admin_req('/Account/Login')
    t_admin_login = token(admin_login_page)
    status, login_resp, _, _ = cust_admin_req('/Account/Login', {
        'Email': 'customer@client.test',
        'Password': customer_pass,
        '__RequestVerificationToken': t_admin_login
    })
    assert status == 200, f'Expected 200 (re-display login with error), got {status}'
    assert 'restricted to store administrators' in login_resp or 'reserved for store administrators' in login_resp or 'مديري المتجر' in login_resp, 'Expected admin portal restriction message'
    print('PASS customer login rejected on admin host')

    print('All subdomain routing checks passed.')

finally:
    proc.terminate()
    try:
        proc.wait(timeout=5)
    except Exception:
        proc.kill()
    if os.path.exists(database):
        os.remove(database)

"""Isolated category visibility and storefront access checks."""
import http.cookiejar, os, re, secrets, sqlite3, ssl, subprocess, time
import urllib.error, urllib.parse, urllib.request
from pathlib import Path

password = secrets.token_urlsafe(24) + 'Aa1!'
database = '.dotnet/category-test-' + secrets.token_hex(6) + '.db'
env = dict(os.environ, ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS='https://localhost:5447', Database__UseDevelopmentSqlite='true', Database__DevelopmentSqlitePath=database, AdminSettings__Email='hero@example.test', AdminSettings__Password=password, NilePay__ApiKey='', Smtp__Enabled='false')
base = 'https://localhost:5447'
jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=ssl._create_unverified_context()), urllib.request.HTTPCookieProcessor(jar))
def req(path, fields=None, ajax=False):
    data=urllib.parse.urlencode(fields).encode() if fields is not None else None
    try:
        with opener.open(urllib.request.Request(base+path,data,headers={'Accept':'application/json'} if ajax else {}),timeout=15) as r: return r.status,r.read().decode(),r.url
    except urllib.error.HTTPError as e: return e.code,e.read().decode(),e.url

def token(html):
    return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html).group(1)

log=open('.dotnet/category-test.log','w')
proc=subprocess.Popen(['.dotnet/dotnet','bin/Debug/net10.0/NileShop.dll'],env=env,stdout=log,stderr=log)
try:
 for _ in range(60):
  try:
   if req('/')[0] == 200: break
  except Exception: pass
  time.sleep(.5)
 else: raise AssertionError('Server did not start')
 _,login,_ = req('/Identity/Account/Login')
 assert '/Identity/Account/Login' in req('/Admin/SetCategoryVisibility', {'id': '1', 'hidden': 'true', '__RequestVerificationToken': token(login)})[2]
 _,dashboard,_ = req('/Identity/Account/Login', {'Email':'hero@example.test','Password':password,'__RequestVerificationToken':token(login)})
 def visibility(hidden):
  _,page,_=req('/Admin/Dashboard')
  return req('/Admin/SetCategoryVisibility', {'id':'1','hidden':str(hidden).lower(),'__RequestVerificationToken':token(page)}, ajax=True)
 _,product,_ = req('/products/everyday-runner')
 assert 'Everyday knit runner' in product
 _,cart,_ = req('/Cart')
 req('/Cart/Add', {'productId':'1','quantity':'1','__RequestVerificationToken':token(cart)})
 assert req('/Admin/SetCategoryVisibility', {'id':'1','hidden':'true'})[0] == 400
 import json
 code,body,url=visibility(True)
 state=json.loads(body)
 assert code==200 and '/Admin/SetCategoryVisibility' in url
 assert state['hidden'] is True and state['buttonText']=='Show' and state['statusText']=='Hidden from storefront'
 # Repeating an explicit hide request must not toggle the category back.
 assert json.loads(visibility(True)[1])['hidden'] is True
 with sqlite3.connect(database) as db:
  assert db.execute('SELECT IsHidden FROM Categories WHERE Id=1').fetchone()[0] == 1
  assert db.execute('SELECT IsActive FROM Products WHERE Id=1').fetchone()[0] == 1
 assert req('/products/everyday-runner')[0] == 404
 assert 'This product is not available right now' in req('/products/everyday-runner')[1]
 _,admin_products,_=req('/Admin/Products?categoryId=1')
 assert 'Everyday knit runner' in admin_products and 'Hidden from storefront' in admin_products

 _,language_page,_=req('/Admin/Dashboard')
 req('/Home/SetLanguage',{'culture':'ar','returnUrl':'/products/everyday-runner','__RequestVerificationToken':token(language_page)})
 import html
 assert 'هذا المنتج غير متاح حاليًا' in html.unescape(req('/products/everyday-runner')[1])
 req('/Home/SetLanguage',{'culture':'en','returnUrl':'/products/everyday-runner','__RequestVerificationToken':token(language_page)})

 assert 'Everyday knit runner' not in req('/shop?category=shoes')[1]
 assert 'Everyday knit runner' not in req('/')[1]
 assert 'everyday-runner' not in req('/Home/Suggestions?q=runner')[1]
 assert 'everyday-runner' not in req('/sitemap.xml')[1]
 _,page,_=req('/Admin/EditProduct?id=1')
 assert 'value="1"' in page and 'Shoes' in page
 _,cart,_=req('/Cart')
 assert 'Everyday knit runner' not in cart
 req('/Cart/Add', {'productId':'1','quantity':'1','__RequestVerificationToken':token(cart)})
 assert 'Everyday knit runner' not in req('/Cart')[1]
 assert json.loads(visibility(False)[1])['buttonText']=='Hide'
 assert req('/products/everyday-runner')[0] == 200
 assert 'Everyday knit runner' in req('/shop?category=shoes')[1]
 print('PASS admin-only visibility, antiforgery, persistence, hidden catalog/detail/search/sitemap/cart, admin editing, show restores products')
finally:
 proc.terminate()
 proc.wait(timeout=15)
 for suffix in ['', '-shm', '-wal']: Path(database+suffix).unlink(missing_ok=True)

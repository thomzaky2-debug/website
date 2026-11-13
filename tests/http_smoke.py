import os,subprocess,secrets,time,ssl,urllib.request,urllib.parse,http.cookiejar,re
from pathlib import Path
password=secrets.token_urlsafe(24)+'Aa1!'
env=dict(os.environ,ASPNETCORE_ENVIRONMENT='Development',Database__UseDevelopmentSqlite='true',Database__DevelopmentSqlitePath='.dotnet/smoke-'+secrets.token_hex(6)+'.db',AdminSettings__Email='smoke@example.com',AdminSettings__Password=password,ASPNETCORE_URLS='https://localhost:5443')
log=open('http-server.log','w');proc=subprocess.Popen(['.dotnet/dotnet','bin/Debug/net8.0/BubbleThemeShop.dll'],env=env,stdout=log,stderr=log)
jar=http.cookiejar.CookieJar();opener=urllib.request.build_opener(urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),urllib.request.HTTPCookieProcessor(jar));base='https://localhost:5443'
def req(path,data=None):
 try:
  with opener.open(base+path,urllib.parse.urlencode(data).encode() if data is not None else None,timeout=10) as r:return r.status,r.read().decode(),r.url
 except urllib.error.HTTPError as e:return e.code,e.read().decode(),e.url
def token(body):return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',body).group(1)
def check(ok,name):
 if not ok:raise AssertionError(name)
 print('PASS '+name,flush=True)
try:
 for _ in range(60):
  try:
   status,home,_=req('/')
   if status==200:break
  except Exception:pass
  time.sleep(.5)
 else:raise Exception('Server failed to start; inspect http-server.log')
 check('Discover your everyday' in home and 'Everyday knit runner' in home,'rendered storefront and seed catalog')
 check(req('/products/everyday-runner')[0]==200,'product details route')
 check(req('/Cart/Add',{'productId':1})[0]==400,'POST without antiforgery rejected')
 check('/Identity/Account/Login' in req('/Identity/Account/Register')[2],'registration blocked')
 check('/Identity/Account/Login' in req('/Admin/Dashboard')[2],'anonymous admin access denied')
 s,ar,_=req('/Home/SetLanguage',{'culture':'ar','returnUrl':'/?q=runner','__RequestVerificationToken':token(home)})
 check(s==200 and 'dir="rtl"' in ar and 'lang="ar"' in ar,'Arabic cookie switch and RTL rendering')
 _,login,_=req('/Identity/Account/Login')
 s,dashboard,url=req('/Identity/Account/Login',{'Email':'smoke@example.com','Password':password,'__RequestVerificationToken':token(login)})
 check(s==200 and '/Admin/Dashboard' in url and 'audit-table' in dashboard,'admin login and dashboard rendering')
 check(req('/Admin/CreateProduct')[0]==200 and req('/Admin/CreateCategory')[0]==200,'bilingual editor rendering')
 print('All HTTPS smoke checks passed.',flush=True)
finally:
 proc.terminate();proc.wait(timeout=10);log.close()

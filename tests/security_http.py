"""Local isolated regression checks for headers, credentials, limits and private sessions."""
import os, re, secrets, sqlite3, ssl, subprocess, time, urllib.request, urllib.parse, urllib.error, http.cookiejar, html
from pathlib import Path

base='https://localhost:5452'; database='.dotnet/security-'+secrets.token_hex(6)+'.db'
password=secrets.token_urlsafe(24)+'Aa1!'
env=dict(os.environ, ASPNETCORE_ENVIRONMENT='Development', EmailOtp__Enabled='false', ASPNETCORE_URLS=base,
 Database__UseDevelopmentSqlite='true', Database__DevelopmentSqlitePath=database,
 AdminSettings__Email='security-admin@example.test', AdminSettings__Password=password,
 StoreWorker__ReconcilePayments='false', NilePay__Enabled='false', Smtp__Enabled='false')
jar=http.cookiejar.CookieJar()
opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),urllib.request.HTTPCookieProcessor(jar))
log=open('.dotnet/security-http.log','w'); proc=None
def req(path,data=None,headers=None):
 request=urllib.request.Request(base+path, urllib.parse.urlencode(data).encode() if data is not None else None,headers or {})
 try: response=opener.open(request,timeout=15)
 except urllib.error.HTTPError as error: response=error
 with response:return response.status,response.read().decode(),response.headers,response.url
def token(body):return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',body).group(1)
def check(value,message):
 assert value,message
 print('PASS '+message,flush=True)
def start():
 global proc
 proc=subprocess.Popen(['.dotnet/dotnet','bin/Debug/net10.0/NileShop.dll'],env=env,stdout=log,stderr=log)
 # Cold startup on a busy development machine can exceed 20 seconds.
 for _ in range(450):
  try:
   if req('/')[0]==200:return
  except (OSError,urllib.error.URLError):pass
  if proc.poll() is not None:raise AssertionError('Security test server failed; see .dotnet/security-http.log')
  time.sleep(.2)
 raise AssertionError('Security test server startup timeout')
try:
 start()
 code,home,headers,_=req('/')
 policy=headers['Content-Security-Policy']
 nonce=re.search(r"script-src 'self' 'nonce-([^']+)'",policy).group(1)
 check('script-src-attr \'none\'' in policy and 'object-src \'none\'' in policy and 'frame-ancestors \'none\'' in policy,'CSP blocks inline handlers, plugins and framing')
 check('nonce="'+nonce+'"' in html.unescape(home) and nonce not in req('/')[2]['Content-Security-Policy'],'inline scripts use a fresh nonce for each response')
 check(headers['X-Content-Type-Options']=='nosniff' and headers['X-Frame-Options']=='DENY','content type and framing headers enabled')
 check(req('/sitemap.xml',headers={'Host':'attacker.invalid'})[0]==400,'unrecognized Host headers are rejected')
 check(all(cookie.secure and any(name.lower()=='httponly' for name in cookie._rest) for cookie in jar),'session and antiforgery cookies are Secure and HttpOnly')
 req('/Cart/Add',{'productId':'1','quantity':'1','__RequestVerificationToken':token(home)})
 _,checkout,_,_=req('/Cart/Checkout');old_checkout=re.search(r'name="CheckoutToken"[^>]*value="([^"]+)"',checkout).group(1)
 _,register,_,_=req('/Account/Register')
 req('/Account/Register',{'FullName':'Security Customer','Phone':'01012345678','Email':'security-customer@example.test','Password':password,'ConfirmPassword':password,'PreferredLanguage':'en','__RequestVerificationToken':token(register)})
 _,profile,_,_=req('/Account')
 check(req('/Cart/Checkout',{'CheckoutToken':old_checkout,'__RequestVerificationToken':token(profile)})[0]==400,'changing identity invalidates the previous checkout capability')
 req('/Account/Logout',{'__RequestVerificationToken':token(profile)})
 _,login,_,_=req('/Identity/Account/Login')
 code,body,headers,_=req('/Identity/Account/Login',{'Email':'security-admin@example.test','Password':password,'__RequestVerificationToken':token(login)})
 check(code==200 and 'Dashboard' in body and 'no-store' in headers.get('Cache-Control',''),'authenticated pages disable shared/browser caching')
 check(all(cookie.secure and any(name.lower()=='httponly' for name in cookie._rest) for cookie in jar),'authentication and session cookies retain secure flags')
 check(req('/Admin/SetProductVisibility',{'id':1,'hidden':'true'})[0]==400,'admin changes require an antiforgery token')
 check(req('/webhooks/nilepay',data={})[0]==503,'disabled gateway never accepts unsigned webhook updates')
 # Force a storage failure only in this disposable database; no exception detail may escape.
 with sqlite3.connect(database) as db:db.execute('ALTER TABLE StoreSettings RENAME TO SecurityTestSettings')
 code,body,headers,_=req('/')
 check(code==500 and 'Reference:' in body and all(word not in body for word in ['SQLite','Exception','StackTrace','SELECT','Application Diagnostic Details']),'public errors reveal no database details or stack traces')
 check('no-store' in headers.get('Cache-Control','') and headers.get('Content-Security-Policy'),'error responses retain cache and browser protections')
 with sqlite3.connect(database) as db:db.execute('ALTER TABLE SecurityTestSettings RENAME TO StoreSettings')
 # A bootstrap password change must not silently reset an existing administrator.
 proc.terminate();proc.wait(timeout=15);jar.clear()
 env['AdminSettings__Email']=''
 env['AdminSettings__Password']=secrets.token_urlsafe(24)+'Bb2!'
 start();_,login,_,_=req('/Identity/Account/Login')
 code,body,_,_=req('/Identity/Account/Login',{'Email':'security-admin@example.test','Password':password,'__RequestVerificationToken':token(login)})
 check(code==200 and 'Dashboard' in body,'existing admin password survives restart without a bootstrap email and with a different bootstrap password')
 _,dashboard,_,_=req('/Admin/Dashboard')
 req('/Identity/Account/Logout',{'__RequestVerificationToken':token(dashboard)})
 _,login,_,_=req('/Identity/Account/Login');csrf=token(login)
 attempts=[req('/Identity/Account/Login',{'Email':'nobody@example.test','Password':password,'__RequestVerificationToken':csrf})[0] for _ in range(22)]
 check(429 in attempts,'admin login POST is rate limited, including unknown accounts')
 # Configuration guards fail before any production database connection is attempted.
 import resource
 resource.setrlimit(resource.RLIMIT_CORE,(0,0))
 invalid=dict(env,ASPNETCORE_ENVIRONMENT='Production',Database__UseDevelopmentSqlite='false',AllowedHosts='')
 rejected=subprocess.run(['.dotnet/dotnet','bin/Debug/net10.0/NileShop.dll'],env=invalid,capture_output=True,text=True,timeout=20)
 check(rejected.returncode!=0 and 'Configure explicit AllowedHosts' in rejected.stderr,'production refuses an unspecified host allowlist')
 invalid.update(AllowedHosts='shop.example',ConnectionStrings__DefaultConnection='Server=localhost;Database=SecurityTest;Integrated Security=True;Encrypt=False;TrustServerCertificate=True')
 rejected=subprocess.run(['.dotnet/dotnet','bin/Debug/net10.0/NileShop.dll'],env=invalid,capture_output=True,text=True,timeout=20)
 check(rejected.returncode!=0 and 'Production SQL Server connections require' in rejected.stderr,'production refuses unencrypted or unvalidated SQL connections')
 print('All security HTTP checks passed.',flush=True)
finally:
 if proc and proc.poll() is None:proc.terminate();proc.wait(timeout=15)
 log.close()

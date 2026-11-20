"""Isolated browser/admin/account language preference regression checks."""
import http.cookiejar, os, re, secrets, sqlite3, ssl, subprocess, time
import urllib.request, urllib.parse, urllib.error
from pathlib import Path
base='https://localhost:5449'
password=secrets.token_urlsafe(24)+'Aa1!'
database='.dotnet/language-test-'+secrets.token_hex(6)+'.db'
env=dict(os.environ,ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS=base,Database__UseDevelopmentSqlite='true',Database__DevelopmentSqlitePath=database,AdminSettings__Email='language-admin@example.test',AdminSettings__Password=password,StoreWorker__ReconcilePayments='false')
class Client:
 def __init__(self):
  self.jar=http.cookiejar.CookieJar()
  self.opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),urllib.request.HTTPCookieProcessor(self.jar))
 def req(self,path,data=None):
  request=urllib.request.Request(base+path,urllib.parse.urlencode(data).encode() if data is not None else None)
  try:
   with self.opener.open(request,timeout=15) as response:return response.status,response.read().decode()
  except urllib.error.HTTPError as error:return error.code,error.read().decode()
 def language(self,culture,destination):
  _,page=self.req(destination)
  return self.req('/Home/SetLanguage',{'culture':culture,'returnUrl':destination,'__RequestVerificationToken':token(page)})
def token(page):return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',page).group(1)
def has_language(page,culture):return f'lang="{culture}"' in page and ('dir="rtl"' if culture=='ar' else 'dir="ltr"') in page
def check(value,message):
 assert value,message
 print('PASS '+message,flush=True)
client=Client()
log=open('.dotnet/language-test.log','w')
proc=subprocess.Popen(['.dotnet/dotnet','bin/Debug/net10.0/NileShop.dll'],env=env,stdout=log,stderr=log)
try:
 for _ in range(60):
  try:
   if client.req('/')[0]==200:break
  except Exception:pass
  time.sleep(.5)
 else:raise AssertionError('Server did not start')
 check(has_language(client.language('ar','/shop')[1],'ar'),'guest selects Arabic storefront')
 check(has_language(client.req('/Identity/Account/Login')[1],'en'),'guest storefront choice leaves admin login independent')
 check(has_language(Client().req('/shop')[1],'en'),'another guest browser has its own default preference')
 _,login=client.req('/Identity/Account/Login')
 client.req('/Identity/Account/Login',{'Email':'language-admin@example.test','Password':password,'__RequestVerificationToken':token(login)})
 check(has_language(client.language('ar','/Admin/Dashboard')[1],'ar'),'admin selects Arabic independently')
 check(has_language(client.req('/shop')[1],'en'),'admin preference does not set storefront preference')
 client.language('en','/shop')
 check(has_language(client.req('/Admin/Dashboard')[1],'ar'),'storefront language does not change admin preference')
 check(has_language(client.req('/StoreAdmin')[1],'ar'),'all admin workspaces use admin preference')
 check(client.language('fr','/shop')[0]==400,'unsupported language rejected')
 _,admin=client.req('/Admin/Dashboard')
 client.req('/Identity/Account/Logout',{'__RequestVerificationToken':token(admin)})
 def register(email,culture):
  _,page=client.req('/Account/Register')
  return client.req('/Account/Register',{'FullName':'Language Customer','Phone':'01012345678','Email':email,'Password':password,'ConfirmPassword':password,'PreferredLanguage':culture,'__RequestVerificationToken':token(page)})
 check(has_language(register('first@example.test','ar')[1],'ar'),'signup saves first customer preferred language')
 _,account=client.req('/Account')
 check('id="preferred-language"' in account,'account includes a preferred-language selector')
 client.req('/Account/Logout',{'__RequestVerificationToken':token(account)})
 check(has_language(register('second@example.test','en')[1],'en'),'second customer uses own language on the same browser')
 check(has_language(client.language('ar','/Account')[1],'ar'),'customer can update preferred language from account')
 check(has_language(client.req('/Identity/Account/Login')[1],'en'),'customer preference does not leak into admin area')
 other=Client()
 _,login=other.req('/Account/Login')
 check(has_language(other.req('/Account/Login',{'Email':'first@example.test','Password':password,'__RequestVerificationToken':token(login)})[1],'ar'),'first customer preference follows login on another browser')
 check(has_language(other.language('en','/Account')[1],'en'),'first customer changes only own preference')
 check(has_language(client.req('/Account')[1],'ar'),'another signed-in customer remains Arabic')
 check(has_language(other.req('/Account')[1],'en'),'first customer remains English')
 with sqlite3.connect(database) as db:
  values=db.execute("SELECT u.Email,c.ClaimValue FROM AspNetUserClaims c JOIN AspNetUsers u ON u.Id=c.UserId WHERE c.ClaimType='nileshop:language:storefront' ORDER BY u.Email").fetchall()
  check(('first@example.test','en') in values and ('second@example.test','ar') in values,'language preferences persist separately per account')
 print('All language preference checks passed.',flush=True)
finally:
 proc.terminate();proc.wait(timeout=15);log.close()
 for suffix in ['', '-shm', '-wal']:Path(database+suffix).unlink(missing_ok=True)

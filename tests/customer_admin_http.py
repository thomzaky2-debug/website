"""Admin customer details, financial summaries and ownership isolation."""
import datetime, http.cookiejar, os, re, secrets, sqlite3, ssl, subprocess, time
import urllib.error, urllib.parse, urllib.request
from pathlib import Path

class Client:
 def __init__(self,base):
  self.base=base
  self.opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
 def req(self,path,data=None):
  try:
   with self.opener.open(self.base+path,urllib.parse.urlencode(data).encode() if data is not None else None,timeout=15) as response:return response.status,response.read().decode(),response.url
  except urllib.error.HTTPError as error:return error.code,error.read().decode(),error.url

def token(page):return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',page).group(1)
def check(value,message):
 assert value,message
 print('PASS '+message,flush=True)
def register(client,email,name,password):
 _,page,_=client.req('/Account/Register')
 code,body,_=client.req('/Account/Register',{'Email':email,'FullName':name,'Phone':'01012345678','Password':password,'ConfirmPassword':password,'PreferredLanguage':'en','__RequestVerificationToken':token(page)})
 assert code==200 and 'validation-summary-errors' not in body

def seed(database):
 with sqlite3.connect(database) as db:
  alice=db.execute("SELECT Id FROM AspNetUsers WHERE Email='alice@example.test'").fetchone()[0]
  bob=db.execute("SELECT Id FROM AspNetUsers WHERE Email='bob@example.test'").fetchone()[0]
  now=datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None)
  specs=[(alice,'PAID','Paid','Processing','100.10',0),(alice,'OVERPAID','Overpaid','Shipped','200.20',0),(alice,'REFUNDED','Refunded','Cancelled','50.00',0),(alice,'CANCELLED','Paid','Cancelled','75.00',0),(alice,'REVIEW','Paid','Processing','1000.00',1),(alice,'EXPIRED','Unpaid','Pending','40.00',0),(bob,'BOB','Paid','Processing','99.00',0),(None,'GUEST','Paid','Processing','70.00',0)]
  for i,(owner,label,payment,status,amount,review) in enumerate(specs):
   values={row[1]:(0 if 'INT' in row[2].upper() or row[2].upper()=='REAL' else '') for row in db.execute('PRAGMA table_info(Orders)') if row[3] and row[1]!='Id'}
   values.update(CustomerUserId=owner,OrderNumber='CUSTOMER-TEST-'+label,CheckoutToken=secrets.token_hex(16),PaymentAccessToken=secrets.token_hex(32),CustomerName='Preview customer',CustomerEmail='alice@example.test',CustomerPhone='01012345678',ShippingAddress='Test delivery address',TotalAmount=amount,PaymentStatus=payment,Status=status,NeedsPaymentReview=review,CreatedAt=(now+datetime.timedelta(minutes=i)).isoformat())
   columns=list(values)
   cursor=db.execute('INSERT INTO Orders ('+','.join(columns)+') VALUES ('+','.join('?' for _ in columns)+')',[values[column] for column in columns])
   if label=='EXPIRED':db.execute('INSERT INTO PaymentAttempts (OrderId,IdempotencyKey,RequestJson,SessionId,GatewayStatus,CreatedAt) VALUES (?,?,?,?,?,?)',(cursor.lastrowid,secrets.token_hex(16),'{}','test-session','EXPIRED',now.isoformat()))
  for owner,amount,kind in [(alice,'20.25','OverpaidCredit'),(alice,'-5.00','OrderPayment'),(bob,'999.00','OverpaidCredit')]:
   db.execute('INSERT INTO WalletTransactions (UserId,Amount,Kind,Description,CreatedAt) VALUES (?,?,?,?,?)',(owner,amount,kind,'Preview wallet entry',now.isoformat()))
  db.execute('INSERT INTO SavedProducts (UserId,ProductId,CreatedAt) VALUES (?,?,?)',(alice,1,now.isoformat()))
  db.execute('INSERT INTO ProductReviews (UserId,ProductId,Rating,DisplayName,Text,IsApproved,CreatedAt) VALUES (?,?,?,?,?,?,?)',(alice,3,4,'Alice','<script>preview</script>',0,now.isoformat()))
  return alice,bob

def main():
 base='https://localhost:5446';password=secrets.token_urlsafe(24)+'Aa1!';database='.dotnet/customer-test-'+secrets.token_hex(6)+'.db'
 env=dict(os.environ,ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS=base,Database__UseDevelopmentSqlite='true',Database__DevelopmentSqlitePath=database,AdminSettings__Email='customer-admin@example.test',AdminSettings__Password=password,StoreWorker__ReconcilePayments='false')
 log=open('.dotnet/customer-test.log','w');proc=subprocess.Popen(['.dotnet/dotnet','bin/Debug/net10.0/NileShop.dll'],env=env,stdout=log,stderr=log)
 admin=Client(base);alice=Client(base);bob=Client(base);new=Client(base)
 try:
  for _ in range(60):
   try:
    if admin.req('/')[0]==200:break
   except Exception:pass
   time.sleep(.5)
  else:raise AssertionError('Server did not start')
  register(alice,'alice@example.test','Alice Customer',password)
  register(bob,'bob@example.test','Bob Customer',password)
  register(new,'new@example.test','New Customer',password)
  _,profile,_=alice.req('/Account')
  alice.req('/Account/Profile',{'FullName':'Alice Customer','Phone':'01012345678','WhatsAppPhone':'01099887766','Address':'Example delivery street','__RequestVerificationToken':token(profile)})
  alice_id,bob_id=seed(database)
  check('AccessDenied' in bob.req('/StoreAdmin/Customer/'+alice_id)[2],'customer profile is restricted to administrators')
  check('/Identity/Account/Login' in Client(base).req('/StoreAdmin/Customer/'+alice_id)[2],'anonymous visitors cannot open customer details')
  _,login,_=admin.req('/Identity/Account/Login')
  admin.req('/Identity/Account/Login',{'Email':'customer-admin@example.test','Password':password,'__RequestVerificationToken':token(login)})
  code,directory,_=admin.req('/StoreAdmin/Customers?q=Alice')
  check(code==200 and 'Alice Customer' in directory and '01099887766' in directory,'directory includes customer contact and WhatsApp details')
  check('300.30' in directory,'paid value excludes cancelled, refunded and review-needed orders')
  check('Alice Customer' in admin.req('/StoreAdmin/Customers?q=Example+delivery')[1],'directory search includes saved delivery address')
  check('New Customer' not in admin.req('/StoreAdmin/Customers?segment=with-orders')[1] and 'New Customer' in admin.req('/StoreAdmin/Customers?segment=without-orders')[1],'order activity filters distinguish customers without orders')
  code,details,_=admin.req('/StoreAdmin/Customer/'+alice_id)
  check(code==200 and 'Example delivery street' in details and '01099887766' in details and 'English' in details,'profile shows saved contact, address and language')
  check('<h2>6</h2>' in details and '<h2>300.30 <small>EGP' in details and '<h2>15.25 <small>EGP' in details,'profile totals and wallet balance are customer scoped')
  check('CUSTOMER-TEST-BOB' not in details and 'CUSTOMER-TEST-GUEST' not in details,'customer history excludes other customers and guest orders sharing an email')
  check('Payment session expired' in details,'customer order history includes the accurate session status')
  check('&lt;script&gt;preview&lt;/script&gt;' in details and '<script>preview</script>' not in details,'review text is safely escaped')
  check('999.00' not in details,'other customer wallet activity remains excluded')
  with sqlite3.connect(database) as db:
   admin_id=db.execute("SELECT Id FROM AspNetUsers WHERE Email='customer-admin@example.test'").fetchone()[0]
   db.execute('UPDATE AspNetUsers SET LockoutEnd=? WHERE Id=?',((datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(hours=1)).isoformat(),alice_id))
  check('Temporarily locked' in admin.req('/StoreAdmin/Customer/'+alice_id)[1],'actual temporary account lockout is displayed')
  check(admin.req('/StoreAdmin/Customer/'+admin_id)[0]==404,'administrator accounts are excluded from customer profiles')
  check(admin.req('/StoreAdmin/Customer/unknown-customer')[0]==404,'unknown customers return not found')
  check('CUSTOMER-TEST-EXPIRED' in admin.req('/StoreAdmin/Customer/'+alice_id+'?page=999999')[1],'customer order pagination clamps to a valid page')
  print('All customer admin checks passed.',flush=True)
 finally:
  proc.terminate();proc.wait(timeout=15);log.close()
  for suffix in ['', '-wal', '-shm']:Path(database+suffix).unlink(missing_ok=True)

if __name__=='__main__':main()

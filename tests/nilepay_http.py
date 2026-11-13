import os,subprocess,secrets,time,ssl,urllib.request,urllib.parse,urllib.error,http.cookiejar,re,json,threading,hmac,hashlib
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
secret=secrets.token_urlsafe(32);key=secrets.token_urlsafe(32);password=secrets.token_urlsafe(24)+'Aa1!'
sessions={};requests=[];status='PENDING';mode='normal'
class Gateway(BaseHTTPRequestHandler):
 def log_message(self,*a):pass
 def do_POST(self):
  global mode
  assert self.headers.get('Authorization')=='Bearer '+key
  body=self.rfile.read(int(self.headers['Content-Length']));request=json.loads(body);identity=self.headers['Idempotency-Key'];requests.append((identity,body))
  if identity in sessions:assert sessions[identity][0]==body
  else:sessions[identity]=(body,{'sessionId':'session-'+str(len(sessions)+1),'status':'PENDING','amountEgp':request['amountEgp'],'currency':'EGP','checkoutUrl':'http://localhost:5450/hosted'})
  if mode=='failure':self.send_response(503);self.end_headers();return
  if mode=='conflict':self.send_response(409);self.end_headers();return
  self.send_response(201);self.end_headers();self.wfile.write(json.dumps({'ok':True,'checkout':sessions[identity][1]}).encode())
 def do_GET(self):
  assert self.headers.get('Authorization')=='Bearer '+key
  session=urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)['sessionId'][0]
  checkout=next(v[1].copy() for v in sessions.values() if v[1]['sessionId']==session);checkout['status']=status
  self.send_response(200);self.end_headers();self.wfile.write(json.dumps({'ok':True,'checkout':checkout}).encode())
server=ThreadingHTTPServer(('localhost',5450),Gateway);threading.Thread(target=server.serve_forever,daemon=True).start()
class NoRedirect(urllib.request.HTTPRedirectHandler):
 def redirect_request(self,*a,**k):return None
jar=http.cookiejar.CookieJar();opener=urllib.request.build_opener(urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),urllib.request.HTTPCookieProcessor(jar),NoRedirect())
base='https://localhost:5443';dbfile='.dotnet/nilepay-'+secrets.token_hex(6)+'.db'
env=dict(os.environ,ASPNETCORE_ENVIRONMENT='Development',Database__UseDevelopmentSqlite='true',Database__DevelopmentSqlitePath=dbfile,AdminSettings__Email='nilepay-test@example.com',AdminSettings__Password=password,ASPNETCORE_URLS=base,NilePay__Enabled='true',NilePay__ApiUrl='http://localhost:5450',NilePay__ApiKey=key,NilePay__WebhookSecret=secret,NilePay__PublicUrl=base)
proc=None
log=open('nilepay-server.log','w')
def start():
 global proc
 proc=subprocess.Popen(['.dotnet/dotnet','bin/Debug/net8.0/BubbleThemeShop.dll'],env=env,stdout=log,stderr=log)
 for _ in range(100):
  try:
   if req('/')[0]==200:return
  except Exception:pass
  time.sleep(.2)
 raise Exception('server did not start')
def req(path,data=None,headers=None,raw=None):
 payload=raw if raw is not None else urllib.parse.urlencode(data).encode() if data is not None else None
 request=urllib.request.Request(base+path,data=payload,headers=headers or {})
 try:r=opener.open(request,timeout=15)
 except urllib.error.HTTPError as e:r=e
 return r.code,r.read().decode(),dict(r.headers)
def token(body):return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',body).group(1)
def check(ok,name):
 if not ok:raise AssertionError(name)
 print('PASS '+name,flush=True)
def webhook(session,event='event-one',amount=None,currency='EGP',state='CONFIRMED',age=0,bad=False,headerid=None):
 body=json.dumps({'event':'checkout.updated','eventId':event,'transaction':{'sessionId':session,'amountEgp':amount if amount is not None else total,'currency':currency,'status':state}},separators=(',',':')).encode()
 ts=str(int(time.time())+age);sig=hmac.new(secret.encode(),ts.encode()+b'.'+body,hashlib.sha256).hexdigest()
 headers={'Content-Type':'application/json','X-Instapay-Event-Id':headerid or event,'X-Instapay-Timestamp':ts,'X-Instapay-Signature':'v1='+('0'*64 if bad else sig)}
 return req('/webhooks/nilepay',headers=headers,raw=body)[0]
try:
 start()
 _,home,_=req('/');req('/Cart/Add',{'productId':'1','quantity':'2','__RequestVerificationToken':token(home)})
 _,checkout,_=req('/Cart/Checkout');checkouttoken=re.search(r'name="CheckoutToken"[^>]*value="([^"]+)"',checkout).group(1)
 code,_,headers=req('/Cart/Checkout',{'CheckoutToken':checkouttoken,'CustomerName':'Payment Test','CustomerEmail':'buyer@example.com','CustomerPhone':'01234567890','ShippingAddress':'Test address','__RequestVerificationToken':token(checkout)})
 check(code==302 and '/payment/return' in headers['Location'],'order goes to payment page')
 location=headers['Location'];access=urllib.parse.parse_qs(urllib.parse.urlparse(location).query)['token'][0]
 _,page,_=req(location);check('Awaiting payment' in page,'browser return is not payment proof')
 check(req('/payment/return?token='+'A'*64)[0]==404,'unrelated customer cannot read payment')
 form={'token':access,'__RequestVerificationToken':token(page)}
 mode='failure';check(req('/payment/pay',form)[0]==302,'unknown gateway outcome returns recoverable payment page')
 check(len(sessions)==1 and len(requests)==2 and requests[0]==requests[1],'bounded retry reuses exact durable request and key')
 checkoutremote=next(iter(sessions.values()))[1];total=checkoutremote['amountEgp'];session=checkoutremote['sessionId']
 check(webhook(session)==503,'early webhook retries before mapping exists')
 proc.terminate();proc.wait(timeout=10);start();mode='conflict'
 _,conflictpage,_=req(location);conflictcode,_,conflicthead=req('/payment/pay',{'token':access,'__RequestVerificationToken':token(conflictpage)})
 check(conflictcode==302 and '/payment/return' in conflicthead['Location'] and len(sessions)==1,'idempotency conflict preserves original attempt for review')
 mode='normal'
 _,page,_=req(location);form={'token':access,'__RequestVerificationToken':token(page)}
 code,_,headers=req('/payment/pay',form)
 check(code==302 and headers['Location']=='http://localhost:5450/hosted','redirect uses returned hosted checkout URL')
 check(len(sessions)==1 and all(v==requests[0] for v in requests),'restart retry preserves request and single session')
 check(webhook(session,bad=True)==401,'invalid signature rejected')
 check(webhook(session,age=-301)==401,'stale signature rejected')
 check(webhook(session,age=301)==401,'future signature rejected')
 check(webhook(session,headerid='different')==400,'event header must match signed body')
 check(webhook('unknown-session')==503,'unknown session returns retryable response')
 check(webhook(session,amount=total+1)==400,'wrong amount rejected')
 check(webhook(session,currency='USD')==400,'wrong currency rejected')
 check(webhook(session,event='underpaid',state='UNDERPAID')==200 and 'Awaiting payment' in req(location)[1],'underpayment never marks order paid')
 check(webhook(session,event='overpaid',state='OVERPAID')==200 and 'Awaiting payment' in req(location)[1],'overpayment event alone never marks paid')
 check(webhook(session,event='expired',state='EXPIRED')==200 and 'Awaiting payment' in req(location)[1],'expiry never marks paid')
 check(webhook(session)==200 and webhook(session)==200 and webhook(session,event='another-confirmation')==200,'duplicate deliveries acknowledged safely')
 check('Payment received' in req(location)[1],'confirmed webhook marks order paid')
 import sqlite3
 with sqlite3.connect(dbfile) as db:
  check(db.execute("SELECT COUNT(*) FROM AuditLogs WHERE Action='PAYMENT_CONFIRMED'").fetchone()[0]==1,'one durable fulfilment transition and audit')
  check(db.execute('SELECT StockQuantity FROM Products WHERE Id=1').fetchone()[0]==22,'payment retries never decrement stock twice')
  check(db.execute('SELECT Status FROM Orders').fetchone()[0]=='Processing','paid order enters fulfilment queue')
 # A second order is settled by authenticated polling instead of a webhook.
 _,home,_=req('/');req('/Cart/Add',{'productId':'2','quantity':'1','__RequestVerificationToken':token(home)})
 _,checkout,_=req('/Cart/Checkout');ct=re.search(r'name="CheckoutToken"[^>]*value="([^"]+)"',checkout).group(1)
 _,_,headers=req('/Cart/Checkout',{'CheckoutToken':ct,'CustomerName':'Second','CustomerEmail':'buyer@example.com','CustomerPhone':'01234567890','ShippingAddress':'Test','__RequestVerificationToken':token(checkout)})
 loc2=headers['Location'];_,page,_=req(loc2);acc2=urllib.parse.parse_qs(urllib.parse.urlparse(loc2).query)['token'][0]
 req('/payment/pay',{'token':acc2,'__RequestVerificationToken':token(page)})
 with sqlite3.connect(dbfile) as db:db.execute('UPDATE PaymentAttempts SET LastCheckedAt=NULL');db.commit()
 status='CONFIRMED';_,page,_=req(loc2);req('/payment/check',{'token':acc2,'__RequestVerificationToken':token(page)})
 check('Payment received' in req(loc2)[1],'authenticated status reconciliation settles delayed webhook')
 print('All NilePay mock checks passed.',flush=True)
finally:
 if proc:proc.terminate();proc.wait(timeout=10)
 server.shutdown();log.close()

import os,subprocess,secrets,time,ssl,urllib.request,urllib.parse,http.cookiejar,re
from pathlib import Path
password=secrets.token_urlsafe(24)+'Aa1!'
env=dict(os.environ,ASPNETCORE_ENVIRONMENT='Development',Database__UseDevelopmentSqlite='true',Database__DevelopmentSqlitePath='.dotnet/smoke-'+secrets.token_hex(6)+'.db',AdminSettings__Email='smoke@example.com',AdminSettings__Password=password,ASPNETCORE_URLS='https://localhost:5444')
log=open('http-server.log','w');proc=subprocess.Popen(['.dotnet/dotnet','bin/Debug/net8.0/NileShop.dll'],env=env,stdout=log,stderr=log)
jar=http.cookiejar.CookieJar();opener=urllib.request.build_opener(urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),urllib.request.HTTPCookieProcessor(jar));base='https://localhost:5444'
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
 check('home-category-grid' in home and 'catalog-filters' not in home,'home is a discovery landing page without catalog filters')
 _,shop,shop_url=req('/shop')
 check('catalog-filters' in shop and 'home-hero' not in shop,'shop provides the catalog without the landing hero')
 check('/shop' in req('/?category=shoes')[2],'legacy category URLs redirect to the shop')

 check(req('/products/everyday-runner')[0]==200,'product details route')
 import json
 def suggestions(path):
  code,body,_=req(path);check(code==200,'suggestions endpoint responds');return json.loads(body)['products']
 check(any(x['name']=='Everyday knit runner' for x in suggestions('/Home/Suggestions?q=RUNNER')),'live suggestions match product names case-insensitively')
 check(suggestions('/Home/Suggestions?q=runner&category=electronics')==[],'suggestions respect the selected category')
 check(suggestions('/Home/Suggestions?q=runner&maxPrice=100')==[],'suggestions respect price limits')
 _,search_results,_=req('/?q=runner+knit')
 check('Everyday knit runner' in search_results and 'products found' in search_results,'multiword search returns ranked results with count')

 check(req('/Cart/Add',{'productId':1})[0]==400,'POST without antiforgery rejected')
 check('/Identity/Account/Login' in req('/Identity/Account/Register')[2],'registration blocked')
 check('/Identity/Account/Login' in req('/Admin/Dashboard')[2],'anonymous admin access denied')
 s,ar,_=req('/Home/SetLanguage',{'culture':'ar','returnUrl':'/?q=runner','__RequestVerificationToken':token(home)})
 check(s==200 and 'dir="rtl"' in ar and 'lang="ar"' in ar,'Arabic cookie switch and RTL rendering')
 _,login,_=req('/Identity/Account/Login')
 s,dashboard,url=req('/Identity/Account/Login',{'Email':'smoke@example.com','Password':password,'__RequestVerificationToken':token(login)})
 check(s==200 and '/Admin/Dashboard' in url and 'audit-table' in dashboard,'admin login and dashboard rendering')
 check(req('/Admin/CreateProduct')[0]==200 and req('/Admin/CreateCategory')[0]==200,'bilingual editor rendering')
 req('/Home/SetLanguage',{'culture':'en','returnUrl':'/StoreAdmin/Settings','__RequestVerificationToken':token(dashboard)})
 status,settings,_=req('/StoreAdmin/Settings')
 check(status==200 and 'SMTP host' in settings and 'Delivery area' in settings,'admin settings workspace renders')
 secret='test-mail-password-'+secrets.token_hex(8)
 status,updated,_=req('/StoreAdmin/Mail',{'Enabled':'false','Host':'smtp.example.test','Port':'587','Username':'test@example.test','NewPassword':secret,'From':'orders@example.test','EnableSsl':'true','__RequestVerificationToken':token(settings)})
 check(status==200 and 'smtp.example.test' in updated and secret not in updated,'SMTP settings saved without exposing password')
 import sqlite3
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:
  saved=db.execute('SELECT Host,EncryptedPassword FROM StoreMailSettings').fetchone()
  check(saved[0]=='smtp.example.test' and secret not in saved[1] and saved[1],'SMTP password encrypted at rest')
 check(req('/StoreAdmin/Reports')[0]==200,'sales report renders')
 status,blank,_=req('/StoreAdmin/Mail',{'Enabled':'false','Host':'','Port':'587','Username':'','From':'','__RequestVerificationToken':token(updated)})
 check(status==200,'disabled SMTP accepts blank optional fields')
 # The editor can replace the main image while preserving product details.
 import sqlite3
 from pathlib import Path
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:
  product=db.execute('SELECT Slug,Price,OldPrice,StockQuantity,ImageUrl,CategoryId,IsActive,IsFeatured FROM Products WHERE Id=1').fetchone()
  translations={row[0]:row[1:] for row in db.execute('SELECT LanguageCode,Name,ShortDescription,FullDescription FROM ProductTranslations WHERE ProductId=1')}
 _,edit,_=req('/Admin/EditProduct/1')
 fields={'__RequestVerificationToken':token(edit),'Id':'1','OriginalStockQuantity':str(product[3]),'Slug':product[0],'Price':str(product[1]),'OldPrice':str(product[2]),'StockQuantity':str(product[3]),'ImageUrl':product[4],'CategoryId':str(product[5]),'IsActive':'true','IsFeatured':'true','ReplacePrimaryImage':'true'}
 for language in ('en','ar'):
  suffix=language.title();name,short,full=translations[language]
  fields['Name'+suffix]=name;fields['ShortDescription'+suffix]=short or '';fields['FullDescription'+suffix]=full
 # Invalid edits must render actionable errors without changing saved data.
 invalid=dict(fields,ImageUrl='/invalid/path.jpg')
 status,rejected,_=req('/Admin/EditProduct',invalid)
 check(status==200 and 'validation-summary-errors' in rejected and re.search(r'<details[^>]*open',rejected),'invalid image path keeps advanced controls open')
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:
  check(db.execute('SELECT ImageUrl,StockQuantity FROM Products WHERE Id=1').fetchone()==(product[4],product[3]),'rejected edit preserves saved image and stock')
 boundary='nileshop-'+secrets.token_hex(12);parts=[]
 for name,value in fields.items():parts.append(('--'+boundary+'\r\nContent-Disposition: form-data; name="'+name+'"\r\n\r\n'+value+'\r\n').encode())
 image=Path('wwwroot/assets/img/products/shoe1.jpg').read_bytes()
 parts.append(('--'+boundary+'\r\nContent-Disposition: form-data; name="Uploads"; filename="shoe1.jpg"\r\nContent-Type: image/jpeg\r\n\r\n').encode()+image+b'\r\n')
 parts.append(('--'+boundary+'--\r\n').encode())
 upload=urllib.request.Request(base+'/Admin/EditProduct',data=b''.join(parts),headers={'Content-Type':'multipart/form-data; boundary='+boundary})
 with opener.open(upload,timeout=20) as response:check(response.status==200 and '/Admin/Dashboard' in response.url,'product editor saves uploaded image')
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:new_image=db.execute('SELECT ImageUrl FROM Products WHERE Id=1').fetchone()[0]
 check(new_image.startswith('/assets/uploads/') and new_image!=product[4],'first upload becomes main image')
 Path('wwwroot'+new_image).unlink(missing_ok=True)
 # New category + new product must use its uploaded image, never the sample shoe.
 _,category_form,_=req('/Admin/CreateCategory')
 req('/Admin/CreateCategory',{'Slug':'perfum-check','NameEn':'Perfum','NameAr':'عطور','__RequestVerificationToken':token(category_form)})
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:category_id=db.execute("SELECT Id FROM Categories WHERE Slug='perfum-check'").fetchone()[0]
 _,create_form,_=req('/Admin/CreateProduct')
 new_fields=dict(fields,Id='0',Slug='tobacco-check',ImageUrl='',CategoryId=str(category_id),NameEn='Tobacco',NameAr='عطر التبغ',__RequestVerificationToken=token(create_form))
 _,missing,_=req('/Admin/CreateProduct',new_fields)
 check('Upload a main product image before saving.' in missing,'new product without image is rejected clearly')
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:check(db.execute("SELECT COUNT(*) FROM Products WHERE Slug='tobacco-check'").fetchone()[0]==0,'missing image cannot silently save a product')
 parts=[]
 for name,value in new_fields.items():parts.append(('--'+boundary+'\r\nContent-Disposition: form-data; name="'+name+'"\r\n\r\n'+value+'\r\n').encode())
 parts.append(('--'+boundary+'\r\nContent-Disposition: form-data; name="Uploads"; filename="perfume-test.jpg"\r\nContent-Type: image/jpeg\r\n\r\n').encode()+image+b'\r\n')
 parts.append(('--'+boundary+'--\r\n').encode())
 with opener.open(urllib.request.Request(base+'/Admin/CreateProduct',data=b''.join(parts),headers={'Content-Type':'multipart/form-data; boundary='+boundary}),timeout=20) as response:check('/Admin/Dashboard' in response.url,'product with upload saves under new perfume category')
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:saved_image=db.execute("SELECT ImageUrl FROM Products WHERE Slug='tobacco-check'").fetchone()[0]
 check(saved_image.startswith('/assets/uploads/') and Path('wwwroot'+saved_image).read_bytes()==image,'new category product retains the exact uploaded image')
 Path('wwwroot'+saved_image).unlink(missing_ok=True)


 _,catalog,_=req('/')
 req('/Cart/Add',{'productId':'2','quantity':'1','__RequestVerificationToken':token(catalog)})

 import json
 def quantity_update(quantity):
  payload=urllib.parse.urlencode({'cartKey':'2::','quantity':quantity,'__RequestVerificationToken':token(catalog)}).encode()
  with opener.open(urllib.request.Request(base+'/Cart/UpdateQuantity',data=payload,headers={'Accept':'application/json'})) as response:return json.loads(response.read())
 increased=quantity_update(3)
 check(increased['count']==3 and increased['total']==2697 and increased['lines'][0]['total']==2697,'automatic quantity increase returns authoritative cart totals')
 decreased=quantity_update(1)
 check(decreased['count']==1 and decreased['total']==899,'automatic quantity decrease updates item count and total')
 rejected=quantity_update(99)
 check(rejected['error'] and rejected['count']==1 and rejected['total']==899,'stock limit preserves the valid quantity and totals')
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:db.execute('UPDATE Products SET IsActive=0 WHERE Id=2');db.commit()
 check('Your cart is empty.' in req('/Cart/Index')[1],'inactive product removed from stale customer cart')
 check(suggestions('/Home/Suggestions?q=cardigan')==[],'inactive products never appear in search suggestions')

 # Customer authentication remains separate from administrator privileges.
 _,page,_=req('/Account/Register')
 req('/Account/Logout',{'__RequestVerificationToken':token(page)})
 status,signup,_=req('/Account/Register')
 check(status==200 and 'Create your account' in signup,'customer signup uses storefront layout')
 code,anon_save,login_destination=req('/Saved/Save',{'productId':'3','__RequestVerificationToken':token(signup)})
 check(code==200 and '/Account/Login' in login_destination and urllib.parse.parse_qs(urllib.parse.urlparse(login_destination).query).get('returnUrl')==['/saved'],'anonymous save redirects to customer login with a GET-safe return URL')

 customer_email='customer@example.test';customer_password=secrets.token_urlsafe(20)+'Aa1!'
 fields={'FullName':'Customer Example','Phone':'01234567890','Email':customer_email,'Password':customer_password,'ConfirmPassword':'different','__RequestVerificationToken':token(signup)}
 status,mismatch,_=req('/Account/Register',fields)
 check(status==200 and 'validation-summary-errors' in mismatch,'signup rejects mismatched passwords')
 fields['ConfirmPassword']=customer_password
 fields['ReturnUrl']='https://example.com/untrusted'
 status,account,url=req('/Account/Register',fields)
 check(status==200 and urllib.parse.urlparse(url).path=='/' and 'My account' in account,'customer signup returns home and rejects external redirect')
 req('/Saved/Save',{'productId':'3','__RequestVerificationToken':token(account)})
 _,saved,_=req('/saved')
 check('Classic cotton shirt' in saved,'signed-in customer can save a product')
 req('/Saved/Save',{'productId':'3','__RequestVerificationToken':token(saved)})
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:check(db.execute('SELECT COUNT(*) FROM SavedProducts').fetchone()[0]==1,'saving twice is idempotent')
 code,_,_=req('/Reviews/Create',{'productId':'3','rating':'5','text':'Unverified review','__RequestVerificationToken':token(saved)})
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:check(db.execute('SELECT COUNT(*) FROM ProductReviews').fetchone()[0]==0,'customers without a qualifying order cannot review')
 req('/Saved/Remove',{'productId':'3','__RequestVerificationToken':token(saved)})
 check('Your saved list is empty' in req('/saved')[1],'customer can remove a saved item')
 check('No orders yet' in req('/Account/Orders')[1],'new account does not inherit unrelated guest orders')

 status,profile,profile_destination=req('/Account/Profile',{'FullName':'Updated Customer','Phone':'01099998888','__RequestVerificationToken':token(account)})
 check(status==200 and urllib.parse.urlparse(profile_destination).path=='/','saving profile returns the customer home')
 _,saved_profile,_=req('/Account/Index')
 check('Updated Customer' in saved_profile and '01099998888' in saved_profile,'customer details persist after redirecting home')
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:
  customer_id=db.execute('SELECT Id FROM AspNetUsers WHERE Email=?',(customer_email,)).fetchone()[0]
  check(db.execute('SELECT COUNT(*) FROM AspNetUserRoles WHERE UserId=?',(customer_id,)).fetchone()[0]==0,'signup cannot assign administrator privileges')
 code,denied,url=req('/Admin/Dashboard')
 check('AccessDenied' in url or code==403,'customer cannot access admin dashboard')
 code,_,destination=req('/StoreAdmin/Customers')
 check('AccessDenied' in destination or code==403,'customer cannot access the customer directory')
 req('/Account/Logout',{'__RequestVerificationToken':token(account)})
 _,login,_=req('/Account/Login')
 status,bad,_=req('/Account/Login',{'Email':customer_email,'Password':'wrong-password','__RequestVerificationToken':token(login)})
 check('validation-summary-errors' in bad,'customer login rejects incorrect password')
 status,logged,url=req('/Account/Login',{'Email':customer_email,'Password':customer_password,'ReturnUrl':'/Cart/Index','__RequestVerificationToken':token(login)})
 check(status==200 and '/Cart/Index' in url and 'My account' in logged,'customer login returns to requested local page')
 check('admin-bar' not in logged and '/Account/Logout' in logged,'customer navigation excludes administrator controls')
 req('/Account/Logout',{'__RequestVerificationToken':token(logged)})
 check('/Account/Login' in req('/Account/Index')[2],'logout protects the customer account page')

 _,admin_login,_=req('/Identity/Account/Login')
 req('/Identity/Account/Login',{'Email':'smoke@example.com','Password':password,'__RequestVerificationToken':token(admin_login)})
 code,directory,_=req('/StoreAdmin/Customers?q=Updated')
 check(code==200 and customer_email in directory and 'Updated Customer' in directory,'admin customer directory searches saved profile details')
 print('All HTTPS smoke checks passed.',flush=True)
finally:
 proc.terminate();proc.wait(timeout=10);log.close()

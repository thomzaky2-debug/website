import os,subprocess,secrets,time,ssl,urllib.request,urllib.parse,http.cookiejar,re
from pathlib import Path
password=secrets.token_urlsafe(24)+'Aa1!'
env=dict(os.environ,ASPNETCORE_ENVIRONMENT='Development',Database__UseDevelopmentSqlite='true',Database__DevelopmentSqlitePath='.dotnet/smoke-'+secrets.token_hex(6)+'.db',AdminSettings__Email='smoke@example.com',AdminSettings__Password=password,ASPNETCORE_URLS='https://localhost:5444')
log=open('http-server.log','w');proc=subprocess.Popen(['.dotnet/dotnet','bin/Debug/net10.0/NileShop.dll'],env=env,stdout=log,stderr=log)
jar=http.cookiejar.CookieJar();opener=urllib.request.build_opener(urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),urllib.request.HTTPCookieProcessor(jar));base='https://localhost:5444'
def req(path,data=None,headers=None):
 try:
  with opener.open(urllib.request.Request(base+path,urllib.parse.urlencode(data).encode() if data is not None else None,headers or {}),timeout=10) as r:return r.status,r.read().decode(),r.url
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
 check(shop.index('id="header-catalog"') < shop.index('class="catalog-filters panel"') < shop.index('</header>') and shop.count('id="catalog-search"')==1,'shop toolbar is in the header with no duplicate search field')
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
 check('lang="en"' in req('/Identity/Account/Login')[1],'storefront language does not affect admin login')
 req('/Home/SetLanguage',{'culture':'en','returnUrl':'/','__RequestVerificationToken':token(ar)})

 _,login,_=req('/Identity/Account/Login')
 s,dashboard,url=req('/Identity/Account/Login',{'Email':'smoke@example.com','Password':password,'__RequestVerificationToken':token(login)})
 check(s==200 and '/Admin/Dashboard' in url and 'admin-section-directory' in dashboard,'admin login and dashboard rendering')
 check('id="products"' not in dashboard and 'id="categories"' not in dashboard and 'audit-table' not in dashboard,'dashboard is an overview without embedded management sections')
 for route,marker in [('/Admin/Products','admin-products-table'),('/Admin/Categories','admin-category-grid'),('/Admin/AuditLogs','audit-table')]:
  code,section,_=req(route)
  check(code==200 and marker in section,'dedicated admin section renders: '+route)
  nav=re.search(r'<nav id="site-nav".*?</nav>',section,re.S).group(0)
  check(nav.count('aria-current="page"')==1,'dedicated section highlights one navigation link')
 check('ADMIN_LOGIN' in req('/Admin/AuditLogs?q=ADMIN_LOGIN')[1] and 'ADMIN_LOGIN' not in req('/Admin/AuditLogs?q=does-not-exist-search')[1],'audit search filters all matching activity on the server')
 _,grouped_products,_=req('/Admin/Products')
 check(grouped_products.count('class="admin-product-category"')==5 and grouped_products.count('class="admin-product-identity"')==12,'admin products are grouped by category without duplicate products')
 _,shoes_only,_=req('/Admin/Products?categoryId=1')
 check(shoes_only.count('class="admin-product-category"')==1 and shoes_only.count('class="admin-product-identity"')==3 and 'Soft knit cardigan' not in shoes_only,'admin category selection lists only matching products')
 check(req('/Admin/Products?categoryId=999999')[0]==404,'unknown admin category is rejected')
 check(req('/Admin/Products?categoryId=1&page=999999')[1].count('class="admin-product-identity"')==3,'filtered pagination clamps to a valid category page')


 check(req('/Admin/CreateProduct')[0]==200 and req('/Admin/CreateCategory')[0]==200,'bilingual editor rendering')
 _,category_editor,_=req('/Admin/CreateCategory')
 async_headers={'X-NileShop-Async':'true','Accept':'text/html'}
 code,invalid_category,_=req('/Admin/CreateCategory',{'Slug':'shoes','NameEn':'Duplicate','NameAr':'تكرار','__RequestVerificationToken':token(category_editor)},async_headers)
 check(code==200 and 'validation-summary-errors' in invalid_category,'background editor validation returns form errors')
 code,new_category,editor_url=req('/Admin/CreateCategory',{'Slug':'async-check','NameEn':'Async category','NameAr':'فئة تجريبية','__RequestVerificationToken':token(category_editor)},async_headers)
 check(code==200 and '/Admin/EditCategory' in editor_url and 'Async category' in new_category,'background category creation stays in its editor')

 req('/Home/SetLanguage',{'culture':'en','returnUrl':'/StoreAdmin/Settings','__RequestVerificationToken':token(dashboard)})
 status,settings,_=req('/StoreAdmin/Settings')
 check(status==200 and 'SMTP host' in settings and 'Delivery area' in settings,'admin settings workspace renders')
 secret='test-mail-password-'+secrets.token_hex(8)
 status,updated,_=req('/StoreAdmin/Mail',{'Enabled':'false','Host':'smtp.example.test','Port':'587','Username':'test@example.test','NewPassword':secret,'From':'orders@example.test','EnableSsl':'true','__RequestVerificationToken':token(settings)})
 check(status==200 and 'smtp.example.test' in updated and secret not in updated,'SMTP settings saved without exposing password')
 import sqlite3
 status,_,_=req('/StoreAdmin/Settings',{
  'ReturnDays':'7','ReturnsPolicyEn':'Request a return within 14 days. Refunds take 5 days.',
  'ReturnsPolicyAr':'يمكنك طلب استرجاع المنتجات خلال ٢ يومًا من تاريخ الاستلام.',
  '__RequestVerificationToken':token(settings)})
 check(status==200 and 'Request a return within 7 days. Refunds take 5 days.' in req('/returns')[1],'return window updates the storefront without changing the refund deadline')
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:
  check(db.execute('SELECT ReturnsPolicyAr FROM StoreSettings').fetchone()[0]=='يمكنك طلب استرجاع المنتجات خلال 7 يومًا من تاريخ الاستلام.','return window synchronizes the saved Arabic policy')
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
 with opener.open(upload,timeout=20) as response:check(response.status==200 and '/Admin/Products' in response.url,'product editor saves uploaded image')
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
 with opener.open(urllib.request.Request(base+'/Admin/CreateProduct',data=b''.join(parts),headers={'Content-Type':'multipart/form-data; boundary='+boundary}),timeout=20) as response:check('/Admin/Products' in response.url,'product with upload saves under new perfume category')
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:saved_image=db.execute("SELECT ImageUrl FROM Products WHERE Slug='tobacco-check'").fetchone()[0]
 check(saved_image.startswith('/assets/uploads/') and Path('wwwroot'+saved_image).read_bytes()==image,'new category product retains the exact uploaded image')
 # The simplified create form can omit its technical URL field, including without JS.
 generated=[]
 for _ in range(2):
  _,fresh_create,_=req('/Admin/CreateProduct')
  auto_fields=dict(new_fields,Slug='',NameEn='Automatic URL Product',ImageUrl=saved_image,__RequestVerificationToken=token(fresh_create))
  status,_,destination=req('/Admin/CreateProduct',auto_fields)
  check(status==200 and '/Admin/Products' in destination,'new product saves without a manually entered URL')
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:
  generated=[row[0] for row in db.execute("SELECT p.Slug FROM Products p JOIN ProductTranslations t ON t.ProductId=p.Id WHERE t.LanguageCode='en' AND t.Name='Automatic URL Product'")]
 check(len(generated)==2 and len(set(generated))==2 and all(slug.startswith('automatic-url-product') for slug in generated),'automatic product URLs are readable and disambiguate duplicate names')
 Path('wwwroot'+saved_image).unlink(missing_ok=True)


 _,catalog,_=req('/')
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:
  original_colors=db.execute('SELECT Colors FROM Products WHERE Id=2').fetchone()[0]
  db.execute("UPDATE Products SET Colors='Blue, Black' WHERE Id=2")
 _,quick_shop,_=req('/shop?q=cardigan')
 check('class="quick-add-trigger"' in quick_shop and '<option value="Blue">Blue</option>' in quick_shop,'quick add exposes product options on the card')
 add={'productId':'2','__RequestVerificationToken':token(quick_shop)}
 check(req('/Cart/AddAjax',add)[0]==400,'quick add requires a valid variant')
 code,added,_=req('/Cart/AddAjax',dict(add,color='Blue'))
 check(code==200 and '"cartCount":1' in added,'quick add updates the cart count for the selected variant')
 req('/Cart/Remove',{'cartKey':'2::Blue','__RequestVerificationToken':token(quick_shop)})
 with sqlite3.connect(env['Database__DevelopmentSqlitePath']) as db:
  db.execute('UPDATE Products SET Colors=? WHERE Id=2',(original_colors,))
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
 _,visibility_dashboard,_=req('/Admin/Dashboard')
 check(req('/Admin/SetProductVisibility',{'id':'2','hidden':'false'})[0]==400,'product visibility requires antiforgery')
 visibility_fields={'id':'2','hidden':'false','categoryId':'2','__RequestVerificationToken':token(visibility_dashboard)}
 code,shown,visibility_url= req('/Admin/SetProductVisibility',visibility_fields,async_headers)
 check(urllib.parse.parse_qs(urllib.parse.urlparse(visibility_url).query).get('categoryId')==['2'],'background product actions retain the selected category')
 check(code==200 and 'SetProductVisibility' in shown and req('/products/soft-knit-cardigan')[0]==200,'show product restores storefront visibility')
 visibility_fields['hidden']='true'
 req('/Admin/SetProductVisibility',visibility_fields,async_headers)
 req('/Admin/SetProductVisibility',visibility_fields,async_headers)
 check(req('/products/soft-knit-cardigan')[0]==404 and suggestions('/Home/Suggestions?q=cardigan')==[],'hide product is idempotent and removes direct access and search')
 check(req('/Admin/EditProduct/2')[0]==200,'hidden product remains editable in admin')
 unavailable_code,unavailable,_=req('/products/soft-knit-cardigan')
 check(unavailable_code==404 and 'This product is not available right now' in unavailable and 'id="recentlyViewed"' in unavailable,'hidden product link renders helpful unavailable page with recent history')
 check('product-buy-form' not in unavailable and 'product-detail-page' not in unavailable and 'noindex, nofollow' in unavailable,'unavailable page offers no purchase controls and does not overwrite recent history')
 check('This product is not available right now' not in req('/products/never-existed-check')[1],'unknown product retains ordinary not-found behavior')



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
 status,profile,profile_destination=req('/Account/Profile',{'FullName':'Updated Customer','Phone':'01099998888','__RequestVerificationToken':token(saved_profile)},async_headers)
 check(status==200 and '/Account' in urllib.parse.urlparse(profile_destination).path and 'Updated Customer' in profile,'background profile saves stay on the account page')

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

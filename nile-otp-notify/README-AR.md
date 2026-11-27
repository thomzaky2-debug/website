# إضافة api/notify لخدمة nile-otp (إرسال رسائل الطلبات على واتساب)

الترتيب مهم: **الخدمة الأول، وبعدين الموقع.** الموقع بيتحمل غياب الـ endpoint (بيعيد المحاولة ومبيبوظش حاجة)، لكن لازم تعمل الخطوات بالترتيب ده.

## روابط الملفات (افتح الرابط واعمل نسخ للنص كله)
- notify.js (ملف جديد): https://raw.githubusercontent.com/thomzaky2-debug/website/nile-otp-notify/nile-otp-notify/src/notify.js
- baileys.js (يستبدل القديم): https://raw.githubusercontent.com/thomzaky2-debug/website/nile-otp-notify/nile-otp-notify/src/senders/baileys.js
- تعديل server.js (سطرين بس): https://raw.githubusercontent.com/thomzaky2-debug/website/nile-otp-notify/nile-otp-notify/SERVER-JS-EDIT.txt
- كل الملفات مرة واحدة (ZIP للكمبيوتر): https://github.com/thomzaky2-debug/website/archive/refs/heads/nile-otp-notify.zip

## الجزء 1: خدمة nile-otp (من File Manager في MonsterASP)
الملفات المتغيرة: **3 ملفات بس** (ما فيش تغيير في web.config ولا الداتا ولا الباسوردات).

1. ارفع ملف جديد: `src/notify.js` (الرابط تحت) داخل `wwwroot/src/`.
2. استبدل الملف كله: `wwwroot/src/senders/baileys.js` بالنسخة من الرابط تحت. (النسخة القديمة اللي اتسلمتلك يوم 25/11 كان ال hash بتاعها `cab7012e`؛ لو كنت عدلت الملف ده بإيدك قولي قبل ما تستبدله.)
3. عدّل `wwwroot/src/server.js` (Edit) بسطرين بس، **من غير ما تمسح أو تغير أي حاجة تانية** (عشان إصلاح الـ rate-limit اللي عملته قبل كده يفضل زي ما هو):
   - تحت سطر `import { PAIR_PAGE_HTML, PAIR_PAGE_HEADERS } from './pair-page.js';` ضيف سطر:
     `import { registerNotify } from './notify.js';`
   - فوق التعليق `// Self-contained pairing page for phone-only owners.` ضيف سطر:
     `registerNotify(app, { auth, sender });`
4. اضغط **Restart** للموقع nile-otp.
5. تأكد: افتح `https://nile-otp.runasp.net/api/otp/health` لازم يرد `status: ok`. (الـ OTP لازم يفضل شغال زي ما هو.)

الجديد في الخدمة: `POST /api/notify` بنفس الـ `x-api-key` بتاع الـ OTP، بياخد `phoneNumber` و `message` (حد أقصى 1000 حرف). له حدود مستقلة تماماً عن الـ OTP: 3 رسائل في الساعة للرقم الواحد و 60 في الساعة للموقع، ومبيحسبش على حدود الـ OTP أبداً (مبيسجلش في سجل إرسال الـ OTP). الإرسال بنفس الطابور اللي بين كل رسالتين 8 ثواني.

## الجزء 2: الموقع (بعد ما الخدمة تشتغل)
1. اعمل **deploy** للموقع من main بعد دمج PR الموقع. **مفيش SQL ولا migration.**
2. في نفس المكان اللي فيه إعدادات `WhatsAppOtp__ApiKey` (web.config بتاع الموقع)، ضيف: `WhatsAppOtp__NotifyEnabled` = `true` وبعدين Restart للموقع.
   (من غير السطر ده الموقع مبيبعتش أي رسائل واتساب للعملاء، وده الوضع الافتراضي.)
3. جرّب بطلب تجريبي برقمك: لازم توصلك رسالة واتساب مع كل خطوة (طلب جديد، دفع، شحن، توصيل، إلغاء...).

## سلوك مهم
- كل خطوة بتتبعت **مرة واحدة** بس لكل طلب.
- لو الخدمة واقفة (الـ free plan بينام بعد 30 دقيقة)، الموقع بيعيد المحاولة 4 مرات على فترات (10 دقايق تقريباً)، ورسائل أقدم من ساعتين مبتتبعتش.
- فشل الواتساب مبيأثرش على الدفع أو حالة الطلب أو الإيميل.
- كل رسالة بتتسجل في سجل التدقيق باسم WHATSAPP_NOTICE (من غير رقم التليفون).
- تحذير الحظر: الرقم المربوط عن طريق Baileys (غير رسمي). زيادة عدد الرسائل مع كل خطوة بتزود مخاطر تقييد الرقم، والرقم مخصص ومش شخصي زي ما اتفقنا.

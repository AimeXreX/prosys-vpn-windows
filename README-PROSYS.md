# ProSyS VPN for Windows

نسخهٔ ویندوزی ProSyS VPN، فورک [v2rayN](https://github.com/2dust/v2rayN) با رابط گیمینگ هم‌سبک سایت، مینی‌اپ تلگرام و اپ اندروید (v4). مجوز GPL-3.0 و انتساب پروژهٔ اصلی حفظ شده است.

## چه چیزی اضافه شده

| بخش | فایل |
| --- | --- |
| صفحهٔ اصلی: اوربِ پورتال با حلقه‌های چرخان، glow هنگام اتصال، سهمیهٔ روزانه، سرعت، خرید | `v2rayN/v2rayN/Views/ProSySHomeView.xaml` |
| منطق صفحهٔ اصلی (اتصال/قطع، TUN، شمارش معکوس تمدید) | `v2rayN/v2rayN/ViewModels/ProSySHomeViewModel.cs` |
| سرویس رایگان ۵۰۰ مگ روزانه (همتای `FreeAccessManager` اندروید) | `v2rayN/v2rayN/ProSyS/ProSySFreeManager.cs` |
| توکن‌ها و آدرس‌ها | `v2rayN/v2rayN/ProSyS/ProSySBrand.cs` |
| رنگ‌ها، گرادیان‌ها و استایل دکمه‌ها (از `home.css` و `miniapp-theme.css` سایت) | `v2rayN/v2rayN/ProSyS/ProSySTheme.xaml` |
| تم تیرهٔ اجباری برای حالت پیشرفته | `v2rayN/v2rayN/ViewModels/ThemeSettingViewModel.cs` |
| قفل سرور رایگان (بدون ویرایش، کپی، export، QR، انتقال گروه) | `ServiceLib/ViewModels/ProfilesViewModel.cs`، `ServiceLib/Handler/Fmt/FmtHandler.cs` |
| آیکن‌ها و فونت Vazirmatn | `v2rayN/v2rayN/Resources/` |
| بسته‌سازی و CI | `prosys/package-windows.sh`، `.github/workflows/prosys-windows.yml` |

## سرویس رایگان

- شناسهٔ نصب: SHA-256 از `MachineGuid` ویندوز با پیشوند `prosys-windows:`؛ شناسهٔ خام ارسال نمی‌شود.
- توکن دستگاه فقط با DPAPI (حساب کاربری فعلی) در `guiConfigs/prosys-device.bin` ذخیره می‌شود و در دیتابیس پروفایل‌ها یا بکاپ نیست.
- همان API بک‌اند v4 اندروید: `POST /api/free/register`، `GET /api/free/status`، `GET /api/free/subscription` با هدر `X-ProSys-Device`. هیچ تغییری در بک‌اند لازم نیست؛ cooldown و سقف‌های روزانهٔ v4 برای ویندوز هم اعمال می‌شود.
- وضعیت هر ۵ دقیقه و subscription هر ۶۰ دقیقه به‌روز می‌شود. با تمام‌شدن حجم، اگر روی سرور رایگان وصل باشید اتصال قطع و کارت خرید نمایش داده می‌شود.
- گروه `prosysfree` آدرس subscription خالی دارد تا به‌روزرسان عمومی v2rayN آن را نادیده بگیرد و توکن جایی ذخیره نشود.

محدودیت: روی دسکتاپ، فایل کانفیگ هسته (`config.json`) روی دیسک است و کاربر حرفه‌ای می‌تواند آن را بخواند. محافظ اصلی همان `limitIp=1` در 3x-ui است.

## ساخت

```bash
# نیاز: .NET 10 SDK، curl، unzip، zip (روی لینوکس هم کار می‌کند)
prosys/package-windows.sh x64 ./dist
prosys/package-windows.sh arm64 ./dist
```

خروجی: `dist/ProSySVPN-<version>-windows-<arch>.zip` به‌همراه Xray-core، sing-box و geo فایل‌های ایران.

با push کردن تگ `v1.0.0` به گیت‌هاب، workflow هر دو معماری را می‌سازد و Release منتشر می‌کند.

نسخهٔ برنامه در `v2rayN/v2rayN/v2rayN.csproj` (`<Version>`) تنظیم می‌شود.

## به‌روزرسانی از upstream

```bash
git remote add upstream https://github.com/2dust/v2rayN.git
git fetch upstream && git merge upstream/master
```

workflowهای اصلی v2rayN به `.github/upstream-workflows/` منتقل شده‌اند تا اجرا نشوند.

## تغییرات 1.1.0

- رفع کرش هنگام اجرا (سرریز عددی در تبدیل رنگ تم، `ThemeSettingViewModel`).
- رفع کرش وقتی همگام‌سازی سرور رایگان زودتر از آماده‌شدن برنامه تمام می‌شد (`MainWindowViewModel` منتظر `Init` می‌ماند).
- پیش‌تنظیم ایران دیگر DNS راه‌دور را فعال نمی‌کند؛ قالب `v2ray.json` آن مخزن با این نسخه سازگار نبود و Xray اجرا نمی‌شد. نصب‌های 1.0.0 یک بار خودکار تعمیر می‌شوند.
- پورت پیش‌فرض محلی `20808` است تا کنار v2rayN معمولی (پورت 10808) بدون تداخل اجرا شود.
- توکن دستگاه در `%LOCALAPPDATA%\ProSySVPN` نگه داشته می‌شود، نه داخل پوشهٔ برنامه. backend برای ثبت دوبارهٔ همان PC بدون توکن خطای 409 می‌دهد، پس با هر بار استخراج دوبارهٔ zip کاربر از سرویس رایگان محروم می‌شد.
- انتخاب خودکار سریع‌ترین سرور رایگان با تست real-ping بعد از هر به‌روزرسانی subscription و با دکمهٔ «سریع‌ترین سرور رایگان».
- نمایش درست سرعت لحظه‌ای (آمار از قبل به کیلوبایت است) و فعال‌بودن آمار به‌صورت پیش‌فرض.
- نوار عنوان تیره، نسخهٔ درست ProSyS در عنوان و صفحهٔ اصلی، حذف پرچم‌های ایموجی از نام سرور.
- به‌روزرسانی خودکار برنامه از مخزن 2dust/v2rayN غیرفعال شد تا ProSyS با v2rayN معمولی جایگزین نشود (به‌روزرسانی هسته‌ها و geo فعال است).
- طراحی تازهٔ حالت پیشرفته: هدر برند، نوار ابزار و جدول سرورها و لاگ و نوار وضعیت در پنل‌های گرد تیره، ردیف سرور فعال با هایلایت سبز ملایم.
- قوانین مسیریابی ایران (`Sample/prosys_ir_*`) داخل برنامه است؛ اگر گیت‌هاب در دسترس نباشد دیگر قوانین چین جایگزین نمی‌شود و نصب‌هایی که روی قوانین چین مانده‌اند خودکار به ایران منتقل می‌شوند.
- نصب‌کنندهٔ Setup با Inno Setup 6 (`prosys/installer/ProSySVPN.iss`): نصب برای کاربر فعلی بدون Administrator، فارسی و انگلیسی، میانبر دسکتاپ و اجرای خودکار اختیاری، حفظ تنظیمات هنگام ارتقا، و حذف کامل با بستن برنامه و خاموش‌کردن پراکسی سیستم.
- CI روی ویندوز اجرا می‌شود تا Setup هم ساخته شود.

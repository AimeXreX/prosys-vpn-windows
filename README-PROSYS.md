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

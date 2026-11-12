<div dir="rtl">

# ProSyS VPN برای ویندوز

کلاینت VPN ویندوز ProSyS، هم‌سبک با [اپ اندروید](https://github.com/AimeXreX/prosys-vpn-android) و [سایت ProSyS](https://vpn.prosysvpn.top/). بر پایهٔ [v2rayN](https://github.com/2dust/v2rayN) و هستهٔ [Xray](https://github.com/XTLS/Xray-core) ساخته شده است.

## دانلود

آخرین نسخه را از بخش [Releases](https://github.com/AimeXreX/prosys-vpn-windows/releases/latest) بگیرید:

| فایل | توضیح |
| --- | --- |
| `ProSySVPN-<نسخه>-windows-x64-setup.exe` | نصب‌کننده (پیشنهادی). بدون نیاز به Administrator نصب می‌شود و میانبر منوی Start می‌سازد. |
| `ProSySVPN-<نسخه>-windows-x64.zip` | نسخهٔ پرتابل: از حالت فشرده خارج کنید و `ProSySVPN.exe` را اجرا کنید. |

کنار هر فایل، هش SHA-256 آن (`.sha256`) هم منتشر می‌شود. ویندوز ۱۰ یا ۱۱ لازم است.

اگر ویندوز پیام «Windows protected your PC» نشان داد، روی **More info** و بعد **Run anyway** بزنید (فایل‌ها هنوز امضای دیجیتال تجاری ندارند).

## امکانات

- **سرویس رایگان:** هر کامپیوتر روزانه ۵۰۰ مگابایت حجم رایگان دارد که هر شب ساعت ۰۰:۰۰ به وقت تهران تمدید می‌شود.
- **انتخاب خودکار سریع‌ترین سرور:** همهٔ سرورهای رایگان با real-ping تست می‌شوند و سریع‌ترین انتخاب می‌شود.
- **یک دکمه برای اتصال:** پراکسی سیستم برای مرورگر و برنامه‌ها، یا **حالت کامل (TUN)** برای بازی‌ها و همهٔ برنامه‌ها.
- **مسیریابی ایران:** سایت‌های ایرانی مستقیم و بقیه از تونل؛ قوانین ایران داخل برنامه هست و به دسترسی به گیت‌هاب وابسته نیست.
- **نمایش زنده:** سهمیهٔ باقی‌مانده، سرعت دانلود و آپلود، و پینگ سرور.
- **حالت پیشرفته:** همهٔ امکانات v2rayN مثل افزودن کانفیگ و subscription شخصی، مسیریابی، DNS و تست سرعت.
- کنار v2rayN معمولی اجرا می‌شود و تداخل ندارد (پورت محلی `20808`).

اشتراک نامحدود را از [سایت](https://vpn.prosysvpn.top/) یا [ربات تلگرام](https://t.me/ProSySsellbot) تهیه کنید. اخبار در [کانال تلگرام](https://t.me/Prosys_VPN) منتشر می‌شود.

## حریم خصوصی

- شناسهٔ دستگاه، هش SHA-256 از `MachineGuid` ویندوز است و شناسهٔ خام ارسال نمی‌شود.
- توکن سرویس رایگان با DPAPI و فقط برای همان کاربر ویندوز در `%LOCALAPPDATA%\ProSySVPN` ذخیره می‌شود.
- متن کامل: [vpn.prosysvpn.top/privacy](https://vpn.prosysvpn.top/privacy)

## ساخت از سورس

نیازها: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)، Git Bash (یا لینوکس/مک) با `curl` و `unzip`، و برای ساخت نصب‌کننده [Inno Setup 6](https://jrsoftware.org/isinfo.php).

</div>

```bash
git clone https://github.com/AimeXreX/prosys-vpn-windows.git
cd prosys-vpn-windows
prosys/package-windows.sh x64 ./dist     # zip + Setup (اگر Inno Setup نصب باشد)
prosys/package-windows.sh arm64 ./dist
```

<div dir="rtl">

با push کردن تگ `v*` به گیت‌هاب، workflow `prosys-windows.yml` نسخه‌های x64 و arm64 (zip و Setup) را می‌سازد و Release منتشر می‌کند. نسخهٔ برنامه در `v2rayN/v2rayN/v2rayN.csproj` (`<Version>`) تنظیم می‌شود.

جزئیات فنی، ساختار کد ProSyS و تغییرات هر نسخه در [README-PROSYS.md](README-PROSYS.md) آمده است.

## مجوز

این پروژه تحت مجوز [GPL-3.0](LICENSE) منتشر می‌شود و انتساب پروژهٔ اصلی حفظ شده است ([README اصلی v2rayN](UPSTREAM_README.md)).

- [v2rayN](https://github.com/2dust/v2rayN) از 2dust (GPL-3.0)
- [Xray-core](https://github.com/XTLS/Xray-core) (MPL-2.0) و [sing-box](https://github.com/SagerNet/sing-box) (GPL-3.0)
- قوانین مسیریابی و geo ایران از [Chocolate4U/Iran-v2ray-rules](https://github.com/Chocolate4U/Iran-v2ray-rules)
- فونت [Vazirmatn](https://github.com/rastikerdar/vazirmatn) (OFL-1.1) و ترجمهٔ فارسی نصب‌کنندهٔ [Inno Setup](https://github.com/jrsoftware/issrc)

</div>

---

**English:** ProSyS VPN for Windows is a branded [v2rayN](https://github.com/2dust/v2rayN) fork with a one-click home screen, a managed free tier (500 MB per PC per day) and automatic fastest-server selection. Download the Setup or the portable zip from [Releases](https://github.com/AimeXreX/prosys-vpn-windows/releases/latest). GPL-3.0.

#!/usr/bin/env bash
# Builds the portable ProSyS VPN for Windows package (x64 or arm64).
# Usage: prosys/package-windows.sh [x64|arm64] [output-dir]
# Needs: .NET 10 SDK, curl, unzip, zip. Works on Linux, macOS or Windows (Git Bash).
set -euo pipefail

ARCH="${1:-x64}"
OUT="${2:-$(pwd)/dist}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SINGBOX_VERSION="${SINGBOX_VERSION:-1.14.4}"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/v2rayN/v2rayN/v2rayN.csproj" | head -1)"

case "$ARCH" in
  x64)   RID=win-x64;   XRAY_ZIP=Xray-windows-64.zip;        SB_ARCH=amd64 ;;
  arm64) RID=win-arm64; XRAY_ZIP=Xray-windows-arm64-v8a.zip; SB_ARCH=arm64 ;;
  *) echo "unknown arch $ARCH" >&2; exit 1 ;;
esac

NAME="ProSySVPN-${VERSION}-windows-${ARCH}"
WORK="$(mktemp -d)"
PKG="$WORK/$NAME"
mkdir -p "$PKG/bin/xray" "$PKG/bin/sing_box" "$OUT"

echo "==> Publishing ProSyS VPN $VERSION ($RID)"
dotnet publish "$ROOT/v2rayN/v2rayN/v2rayN.csproj" -c Release -r "$RID" \
  -p:SelfContained=true -p:EnableWindowsTargeting=true \
  -p:EnableCompressionInSingleFile=true -o "$PKG"

echo "==> Downloading cores"
curl -fsSL -o "$WORK/xray.zip" "https://github.com/XTLS/Xray-core/releases/latest/download/$XRAY_ZIP"
unzip -q -o "$WORK/xray.zip" xray.exe wintun.dll LICENSE -d "$PKG/bin/xray"
mv "$PKG/bin/xray/LICENSE" "$PKG/bin/xray/LICENSE-Xray.txt"

curl -fsSL -o "$WORK/sb.zip" "https://github.com/SagerNet/sing-box/releases/download/v${SINGBOX_VERSION}/sing-box-${SINGBOX_VERSION}-windows-${SB_ARCH}.zip"
unzip -q -o -j "$WORK/sb.zip" -d "$PKG/bin/sing_box"
[ -f "$PKG/bin/sing_box/LICENSE" ] && mv "$PKG/bin/sing_box/LICENSE" "$PKG/bin/sing_box/LICENSE-sing-box.txt"

echo "==> Downloading Iran geo rules"
for f in geoip geosite; do
  curl -fsSL -o "$PKG/bin/$f.dat" "https://github.com/Chocolate4U/Iran-v2ray-rules/releases/latest/download/$f.dat"
done

cp "$ROOT/LICENSE" "$PKG/LICENSE-GPL-3.0.txt"
cp "$ROOT/prosys/README-FA.txt" "$PKG/راهنما.txt"

(cd "$WORK" && zip -qr -9 "$OUT/$NAME.zip" "$NAME")
(cd "$OUT" && sha256sum "$NAME.zip" > "$NAME.zip.sha256")
echo "==> $OUT/$NAME.zip"
rm -rf "$WORK"

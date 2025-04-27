#!/usr/bin/env bash
set -euox pipefail
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT INT SIGTERM

have() { command -v "$1" >/dev/null 2>&1; }
die() { printf 'error: %s\n' "$1" >&2; exit 1; }

have curl || die "missing curl"
have dotnet || die "missing dotnet"
have llvm-strip || die "missing llvm-strip (install llvm and ensure it is in PATH)"

if [ -z "${P12_BASE64-}" ] || [ -z "${P12_BASE64// }" ]; then
  die "P12_BASE64 is required for pack.sh"
fi

GHP="$TMP/gh-pick.sh"
curl -fsSL "https://raw.githubusercontent.com/Itexoft/devops/refs/heads/master/gh-pick.sh" -o "$GHP"
chmod +x "$GHP"

CCR=$("$GHP" "@master" "lib/cert-converter.sh")
SIGN=$("$GHP" "@master" "lib/sign-tool.sh")
DSP=$("$GHP" "@master" "lib/dotnet-sign-pack.sh")

CERT_INPUT="$TMP/cert.input"
printf '%s' "$P12_BASE64" >"$CERT_INPUT"

PFX="$TMP/cert.pfx"
if [ -n "${P12_PASSWORD-}" ]; then
  "$CCR" "$P12_BASE64" pfx "$PFX" "--password=$P12_PASSWORD"
else
  "$CCR" "$P12_BASE64" pfx "$PFX"
fi

SNK="$TMP/strongname.snk"
"$CCR" "$P12_BASE64" snk "$SNK"

RID_LIST="osx-arm64;linux-x64;linux-arm64;win-x64;browser-wasm"
NATIVE_PROJECT="$SCRIPT_DIR/src/Private/Native/Native.csproj"
NATIVE_WASM_PROJECT="$SCRIPT_DIR/src/Private/Native/NativeWasm.csproj"
NATIVE_STAGE="$TMP/native"
mkdir -p "$NATIVE_STAGE"

IFS=';' read -r -a RIDS <<< "$RID_LIST"
for rid in "${RIDS[@]}"; do
  if [ "$rid" = "browser-wasm" ]; then
    continue
  fi
  out_dir="$NATIVE_STAGE/$rid"
  rm -rf "$out_dir"
  mkdir -p "$out_dir"
  DOTNET_CLI_UI_LANGUAGE=en dotnet publish "$NATIVE_PROJECT" -c Release -r "$rid" -o "$out_dir"
done

WASM_PUBLISH="$TMP/wasm-publish"
rm -rf "$WASM_PUBLISH"
mkdir -p "$WASM_PUBLISH"
DOTNET_CLI_UI_LANGUAGE=en dotnet publish "$NATIVE_WASM_PROJECT" -c Release -o "$WASM_PUBLISH"
wasm_out="$(find "$WASM_PUBLISH" -type f -path '*/wwwroot/_framework/itexoft-native*.wasm' | head -n 1)"
[ -n "$wasm_out" ] || die "wasm output not found"
mkdir -p "$NATIVE_STAGE/browser-wasm"
cp "$wasm_out" "$NATIVE_STAGE/browser-wasm/"

mapfile -t native_files < <(find "$NATIVE_STAGE" -type f \( -name 'itexoft-native.*' -o -name 'libitexoft-native.*' \) ! -name '*.wasm')
mapfile -t wasm_files < <(find "$NATIVE_STAGE/browser-wasm" -type f -name 'itexoft-native*.wasm')
[ "${#native_files[@]}" -gt 0 ] || die "no native binaries produced"
[ "${#wasm_files[@]}" -gt 0 ] || die "no wasm binaries produced"

have wasm-strip || die "missing wasm-strip (install binaryen and ensure it is in PATH)"

strip_wasm() {
  local f="$1" tmp="$TMP/$(basename "$f").wasm.strip"
  wasm-strip -o "$tmp" "$f"
  mv -f "$tmp" "$f"
}

for f in "${native_files[@]}"; do
  case "$f" in
    *.dll|*.so|*.dylib) llvm-strip --strip-all "$f";;
  esac
done
for f in "${wasm_files[@]}"; do
  strip_wasm "$f"
done

SIGN_ARGS=()
if [ -n "${P12_PASSWORD-}" ]; then
  SIGN_ARGS+=("--password=$P12_PASSWORD")
fi

if [ "${#native_files[@]}" -gt 0 ]; then
  "$SIGN" "$CERT_INPUT" "${native_files[@]}" "${SIGN_ARGS[@]}"
fi

ITEXOFT_PROJECT="$SCRIPT_DIR/src/Itexoft/Itexoft.csproj"

DOTNET_CLI_UI_LANGUAGE=en dotnet build "$ITEXOFT_PROJECT" -c Release \
  "/p:ItexoftNativeInputRoot=$NATIVE_STAGE/" \
  "/p:ItexoftNativeRids=$RID_LIST" \
  "/p:SignAssembly=true" "/p:PublicSign=false" "/p:AssemblyOriginatorKeyFile=$SNK"

mapfile -t itexoft_dlls < <(find "$SCRIPT_DIR/src/Itexoft/bin" -type f -path '*/Release/net10.0/Itexoft.dll')
[ "${#itexoft_dlls[@]}" -gt 0 ] || die "Itexoft.dll not found after build"

"$SIGN" "$CERT_INPUT" "${itexoft_dlls[@]}" "${SIGN_ARGS[@]}"

OUT_DIR="$SCRIPT_DIR/nuget"
mkdir -p "$OUT_DIR"

"$DSP" -c Release "$ITEXOFT_PROJECT" -o "$OUT_DIR" --no-build \
  "/p:ItexoftNativeInputRoot=$NATIVE_STAGE/" \
  "/p:ItexoftNativeRids=$RID_LIST" \
  --cert="$P12_BASE64" \
  ${P12_PASSWORD:+--password="$P12_PASSWORD"}

mapfile -t nupkgs < <(find "$OUT_DIR" -maxdepth 1 -type f -name '*.nupkg')
[ "${#nupkgs[@]}" -gt 0 ] || die "no .nupkg produced"

for pkg in "${nupkgs[@]}"; do
  dotnet nuget sign "$pkg" --certificate-path "$PFX" --overwrite \
    ${P12_PASSWORD:+--certificate-password "$P12_PASSWORD"} \
    --timestamper "${TIMESTAMPER:-https://timestamp.digicert.com}"
done

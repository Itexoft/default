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

SIGN_ARGS=()
if [ -n "${P12_PASSWORD-}" ]; then
  SIGN_ARGS+=("--password=$P12_PASSWORD")
fi

ITEXOFT_PROJECT="$SCRIPT_DIR/src/Itexoft/Itexoft.csproj"

DOTNET_CLI_UI_LANGUAGE=en dotnet build "$ITEXOFT_PROJECT" -c Release \
  "/p:SignAssembly=true" "/p:PublicSign=false" "/p:AssemblyOriginatorKeyFile=$SNK"

itexoft_dll="$(
  DOTNET_CLI_UI_LANGUAGE=en dotnet msbuild "$ITEXOFT_PROJECT" -nologo -getProperty:TargetPath \
    "/p:Configuration=Release" \
    "/p:SignAssembly=true" "/p:PublicSign=false" "/p:AssemblyOriginatorKeyFile=$SNK"
)"
[ -n "${itexoft_dll-}" ] || die "Itexoft.dll target path is empty"
[ -f "$itexoft_dll" ] || die "Itexoft.dll not found after build: $itexoft_dll"

itexoft_dlls=("$itexoft_dll")
"$SIGN" "$CERT_INPUT" "${itexoft_dlls[@]}" ${SIGN_ARGS[@]+"${SIGN_ARGS[@]}"}

OUT_DIR="$SCRIPT_DIR/nuget"
mkdir -p "$OUT_DIR"

"$DSP" -c Release "$ITEXOFT_PROJECT" -o "$OUT_DIR" --no-build \
  --cert="$P12_BASE64" \
  ${P12_PASSWORD:+--password="$P12_PASSWORD"}

nupkgs=()
while IFS= read -r -d '' pkg; do
  nupkgs+=("$pkg")
done < <(find "$OUT_DIR" -maxdepth 1 -type f -name '*.nupkg' -print0)
[ "${#nupkgs[@]}" -gt 0 ] || die "no .nupkg produced"
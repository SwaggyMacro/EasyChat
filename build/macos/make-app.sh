#!/usr/bin/env bash
# Builds EasyChat.app for Apple Silicon.
#
# Usage: build/macos/make-app.sh [output-directory]
#
# Signing is deliberately separate from distribution. Without a Developer ID certificate this
# produces an ad-hoc signed bundle, which runs on the machine that built it but is rejected by
# Gatekeeper anywhere else. Set EASYCHAT_SIGN_IDENTITY to sign for distribution; notarisation needs
# an Apple account and is a further step, documented in macOS.md rather than automated here.
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output_directory="${1:-${repository_root}/publish/macos}"
host_project="${repository_root}/src/Host/EasyChat.Desktop.MacOS/EasyChat.Desktop.MacOS.csproj"
bundle="${output_directory}/EasyChat.app"
contents="${bundle}/Contents"
staging="${output_directory}/.staging"

echo "==> Publishing osx-arm64"
rm -rf "${bundle}" "${staging}"
mkdir -p "${contents}/MacOS" "${contents}/Resources" "${staging}"

# Single file, but without self-extracting the native libraries.
#
# This is what makes the bundle signable. codesign treats every file beside the main executable as
# nested code, so an ordinary publish — which leaves deps.json, runtimeconfig.json and a few hundred
# managed assemblies there — cannot be signed at all: the JSON files are not code and cannot carry a
# signature. Single file folds the runtime, the assemblies and both JSON files into the executable,
# leaving only Mach-O libraries beside it.
#
# The native libraries are deliberately *not* self-extracted. Extracted copies land outside the
# bundle and carry no signature, which the hardened runtime then refuses to load unless library
# validation is disabled. Keeping them as real files means they are signed with everything else.
dotnet publish "${host_project}" \
    --configuration Release \
    --runtime osx-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:DebugType=none \
    --output "${staging}"

echo "==> Assembling the bundle"
mv "${staging}/Info.plist" "${contents}/Info.plist"
entitlements="${staging}/EasyChat.entitlements"
plutil -lint "${contents}/Info.plist" > /dev/null

mv "${staging}/EasyChat" "${contents}/MacOS/EasyChat"
find "${staging}" -maxdepth 1 -type f -name '*.dylib' -exec mv {} "${contents}/MacOS/" \;

# Everything that is not code belongs in Resources, or codesign would treat it as nested code.
find "${staging}" -mindepth 1 -maxdepth 1 -type d -exec mv {} "${contents}/Resources/" \;

icon="${repository_root}/build/macos/EasyChat.icns"
if [[ -f "${icon}" ]]; then
    cp "${icon}" "${contents}/Resources/EasyChat.icns"
else
    echo "    note: no EasyChat.icns found; the bundle will use the generic application icon."
fi

remaining="$(find "${staging}" -mindepth 1 ! -name 'EasyChat.entitlements' -print -quit)"
if [[ -n "${remaining}" ]]; then
    echo "error: unexpected file left in the publish output: ${remaining}" >&2
    echo "       anything beside the executable must be code, or the bundle cannot be signed." >&2
    exit 1
fi

echo "==> Signing"
identity="${EASYCHAT_SIGN_IDENTITY:--}"

# A secure timestamp is required for notarisation but cannot be obtained for an ad-hoc signature.
#
# The hardened runtime is likewise only applied with a real identity. Its library validation
# requires every dylib to carry the main executable's Team ID, and ad-hoc signatures have none, so
# an ad-hoc hardened bundle passes codesign --verify yet fails to dlopen libSkiaSharp the moment it
# draws its first window. Notarisation needs the hardened runtime; a local build does not.
if [[ "${identity}" == "-" ]]; then
    timestamp=(--timestamp=none)
    runtime=()
else
    timestamp=(--timestamp)
    runtime=(--options runtime)
fi

# Inside out: signing a nested file after the bundle would invalidate the bundle's signature.
while IFS= read -r -d '' binary; do
    codesign --force "${timestamp[@]}" ${runtime[@]+"${runtime[@]}"} --sign "${identity}" "${binary}"
done < <(find "${contents}/MacOS" -type f -name '*.dylib' -print0)

codesign --force "${timestamp[@]}" ${runtime[@]+"${runtime[@]}"} \
    --entitlements "${entitlements}" \
    --sign "${identity}" "${bundle}"
rm -rf "${staging}"

echo "==> Verifying"
codesign --verify --deep --strict --verbose=2 "${bundle}"
codesign --display --entitlements - "${bundle}" > /dev/null

if [[ "${identity}" == "-" ]]; then
    echo "    ad-hoc signed: Gatekeeper assessment is expected to fail until a Developer ID is used."
else
    spctl --assess --type execute --verbose=2 "${bundle}"
fi

echo "==> Checking the bundle starts"
# Builds the whole dependency graph and exits without showing a window, which catches a bundle that
# assembles and signs but cannot actually run — a hardened-runtime entitlement missing for the .NET
# runtime, for instance, shows up here and nowhere else.
#
# While the OCR, image cleaning and audio capture adapters are outstanding the graph is genuinely
# incomplete, so a failure here is reported rather than treated as a broken bundle.
composition_log="${output_directory}/composition.log"
composition_ok=0
# Through a nested shell so this one does not print its own notice when the runtime aborts on an
# incomplete graph; the notice goes to the log with everything else.
{
    bash -c '"$0" --verify-composition' "${contents}/MacOS/EasyChat" > "${composition_log}" 2>&1 \
        || composition_ok=1
} 2>> "${composition_log}"

if [[ "${composition_ok}" -eq 0 ]]; then
    echo "    the dependency graph is complete."
else
    echo "    the bundle starts, but the dependency graph is still incomplete:"
    sed -n 's/.*Unable to resolve service for type .\([A-Za-z0-9._]*\).*/      missing: \1/p' \
        "${composition_log}" | sort -u
fi
rm -f "${composition_log}"

echo "==> Done: ${bundle}"

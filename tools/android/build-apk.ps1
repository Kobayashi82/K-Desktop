<#
  Builds the Android app (android\) into artifacts\android\KDesktop.apk without Gradle: aapt2 → javac → d8 →
  zipalign → apksigner. Needs JDK 17 and the Android SDK (platform 34, build-tools 34.0.0):
    %LOCALAPPDATA%\Programs\jdk-17   and   %LOCALAPPDATA%\Android\Sdk   (or JAVA_HOME / ANDROID_HOME).
  The version is K-Desktop's (Directory.Build.props); KDesktop.apk.json beside the app says it. A Release build of
  K-Desktop runs this script by itself (when the JDK and the SDK are there) and carries the app inside.
  The signing key (kdesktop-release.jks, its password in kdesktop-release.jks.password.txt) is taken from keys\ in the
  project (git ignores it) or else from %USERPROFILE%\.android; the first time it is created in keys\. Keep both:
  updates of the app must be signed with the same key. See docs\COMPILAR.md.
  Run: powershell -ExecutionPolicy Bypass -File tools\android\build-apk.ps1
#>
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$src = Join-Path $root "android"
$out = Join-Path $root "artifacts\android"
$build = Join-Path $out "build"

$jdk = if ($env:JAVA_HOME) { $env:JAVA_HOME } else { Join-Path $env:LOCALAPPDATA "Programs\jdk-17" }
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA "Android\Sdk" }
$bt = Join-Path $sdk "build-tools\34.0.0"
$platform = Join-Path $sdk "platforms\android-34\android.jar"
foreach ($need in @("$jdk\bin\javac.exe", "$bt\aapt2.exe", $platform)) { if (-not (Test-Path $need)) { throw "Falta $need" } }
$env:JAVA_HOME = $jdk
$env:PATH = "$jdk\bin;$env:PATH"

function Run([string]$exe, [string[]]$arguments) {
  & $exe @arguments
  if ($LASTEXITCODE -ne 0) { throw "$exe terminó con código $LASTEXITCODE" }
}

# Version from Directory.Build.props: 5.0.0 → name 5.0.0, code 50000.
[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$parts = $version.Split('.') + @('0', '0')
$code = [int]$parts[0] * 10000 + [int]$parts[1] * 100 + [int]$parts[2]

if (Test-Path $build) { Remove-Item -Recurse -Force $build }
New-Item -ItemType Directory -Force "$build\gen", "$build\classes", "$build\dex" | Out-Null

"Recursos…"
Run "$bt\aapt2.exe" @("compile", "--dir", "$src\res", "-o", "$build\res.zip")
Run "$bt\aapt2.exe" @("link", "-o", "$build\base.apk", "-I", $platform, "--manifest", "$src\AndroidManifest.xml",
  "-A", "$src\assets", "--java", "$build\gen", "--min-sdk-version", "21", "--target-sdk-version", "34",
  "--version-code", "$code", "--version-name", $version, "$build\res.zip")

"Java…"
$sources = @(Get-ChildItem -Recurse "$src\src", "$build\gen" -Filter *.java | ForEach-Object { $_.FullName })
Run "javac" (@("-encoding", "UTF-8", "-source", "11", "-target", "11", "-nowarn", "-Xlint:-options",
  "-classpath", $platform, "-d", "$build\classes") + $sources)

"Dex…"
$classes = @(Get-ChildItem -Recurse "$build\classes" -Filter *.class | ForEach-Object { $_.FullName })
Run "$bt\d8.bat" (@("--release", "--min-api", "21", "--lib", $platform, "--output", "$build\dex") + $classes)

"Empaquetado…"
Copy-Item "$build\base.apk" "$build\unsigned.apk"
Push-Location "$build\dex"
try { Run "jar" @("uf", "$build\unsigned.apk", "classes.dex") } finally { Pop-Location }
Run "$bt\zipalign.exe" @("-f", "-p", "4", "$build\unsigned.apk", "$build\aligned.apk")

"Firma…"
$keyDir = Join-Path $root "keys"
if (-not (Test-Path (Join-Path $keyDir "kdesktop-release.jks")) -and (Test-Path "$env:USERPROFILE\.android\kdesktop-release.jks")) {
  $keyDir = Join-Path $env:USERPROFILE ".android"
}
$keystore = Join-Path $keyDir "kdesktop-release.jks"
$passFile = "$keystore.password.txt"
if (-not (Test-Path $keystore)) {
  New-Item -ItemType Directory -Force $keyDir | Out-Null
  $password = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
  Set-Content -Path $passFile -Value $password -NoNewline -Encoding ASCII
  Run "keytool" @("-genkeypair", "-keystore", $keystore, "-alias", "kdesktop", "-keyalg", "RSA", "-keysize", "2048",
    "-validity", "10000", "-storepass", $password, "-keypass", $password, "-dname", "CN=Kobayashi82, O=K-Desktop")
  "  Clave nueva: $keystore (guárdala junto con $passFile)"
}
$password = (Get-Content $passFile -Raw).Trim()
$apk = Join-Path $out "KDesktop.apk"
Run "$bt\apksigner.bat" @("sign", "--ks", $keystore, "--ks-key-alias", "kdesktop", "--ks-pass", "pass:$password",
  "--key-pass", "pass:$password", "--out", $apk, "$build\aligned.apk")
Run "$bt\apksigner.bat" @("verify", $apk)
Remove-Item "$apk.idsig" -ErrorAction SilentlyContinue
# Its version, for K-Desktop: it carries the app and offers it to older ones (api/android).
$info = "{`"version`": `"$version`", `"code`": $code}"
[System.IO.File]::WriteAllText("$apk.json", $info, (New-Object System.Text.UTF8Encoding($false)))
"K-Desktop $version (código $code) -> $apk ($([Math]::Round((Get-Item $apk).Length / 1KB)) KB)"

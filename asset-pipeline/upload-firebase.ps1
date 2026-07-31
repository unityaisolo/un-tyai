# =============================================================================
#  Nova asset kutuphanesini Firebase Storage'a yukler.
#
#  Kullanim:
#      cd E:\nominal-agent\asset-pipeline
#      .\upload-firebase.ps1
#
#  Yarida kesilirse ayni komutu tekrar calistir - rsync kaldigi yerden devam eder.
#  Gerekli: Google Cloud CLI (https://cloud.google.com/sdk/docs/install)
#
#  NOT: Bu dosya bilerek SADECE ASCII karakter icerir. Windows PowerShell 5.1
#  .ps1 dosyalarini BOM yoksa cp1254 olarak okur; Turkce karakterler ve uzun
#  tire bozulup ayristirma hatasina yol acar. Buraya Turkce karakter EKLEME.
# =============================================================================

# ONEMLI: 'Stop' KULLANMA. gcloud'un kendi PowerShell sarmalayicisi (gcloud.ps1) bu
# ayari miras alir ve gcloud'un stderr'e yazdigi zararsiz bilgi mesajlarini ("(unset)",
# "Updates are available...") olumcul hataya cevirip scripti oldurur.
# Hata kontrolunu $LASTEXITCODE ile yapiyoruz.
$ErrorActionPreference = 'Continue'
$Bucket  = 'gs://unityai-dd9c1.firebasestorage.app'
$Project = 'unityai-dd9c1'
$Root    = $PSScriptRoot

function Step($n, $msg) { Write-Host "`n[$n] $msg" -ForegroundColor Cyan }
function Ok($msg)       { Write-Host "    [OK] $msg" -ForegroundColor Green }
function Warn($msg)     { Write-Host "    [!]  $msg" -ForegroundColor Yellow }

# gcloud ciktisini guvenle yakalar: stderr satirlarini ayiklar, sadece gercek cikti doner.
function GcOut {
    param([Parameter(ValueFromRemainingArguments = $true)] $GcArgs)
    & gcloud @GcArgs 2>&1 |
        Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } |
        ForEach-Object { "$_".Trim() } |
        Where-Object { $_ -ne '' }
}

# ---------------------------------------------------------------- on kontroller
Step 1 'Ortam kontrolu'

if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    Write-Host "`nHATA: gcloud bulunamadi." -ForegroundColor Red
    Write-Host "  Kur: https://cloud.google.com/sdk/docs/install"
    Write-Host "  Kurduktan sonra YENI bir PowerShell ac ve bu scripti tekrar calistir."
    exit 1
}
Ok 'gcloud kurulu'

$account = GcOut auth list --filter=status:ACTIVE --format="value(account)" | Select-Object -First 1
if (-not $account -or $account -like '*unset*') {
    Warn 'Giris yapilmamis - tarayici aciliyor, Google hesabinla onayla...'
    gcloud auth login
    if ($LASTEXITCODE -ne 0) { Write-Host 'HATA: giris yapilamadi' -ForegroundColor Red; exit 1 }
    $account = GcOut auth list --filter=status:ACTIVE --format="value(account)" | Select-Object -First 1
}
if (-not $account) { Write-Host 'HATA: aktif hesap yok' -ForegroundColor Red; exit 1 }
Ok "hesap: $account"

GcOut config set project $Project | Out-Null
Ok "proje: $Project"

foreach ($p in @('assets-raw', 'textures-raw', 'catalog.json')) {
    if (-not (Test-Path (Join-Path $Root $p))) {
        Write-Host "`nHATA: '$p' bulunamadi ($Root)" -ForegroundColor Red
        exit 1
    }
}
Ok 'kaynak dosyalar yerinde'

# ---------------------------------------------------------------- doku paketi
Step 2 'Doku paketi (textures-raw.zip)'

$zip = Join-Path $Root 'textures-raw.zip'
if (Test-Path $zip) {
    $mb = [math]::Round((Get-Item $zip).Length / 1MB)
    Ok "zaten var ($mb MB) - yeniden olusturulmuyor"
} else {
    # ONEMLI: zip'in KOKUNDE dogrudan doku klasorleri olmali (Grass001, Asphalt031 ...).
    # Fazladan bir 'textures-raw' katmani olursa plugin dokulari bulamaz.
    # Bu yuzden klasorun ICINDEN paketliyoruz.
    # PowerShell native komutlara * genisletmez; dosya adlarini kendimiz veriyoruz.
    Push-Location (Join-Path $Root 'textures-raw')
    try {
        $names = Get-ChildItem -Name
        Write-Host "    $($names.Count) klasor paketleniyor (birkac dakika surebilir)..."
        tar -a -c -f $zip $names
        if ($LASTEXITCODE -ne 0) { throw "tar basarisiz (kod $LASTEXITCODE)" }
    } finally { Pop-Location }
    $mb = [math]::Round((Get-Item $zip).Length / 1MB)
    Ok "olusturuldu: $mb MB"
}

# ---------------------------------------------------------------- yukleme
Step 3 'Modeller yukleniyor (assets-raw ~1.9 GB)'
Write-Host '    Baglantina gore 20-60 dk surebilir. Kesilirse scripti tekrar calistir.'
gcloud storage rsync -r (Join-Path $Root 'assets-raw') "$Bucket/assets-raw"
if ($LASTEXITCODE -ne 0) {
    Write-Host 'HATA: assets-raw yuklenemedi' -ForegroundColor Red
    exit 1
}
Ok 'assets-raw yuklendi'

Step 4 'catalog.json + textures-raw.zip'
gcloud storage cp (Join-Path $Root 'catalog.json') "$Bucket/catalog.json"
if ($LASTEXITCODE -ne 0) { Write-Host 'HATA: catalog.json yuklenemedi' -ForegroundColor Red; exit 1 }
gcloud storage cp $zip "$Bucket/textures-raw.zip"
if ($LASTEXITCODE -ne 0) { Write-Host 'HATA: textures-raw.zip yuklenemedi' -ForegroundColor Red; exit 1 }
Ok 'yuklendi'

# ---------------------------------------------------------------- onbellek
Step 5 'Onbellek basliklari (modeller degismez icerik)'
GcOut storage objects update --recursive --cache-control="public,max-age=31536000" "$Bucket/assets-raw" | Out-Null
if ($LASTEXITCODE -ne 0) { Warn 'atlandi (kritik degil)' } else { Ok 'ayarlandi' }

# ---------------------------------------------------------------- dogrulama
Step 6 'Dogrulama'
$count = (GcOut storage ls "$Bucket/assets-raw/**" | Measure-Object).Count
Write-Host "    bucket'taki dosya sayisi: $count"
if ($count -lt 700) { Warn 'beklenen ~735 - eksik olabilir, scripti tekrar calistir' }
else { Ok 'sayi beklenen aralikta' }

# ---------------------------------------------------------------- sonuc
Write-Host "`n=======================================================" -ForegroundColor Green
Write-Host " YUKLEME TAMAM" -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Green
Write-Host @"

Simdi backend/.env dosyasina sunlari ekle:

NOVA_ASSET_CATALOG_URL=https://firebasestorage.googleapis.com/v0/b/unityai-dd9c1.firebasestorage.app/o/catalog.json?alt=media
NOVA_ASSET_BASE_URL=https://firebasestorage.googleapis.com/v0/b/unityai-dd9c1.firebasestorage.app/o/assets-raw%2F{file}?alt=media
NOVA_TEXTURES_ZIP_URL=https://firebasestorage.googleapis.com/v0/b/unityai-dd9c1.firebasestorage.app/o/textures-raw.zip?alt=media
NOVA_ASSET_VERSION=1

Sonra backend'i yeniden baslat ve Unity'de:  UnityAI > Kutuphaneyi Buluttan Indir

"@

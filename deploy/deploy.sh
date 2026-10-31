#!/usr/bin/env bash
# Saytning yangi versiyasini serverga yuklaydi (kompyuterdan, Git Bash'da):
#   SERVER=root@1.2.3.4 bash deploy/deploy.sh
# Har bir versiya alohida papkada saqlanadi; ishga tushmasa oldingisiga qaytariladi.
set -euo pipefail

SERVER="${SERVER:?SERVER kerak, masalan root@1.2.3.4}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RELEASE="$(date +%Y%m%d-%H%M%S)"
OUT="$ROOT/bin/deploy/$RELEASE"

echo "==> Linux uchun yig'ish"
dotnet publish "$ROOT/EduHisob.csproj" -c Release -r linux-x64 --self-contained true -p:PublishReadyToRun=false -o "$OUT"
rm -f "$OUT"/appsettings.Development.json "$OUT"/*.db
tar -czf "$OUT.tar.gz" -C "$OUT" .

echo "==> Serverga yuborish"
scp -q "$OUT.tar.gz" "$SERVER:/tmp/eduhisob-$RELEASE.tar.gz"

echo "==> O'rnatish va qayta ishga tushirish"
ssh "$SERVER" RELEASE="$RELEASE" 'bash -s' <<'REMOTE'
set -euo pipefail
APP=/opt/eduhisob
mkdir -p "$APP/releases/$RELEASE"
tar -xzf "/tmp/eduhisob-$RELEASE.tar.gz" -C "$APP/releases/$RELEASE"
rm -f "/tmp/eduhisob-$RELEASE.tar.gz"
chmod +x "$APP/releases/$RELEASE/EduHisob"
chown -R eduhisob:eduhisob "$APP/releases/$RELEASE"
PREVIOUS="$(readlink -f "$APP/current" || true)"

# Yangilashdan oldin bazaning zaxira nusxasi.
sudo -u postgres pg_dump -Fc eduhisob > "/var/backups/eduhisob/before-$RELEASE.dump" || true

ln -sfn "$APP/releases/$RELEASE" "$APP/current"
systemctl restart eduhisob
for i in $(seq 1 60); do
    if curl -fs http://127.0.0.1:5000/healthz >/dev/null; then
        echo "    Sayt ishlayapti: $RELEASE"
        ls -1dt "$APP"/releases/* | tail -n +6 | xargs -r rm -rf
        exit 0
    fi
    sleep 2
done

echo "    Yangi versiya ishga tushmadi — oldingisiga qaytarilmoqda."
journalctl -u eduhisob -n 40 --no-pager || true
if [ -n "$PREVIOUS" ]; then
    ln -sfn "$PREVIOUS" "$APP/current"
    systemctl restart eduhisob
fi
exit 1
REMOTE

rm -rf "$OUT" "$OUT.tar.gz"
echo "==> Tayyor."

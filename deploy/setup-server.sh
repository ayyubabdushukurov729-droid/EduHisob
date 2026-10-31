#!/usr/bin/env bash
# EduHisob serverini bir marta tayyorlaydi (Ubuntu 22.04 / 24.04, root sifatida).
# Qayta ishga tushirish xavfsiz: bor narsalar qayta yaratilmaydi, baza paroli o'zgarmaydi.
#
#   DOMAIN=eduhisob.uz EMAIL=siz@pochta.uz PLATFORM_PHONE=+998904043334 bash setup-server.sh
set -euo pipefail

DOMAIN="${DOMAIN:-eduhisob.uz}"
EMAIL="${EMAIL:?EMAIL kerak (HTTPS sertifikati xabarlari uchun)}"
PLATFORM_PHONE="${PLATFORM_PHONE:?PLATFORM_PHONE kerak}"
APP_USER=eduhisob
APP_DIR=/opt/eduhisob
ENV_FILE=/etc/eduhisob/eduhisob.env

echo "==> Paketlar"
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y nginx postgresql certbot python3-certbot-nginx ufw curl openssl libicu-dev

echo "==> Xavfsizlik devori: faqat SSH, HTTP, HTTPS"
ufw allow OpenSSH
ufw allow 'Nginx Full'
ufw --force enable

echo "==> Foydalanuvchi va papkalar"
id -u "$APP_USER" >/dev/null 2>&1 || useradd --system --home "$APP_DIR" --shell /usr/sbin/nologin "$APP_USER"
mkdir -p "$APP_DIR/releases" /var/lib/eduhisob/keys /var/backups/eduhisob /etc/eduhisob
chown -R "$APP_USER:$APP_USER" "$APP_DIR" /var/lib/eduhisob
chmod 700 /var/lib/eduhisob/keys

echo "==> PostgreSQL bazasi"
if [ ! -f "$ENV_FILE" ]; then
    DB_PASSWORD="$(openssl rand -hex 24)"
    PLATFORM_PASSWORD="$(openssl rand -base64 18 | tr -d '/+=' | cut -c1-16)"
    sudo -u postgres psql -v ON_ERROR_STOP=1 -c "CREATE ROLE eduhisob LOGIN PASSWORD '$DB_PASSWORD';"
    sudo -u postgres psql -v ON_ERROR_STOP=1 -c "CREATE DATABASE eduhisob OWNER eduhisob ENCODING 'UTF8' TEMPLATE template0;"
    cat > "$ENV_FILE" <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5000
DatabaseProvider=PostgreSQL
ConnectionStrings__PostgreSQL=Host=localhost;Database=eduhisob;Username=eduhisob;Password=$DB_PASSWORD
DataProtection__KeysPath=/var/lib/eduhisob/keys
Registration__Enabled=false
PlatformAdmin__Phone=$PLATFORM_PHONE
PlatformAdmin__Password=$PLATFORM_PASSWORD
PlatformAdmin__Name=Platforma egasi
EOF
    chmod 600 "$ENV_FILE"
    echo
    echo "*************************************************************"
    echo "  Platforma kabineti: https://$DOMAIN/platforma/kirish"
    echo "  Login:  $PLATFORM_PHONE"
    echo "  Parol:  $PLATFORM_PASSWORD"
    echo "  (parol $ENV_FILE faylida ham saqlangan)"
    echo "*************************************************************"
    echo
else
    echo "    $ENV_FILE bor — baza va parollar o'zgartirilmadi."
fi

echo "==> Avtomatik ishga tushish (systemd)"
cat > /etc/systemd/system/eduhisob.service <<EOF
[Unit]
Description=EduHisob
After=network.target postgresql.service
Requires=postgresql.service

[Service]
User=$APP_USER
WorkingDirectory=$APP_DIR/current
ExecStart=$APP_DIR/current/EduHisob
EnvironmentFile=$ENV_FILE
Restart=always
RestartSec=5
KillSignal=SIGINT
TimeoutStopSec=20
Environment=DOTNET_NOLOGO=1

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable eduhisob >/dev/null

echo "==> nginx"
cat > /etc/nginx/sites-available/eduhisob <<EOF
map \$http_upgrade \$connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN www.$DOMAIN;
    client_max_body_size 20m;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        # Blazor Server WebSocket orqali ishlaydi.
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection \$connection_upgrade;
        proxy_set_header Host \$host;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_read_timeout 300s;
        proxy_buffering off;
    }
}
EOF
ln -sf /etc/nginx/sites-available/eduhisob /etc/nginx/sites-enabled/eduhisob
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl reload nginx

echo "==> Kunlik zaxira nusxa (har kuni 03:30, 14 kun saqlanadi)"
cat > /etc/cron.d/eduhisob-backup <<'EOF'
30 3 * * * postgres pg_dump -Fc eduhisob > /var/backups/eduhisob/eduhisob-$(date +\%F).dump && find /var/backups/eduhisob -name '*.dump' -mtime +14 -delete
EOF
chown postgres:postgres /var/backups/eduhisob

echo "==> HTTPS sertifikati"
SERVER_IP="$(curl -4 -s https://ifconfig.me || true)"
DOMAIN_IP="$(getent ahostsv4 "$DOMAIN" | awk 'NR==1{print $1}' || true)"
if [ -n "$SERVER_IP" ] && [ "$SERVER_IP" = "$DOMAIN_IP" ]; then
    DOMAINS=(-d "$DOMAIN")
    [ "$(getent ahostsv4 "www.$DOMAIN" | awk 'NR==1{print $1}')" = "$SERVER_IP" ] && DOMAINS+=(-d "www.$DOMAIN")
    certbot --nginx "${DOMAINS[@]}" --redirect --agree-tos -m "$EMAIL" -n
else
    echo "    $DOMAIN hali bu serverga ($SERVER_IP) qaramayapti (hozir: ${DOMAIN_IP:-yo‘q})."
    echo "    DNS yangilangach shuni qayta ishga tushiring: certbot --nginx -d $DOMAIN -d www.$DOMAIN --redirect --agree-tos -m $EMAIL -n"
fi

echo "==> Tayyor. Endi saytni deploy.sh bilan yuklang."

#!/usr/bin/env bash
set -euo pipefail

# ────────── CONFIG ──────────
DOTNET_CHANNEL="9.0"
DOTNET_INSTALL_DIR="/opt/dotnet"
SYSTEMD_DIR="/etc/systemd/system"

SERVICE_NAME="serialrelaycontroller.service"
SERVICE_FILE="$SYSTEMD_DIR/$SERVICE_NAME"

TARGET_USER="user"
TARGET_HOME="/home/$TARGET_USER"
PROJECT_DIR="$TARGET_HOME/ControlCenter/SerialRelayController"
PUBLISH_DIR="$PROJECT_DIR/publish"

ARCH=$(uname -m)

# ────────── DETECT RUNTIME ──────────
case "$ARCH" in
    x86_64)   RUNTIME="linux-x64" ;;
    aarch64)  RUNTIME="linux-arm64" ;;
    armv7l)   RUNTIME="linux-arm" ;;
    *) echo "❌ Unsupported architecture: $ARCH"; exit 1 ;;
esac

echo "Detected architecture: $ARCH → runtime: $RUNTIME"

# ────────── USER SETUP ──────────
if ! id "$TARGET_USER" &>/dev/null; then
    echo "👤 Creating system user '$TARGET_USER'..."
    sudo adduser --system --group --home "$TARGET_HOME" "$TARGET_USER"
fi

echo "👤 Ensuring $TARGET_USER is in dialout and plugdev..."
sudo usermod -a -G dialout "$TARGET_USER"
sudo usermod -a -G plugdev "$TARGET_USER"

# ────────── .NET RUNTIME ──────────
LATEST_DOTNET_RUNTIME=$(curl -sSL "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/${DOTNET_CHANNEL}/releases.json" \
    | grep -Po '"latest-runtime":\s*"\K[^"]+' | head -n1)

if ! command -v dotnet &>/dev/null || [[ "$(dotnet --list-runtimes | grep -c $LATEST_DOTNET_RUNTIME)" -eq 0 ]]; then
    echo "⬆️ Installing ASP.NET Core Runtime $LATEST_DOTNET_RUNTIME"
    TMP_SCRIPT="$(mktemp)"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$TMP_SCRIPT"
    sudo bash "$TMP_SCRIPT" --channel "$DOTNET_CHANNEL" --runtime aspnetcore --install-dir "$DOTNET_INSTALL_DIR" --quality ga
    rm -f "$TMP_SCRIPT"
    sudo ln -sf "$DOTNET_INSTALL_DIR/dotnet" /usr/local/bin/dotnet
fi

echo "✅ dotnet runtimes installed:"
dotnet --list-runtimes

# ────────── DEPLOY PUBLISH DIR ──────────
# (Assumes you already copied publish/ folder from your dev box)
if [[ ! -d "$PUBLISH_DIR" ]]; then
    echo "❌ Publish directory missing at $PUBLISH_DIR"
    exit 1
fi
sudo chown -R "$TARGET_USER:$TARGET_USER" "$TARGET_HOME"

# ────────── CONFIG CHECK ──────────
echo "🔍 Checking configs..."
for f in appsettings.json commands.json; do
    if [[ ! -f "$PUBLISH_DIR/$f" ]]; then
        echo "❌ Missing $f in publish dir!"
        exit 1
    fi
done
echo "✅ Found configs: appsettings.json, commands.json"

grep -A5 SerialPortOptions "$PUBLISH_DIR/appsettings.json" || echo "⚠️ No SerialPortOptions section found!"

# ────────── SYSTEMD SERVICE ──────────
echo "⚙️ Writing service file to $SERVICE_FILE"

sudo tee "$SERVICE_FILE" >/dev/null <<EOF
[Unit]
Description=SerialRelayController .NET Service
After=network.target

[Service]
WorkingDirectory=$PUBLISH_DIR
ExecStart=/usr/local/bin/dotnet $PUBLISH_DIR/SerialRelayController.dll

Restart=always
RestartSec=5
User=$TARGET_USER

Environment=SERIAL_RELAY_CONTROLLER_PORT=80
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
Environment=ASPNETCORE_URLS=http://0.0.0.0:80

SyslogIdentifier=serialrelaycontroller

[Install]
WantedBy=multi-user.target
EOF

# ────────── ENABLE SERVICE ──────────
sudo systemctl daemon-reload
sudo systemctl enable "$SERVICE_NAME"
sudo systemctl restart "$SERVICE_NAME"

echo "✅ Service deployed. Status:"
sudo systemctl status "$SERVICE_NAME" --no-pager
echo ""
echo "📜 Logs (last 50 lines):"
sudo journalctl -u "$SERVICE_NAME" -n 50 --no-pager

#!/usr/bin/env bash
set -euo pipefail

# ────────── CONFIG ──────────
DOTNET_CHANNEL="9.0"
DOTNET_INSTALL_DIR="/opt/dotnet"
SYSTEMD_DIR="/etc/systemd/system"

SERVICE_NAME="serialrelaycontroller.service"
SERVICE_FILE="$SYSTEMD_DIR/$SERVICE_NAME"

TARGET_USER="relayuser"
TARGET_HOME="/home/$TARGET_USER"

# Path to your project file (adjust if needed)
PROJECT_FILE="/home/admin/ControlCenter/SerialRelayController/SerialRelayController.csproj"

# Final deployment dir (runtime-only, no git repo)
DEPLOY_DIR="/opt/serialrelaycontroller"
PUBLISH_DIR="$DEPLOY_DIR/publish"

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

# ────────── .NET SDK (for build on Pi) ──────────
LATEST_DOTNET_SDK=$(curl -sSL "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/${DOTNET_CHANNEL}/releases.json" \
    | grep -Po '"latest-sdk":\s*"\K[^"]+' | head -n1)

if ! command -v dotnet &>/dev/null || [[ "$(dotnet --version)" != "$LATEST_DOTNET_SDK" ]]; then
    echo "⬆️ Installing .NET SDK $LATEST_DOTNET_SDK"
    TMP_SCRIPT="$(mktemp)"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$TMP_SCRIPT"
    sudo bash "$TMP_SCRIPT" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_INSTALL_DIR" --quality ga
    rm -f "$TMP_SCRIPT"
    sudo ln -sf "$DOTNET_INSTALL_DIR/dotnet" /usr/local/bin/dotnet
fi

echo "✅ dotnet SDK installed: $(dotnet --version)"

# ────────── BUILD APP LOCALLY ──────────
echo "🚀 Publishing SerialRelayController..."
sudo mkdir -p "$PUBLISH_DIR"
sudo chown -R "$USER:$USER" "$DEPLOY_DIR"

dotnet publish "$PROJECT_FILE" -c Release -r "$RUNTIME" --self-contained false -o "$PUBLISH_DIR"

# Make relayuser own runtime files
sudo chown -R "$TARGET_USER:$TARGET_USER" "$DEPLOY_DIR"

# ────────── CONFIG CHECK ──────────
if [[ -f "$PUBLISH_DIR/appsettings.json" && -f "$PUBLISH_DIR/commands.json" ]]; then
    echo "✅ Found configs: appsettings.json, commands.json"
else
    echo "⚠️ WARNING: Configs missing — service may fail if configs are required."
fi

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

# Allow non-root user to bind to port 80 safely
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE

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
sudo systemctl enable "$SERVICE_NAME" || true
sudo systemctl restart "$SERVICE_NAME"

echo "✅ Service deployed. Status:"
sudo systemctl status "$SERVICE_NAME" --no-pager
echo ""
echo "📜 Logs (last 50 lines):"
sudo journalctl -u "$SERVICE_NAME" -n 50 --no-pager

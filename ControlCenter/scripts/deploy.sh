#!/usr/bin/env bash
set -euo pipefail

# ────────── CONFIG ──────────
DOTNET_CHANNEL="LTS"
DOTNET_INSTALL_DIR="/usr/share/dotnet"

PROJECT_DIR="$(pwd)"
PUBLISH_DIR="$PROJECT_DIR/publish"
SERVICE_NAME="controlcenter.service"
SYSTEMD_DIR="/etc/systemd/system"
SERVICE_FILE="$SYSTEMD_DIR/$SERVICE_NAME"

install_dotnet_for_ubuntu_24_04() {
    echo "⬇️ Adding dotnet/backports PPA and installing dotnet-sdk-9.0…"
    sudo add-apt-repository -y ppa:dotnet/backports
    sudo apt-get update -y
    sudo apt-get install -y dotnet-sdk-9.0
}

install_dotnet_portable() {
    echo "⬇️ Installing .NET via dotnet-install.sh (channel=$DOTNET_CHANNEL)…"
    TMP_SCRIPT="$(mktemp)"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$TMP_SCRIPT"
    sudo bash "$TMP_SCRIPT" \
        --channel "$DOTNET_CHANNEL" \
        --install-dir "$DOTNET_INSTALL_DIR" \
        --quality ga
    rm -f "$TMP_SCRIPT"
    export PATH="$PATH:$DOTNET_INSTALL_DIR"
    if ! grep -q "$DOTNET_INSTALL_DIR" /etc/profile &>/dev/null; then
        echo "export PATH=\"\$PATH:$DOTNET_INSTALL_DIR\"" \
            | sudo tee /etc/profile.d/dotnet.sh >/dev/null
    fi
}

# ────────── ENSURE SDK ──────────
echo ""
echo "====================================="
echo "Checking .NET SDK"
echo "====================================="

if ! command -v dotnet >/dev/null 2>&1; then
    echo "⚠️ dotnet SDK not found – determining best install method…"
    if [[ -f /etc/os-release ]]; then
        . /etc/os-release
        if [[ "$ID" == "ubuntu" && "$VERSION_ID" == "24.04" ]]; then
            install_dotnet_for_ubuntu_24_04
        else
            install_dotnet_portable
        fi
    else
        install_dotnet_portable
    fi
    echo "✅ dotnet SDK installed: $(dotnet --version)"
else
    echo "✅ dotnet SDK detected: $(dotnet --version)"
fi

# ────────── BUILD & PUBLISH ──────────
echo ""
echo "====================================="
echo "Publishing ControlCenter to $PUBLISH_DIR"
echo "====================================="

dotnet publish -c Release -r linux-x64 --self-contained false -o "$PUBLISH_DIR"

# ────────── WRITE SYSTEMD SERVICE FILE ──────────
echo ""
echo "====================================="
echo "Deploying service file to $SERVICE_FILE"
echo "====================================="

sudo tee "$SERVICE_FILE" >/dev/null <<EOF
[Unit]
Description=ControlCenter .NET Service
After=network.target

[Service]
WorkingDirectory=$PUBLISH_DIR
ExecStart=/usr/bin/dotnet $PUBLISH_DIR/ControlCenter.dll

# Always restart on crash
Restart=always
RestartSec=5

# Run as 'user' (change this if needed)
User=user

# Capabilities: allow non-root to bind port 80
AmbientCapabilities=CAP_NET_BIND_SERVICE

# Environment variables for runtime config
Environment=SERIAL_RELAY_CONTROLLER_HOST=10.1.10.150
Environment=SERIAL_RELAY_CONTROLLER_PORT=5000
Environment=USE_LEGACY_LOCKER_API=true
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
Environment=ASPNETCORE_URLS=http://0.0.0.0:80

SyslogIdentifier=controlcenter

[Install]
WantedBy=multi-user.target
EOF

# ────────── RELOAD SYSTEMD & START SERVICE ──────────
echo ""
echo "====================================="
echo "Reloading systemd, enabling & restarting $SERVICE_NAME"
echo "====================================="

sudo systemctl daemon-reload
sudo systemctl enable "$SERVICE_NAME"
sudo systemctl restart "$SERVICE_NAME"

# ────────── STATUS & LOGS ──────────
echo ""
echo "====================================="
echo "Status of $SERVICE_NAME"
echo "====================================="

sudo systemctl status "$SERVICE_NAME" --no-pager

echo ""
read -rp "Do you want to tail live logs? (y/n): " tail_logs
if [[ "${tail_logs,,}" =~ ^y(es)?$ ]]; then
    echo ""
    echo "====================================="
    echo "Tailing logs for $SERVICE_NAME (Ctrl+C to stop)"
    echo "====================================="
    sudo journalctl -fu "$SERVICE_NAME"
fi

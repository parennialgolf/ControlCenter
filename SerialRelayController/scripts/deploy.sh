#!/usr/bin/env bash
set -euo pipefail

# ────────── CONFIG ──────────
DOTNET_CHANNEL="9.0"
DOTNET_INSTALL_DIR="/opt/dotnet"
SYSTEMD_DIR="/etc/systemd/system"

PROJECT_DIR="$(pwd)"
PUBLISH_DIR="$PROJECT_DIR/publish"
SERVICE_TEMPLATE="$PROJECT_DIR/scripts/serialrelaycontroller.service"
SERVICE_FILE="$SYSTEMD_DIR/serialrelaycontroller.service"
SERVICE_NAME="serialrelaycontroller.service"

CURRENT_USER=$(whoami)
ARCH=$(uname -m)

# ────────── DETECT RUNTIME ──────────
if [[ "$ARCH" == "x86_64" ]]; then
    RUNTIME="linux-x64"
elif [[ "$ARCH" == "aarch64" ]]; then
    RUNTIME="linux-arm64"
elif [[ "$ARCH" == "armv7l" ]]; then
    RUNTIME="linux-arm"
else
    echo "Unsupported architecture: $ARCH"
    exit 1
fi

echo "Detected architecture: $ARCH → runtime: $RUNTIME"

# ────────── INSTALL DOTNET IF MISSING ──────────
if ! command -v dotnet &>/dev/null; then
    echo "dotnet not found — installing to $DOTNET_INSTALL_DIR"

    TMP_SCRIPT="$(mktemp)"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$TMP_SCRIPT"

    sudo bash "$TMP_SCRIPT" \
        --channel "$DOTNET_CHANNEL" \
        --install-dir "$DOTNET_INSTALL_DIR" \
        --quality ga

    rm -f "$TMP_SCRIPT"

    # Symlink for convenience
    if [[ ! -L /usr/bin/dotnet ]]; then
        sudo ln -s "$DOTNET_INSTALL_DIR/dotnet" /usr/bin/dotnet
    fi

    echo "dotnet installed: $(dotnet --version)"
else
    echo "dotnet SDK detected: $(dotnet --version)"
fi

# ────────── BUILD & PUBLISH ──────────
echo ""
echo "====================================="
echo "Publishing SerialRelayController to $PUBLISH_DIR"
echo "====================================="

dotnet publish -c Release -r $RUNTIME --self-contained false -o "$PUBLISH_DIR"

# ────────── DEPLOY SYSTEMD SERVICE FILE ──────────
echo ""
echo "====================================="
echo "Deploying service file to $SERVICE_FILE"
echo "====================================="

# Replace {{USER}} and {{PROJECT_DIR}} in service template
sed "s|{{USER}}|$CURRENT_USER|g; s|{{PROJECT_DIR}}|$PROJECT_DIR|g" "$SERVICE_TEMPLATE" | sudo tee "$SERVICE_FILE" > /dev/null

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

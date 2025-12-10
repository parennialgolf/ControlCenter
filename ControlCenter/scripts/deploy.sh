#!/usr/bin/env bash
set -euo pipefail

# ────────── CONFIG ──────────
LOCKER_HOST="pgl-1-lockers"

DOTNET_CHANNEL="9.0"                 # or "LTS"
DOTNET_INSTALL_DIR="/opt/dotnet"
SYSTEMD_DIR="/etc/systemd/system"

SERVICE_NAME="controlcenter.service"
SERVICE_FILE="$SYSTEMD_DIR/$SERVICE_NAME"

TARGET_USER="user"
TARGET_HOME="/home/$TARGET_USER"
PROJECT_DIR="$TARGET_HOME/ControlCenter/ControlCenter"
PUBLISH_DIR="$PROJECT_DIR/publish"
ARTIFACTS_DIR="$PROJECT_DIR/artifacts"
CONFIG_FILE="$PUBLISH_DIR/config.json"

ARCH=$(uname -m)

# ────────── DETECT RUNTIME ──────────
if [[ "$ARCH" == "x86_64" ]]; then
    RUNTIME="linux-x64"
elif [[ "$ARCH" == "aarch64" ]]; then
    RUNTIME="linux-arm64"
elif [[ "$ARCH" == "armv7l" ]]; then
    RUNTIME="linux-arm"
else
    echo "❌ Unsupported architecture: $ARCH"
    exit 1
fi

echo "Detected architecture: $ARCH → runtime: $RUNTIME"

# ────────── ENSURE TARGET USER ──────────
if ! id "$TARGET_USER" &>/dev/null; then
    echo "👤 Creating system user '$TARGET_USER'..."
    sudo adduser --system --group --home "$TARGET_HOME" "$TARGET_USER"
fi

# ────────── INSTALL / UPDATE DOTNET ──────────
echo ""
echo "====================================="
echo "Checking .NET SDK"
echo "====================================="

INSTALLED_DOTNET_VER=""
if command -v dotnet &>/dev/null; then
    INSTALLED_DOTNET_VER=$(dotnet --version)
    echo "✅ Found dotnet SDK: $INSTALLED_DOTNET_VER"
else
    echo "⚠️ dotnet not found"
fi

LATEST_DOTNET_VER=$(curl -sSL "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/${DOTNET_CHANNEL}/releases.json" \
    | grep -Po '"latest-sdk":\s*"\K[^"]+' | head -n1)

if [[ -z "$LATEST_DOTNET_VER" ]]; then
    echo "❌ Could not determine latest .NET version for channel $DOTNET_CHANNEL"
    exit 1
fi

echo "ℹ️ Latest $DOTNET_CHANNEL SDK available: $LATEST_DOTNET_VER"

# Prompt for update if versions differ
if [[ "$INSTALLED_DOTNET_VER" != "$LATEST_DOTNET_VER" ]]; then
    echo ""
    echo "A newer .NET SDK version ($LATEST_DOTNET_VER) is available."
    read -rp "Do you want to download and install the latest version? (y/n): " update_dotnet
    if [[ "${update_dotnet,,}" =~ ^y(es)?$ ]]; then
        echo "⬆️ Installing/updating dotnet SDK to $LATEST_DOTNET_VER"
        TMP_SCRIPT="$(mktemp)"
        curl -sSL https://dot.net/v1/dotnet-install.sh -o "$TMP_SCRIPT"
        sudo bash "$TMP_SCRIPT" \
            --channel "$DOTNET_CHANNEL" \
            --install-dir "$DOTNET_INSTALL_DIR" \
            --quality ga
        rm -f "$TMP_SCRIPT"

        if [[ ! -L /usr/local/bin/dotnet ]]; then
            sudo ln -s "$DOTNET_INSTALL_DIR/dotnet" /usr/local/bin/dotnet
        fi

        echo "✅ dotnet installed/updated: $(dotnet --version)"
    else
        echo "⏭️ Skipping .NET SDK update, continuing with current version..."
    fi
else
    echo "✅ dotnet is already up to date ($INSTALLED_DOTNET_VER)"
fi

# ────────── BUILD & PUBLISH ──────────
echo ""
echo "====================================="
echo "Publishing ControlCenter"
echo "====================================="

rm -rf "$ARTIFACTS_DIR"
mkdir -p "$ARTIFACTS_DIR"

pushd "$PROJECT_DIR" >/dev/null
dotnet publish ControlCenter.csproj \
  -c Release \
  -r "$RUNTIME" \
  --self-contained false \
  -o "$ARTIFACTS_DIR"
popd >/dev/null

# ────────── COPY TO TARGET DIRECTORY ──────────
echo ""
echo "====================================="
echo "Copying files to $PUBLISH_DIR"
echo "====================================="

sudo mkdir -p "$PUBLISH_DIR"
# do NOT touch config.json on redeploy
sudo rsync -a --delete --exclude 'config.json' "$ARTIFACTS_DIR/" "$PUBLISH_DIR/"
sudo chown -R "$TARGET_USER:$TARGET_USER" "$TARGET_HOME"

# ────────── CONFIG FILE ──────────
echo ""
echo "====================================="
echo "Checking for existing config.json"
echo "====================================="

if [[ -f "$CONFIG_FILE" ]]; then
    echo "✅ config.json already exists at $CONFIG_FILE"
    echo "   Skipping creation to preserve existing configuration."
else
    echo "⚠️ No config.json found — creating default config…"
    sudo mkdir -p "$(dirname "$CONFIG_FILE")"

    cat <<'EOF' | sudo tee "$CONFIG_FILE" >/dev/null
{
  "Doors": {
    "Managed": true,
    "Max": 1,
    "IpAddress": "10.1.10.101"
  },
  "Projectors": {
    "Managed": true,
    "Max": 0,
    "Projectors": [
      { "Id": 1, "Name": "Bay 1", "IpAddress": "10.1.10.182", "Protocol": "PJLink" },
      { "Id": 2, "Name": "Bay 2", "IpAddress": "10.1.10.138", "Protocol": "PJLink" },
      { "Id": 3, "Name": "Bay 3", "IpAddress": "10.1.10.57", "Protocol": "PJLink" }
    ]
  },
  "Lockers": {
    "LegacyEnabled": true,
    "Host": "pgl-1-lockers",
    "Managed": true,
    "Max": 34,
    "SerialPorts": [
      "/dev/ttyUSB0",
      "/dev/ttyUSB1",
      "/dev/ttyUSB2"
    ]
  }
}
EOF

    sudo chown "$TARGET_USER:$TARGET_USER" "$CONFIG_FILE"
    echo "✅ Default config.json created at $CONFIG_FILE"
fi

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
ExecStart=/usr/local/bin/dotnet $PUBLISH_DIR/ControlCenter.dll

Restart=always
RestartSec=5

User=$TARGET_USER
AmbientCapabilities=CAP_NET_BIND_SERVICE

Environment=SERIAL_RELAY_CONTROLLER_HOST=$LOCKER_HOST
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

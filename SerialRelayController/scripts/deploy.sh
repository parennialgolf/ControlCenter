#!/usr/bin/env bash
set -euo pipefail

# ────────── CONFIG ──────────
DOTNET_CHANNEL="9.0"
DOTNET_INSTALL_DIR="/opt/dotnet"
SYSTEMD_DIR="/etc/systemd/system"

SERVICE_NAME="serialrelaycontroller.service"
SERVICE_FILE="$SYSTEMD_DIR/$SERVICE_NAME"

# Always use the dedicated 'user' account
TARGET_USER="user"
TARGET_HOME="/home/$TARGET_USER"
PROJECT_DIR="$TARGET_HOME/ControlCenter/SerialRelayController"
PUBLISH_DIR="$PROJECT_DIR/publish"

# Quartz persistence
QUARTZ_DB="$TARGET_HOME/ControlCenter/quartz.db"
QUARTZ_SCHEMA_URL="https://raw.githubusercontent.com/quartznet/quartznet/main/database/tables/tables_sqlite.sql"

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

# ────────── ENSURE TARGET USER EXISTS ──────────
if ! id "$TARGET_USER" &>/dev/null; then
    echo "👤 Creating system user '$TARGET_USER'..."
    sudo adduser --system --group --home "$TARGET_HOME" "$TARGET_USER"
fi

# Always ensure the user is in the right groups
echo "👤 Adding $TARGET_USER to dialout and plugdev groups..."
sudo usermod -a -G dialout "$TARGET_USER"
sudo usermod -a -G plugdev "$TARGET_USER"

# ────────── INSTALL / UPDATE DOTNET ──────────
echo ""
echo "====================================="
echo "Checking .NET SDK"
echo "====================================="

INSTALLED_DOTNET_VER=""
if command -v dotnet &>/dev/null; then
    INSTALLED_DOTNET_VER=$(dotnet --version || echo "")
    echo "✅ Found dotnet SDK: $INSTALLED_DOTNET_VER"
else
    echo "⚠️ dotnet not found"
fi

# Get latest available version for this channel from Microsoft
LATEST_DOTNET_VER=$(curl -sSL "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/${DOTNET_CHANNEL}/releases.json" \
    | grep -Po '"latest-sdk":\s*"\K[^"]+' | head -n1)

if [[ -z "$LATEST_DOTNET_VER" ]]; then
    echo "❌ Could not determine latest .NET version for channel $DOTNET_CHANNEL"
    exit 1
fi

echo "ℹ️ Latest $DOTNET_CHANNEL SDK available: $LATEST_DOTNET_VER"

# Compare versions
if [[ "$INSTALLED_DOTNET_VER" != "$LATEST_DOTNET_VER" ]]; then
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
    echo "✅ dotnet is already up to date ($INSTALLED_DOTNET_VER)"
fi

# ────────── INSTALL SQLITE IF MISSING ──────────
echo ""
echo "====================================="
echo "Checking sqlite3"
echo "====================================="

if ! command -v sqlite3 &>/dev/null; then
    echo "⬇️ Installing sqlite3..."
    sudo apt-get update -y
    sudo apt-get install -y sqlite3
    echo "✅ sqlite3 installed: $(sqlite3 --version)"
else
    echo "✅ sqlite3 detected: $(sqlite3 --version)"
fi

# ────────── BUILD & PUBLISH ──────────
echo ""
echo "====================================="
echo "Publishing SerialRelayController"
echo "====================================="

dotnet publish -c Release -r "$RUNTIME" --self-contained false -o "publish"

# ────────── COPY TO TARGET DIRECTORY ──────────
echo ""
echo "====================================="
echo "Copying files to $PUBLISH_DIR"
echo "====================================="

sudo mkdir -p "$PUBLISH_DIR"
sudo cp -r publish/* "$PUBLISH_DIR/"
sudo chown -R "$TARGET_USER:$TARGET_USER" "$TARGET_HOME"

# ────────── ENSURE QUARTZ DB & SCHEMA ──────────
echo ""
echo "====================================="
echo "Ensuring Quartz schema exists in $QUARTZ_DB"
echo "====================================="

if [[ ! -f "$QUARTZ_DB" ]]; then
    echo "📂 Creating Quartz DB at $QUARTZ_DB"
    sudo -u "$TARGET_USER" touch "$QUARTZ_DB"
fi

TMP_SCHEMA="$(mktemp)"
curl -sSL "$QUARTZ_SCHEMA_URL" -o "$TMP_SCHEMA"

# Apply schema (ignore errors if tables already exist)
echo "📜 Applying Quartz schema..."
sudo -u "$TARGET_USER" sqlite3 "$QUARTZ_DB" < "$TMP_SCHEMA" || true
rm -f "$TMP_SCHEMA"

# Fix ownership & perms
sudo chown "$TARGET_USER:$TARGET_USER" "$QUARTZ_DB"
sudo chmod 664 "$QUARTZ_DB"

# ────────── WRITE SYSTEMD SERVICE FILE ──────────
echo ""
echo "====================================="
echo "Deploying service file to $SERVICE_FILE"
echo "====================================="

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

# Allow binding to port 80 without root
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

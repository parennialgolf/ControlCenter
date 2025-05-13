#!/bin/bash
set -e

PROJECT_DIR="$(pwd)/ControlCenter/ControlCenter"
PUBLISH_DIR="$PROJECT_DIR/publish"
SERVICE_FILE="$(pwd)/scripts/controlcenter.service"
SERVICE_NAME="controlcenter.service"
SYSTEMD_DIR="/etc/systemd/system"

echo "====================================="
echo "Publishing ControlCenter to $PUBLISH_DIR"
echo "====================================="

cd "$PROJECT_DIR"
dotnet publish -c Release -r linux-x64 --self-contained false -o "$PUBLISH_DIR"

echo ""
echo "====================================="
echo "Deploying service file to $SYSTEMD_DIR"
echo "====================================="

sudo cp "$SERVICE_FILE" "$SYSTEMD_DIR/$SERVICE_NAME"

echo ""
echo "====================================="
echo "Reloading systemd, enabling & restarting $SERVICE_NAME"
echo "====================================="

sudo systemctl daemon-reload
sudo systemctl enable "$SERVICE_NAME"
sudo systemctl restart "$SERVICE_NAME"

echo ""
echo "====================================="
echo "Status of $SERVICE_NAME"
echo "====================================="

sudo systemctl status "$SERVICE_NAME" --no-pager

echo ""
read -p "Do you want to tail live logs? (y/n): " tail_logs

if [[ "$tail_logs" == "y" || "$tail_logs" == "Y" || "$tail_logs" == "yes" || "$tail_logs" == "Yes" || "$tail_logs" == "YES" ]]; then
    echo ""
    echo "====================================="
    echo "Tailing logs for $SERVICE_NAME (Ctrl+C to stop)"
    echo "====================================="
    sudo journalctl -fu "$SERVICE_NAME"
fi

.PHONY: deploy status logs restart

# Deploy the SerialRelayController service
deploy:
	@echo "👉 Running deploy script for SerialRelayController..."
	@scripts/deploy.sh

# Show current service status
status:
	@echo "🔎 Status of serialrelaycontroller.service"
	@sudo systemctl status serialrelaycontroller.service --no-pager

# Tail live logs
logs:
	@echo "📄 Tailing logs for serialrelaycontroller.service (Ctrl+C to exit)"
	@sudo journalctl -fu serialrelaycontroller.service

# Restart the service manually
restart:
	@echo "🔄 Restarting serialrelaycontroller.service"
	@sudo systemctl restart serialrelaycontroller.service
	@sudo systemctl status serialrelaycontroller.service --no-pager

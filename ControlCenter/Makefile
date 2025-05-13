.PHONY: deploy status logs restart

# Deploy the ControlCenter service
deploy:
	@echo "👉 Running deploy script for ControlCenter..."
	@scripts/deploy.sh

# Show current service status
status:
	@echo "🔎 Status of controlcenter.service"
	@sudo systemctl status controlcenter.service --no-pager

# Tail live logs
logs:
	@echo "📄 Tailing logs for controlcenter.service (Ctrl+C to exit)"
	@sudo journalctl -fu controlcenter.service

# Restart the service manually
restart:
	@echo "🔄 Restarting controlcenter.service"
	@sudo systemctl restart controlcenter.service
	@sudo systemctl status controlcenter.service --no-pager

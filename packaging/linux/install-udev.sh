#!/usr/bin/env bash
set -euo pipefail

sudo groupadd -f input
sudo usermod -aG input "$USER"
echo 'KERNEL=="event*", SUBSYSTEM=="input", MODE="0660", GROUP="input", TAG+="uaccess"' \
  | sudo tee /etc/udev/rules.d/99-optisens-input.rules >/dev/null
sudo udevadm control --reload
sudo udevadm trigger
echo "Installed udev rule. Please log out and back in for group membership to take effect."

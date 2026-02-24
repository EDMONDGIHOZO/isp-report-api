#!/bin/bash

# ============================================
# .NET Deployment Script - bss_report
# Target: linux-x64
# Server: 192.168.10.172
# Path: /var/www/apps/bss_report
# ============================================

set -e  # Stop on error

APP_NAME="bss_report"
REMOTE_USER="api"
REMOTE_HOST="192.168.10.172"
REMOTE_PATH="/var/www/apps/bss_report"
RUNTIME="linux-x64"
BUILD_CONFIG="Release"

echo "===================================="
echo "Starting deployment for $APP_NAME"
echo "===================================="

# Step 1: Clean old build artifacts
echo "Cleaning old build files..."
rm -rf bin obj publish

# Step 2: Publish .NET project
echo "Publishing project..."
dotnet publish -c $BUILD_CONFIG -r $RUNTIME -o ./publish

# Step 3: Deploy via rsync
echo "Syncing files to server..."
rsync -avz \
  -e "ssh -o KexAlgorithms=+diffie-hellman-group1-sha1 -o HostKeyAlgorithms=+ssh-rsa" \
  --exclude 'appsettings.Production.json' \
  ./publish/ \
  $REMOTE_USER@$REMOTE_HOST:$REMOTE_PATH/

echo "===================================="
echo "Deployment completed successfully!"
echo "===================================="

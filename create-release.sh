#!/bin/bash
# Manual release script

echo "Creating manual release..."

# Create release package
mkdir -p release-package
cp bridge/SimpleBridge/bin/Release/PhotoniumZemaxBridge.exe release-package/
cp bridge/SimpleBridge/config.txt release-package/

# Zip the files
cd release-package
zip -r ../PhotoniumZemaxBridge.zip *
cd ..

echo "Package created: PhotoniumZemaxBridge.zip"
echo ""
echo "Now go to https://github.com/Photonium-Optics/Photonium-Windows-Zemax-Test/releases/new"
echo "and upload PhotoniumZemaxBridge.zip"
@echo off
echo Starting Photonium Zemax Bridge...
echo.
echo Make sure you have:
echo - Ansys Zemax OpticStudio installed
echo - Python 3.8+ (64-bit)
echo - Required packages (run: pip install -r requirements.txt)
echo.
python zemax_bridge.py
if %errorlevel% neq 0 (
    echo.
    echo Error: Could not start the bridge.
    echo Please check the requirements above.
    pause
)
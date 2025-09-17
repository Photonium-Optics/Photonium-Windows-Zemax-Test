# Photonium Zemax Bridge

Control Zemax OpticStudio from a Vercel-hosted website through a local Python bridge.

## Architecture

1. **Vercel Website** (Next.js): Hosted frontend with control buttons
2. **Local Bridge** (Python/FastAPI): Runs on user's Windows PC, exposes HTTP endpoints
3. **Zemax OpticStudio**: Controlled via ZOS-API in Standalone mode

## Setup Instructions

### On Windows PC (with Zemax)

1. **Prerequisites:**
   - Windows 64-bit
   - Ansys Zemax OpticStudio installed
   - Python 3.8+ (64-bit) 

2. **Install the bridge:**
   ```powershell
   # Open PowerShell as Administrator
   cd C:\Users\YourName\Desktop
   python -m venv zemax_env
   .\zemax_env\Scripts\Activate.ps1
   pip install -r requirements.txt
   ```

3. **Run the bridge:**
   ```powershell
   python zemax_bridge.py
   ```
   
   Or with uvicorn directly:
   ```powershell
   uvicorn zemax_bridge:app --host 127.0.0.1 --port 8765
   ```

   The bridge will be accessible at `http://127.0.0.1:8765`

### Deploy Website to Vercel

1. **Install dependencies:**
   ```bash
   cd photonium-zemax-web
   npm install
   ```

2. **Test locally:**
   ```bash
   npm run dev
   ```
   Visit http://localhost:3000

3. **Deploy to Vercel:**
   ```bash
   vercel
   ```

## How It Works

### Button 1: Start OpticStudio
- Calls `POST http://127.0.0.1:8765/start`
- Creates OpticStudio instance via `CreateNewApplication()` 
- Runs in Standalone mode (no GUI, background server)

### Button 2: Load .ZMX File
- Calls `POST http://127.0.0.1:8765/open_url`
- Bridge downloads the .zmx file from Vercel site
- Loads it into OpticStudio via `LoadFile()`

## API Endpoints

| Endpoint | Method | Description |
|----------|---------|-----------|
| `/health` | GET | Check bridge status |
| `/start` | POST | Start OpticStudio server |
| `/open_path` | POST | Load local .zmx file |
| `/open_url` | POST | Download & load .zmx from URL |
| `/shutdown` | POST | Close OpticStudio |

## Security Notes

- Bridge only accepts connections from `127.0.0.1`
- CORS headers configured for your Vercel domain
- Private Network Access headers included for Chrome
- Set `PHOTONIUM_ORIGIN` environment variable to restrict origins

## Troubleshooting

1. **"Bridge not reachable"**: Ensure zemax_bridge.py is running
2. **"License not available"**: Check OpticStudio license
3. **CORS errors**: Verify CORS headers in bridge
4. **File not loading**: Ensure .zmx file is valid

## Alternative: ZOSPy

For modern Python (3.10+), use ZOSPy instead:

```python
pip install zospy
```

Replace the ZOS-API bootstrap section with:
```python
from zospy import ZOS
zos = ZOS()
app_obj = zos.connect(mode="standalone")
sys_obj = app_obj.PrimarySystem
```
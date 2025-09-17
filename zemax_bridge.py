# zemax_bridge.py
# Windows + Python 3.8 x64 + pythonnet==2.5.2
import os, sys, tempfile, pathlib, requests
from typing import Optional
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

# --- ZOS-API bootstrap (pythonnet) ---
ZMX_DIR = r"C:\Program Files\Zemax OpticStudio"
ASM_DIR = os.path.join(ZMX_DIR, "ZOS-API Assemblies")
if not os.path.isdir(ASM_DIR):
    raise RuntimeError(f"ZOS-API Assemblies folder not found: {ASM_DIR}")

sys.path.append(ASM_DIR)
import clr  # pythonnet

clr.AddReference("ZOSAPI_NetHelper")
from ZOSAPI_NetHelper import ZOSAPI_Initializer
if not ZOSAPI_Initializer.Initialize():
    raise RuntimeError("Failed to locate OpticStudio via ZOSAPI_Initializer")

clr.AddReference("ZOSAPI"); clr.AddReference("ZOSAPI_Interfaces")
import ZOSAPI

_conn = ZOSAPI.ZOSAPI_Connection()
_app = None
_sys = None

def ensure_app():
    global _app, _sys
    if _app is None:
        _app = _conn.CreateNewApplication()   # Standalone mode (server)
        if _app is None or not _app.IsValidLicenseForAPI:
            raise RuntimeError(f"Zemax license/API not available: {_app and _app.LicenseStatus}")
        if _app.Mode != ZOSAPI.ZOSAPI_Mode.Server:
            raise RuntimeError(f"Expected Server mode, got: {_app.Mode}")
        _sys = _app.PrimarySystem
    return _app, _sys

# --- HTTP server ---
app = FastAPI(title="Photonium Zemax Bridge")

# CORS: allow your Vercel origin (set PHOTONIUM_ORIGIN env) or '*' during dev.
allow_origin = os.environ.get("PHOTONIUM_ORIGIN", "*")
app.add_middleware(
    CORSMiddleware,
    allow_origins=[allow_origin] if allow_origin != "*" else ["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Private Network Access: add header on preflight when requested by the browser.
@app.middleware("http")
async def add_pna_header(request, call_next):
    response = await call_next(request)
    if request.headers.get("access-control-request-private-network", "").lower() == "true":
        response.headers["Access-Control-Allow-Private-Network"] = "true"
    return response

class OpenPathReq(BaseModel):
    path: str

class OpenURLReq(BaseModel):
    url: str
    filename: Optional[str] = None

@app.get("/health")
def health():
    try:
        app_obj, _ = ensure_app()
        return {"ok": True, "mode": str(app_obj.Mode)}
    except Exception as e:
        return {"ok": False, "error": str(e)}

@app.post("/start")
def start():
    app_obj, _ = ensure_app()
    # Standalone starts the OpticStudio server (no GUI)
    return {"ok": True, "mode": str(app_obj.Mode), "license_ok": bool(app_obj.IsValidLicenseForAPI)}

@app.post("/open_path")
def open_path(req: OpenPathReq):
    _, sys_obj = ensure_app()
    p = pathlib.Path(req.path)
    if not p.exists():
        raise HTTPException(404, f"File not found: {p}")
    ok = bool(sys_obj.LoadFile(str(p), False))  # load .ZMX/.ZOS
    if not ok:
        raise HTTPException(500, "Zemax failed to load the file")
    return {"ok": True, "loaded": str(p)}

@app.post("/open_url")
def open_url(req: OpenURLReq):
    _, sys_obj = ensure_app()
    r = requests.get(req.url, timeout=60)
    if r.status_code != 200:
        raise HTTPException(502, f"Download failed: HTTP {r.status_code}")
    fn = req.filename or pathlib.Path(req.url.split("?")[0]).name or "download.zmx"
    if not (fn.lower().endswith(".zmx") or fn.lower().endswith(".zos")):
        fn = fn + ".zmx"
    tdir = pathlib.Path(tempfile.gettempdir()) / "photonium_zmx"
    tdir.mkdir(parents=True, exist_ok=True)
    fpath = tdir / fn
    fpath.write_bytes(r.content)
    ok = bool(sys_obj.LoadFile(str(fpath), False))
    if not ok:
        raise HTTPException(500, "Zemax failed to load downloaded file")
    return {"ok": True, "loaded": str(fpath)}

@app.post("/shutdown")
def shutdown():
    global _app
    if _app is not None:
        _app.CloseApplication()
        _app = None
    return {"ok": True}
'use client';
import React, { useEffect, useState } from 'react';

const BRIDGE = 'http://127.0.0.1:8765';
const ZMX_URL = '/zmx/DoubleGauss.zmx';
const INSTALLER_URL = 'https://github.com/Photonium-Optics/Photonium-Windows-Zemax-Test/releases/latest';

async function callBridge(path: string, init?: RequestInit) {
  const ctrl = new AbortController();
  const id = setTimeout(() => ctrl.abort(), 3000);
  try {
    const res = await fetch(`${BRIDGE}${path}`, {
      method: 'GET',
      ...init,
      headers: {'content-type': 'application/json', ...(init?.headers || {})},
      signal: ctrl.signal,
    });
    clearTimeout(id);
    if (!res.ok) {
      const text = await res.text();
      throw new Error(`${res.status} ${res.statusText}: ${text}`);
    }
    return await res.json();
  } catch (e) {
    clearTimeout(id);
    throw e;
  }
}

export default function Home() {
  const [bridgeStatus, setBridgeStatus] = useState<'checking' | 'online' | 'offline'>('checking');
  const [log, setLog] = useState<string[]>([]);
  const [loading, setLoading] = useState<string | null>(null);
  const [showPathConfig, setShowPathConfig] = useState(false);
  const [zemaxPath, setZemaxPath] = useState('C:\\Program Files\\Zemax OpticStudio');

  const push = (m: string) => setLog(l => [`${new Date().toLocaleTimeString()}  ${m}`, ...l]);

  // Check if bridge is running on page load
  useEffect(() => {
    const checkBridge = async () => {
      try {
        await callBridge('/health');
        setBridgeStatus('online');
        push('✓ Bridge detected and running');
      } catch {
        setBridgeStatus('offline');
      }
    };
    checkBridge();
    
    // Re-check every 5 seconds if offline
    const interval = setInterval(() => {
      if (bridgeStatus === 'offline') {
        checkBridge();
      }
    }, 5000);
    
    return () => clearInterval(interval);
  }, [bridgeStatus]);

  const scanPath = async () => {
    try {
      push(`Scanning: ${zemaxPath}`);
      const json = await callBridge('/scan_path', {
        method: 'POST',
        body: JSON.stringify({ path: zemaxPath }),
      });
      if (json.exists) {
        push(`✓ Path exists`);
        if (json.directories && json.directories.length > 0) {
          push(`Directories found: ${json.directories.slice(0, 8).join(', ')}${json.directories.length > 8 ? '...' : ''}`);
        }
        if (json.dlls && json.dlls.length > 0) {
          push(`DLL files: ${json.dlls.slice(0, 5).join(', ')}${json.dlls.length > 5 ? '...' : ''}`);
        }
        if (json.api_files && json.api_files.length > 0) {
          push(`✓ Potential API files: ${json.api_files.join(', ')}`);
        }
        if (json.api_found_in && json.api_found_in.length > 0) {
          push(`✓ API subdirectories: ${json.api_found_in.join(', ')}`);
          push(`Try one of these paths or check the APIP directory`);
        } else if (!json.api_files || json.api_files.length === 0) {
          push(`✗ No obvious API files found. Check subdirectories like APIP`);
        }
      } else {
        push(`✗ Path does not exist`);
      }
    } catch (err) {
      const error = err as Error;
      push(`✗ Scan failed: ${String(error?.message || err)}`);
    }
  };

  const setZemaxPathOnBridge = async () => {
    try {
      push(`Setting Zemax path to: ${zemaxPath}`);
      await callBridge('/set_path', {
        method: 'POST',
        body: JSON.stringify({ path: zemaxPath }),
      });
      push(`✓ Path set successfully`);
      setShowPathConfig(false);
    } catch (err) {
      const error = err as Error;
      push(`✗ Failed to set path: ${String(error?.message || err)}`);
    }
  };

  const onStart = async () => {
    try {
      setLoading('start');
      push('Starting OpticStudio (Standalone)...');
      const json = await callBridge('/start', { method: 'POST' });
      push(`✓ OK: mode=${json.mode}, license_ok=${json.license_ok}`);
    } catch (err) {
      const error = err as Error;
      push(`✗ Failed: ${String(error?.message || err)}`);
      
      // Try to parse error response for more details
      try {
        const errorText = error.message;
        if (errorText.includes('{')) {
          const errorJson = JSON.parse(errorText.substring(errorText.indexOf('{')));
          if (errorJson.details) {
            push(`Debug info: ${errorJson.details}`);
          }
        }
      } catch {}
      
      // Try fallback protocol
      push('Attempting protocol handler fallback...');
      window.location.href = 'photonium-zemax://start';
    } finally {
      setLoading(null);
    }
  };

  const onLoad = async () => {
    try {
      setLoading('load');
      push('Loading ZMX from website into OpticStudio...');
      const url = new URL(ZMX_URL, window.location.origin).toString();
      const json = await callBridge('/open_url', {
        method: 'POST',
        body: JSON.stringify({ url, filename: 'site_file.zmx' }),
      });
      push(`✓ Loaded: ${json.loaded}`);
    } catch (err) {
      const error = err as Error;
      push(`✗ Failed: ${String(error?.message || err)}`);
      // Try fallback protocol
      push('Attempting protocol handler fallback...');
      const url = encodeURIComponent(new URL(ZMX_URL, window.location.origin).toString());
      window.location.href = `photonium-zemax://open?url=${url}&filename=site_file.zmx`;
    } finally {
      setLoading(null);
    }
  };

  // Show installer download if bridge is not running
  if (bridgeStatus === 'offline') {
    return (
      <main className="max-w-3xl mx-auto p-8">
        <div className="mb-8">
          <h1 className="text-4xl font-bold mb-2">Photonium ↔ OpticStudio</h1>
          <p className="text-gray-600">Control OpticStudio from your browser</p>
        </div>

        <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-6 mb-8">
          <h2 className="text-xl font-semibold text-yellow-900 mb-4">
            Bridge Not Detected
          </h2>
          <p className="text-yellow-800 mb-6">
            To control OpticStudio from this website, you need to install the Photonium Zemax Bridge on your Windows PC.
          </p>
          
          <div className="space-y-4">
            <a 
              href={INSTALLER_URL}
              className="inline-block px-8 py-4 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors text-lg font-medium"
              target="_blank"
              rel="noopener noreferrer"
            >
              ⬇ Go to Releases Page
            </a>
            
            <div className="text-sm text-yellow-700">
              <p className="font-semibold mb-2">Setup Instructions:</p>
              <ol className="list-decimal list-inside space-y-1">
                <li>Click the button above to go to GitHub Releases</li>
                <li>Download the latest installer (.exe file)</li>
                <li>Run the installer (admin rights required)</li>
                <li>The bridge will start automatically</li>
                <li>Refresh this page to continue</li>
              </ol>
              <p className="mt-2 text-xs italic">Note: If no release exists yet, the installer needs to be built from source on a Windows PC with Visual Studio.</p>
            </div>
          </div>
        </div>

        <div className="text-center">
          <button 
            onClick={() => window.location.reload()}
            className="px-6 py-3 bg-gray-600 text-white rounded-lg hover:bg-gray-700 transition-colors"
          >
            🔄 Refresh Page
          </button>
        </div>
      </main>
    );
  }

  // Show loading state while checking
  if (bridgeStatus === 'checking') {
    return (
      <main className="max-w-3xl mx-auto p-8">
        <div className="text-center">
          <h1 className="text-2xl mb-4">Checking bridge connection...</h1>
          <div className="animate-pulse">⏳</div>
        </div>
      </main>
    );
  }

  // Bridge is online - show control buttons
  return (
    <main className="max-w-3xl mx-auto p-8">
      <div className="mb-8">
        <h1 className="text-4xl font-bold mb-2">Photonium ↔ OpticStudio</h1>
        <p className="text-gray-600">Control OpticStudio on your PC</p>
        <div className="mt-2">
          <span className="inline-flex items-center gap-2 text-sm text-green-600">
            <span className="w-2 h-2 bg-green-500 rounded-full animate-pulse"></span>
            Bridge Connected
          </span>
        </div>
      </div>

      <div className="flex gap-4 mb-8">
        <button 
          onClick={onStart}
          disabled={loading === 'start'}
          className="px-6 py-3 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2"
        >
          <span className="text-xl">▶</span>
          {loading === 'start' ? 'Starting...' : 'Start OpticStudio (Standalone)'}
        </button>
        <button 
          onClick={onLoad}
          disabled={loading === 'load'}
          className="px-6 py-3 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2"
        >
          <span className="text-xl">⤓</span>
          {loading === 'load' ? 'Loading...' : 'Load .ZMX from this site'}
        </button>
        <button 
          onClick={() => setShowPathConfig(!showPathConfig)}
          className="px-6 py-3 bg-gray-600 text-white rounded-lg hover:bg-gray-700 transition-colors flex items-center gap-2"
        >
          <span className="text-xl">⚙</span>
          Configure Zemax Path
        </button>
      </div>

      {showPathConfig && (
        <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4 mb-8">
          <h3 className="font-semibold text-yellow-900 mb-2">Configure Zemax Installation Path</h3>
          <p className="text-sm text-yellow-700 mb-3">
            If Zemax is not being detected, enter the correct installation path:
          </p>
          <div className="flex gap-2">
            <input
              type="text"
              value={zemaxPath}
              onChange={(e) => setZemaxPath(e.target.value)}
              className="flex-1 px-3 py-2 border border-yellow-300 rounded-lg font-mono text-sm"
              placeholder="C:\Program Files\Zemax OpticStudio"
            />
            <button
              onClick={scanPath}
              className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
            >
              Scan
            </button>
            <button
              onClick={setZemaxPathOnBridge}
              className="px-4 py-2 bg-yellow-600 text-white rounded-lg hover:bg-yellow-700 transition-colors"
            >
              Set Path
            </button>
          </div>
          <div className="mt-2 text-xs text-yellow-600">
            Common paths: 
            <br />• C:\Program Files\Zemax OpticStudio
            <br />• C:\Program Files\Zemax OpticStudio 2024
            <br />• C:\Program Files\Ansys\Zemax OpticStudio
          </div>
        </div>
      )}

      <div className="bg-gray-50 border border-gray-200 rounded-lg p-4 mb-8">
        <h3 className="font-semibold text-gray-900 mb-2">System Requirements</h3>
        <ul className="text-gray-700 space-y-1 text-sm">
          <li>✓ Windows 10/11 (64-bit)</li>
          <li>✓ Ansys Zemax OpticStudio installed</li>
          <li>✓ Valid OpticStudio license with API access</li>
          <li>✓ Photonium Zemax Bridge running</li>
        </ul>
      </div>

      <div>
        <h3 className="font-semibold mb-2">Activity Log</h3>
        <pre className="bg-gray-900 text-green-400 p-4 rounded-lg min-h-[200px] max-h-[400px] overflow-y-auto font-mono text-sm">
          {log.length > 0 ? log.join('\n') : 'Ready. Click a button to control OpticStudio.'}
        </pre>
      </div>
    </main>
  );
}
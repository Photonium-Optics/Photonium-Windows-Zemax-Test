'use client';
import React, { useState } from 'react';

const BRIDGE = 'http://127.0.0.1:8765';
const ZMX_URL = '/zmx/DoubleGauss.zmx';

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
  const [log, setLog] = useState<string[]>([]);
  const [loading, setLoading] = useState<string | null>(null);

  const push = (m: string) => setLog(l => [`${new Date().toLocaleTimeString()}  ${m}`, ...l]);

  const onStart = async () => {
    try {
      setLoading('start');
      push('Starting OpticStudio (Standalone)...');
      const json = await callBridge('/start', { method: 'POST' });
      push(`✓ OK: mode=${json.mode}, license_ok=${json.license_ok}`);
    } catch (err: any) {
      push(`✗ Bridge not reachable. Is zemax_bridge.py running? ${String(err?.message || err)}`);
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
    } catch (err: any) {
      push(`✗ Load failed: ${String(err?.message || err)}`);
    } finally {
      setLoading(null);
    }
  };

  return (
    <main className="max-w-3xl mx-auto p-8">
      <div className="mb-8">
        <h1 className="text-4xl font-bold mb-2">Photonium ↔ OpticStudio</h1>
        <p className="text-gray-600">Control OpticStudio on your PC through a local bridge</p>
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
      </div>

      <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 mb-8">
        <h3 className="font-semibold text-blue-900 mb-2">Setup Requirements</h3>
        <ol className="list-decimal list-inside text-blue-800 space-y-1">
          <li>Install Ansys Zemax OpticStudio on your Windows PC</li>
          <li>Install Python 3.8+ (64-bit) with required packages</li>
          <li>Run the bridge: <code className="bg-white px-2 py-1 rounded">python zemax_bridge.py</code></li>
          <li>Bridge must be accessible at <code className="bg-white px-2 py-1 rounded">http://127.0.0.1:8765</code></li>
        </ol>
      </div>

      <div>
        <h3 className="font-semibold mb-2">Activity Log</h3>
        <pre className="bg-gray-900 text-green-400 p-4 rounded-lg min-h-[200px] overflow-y-auto font-mono text-sm">
          {log.length > 0 ? log.join('\n') : 'No activity yet. Click a button to start.'}
        </pre>
      </div>
    </main>
  );
}
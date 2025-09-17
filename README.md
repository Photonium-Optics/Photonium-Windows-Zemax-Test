# Photonium Zemax Bridge

Control Zemax OpticStudio from your web browser - no Python or complex setup required!

## 🚀 Quick Start

### For Users

1. **Visit the website**: https://photonium-windows-zemax-test.vercel.app
2. **Download and run the installer** when prompted
3. **Start controlling Zemax** with the web buttons

That's it! No command line, no configuration.

### For Developers - Build the Bridge

**No Zemax required to compile!** Build on any Windows PC:

```cmd
cd bridge\SimpleBridge
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe SimpleBridge.csproj /p:Configuration=Release
```

The exe will be in `bin\Release\PhotoniumZemaxBridge.exe`

## 📁 Project Structure

```
/
├── app/                    # Next.js website (Vercel)
│   └── page.tsx           # Main control interface
├── bridge/                
│   ├── SimpleBridge/      # C# bridge (compiles without Zemax!)
│   │   └── SimpleBridge.cs
│   └── installer.iss      # Inno Setup installer script
├── public/zmx/            # Sample lens files
└── BUILD_INSTRUCTIONS.md  # Detailed build guide
```

## 🎯 Features

- **Auto-detection**: Website detects if bridge is installed
- **One-click install**: Simple installer handles everything
- **No dependencies**: Uses .NET Framework built into Windows
- **Late binding**: Bridge loads Zemax at runtime, not compile time

## 🔧 How It Works

1. Website (Vercel) → Makes HTTP calls to localhost:8765
2. Bridge (C#) → Receives HTTP, controls Zemax via API
3. Zemax → Loads files, performs operations

## 📋 Requirements

- Windows 10/11 (64-bit)
- Zemax OpticStudio with API license
- .NET Framework 4.8 (included in Windows)

## 🛠️ Building from Source

See [BUILD_INSTRUCTIONS.md](BUILD_INSTRUCTIONS.md) for detailed steps.

## 📄 License

MIT License
# Photonium Zemax Bridge

Control Zemax OpticStudio from any web browser - no Python or CLI required!

## 🚀 Quick Start for Users (2 minutes)

1. **Install the Bridge**
   - Download the installer from the website
   - Run the installer (admin rights required)
   - The bridge starts automatically

2. **Visit the Website**
   - Go to https://photonium-windows-zemax-test.vercel.app
   - Click buttons to control OpticStudio

That's it! No command line, no Python, no configuration.

## 🎯 What This Does

- **Start OpticStudio**: Launches OpticStudio in API mode (no GUI)
- **Load .ZMX Files**: Downloads lens files from the web and loads them into OpticStudio
- **Automatic Detection**: Website auto-detects if bridge is installed

## 🛠️ For Developers

### Architecture

```
Web Browser → Vercel Website → Local Bridge (C#/.NET) → ZOS-API → OpticStudio
```

### Building from Source

#### Prerequisites
- Visual Studio 2019+ with .NET Framework 4.8
- Zemax OpticStudio installed (for API assemblies)
- Inno Setup (for installer)

#### Build Steps

1. **Build the Bridge**
   ```cmd
   cd bridge\Photonium.Zemax.Bridge
   msbuild /p:Configuration=Release /p:Platform=x64
   ```

2. **Create Installer**
   ```cmd
   cd bridge
   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
   ```

3. **Deploy Website**
   ```bash
   npm install
   npm run build
   vercel --prod
   ```

### Project Structure

```
/
├── app/                    # Next.js website
│   └── page.tsx           # Main control interface
├── bridge/                # C# bridge application  
│   ├── Photonium.Zemax.Bridge/
│   │   ├── Program.cs     # HTTP server + ZOS-API
│   │   └── *.csproj       # Project file
│   └── installer.iss      # Inno Setup script
├── public/
│   └── zmx/              # Sample lens files
└── README.md
```

### API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Check if bridge is running |
| `/start` | POST | Start OpticStudio (Standalone) |
| `/open_url` | POST | Download and load .zmx file |
| `/shutdown` | POST | Close OpticStudio |

### Custom Protocol

The installer registers `photonium-zemax://` protocol for fallback:
- `photonium-zemax://start` - Start OpticStudio
- `photonium-zemax://open?url=...` - Load file from URL

## 🔒 Security

- Bridge only accepts connections from `localhost` (127.0.0.1)
- Optional CORS restriction to official website
- No external network access from bridge
- URL reservation allows non-admin execution

## 📋 Requirements

- Windows 10/11 (64-bit)
- Ansys Zemax OpticStudio installed
- Valid OpticStudio license with API access
- .NET Framework 4.8 (included in Windows)

## 🐛 Troubleshooting

| Issue | Solution |
|-------|----------|
| "Bridge not detected" | Run the installer and restart browser |
| "License not available" | Check OpticStudio license |
| "Failed to start listener" | Installer reserves URL automatically |
| Buttons don't work | Check Windows Firewall settings |

## 📄 License

MIT License - See LICENSE file for details

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## 📧 Support

For issues or questions, open an issue on GitHub.
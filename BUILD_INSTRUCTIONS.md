# Building the Bridge - No Zemax Required!

## Option 1: Simple Command Line Build (No Visual Studio needed)

1. **Install .NET Framework 4.8 Developer Pack** (if not already installed)
   - Download from: https://dotnet.microsoft.com/download/dotnet-framework/net48
   
2. **Open Command Prompt** and navigate to the project:
   ```cmd
   cd bridge\SimpleBridge
   ```

3. **Build using MSBuild** (comes with .NET):
   ```cmd
   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe SimpleBridge.csproj /p:Configuration=Release
   ```

4. **Find your exe**:
   - Look in `bridge\SimpleBridge\bin\Release\PhotoniumZemaxBridge.exe`

## Option 2: Using Visual Studio

1. Open `bridge\SimpleBridge\SimpleBridge.csproj` in Visual Studio
2. Build → Build Solution (Ctrl+Shift+B)
3. Find exe in `bin\Release\`

## That's it!

The SimpleBridge version:
- ✅ Compiles WITHOUT Zemax installed
- ✅ Loads Zemax DLLs at runtime (when user runs it)
- ✅ Uses late binding - no compile-time dependencies
- ✅ Works with just .NET Framework (built into Windows)

## Testing the Bridge

1. Run the exe: `PhotoniumZemaxBridge.exe`
2. You should see:
   ```
   Photonium Zemax Bridge (Simple Version)
   Listening at http://127.0.0.1:8765/
   Press Ctrl+C to stop...
   ```
3. Visit https://photonium-windows-zemax-test.vercel.app
4. The website should detect the bridge!

## Creating an Installer (Optional)

If you have Inno Setup:
1. Update `bridge\installer.iss` to point to SimpleBridge exe
2. Compile with Inno Setup
3. Upload to GitHub Releases
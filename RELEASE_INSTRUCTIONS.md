# Creating the Installer Release

## On a Windows PC with Visual Studio and Zemax:

1. **Clone the repository**
   ```cmd
   git clone https://github.com/Photonium-Optics/Photonium-Windows-Zemax-Test.git
   cd Photonium-Windows-Zemax-Test
   ```

2. **Build the C# Bridge**
   - Open `bridge\Photonium.Zemax.Bridge\Photonium.Zemax.Bridge.csproj` in Visual Studio
   - Change build configuration to `Release | x64`
   - Build → Build Solution
   - Output will be in `bridge\Photonium.Zemax.Bridge\bin\x64\Release\`

3. **Create the Installer**
   - Install Inno Setup from https://jrsoftware.org/isdl.php
   - Open `bridge\installer.iss` in Inno Setup
   - Compile (Ctrl+F9)
   - Output will be `bridge\Photonium-Zemax-Bridge-Setup.exe`

4. **Upload to GitHub Releases**
   - Go to https://github.com/Photonium-Optics/Photonium-Windows-Zemax-Test/releases
   - Click "Create a new release"
   - Tag version: `v1.0.0`
   - Release title: `Photonium Zemax Bridge v1.0.0`
   - Upload the `Photonium-Zemax-Bridge-Setup.exe` file
   - Publish release

The download link will then work automatically!
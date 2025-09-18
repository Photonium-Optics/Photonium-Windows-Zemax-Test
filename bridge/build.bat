@echo off
echo Building Photonium Zemax Bridge...
echo.

REM Find MSBuild - try multiple locations
set MSBUILD=
if exist "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" (
    set MSBUILD=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe
) else if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe
) else if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD=C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe
)

if "%MSBUILD%"=="" (
    echo ERROR: Could not find MSBuild.exe
    echo Please install .NET Framework 4.8 SDK or Visual Studio
    pause
    exit /b 1
)

echo Using MSBuild: %MSBUILD%
echo.

REM Build the project
"%MSBUILD%" SimpleBridge\SimpleBridge.csproj /p:Configuration=Release /p:Platform="Any CPU" /v:minimal

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD FAILED!
    pause
    exit /b 1
)

echo.
echo BUILD SUCCESSFUL!
echo Output: SimpleBridge\bin\Release\PhotoniumZemaxBridge.exe
echo.
echo You can now run the bridge by double-clicking PhotoniumZemaxBridge.exe
pause
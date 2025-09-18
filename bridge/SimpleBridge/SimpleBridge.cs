// Simple bridge that uses late binding - no Zemax required at compile time!
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace SimpleBridge
{
    class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        
        static HttpListener _http;
        static dynamic _app;  // Late binding - no compile-time reference needed
        static dynamic _sys;
        static JavaScriptSerializer _json = new JavaScriptSerializer();
        static string _origin = "*";
        static string _customZemaxPath = null;

        static string GetZemaxPath()
        {
            // First check if custom path was set via API
            if (!string.IsNullOrEmpty(_customZemaxPath))
            {
                Console.WriteLine("Using custom Zemax path: " + _customZemaxPath);
                return _customZemaxPath;
            }
            
            // Try to read from config file
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            if (File.Exists(configPath))
            {
                foreach (string line in File.ReadAllLines(configPath))
                {
                    if (line.StartsWith("ZEMAX_PATH="))
                    {
                        string path = line.Substring("ZEMAX_PATH=".Length).Trim();
                        Console.WriteLine("Using Zemax path from config: " + path);
                        return path;
                    }
                }
            }
            
            // Try common locations
            string[] commonPaths = new string[] {
                @"C:\Program Files\Ansys Zemax OpticStudio 2025 R1.00",
                @"C:\Program Files\Zemax OpticStudio",
                @"C:\Program Files\Zemax OpticStudio 2024",
                @"C:\Program Files\Ansys\Zemax OpticStudio",
                @"C:\Program Files\ZEMAX13"
            };
            
            foreach (string path in commonPaths)
            {
                if (Directory.Exists(Path.Combine(path, "ZOS-API")))
                {
                    Console.WriteLine("Found Zemax at: " + path);
                    return path;
                }
            }
            
            throw new Exception("Could not find Zemax. Please set the path using Configure Zemax Path button.");
        }
        
        static void Main(string[] args)
        {
            Console.WriteLine("Photonium Zemax Bridge (Simple Version)");
            
            // Start HTTP server
            _http = new HttpListener();
            _http.Prefixes.Add("http://127.0.0.1:8765/");
            
            try 
            {
                _http.Start();
                Console.WriteLine("Listening at http://127.0.0.1:8765/");
                Console.WriteLine("Press Ctrl+C to stop...");
            }
            catch (HttpListenerException e)
            {
                Console.Error.WriteLine("Failed to start: " + e.Message);
                Console.Error.WriteLine("Run as administrator or use the installer to reserve the URL");
                Console.ReadKey();
                return;
            }
            
            // Main loop
            while (true)
            {
                var ctx = _http.GetContext();
                try 
                {
                    Route(ctx);
                }
                catch (Exception ex)
                {
                    SendJson(ctx, 500, new { ok = false, error = ex.Message });
                }
            }
        }

        static void Route(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var path = req.Url.AbsolutePath;
            
            // CORS preflight
            if (req.HttpMethod == "OPTIONS")
            {
                SetCors(ctx.Response);
                if (req.Headers["Access-Control-Request-Private-Network"] == "true")
                    ctx.Response.AddHeader("Access-Control-Allow-Private-Network", "true");
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
                return;
            }

            // GET /health
            if (req.HttpMethod == "GET" && path == "/health")
            {
                SendJson(ctx, 200, new { ok = true, bridge = "simple" });
                return;
            }

            // POST /start
            if (req.HttpMethod == "POST" && path == "/start")
            {
                try
                {
                    Console.WriteLine("Starting OpticStudio...");
                    
                    // First, try to launch OpticStudio executable
                    string zemaxDir = GetZemaxPath();
                    string exePath = Path.Combine(zemaxDir, "OpticStudio.exe");
                    
                    if (!File.Exists(exePath))
                    {
                        // Try alternate names
                        string[] possibleExeNames = new string[] {
                            "OpticStudio.exe",
                            "Zemax.exe",
                            "ZemaxOpticStudio.exe"
                        };
                        
                        foreach (string exeName in possibleExeNames)
                        {
                            string tryPath = Path.Combine(zemaxDir, exeName);
                            if (File.Exists(tryPath))
                            {
                                exePath = tryPath;
                                break;
                            }
                        }
                    }
                    
                    if (File.Exists(exePath))
                    {
                        Console.WriteLine("Launching OpticStudio from: " + exePath);
                        System.Diagnostics.Process.Start(exePath);
                        
                        // Wait a bit for OpticStudio to start
                        Console.WriteLine("Waiting for OpticStudio to start...");
                        System.Threading.Thread.Sleep(5000);
                    }
                    else
                    {
                        Console.WriteLine("Could not find OpticStudio.exe at: " + zemaxDir);
                    }
                    
                    // Now try to connect
                    LoadZemaxIfNeeded();
                    
                    // Safely check properties
                    string mode = "Standalone";
                    bool licenseOk = false;
                    
                    try
                    {
                        if (_app != null)
                        {
                            mode = _app.Mode.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Could not get Mode: " + ex.Message);
                    }
                    
                    try
                    {
                        if (_app != null)
                        {
                            licenseOk = _app.IsValidLicenseForAPI;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Could not check license: " + ex.Message);
                        // Assume license is OK if we got this far
                        licenseOk = true;
                    }
                    
                    SendJson(ctx, 200, new { 
                        ok = true, 
                        mode = mode,
                        license_ok = licenseOk,
                        opticstudio_launched = File.Exists(exePath)
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error starting OpticStudio: " + ex.ToString());
                    SendJson(ctx, 500, new { ok = false, error = ex.Message });
                }
                return;
            }

            // POST /set_path
            if (req.HttpMethod == "POST" && path == "/set_path")
            {
                try
                {
                    var body = new StreamReader(req.InputStream).ReadToEnd();
                    dynamic data = _json.DeserializeObject(body);
                    string zemaxPath = data["path"];
                    
                    // Validate the path
                    if (!Directory.Exists(zemaxPath))
                    {
                        SendJson(ctx, 400, new { ok = false, error = "Path does not exist: " + zemaxPath });
                        return;
                    }
                    
                    // Check for ZOS-API folder
                    string apiPath = Path.Combine(zemaxPath, "ZOS-API");
                    if (!Directory.Exists(apiPath))
                    {
                        SendJson(ctx, 400, new { ok = false, error = "No ZOS-API folder found in: " + zemaxPath });
                        return;
                    }
                    
                    _customZemaxPath = zemaxPath;
                    _app = null; // Reset connection to use new path
                    _sys = null;
                    
                    // Save to config file for persistence
                    string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                    File.WriteAllText(configPath, "# Auto-updated by website\nZEMAX_PATH=" + zemaxPath);
                    
                    SendJson(ctx, 200, new { ok = true, path = zemaxPath });
                }
                catch (Exception ex)
                {
                    SendJson(ctx, 500, new { ok = false, error = ex.Message });
                }
                return;
            }
            
            // POST /open_url
            if (req.HttpMethod == "POST" && path == "/open_url")
            {
                try
                {
                    Console.WriteLine("Processing /open_url request...");
                    
                    // If we don't have a connection, try to establish one
                    if (_app == null)
                    {
                        Console.WriteLine("No connection to OpticStudio, attempting to connect...");
                        LoadZemaxIfNeeded();
                    }
                    
                    // Don't worry about _sys being null - FileOpen will create it
                    
                    var body = new StreamReader(req.InputStream).ReadToEnd();
                    Console.WriteLine("Request body: " + body);
                    
                    dynamic data = _json.DeserializeObject(body);
                    string url = data["url"];
                    string filename = data.ContainsKey("filename") ? data["filename"] : "download.zmx";
                    
                    Console.WriteLine("URL: " + url);
                    Console.WriteLine("Filename: " + filename);
                    
                    if (!filename.EndsWith(".zmx") && !filename.EndsWith(".zos"))
                        filename += ".zmx";
                    
                    var tempDir = Path.Combine(Path.GetTempPath(), "photonium_zmx");
                    Directory.CreateDirectory(tempDir);
                    var filepath = Path.Combine(tempDir, filename);
                    
                    Console.WriteLine("Downloading to: " + filepath);
                    
                    using (var wc = new WebClient())
                    {
                        wc.DownloadFile(url, filepath);
                    }
                    
                    Console.WriteLine("File downloaded successfully");
                    
                    // Check if _app is available
                    if (_app == null)
                    {
                        Console.WriteLine("ERROR: _app is null, cannot load file");
                        throw new Exception("Not connected to OpticStudio");
                    }
                    
                    Console.WriteLine("Loading file into OpticStudio...");
                    bool ok = false;
                    
                    // First, check if we have a PrimarySystem
                    if (_sys == null)
                    {
                        try
                        {
                            _sys = _app.PrimarySystem;
                        }
                        catch {}
                    }
                    
                    if (_sys == null)
                    {
                        Console.WriteLine("No PrimarySystem available. Need to create a blank file first.");
                        
                        // Try different approaches to create a new system
                        Console.WriteLine("Attempting to create a new system...");
                        
                        // Try approach 1: Use CreateNewSystem on connection
                        try
                        {
                            Console.WriteLine("Reconnecting to create new system...");
                            
                            // Reset connection and try to create new
                            _app = null;
                            _sys = null;
                            
                            Type connType = Type.GetTypeFromProgID("ZOSAPI.ZOSAPI_Connection");
                            if (connType != null)
                            {
                                dynamic conn = Activator.CreateInstance(connType);
                                
                                // Try to create in standalone mode with a new system
                                Console.WriteLine("Creating new application with system...");
                                _app = conn.CreateNewApplication();
                                
                                // Wait a bit for initialization
                                System.Threading.Thread.Sleep(1000);
                                
                                // Try to get or create PrimarySystem
                                _sys = _app.PrimarySystem;
                                
                                if (_sys == null)
                                {
                                    Console.WriteLine("PrimarySystem still null, attempting manual file creation...");
                                    
                                    // Send message to user
                                    Console.WriteLine("IMPORTANT: Please manually do one of the following in OpticStudio:");
                                    Console.WriteLine("  1. Click File > New to create a new lens file");
                                    Console.WriteLine("  2. Or open any existing .zmx file");
                                    Console.WriteLine("Then click 'Load .ZMX from this site' again.");
                                    
                                    throw new Exception("Please create a new file in OpticStudio (File > New), then try again");
                                }
                                else
                                {
                                    Console.WriteLine("PrimarySystem obtained after reconnection!");
                                }
                            }
                        }
                        catch (Exception reconEx)
                        {
                            Console.WriteLine("Reconnection attempt: " + reconEx.Message);
                            
                            // Final fallback - tell user what to do
                            throw new Exception("No lens file open in OpticStudio. Please click File > New in OpticStudio, then try again.");
                        }
                    }
                    
                    // Now try to load the file if we have PrimarySystem
                    if (_sys != null)
                    {
                        try
                        {
                            Console.WriteLine("Have PrimarySystem, loading file...");
                            ok = _sys.LoadFile(filepath, false);
                            Console.WriteLine("LoadFile returned: " + ok);
                        }
                        catch (Exception loadEx)
                        {
                            Console.WriteLine("LoadFile failed: " + loadEx.Message);
                            
                            // Try alternative: SaveAs
                            try
                            {
                                Console.WriteLine("Trying alternative: New() then LoadFile...");
                                _sys.New(false); // Clear system
                                ok = _sys.LoadFile(filepath, false);
                                Console.WriteLine("LoadFile after New returned: " + ok);
                            }
                            catch (Exception newEx)
                            {
                                Console.WriteLine("New+LoadFile failed: " + newEx.Message);
                                throw new Exception("Could not load file: " + loadEx.Message);
                            }
                        }
                    }
                    else
                    {
                        throw new Exception("No PrimarySystem available. Please create or open a file in OpticStudio first.");
                    }
                    
                    if (!ok) 
                    {
                        Console.WriteLine("LoadFile returned false");
                        throw new Exception("OpticStudio could not load the file (LoadFile returned false)");
                    }
                    
                    Console.WriteLine("File loaded successfully in OpticStudio");
                    SendJson(ctx, 200, new { ok = true, loaded = filepath });
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error in /open_url: " + ex.ToString());
                    SendJson(ctx, 500, new { ok = false, error = ex.Message });
                }
                return;
            }

            // 404
            SendJson(ctx, 404, new { ok = false, error = "Not found" });
        }

        static void LoadZemaxIfNeeded()
        {
            if (_app != null && _sys != null) return;
            
            Console.WriteLine("Loading Zemax API...");
            
            // Get Zemax installation path
            string zemaxDir = GetZemaxPath();
            
            try
            {
                // Try COM approach first for Zemax 2025
                Console.WriteLine("Trying COM-based connection for Zemax 2025...");
                Type appType = Type.GetTypeFromProgID("ZOSAPI.ZOSAPI_Application");
                
                if (appType != null)
                {
                    Console.WriteLine("Found ZOSAPI_Application via COM");
                    _app = Activator.CreateInstance(appType);
                    
                    if (_app != null)
                    {
                        try
                        {
                            _sys = _app.PrimarySystem;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Could not get PrimarySystem: " + ex.Message);
                            _sys = null;
                        }
                        Console.WriteLine("Zemax API loaded successfully via COM");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("COM approach failed: " + ex.Message);
            }
            
            // Fallback to NetHelper approach
            Console.WriteLine("Trying NetHelper approach...");
            
            // ZOS-API folder structure
            string zosApiDir = Path.Combine(zemaxDir, "ZOS-API");
            
            if (!Directory.Exists(zosApiDir))
                throw new Exception("ZOS-API folder not found at: " + zosApiDir);
            
            // Load the NetHelper assembly from Extensions folder
            var helperPath = Path.Combine(zosApiDir, "Extensions", "ZOSAPI_NetHelper.dll");
            if (!File.Exists(helperPath))
                throw new Exception("ZOSAPI_NetHelper.dll not found at: " + helperPath);
                
            Console.WriteLine("Loading ZOSAPI_NetHelper.dll from: " + helperPath);
            var helper = Assembly.LoadFrom(helperPath);
            
            // Call ZOSAPI_Initializer.Initialize()
            var initType = helper.GetType("ZOSAPI_NetHelper.ZOSAPI_Initializer");
            if (initType == null)
                throw new Exception("Could not find ZOSAPI_Initializer type");
                
            var initMethod = initType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
            if (initMethod == null)
                throw new Exception("Could not find Initialize method");
            
            Console.WriteLine("Calling Initialize method...");
            bool initialized = false;
            
            try 
            {
                var parameters = initMethod.GetParameters();
                if (parameters.Length == 0)
                {
                    initialized = (bool)initMethod.Invoke(null, null);
                }
                else if (parameters.Length == 1)
                {
                    initialized = (bool)initMethod.Invoke(null, new object[] { zemaxDir });
                }
                else
                {
                    throw new Exception("Unexpected Initialize signature");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to call Initialize: " + ex.Message);
            }
            
            if (!initialized)
                throw new Exception("ZOSAPI_Initializer.Initialize() returned false");
            
            // After initialization, try COM again
            Console.WriteLine("Trying COM after Initialize...");
            try
            {
                Type appType = Type.GetTypeFromProgID("ZOSAPI.ZOSAPI_Application");
                if (appType != null)
                {
                    _app = Activator.CreateInstance(appType);
                    if (_app != null)
                    {
                        try
                        {
                            _sys = _app.PrimarySystem;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Could not get PrimarySystem: " + ex.Message);
                            _sys = null;
                        }
                        Console.WriteLine("Zemax API loaded successfully via COM after Initialize");
                        return;
                    }
                }
            }
            catch {}
            
            // Last resort: try creating connection directly
            try
            {
                Type connType = Type.GetTypeFromProgID("ZOSAPI.ZOSAPI_Connection");
                if (connType != null)
                {
                    Console.WriteLine("Found ZOSAPI_Connection via COM");
                    dynamic conn = Activator.CreateInstance(connType);
                    
                    // Check if OpticStudio is running by looking for the process
                    var zemaxProcesses = System.Diagnostics.Process.GetProcessesByName("OpticStudio");
                    bool isRunning = zemaxProcesses.Length > 0;
                    Console.WriteLine("OpticStudio process running: " + isRunning);
                    
                    // Always use CreateNewApplication for standalone mode
                    Console.WriteLine("Creating application connection (Standalone mode)...");
                    _app = conn.CreateNewApplication();
                    
                    if (_app == null)
                    {
                        Console.WriteLine("CreateNewApplication returned null");
                        throw new Exception("Failed to create application");
                    }
                    
                    Console.WriteLine("Application created, type: " + _app.GetType().FullName);
                    
                    // Check license
                    try
                    {
                        bool hasValidLicense = _app.IsValidLicenseForAPI;
                        Console.WriteLine("License valid: " + hasValidLicense);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Could not check license: " + ex.Message);
                    }
                    
                    Console.WriteLine("Getting PrimarySystem...");
                    try
                    {
                        _sys = _app.PrimarySystem;
                        if (_sys != null)
                        {
                            Console.WriteLine("PrimarySystem obtained successfully");
                        }
                        else
                        {
                            Console.WriteLine("PrimarySystem is null - will be created when opening a file");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Could not get PrimarySystem: " + ex.Message);
                        _sys = null;
                    }
                    
                    Console.WriteLine("Zemax API loaded successfully");
                    return;
                }
            }
            catch {}
            
            throw new Exception("Could not connect to Zemax API. Make sure Zemax OpticStudio is installed correctly.");
        }

        static void SetCors(HttpListenerResponse res)
        {
            res.Headers["Access-Control-Allow-Origin"] = _origin;
            res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            res.Headers["Access-Control-Allow-Headers"] = "content-type";
        }

        static void SendJson(HttpListenerContext ctx, int status, object data)
        {
            SetCors(ctx.Response);
            var json = _json.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
    }
}
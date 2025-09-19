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
        static bool _apiReady = false;     // true only when license is valid and PrimarySystem exists
        static string _lastInitError = null;

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

            // GET /info
            if (req.HttpMethod == "GET" && path == "/info")
            {
                var payload = new {
                    ok = true,
                    mode = _app != null ? SafeGetString(() => _app.Mode.ToString()) : "None",
                    license_ok = _app != null && SafeGetBool(() => _app.IsValidLicenseForAPI),
                    api_ready = _apiReady,
                    last_error = _lastInitError,
                };
                SendJson(ctx, 200, payload);
                return;
            }

            // POST /start
            if (req.HttpMethod == "POST" && path == "/start")
            {
                try
                {
                    LoadZemaxIfNeeded();
                    SendJson(ctx, 200, new { 
                        ok = true, 
                        mode = _app.Mode.ToString(), 
                        license_ok = _app.IsValidLicenseForAPI 
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
            
            // POST /shell_open_url - fallback that opens file via Windows shell (no API needed)
            if (req.HttpMethod == "POST" && path == "/shell_open_url")
            {
                try
                {
                    Console.WriteLine("Processing /shell_open_url request (no-API fallback)...");
                    
                    var body = new StreamReader(req.InputStream).ReadToEnd();
                    dynamic data = _json.DeserializeObject(body);
                    string url = data["url"];
                    string filename = data.ContainsKey("filename") ? data["filename"] : "download.zmx";
                    
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
                    
                    Console.WriteLine("File downloaded, opening with Windows shell...");
                    
                    // Open with whatever app is registered for .zmx (typically OpticStudio)
                    var psi = new System.Diagnostics.ProcessStartInfo {
                        FileName = filepath, 
                        UseShellExecute = true, // critical: invokes file association
                        Verb = "open"
                    };
                    System.Diagnostics.Process.Start(psi);
                    
                    Console.WriteLine("File opened via shell association");
                    SendJson(ctx, 200, new { ok = true, opened = filepath, via = "shell" });
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error in /shell_open_url: " + ex.ToString());
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
                    
                    // Try to ensure we have a connection
                    LoadZemaxIfNeeded();
                    
                    // Check if API is ready
                    if (!_apiReady || _sys == null)
                    {
                        // Don't crash—tell the client exactly what to do instead
                        Console.WriteLine("API not ready - returning 409");
                        SendJson(ctx, 409, new {
                            ok = false,
                            code = "api_unavailable",
                            reason = _lastInitError ?? "ZOS-API not ready",
                            hint = "Use /shell_open_url to open via GUI file association",
                            filepath = filepath
                        });
                        return;
                    }
                    
                    // API is ready: do the real load
                    Console.WriteLine("Loading file into OpticStudio...");
                    _sys.New(false);  // Clear to a new sequential system
                    bool ok = _sys.LoadFile(filepath, false);
                    
                    if (!ok) 
                    {
                        throw new Exception("LoadFile returned false for: " + filepath);
                    }
                    
                    Console.WriteLine("File loaded successfully in OpticStudio");
                    SendJson(ctx, 200, new { ok = true, loaded = filepath, via = "zosapi" });
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
            if (_app != null && _apiReady) return;

            // Reset state
            _apiReady = false;
            _lastInitError = null;

            Console.WriteLine("Initializing ZOS-API (Standalone)...");
            string zemaxDir = GetZemaxPath();

            // Resolve NetHelper from the official location
            string helperPath = Path.Combine(zemaxDir, "ZOS-API", "Libraries", "ZOSAPI_NetHelper.dll");
            if (!File.Exists(helperPath)) 
            {
                // Try Extensions as fallback for 2025
                helperPath = Path.Combine(zemaxDir, "ZOS-API", "Extensions", "ZOSAPI_NetHelper.dll");
            }
            if (!File.Exists(helperPath)) 
            {
                _lastInitError = "ZOSAPI_NetHelper.dll not found";
                throw new Exception("ZOSAPI_NetHelper.dll not found under ZOS-API\\Libraries or ZOS-API\\Extensions");
            }

            Console.WriteLine("Loading NetHelper from: " + helperPath);
            var helperAsm = Assembly.LoadFrom(helperPath);
            var initType = helperAsm.GetType("ZOSAPI_NetHelper.ZOSAPI_Initializer");
            if (initType == null)
            {
                _lastInitError = "ZOSAPI_Initializer type not found";
                throw new Exception("ZOSAPI_Initializer not found");
            }
                
            // Initialize with no parameters first
            var initMethod = initType.GetMethod("Initialize", Type.EmptyTypes);
            bool inited = false;
            
            if (initMethod != null)
            {
                inited = (bool)initMethod.Invoke(null, null);
            }
            else
            {
                // Try with path parameter
                initMethod = initType.GetMethod("Initialize", new Type[] { typeof(string) });
                if (initMethod != null)
                {
                    inited = (bool)initMethod.Invoke(null, new object[] { zemaxDir });
                }
                else
                {
                    _lastInitError = "Initialize method not found";
                    throw new Exception("Could not find Initialize method");
                }
            }
            
            if (!inited) 
            {
                _lastInitError = "ZOSAPI_Initializer.Initialize() returned false";
                throw new Exception("ZOSAPI_Initializer.Initialize() returned false");
            }

            // Load core assemblies from the standard folder (cover both layouts)
            string asmA = Path.Combine(zemaxDir, "ZOS-API Assemblies");
            string asmB = Path.Combine(zemaxDir, "ZOS-API"); // legacy
            string asmDir = Directory.Exists(asmA) ? asmA : Directory.Exists(asmB) ? asmB : null;
            
            if (asmDir == null) 
            {
                _lastInitError = "ZOS-API assemblies folder not found";
                throw new Exception("ZOS-API assemblies folder not found");
            }

            // Load the main assemblies
            string zosapiPath = Path.Combine(asmDir, "ZOSAPI.dll");
            string zintfPath = Path.Combine(asmDir, "ZOSAPI_Interfaces.dll");
            
            Assembly zosapiAsm = null;
            
            if (File.Exists(zosapiPath))
            {
                Console.WriteLine("Loading ZOSAPI.dll from: " + zosapiPath);
                zosapiAsm = Assembly.LoadFrom(zosapiPath);
            }
            
            if (File.Exists(zintfPath))
            {
                Console.WriteLine("Loading ZOSAPI_Interfaces.dll from: " + zintfPath);
                Assembly.LoadFrom(zintfPath);
            }

            // Create a pure Standalone session (NO COM, NO UI, NO ConnectAsExtension)
            Type connType = null;
            
            if (zosapiAsm != null)
            {
                connType = zosapiAsm.GetType("ZOSAPI.ZOSAPI_Connection");
            }
            
            // Fallback to COM if assembly not found
            if (connType == null)
            {
                connType = Type.GetTypeFromProgID("ZOSAPI.ZOSAPI_Connection");
            }
            
            if (connType == null)
            {
                _lastInitError = "ZOSAPI_Connection type not found";
                throw new Exception("Could not find ZOSAPI_Connection type");
            }
                
            dynamic conn = Activator.CreateInstance(connType);
            _app = conn.CreateNewApplication();   // Standalone server
            
            if (_app == null) 
            {
                _lastInitError = "CreateNewApplication() returned null";
                throw new Exception("CreateNewApplication() returned null");
            }

            // Check license
            bool licenseValid = false;
            try
            {
                licenseValid = _app.IsValidLicenseForAPI;
                Console.WriteLine("License valid for API: " + licenseValid);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not check license: " + ex.Message);
            }
            
            if (!licenseValid)
            {
                _lastInitError = "ZOS-API license not valid";
                _sys = null;
                Console.WriteLine("WARNING: License is not valid for ZOS-API (IsValidLicenseForAPI == false)");
                return; // leave _apiReady=false
            }

            // Check mode
            string mode = "Unknown";
            try
            {
                mode = _app.Mode.ToString();
                Console.WriteLine("Connection mode: " + mode);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not check mode: " + ex.Message);
            }
            
            if (mode != "Server")
            {
                _lastInitError = "Expected Server mode, got: " + mode;
                _sys = null;
                return; // leave _apiReady=false
            }

            // Get PrimarySystem
            _sys = _app.PrimarySystem;
            if (_sys == null) 
            {
                _lastInitError = "PrimarySystem is null (license or init problem)";
                Console.WriteLine("WARNING: PrimarySystem is null");
                return; // leave _apiReady=false
            }
            
            // Success!
            _apiReady = true;
            Console.WriteLine("ZOS-API Standalone ready. Mode=" + mode);
        }

        static bool SafeGetBool(Func<bool> f) 
        { 
            try { return f(); } 
            catch { return false; } 
        }
        
        static string SafeGetString(Func<string> f)
        {
            try { return f(); }
            catch { return "Unknown"; }
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
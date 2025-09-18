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
                    
                    // Ensure we have a Standalone connection
                    LoadZemaxIfNeeded();
                    
                    // Clear the system and load the new file
                    Console.WriteLine("Loading file into OpticStudio...");
                    _sys.New(false);  // Clear to a new sequential system
                    bool ok = _sys.LoadFile(filepath, false);
                    
                    if (!ok) 
                    {
                        throw new Exception("LoadFile returned false for: " + filepath);
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

            Console.WriteLine("Initializing ZOS-API (Standalone)...");
            string zemaxDir = GetZemaxPath();

            // Locate the assemblies (handle old and new layouts)
            string asmA = Path.Combine(zemaxDir, "ZOS-API Assemblies"); // modern
            string asmB = Path.Combine(zemaxDir, "ZOS-API");            // older
            string asmDir = Directory.Exists(asmA) ? asmA :
                            Directory.Exists(asmB) ? asmB : null;
            if (asmDir == null) throw new Exception("ZOS-API assemblies folder not found under " + zemaxDir);

            // For ZOS-API folder structure (Zemax 2025), assemblies are in subfolders
            string zosapiPath = null;
            string zintfPath = null;
            string helperPath = null;
            
            if (asmDir.Contains("ZOS-API") && !asmDir.Contains("Assemblies"))
            {
                // Zemax 2025 structure - files in subfolders
                helperPath = Path.Combine(asmDir, "Extensions", "ZOSAPI_NetHelper.dll");
                // Try to find ZOSAPI.dll in Libraries or Extensions
                zosapiPath = Path.Combine(asmDir, "Libraries", "ZOSAPI.dll");
                if (!File.Exists(zosapiPath))
                    zosapiPath = Path.Combine(asmDir, "Extensions", "ZOSAPI.dll");
                zintfPath = Path.Combine(asmDir, "Libraries", "ZOSAPI_Interfaces.dll");
                if (!File.Exists(zintfPath))
                    zintfPath = Path.Combine(asmDir, "Extensions", "ZOSAPI_Interfaces.dll");
            }
            else
            {
                // Standard structure - files in root
                zosapiPath = Path.Combine(asmDir, "ZOSAPI.dll");
                zintfPath = Path.Combine(asmDir, "ZOSAPI_Interfaces.dll");
                helperPath = Path.Combine(asmDir, "ZOSAPI_NetHelper.dll");
            }

            // Check if we have the NetHelper at least
            if (!File.Exists(helperPath))
                throw new Exception("ZOSAPI_NetHelper.dll not found at: " + helperPath);

            // Load assemblies
            Assembly zosapi = null;
            Assembly zintf = null;
            
            if (File.Exists(zosapiPath))
            {
                Console.WriteLine("Loading ZOSAPI.dll from: " + zosapiPath);
                zosapi = Assembly.LoadFrom(zosapiPath);
            }
            if (File.Exists(zintfPath))
            {
                Console.WriteLine("Loading ZOSAPI_Interfaces.dll from: " + zintfPath);
                zintf = Assembly.LoadFrom(zintfPath);
            }
            
            Console.WriteLine("Loading ZOSAPI_NetHelper.dll from: " + helperPath);
            var helper = Assembly.LoadFrom(helperPath);

            // ZOSAPI_Initializer.Initialize()
            var initType = helper.GetType("ZOSAPI_NetHelper.ZOSAPI_Initializer");
            if (initType == null)
                throw new Exception("Could not find ZOSAPI_Initializer type");
                
            var initMethod = initType.GetMethod("Initialize", Type.EmptyTypes);
            if (initMethod == null)
            {
                // Try with parameter
                initMethod = initType.GetMethod("Initialize", new Type[] { typeof(string) });
                if (initMethod != null)
                {
                    bool ok = (bool)initMethod.Invoke(null, new object[] { zemaxDir });
                    if (!ok) throw new Exception("ZOSAPI_Initializer.Initialize() returned false");
                }
                else
                {
                    throw new Exception("Could not find Initialize method");
                }
            }
            else
            {
                bool ok = (bool)initMethod.Invoke(null, null);
                if (!ok) throw new Exception("ZOSAPI_Initializer.Initialize() returned false");
            }

            // Create Standalone application (do NOT use ConnectAsExtension / COM / UI)
            Type connType = null;
            
            // Try to get connection type from loaded assembly
            if (zosapi != null)
            {
                connType = zosapi.GetType("ZOSAPI.ZOSAPI_Connection");
            }
            
            // If not found, try COM
            if (connType == null)
            {
                connType = Type.GetTypeFromProgID("ZOSAPI.ZOSAPI_Connection");
            }
            
            if (connType == null)
                throw new Exception("Could not find ZOSAPI_Connection type");
                
            dynamic conn = Activator.CreateInstance(connType);

            _app = conn.CreateNewApplication();  // Standalone server
            if (_app == null) throw new Exception("CreateNewApplication() returned null");
            
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
                throw new Exception("License is not valid for ZOS-API use (IsValidLicenseForAPI == false)");

            // Check mode
            string mode = "Unknown";
            try
            {
                mode = _app.Mode.ToString();
                Console.WriteLine("Connection mode: " + mode);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not get mode: " + ex.Message);
            }

            _sys = _app.PrimarySystem;
            if (_sys == null) throw new Exception("PrimarySystem is null in Standalone (unexpected)");
            Console.WriteLine("ZOS-API Standalone ready. Mode=" + mode);
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
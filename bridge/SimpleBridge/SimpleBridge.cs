// Simple bridge that uses late binding - no Zemax required at compile time!
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace SimpleBridge
{
    class Program
    {
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
                        mode = _app != null ? _app.Mode.ToString() : "Unknown",
                        license_ok = _app != null ? _app.IsValidLicenseForAPI : false
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
                    LoadZemaxIfNeeded();
                    
                    var body = new StreamReader(req.InputStream).ReadToEnd();
                    dynamic data = _json.DeserializeObject(body);
                    string url = data["url"];
                    string filename = data.ContainsKey("filename") ? data["filename"] : "download.zmx";
                    
                    if (!filename.EndsWith(".zmx") && !filename.EndsWith(".zos"))
                        filename += ".zmx";
                    
                    var tempDir = Path.Combine(Path.GetTempPath(), "photonium_zmx");
                    Directory.CreateDirectory(tempDir);
                    var filepath = Path.Combine(tempDir, filename);
                    
                    using (var wc = new WebClient())
                    {
                        wc.DownloadFile(url, filepath);
                    }
                    
                    bool ok = _sys.LoadFile(filepath, false);
                    if (!ok) throw new Exception("Failed to load file");
                    
                    SendJson(ctx, 200, new { ok = true, loaded = filepath });
                }
                catch (Exception ex)
                {
                    SendJson(ctx, 500, new { ok = false, error = ex.Message });
                }
                return;
            }

            // 404
            SendJson(ctx, 404, new { ok = false, error = "Not found" });
        }

        static void LoadZemaxIfNeeded()
        {
            if (_app != null) return;
            
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
                        _sys = _app.PrimarySystem;
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
                        _sys = _app.PrimarySystem;
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
                    _app = conn.CreateNewApplication();
                    _sys = _app.PrimarySystem;
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
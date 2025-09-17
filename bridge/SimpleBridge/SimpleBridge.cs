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
        static Assembly _zosApi;
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
                @"C:\Program Files\Zemax OpticStudio",
                @"C:\Program Files\Zemax OpticStudio 2024",
                @"C:\Program Files\Zemax OpticStudio 2023",
                @"C:\Program Files\Ansys\Zemax OpticStudio",
                @"C:\Program Files\ZEMAX13"
            };
            
            foreach (string path in commonPaths)
            {
                if (Directory.Exists(Path.Combine(path, "ZOS-API Assemblies")))
                {
                    Console.WriteLine("Found Zemax at: " + path);
                    return path;
                }
            }
            
            throw new Exception("Could not find Zemax. Please edit config.txt and set ZEMAX_PATH to your installation folder.");
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

            // POST /scan_path - helps find Zemax installation
            if (req.HttpMethod == "POST" && path == "/scan_path")
            {
                try
                {
                    var body = new StreamReader(req.InputStream).ReadToEnd();
                    dynamic data = _json.DeserializeObject(body);
                    string scanPath = data["path"];
                    
                    var result = new System.Collections.Generic.Dictionary<string, object>();
                    result["path"] = scanPath;
                    result["exists"] = Directory.Exists(scanPath);
                    
                    if (Directory.Exists(scanPath))
                    {
                        // List subdirectories
                        var dirs = Directory.GetDirectories(scanPath).Select(d => Path.GetFileName(d)).ToArray();
                        result["directories"] = dirs;
                        
                        // Look for DLL files
                        var dlls = Directory.GetFiles(scanPath, "*.dll", SearchOption.TopDirectoryOnly)
                            .Select(f => Path.GetFileName(f)).ToArray();
                        result["dlls"] = dlls;
                        
                        // Check for API in subdirectories
                        var apiDirs = new System.Collections.Generic.List<string>();
                        var apiFiles = new System.Collections.Generic.List<string>();
                        
                        // Look for API files in current directory
                        string[] apiFilePatterns = new string[] {
                            "ZOSAPI*.dll", "ZOS-API*.dll", "*OpticStudio*.dll", 
                            "Ansys.Zemax*.dll", "*Zemax*.dll"
                        };
                        
                        foreach (string pattern in apiFilePatterns)
                        {
                            var files = Directory.GetFiles(scanPath, pattern, SearchOption.TopDirectoryOnly);
                            apiFiles.AddRange(files.Select(f => Path.GetFileName(f)));
                        }
                        
                        // Check subdirectories
                        foreach (string dir in Directory.GetDirectories(scanPath))
                        {
                            bool foundInDir = false;
                            foreach (string pattern in apiFilePatterns)
                            {
                                if (Directory.GetFiles(dir, pattern).Length > 0)
                                {
                                    foundInDir = true;
                                    break;
                                }
                            }
                            if (foundInDir)
                            {
                                apiDirs.Add(Path.GetFileName(dir));
                            }
                        }
                        
                        result["api_found_in"] = apiDirs.ToArray();
                        result["api_files"] = apiFiles.Distinct().ToArray();
                    }
                    
                    SendJson(ctx, 200, result);
                }
                catch (Exception ex)
                {
                    SendJson(ctx, 500, new { ok = false, error = ex.Message });
                }
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
                    SendJson(ctx, 500, new { ok = false, error = ex.Message, details = ex.ToString() });
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
                    
                    // Check for API assemblies in various possible locations
                    string[] possibleApiPaths = new string[] {
                        Path.Combine(zemaxPath, "ZOS-API Assemblies"),
                        Path.Combine(zemaxPath, "ZOS-API"),
                        Path.Combine(zemaxPath, "API"),
                        zemaxPath // Sometimes DLLs are in root
                    };
                    
                    bool foundApi = false;
                    foreach (string apiPath in possibleApiPaths)
                    {
                        if (Directory.Exists(apiPath) && 
                            (File.Exists(Path.Combine(apiPath, "ZOSAPI.dll")) || 
                             File.Exists(Path.Combine(apiPath, "ZOSAPI_NetHelper.dll"))))
                        {
                            foundApi = true;
                            break;
                        }
                    }
                    
                    if (!foundApi)
                    {
                        // List what we did find to help user
                        string[] files = Directory.GetFiles(zemaxPath, "*.dll", SearchOption.TopDirectoryOnly);
                        string[] dirs = Directory.GetDirectories(zemaxPath);
                        string found = "Found dirs: " + string.Join(", ", dirs.Select(d => Path.GetFileName(d)).Take(5));
                        SendJson(ctx, 400, new { ok = false, error = "No API files found. " + found });
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
            
            // Try to find and load Zemax assemblies at runtime
            string zemaxDir = GetZemaxPath();
            
            // Try to find the API assemblies in various locations
            string[] possibleApiPaths = new string[] {
                Path.Combine(zemaxDir, "ZOS-API Assemblies"),
                Path.Combine(zemaxDir, "ZOS-API"),
                Path.Combine(zemaxDir, "API"),
                zemaxDir
            };
            
            string asmDir = null;
            foreach (string path in possibleApiPaths)
            {
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "ZOSAPI_NetHelper.dll")))
                {
                    asmDir = path;
                    Console.WriteLine("Found API assemblies at: " + asmDir);
                    break;
                }
            }
            
            if (asmDir == null)
                throw new Exception("Could not find ZOS-API assemblies in " + zemaxDir);
            
            // Load the NetHelper assembly
            var helperPath = Path.Combine(asmDir, "ZOSAPI_NetHelper.dll");
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
                // Some versions might need parameters
                var parameters = initMethod.GetParameters();
                if (parameters.Length == 0)
                {
                    initialized = (bool)initMethod.Invoke(null, null);
                }
                else if (parameters.Length == 1)
                {
                    // Might need a path parameter
                    initialized = (bool)initMethod.Invoke(null, new object[] { zemaxDir });
                }
                else
                {
                    throw new Exception("Initialize method has " + parameters.Length + " parameters, don't know how to call it");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to call Initialize: " + ex.Message);
            }
            
            if (!initialized)
                throw new Exception("ZOSAPI_Initializer.Initialize() returned false");
            
            // Load main API assembly
            var apiPath = Path.Combine(asmDir, "ZOSAPI.dll");
            _zosApi = Assembly.LoadFrom(apiPath);
            
            // Create connection and app
            var connType = _zosApi.GetType("ZOSAPI.ZOSAPI_Connection");
            dynamic conn = Activator.CreateInstance(connType);
            _app = conn.CreateNewApplication();
            
            if (_app == null || !_app.IsValidLicenseForAPI)
                throw new Exception("No valid Zemax license");
                
            _sys = _app.PrimarySystem;
            Console.WriteLine("Zemax API loaded successfully");
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
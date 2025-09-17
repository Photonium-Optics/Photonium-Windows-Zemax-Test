// Program.cs — Photonium.Zemax.Bridge (x64, .NET Framework 4.8)
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using ZOSAPI;
using ZOSAPI_Interfaces;
using ZOSAPI_NetHelper;

namespace Photonium.Zemax.Bridge
{
    class Program
    {
        static HttpListener _http;
        static IZOSAPI_Application _app;
        static IOpticalSystem _sys;
        static JavaScriptSerializer _json = new JavaScriptSerializer();
        static string _origin = Environment.GetEnvironmentVariable("PHOTONIUM_ORIGIN") ?? "*";

        static void Main(string[] args)
        {
            // Handle custom protocol, e.g. photonium-zemax://open?url=...
            if (args.Length > 0 && args[0].StartsWith("photonium-zemax://", StringComparison.OrdinalIgnoreCase))
            {
                HandleCustomProtocol(args[0]);
                return;
            }

            // Locate OpticStudio & ZOS-API
            if (!ZOSAPI_Initializer.Initialize())
            {
                Console.Error.WriteLine("ZOSAPI_Initializer.Initialize() failed — OpticStudio not found.");
                return;
            }

            // Start HTTP server on localhost:8765
            _http = new HttpListener();
            _http.Prefixes.Add("http://127.0.0.1:8765/");
            
            try 
            { 
                _http.Start(); 
            }
            catch (HttpListenerException e)
            {
                Console.Error.WriteLine("Failed to start listener: " + e.Message);
                Console.Error.WriteLine("Run installer to reserve URL for non-admin access.");
                return;
            }
            
            Console.WriteLine("Photonium Zemax Bridge listening at http://127.0.0.1:8765/");
            Console.WriteLine("Press Ctrl+C to stop...");
            
            Loop();
        }

        static void Loop()
        {
            while (true)
            {
                var ctx = _http.GetContext();
                try 
                { 
                    Route(ctx); 
                }
                catch (Exception ex)
                {
                    RespondJson(ctx, (int)HttpStatusCode.InternalServerError, new { ok = false, error = ex.Message });
                }
            }
        }

        static void Route(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            
            // Handle CORS preflight
            if (req.HttpMethod == "OPTIONS")
            {
                SetCors(res);
                res.StatusCode = 200;
                // Private Network Access header for Chrome
                if (req.Headers["Access-Control-Request-Private-Network"] == "true")
                    res.AddHeader("Access-Control-Allow-Private-Network", "true");
                res.Close();
                return;
            }

            // GET /health
            if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/health")
            {
                SetCors(res);
                RespondJson(ctx, 200, new { ok = true });
                return;
            }

            // POST /start
            if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/start")
            {
                EnsureApp();
                RespondJson(ctx, 200, new
                {
                    ok = true,
                    mode = _app?.Mode.ToString(),
                    license_ok = _app != null && _app.IsValidLicenseForAPI
                });
                return;
            }

            // POST /open_url
            if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/open_url")
            {
                EnsureApp();
                
                string body = new StreamReader(req.InputStream, req.ContentEncoding).ReadToEnd();
                var data = _json.Deserialize<Dictionary<string, object>>(body);
                
                if (!data.ContainsKey("url")) 
                    throw new Exception("Missing 'url' parameter");
                    
                var url = data["url"].ToString();
                var filename = data.ContainsKey("filename") 
                    ? data["filename"].ToString() 
                    : Path.GetFileName(url.Split('?')[0]);
                    
                if (string.IsNullOrWhiteSpace(filename)) 
                    filename = "download.zmx";
                    
                if (!filename.EndsWith(".zmx", StringComparison.OrdinalIgnoreCase) &&
                    !filename.EndsWith(".zos", StringComparison.OrdinalIgnoreCase))
                    filename += ".zmx";

                var tempDir = Path.Combine(Path.GetTempPath(), "photonium_zmx");
                Directory.CreateDirectory(tempDir);
                var fpath = Path.Combine(tempDir, filename);

                using (var wc = new WebClient())
                {
                    wc.DownloadFile(url, fpath);
                }

                bool ok = _sys.LoadFile(fpath, false);
                if (!ok) 
                    throw new Exception("OpticStudio failed to load file: " + fpath);

                RespondJson(ctx, 200, new { ok = true, loaded = fpath });
                return;
            }

            // POST /shutdown
            if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/shutdown")
            {
                if (_app != null)
                {
                    _app.CloseApplication();
                    _app = null;
                    _sys = null;
                }
                RespondJson(ctx, 200, new { ok = true });
                return;
            }

            // 404
            RespondJson(ctx, 404, new { ok = false, error = "Not found" });
        }

        static void EnsureApp()
        {
            if (_app != null) return;
            
            var conn = new ZOSAPI_Connection();
            _app = conn.CreateNewApplication(); // Standalone server mode
            
            if (_app == null || !_app.IsValidLicenseForAPI)
                throw new Exception("No valid OpticStudio API license/session.");
                
            if (_app.Mode != ZOSAPI_Mode.Server)
                throw new Exception("Expected Standalone (Server) mode, got: " + _app.Mode);
                
            _sys = _app.PrimarySystem;
        }

        static void SetCors(HttpListenerResponse res)
        {
            res.Headers["Access-Control-Allow-Origin"] = _origin == "*" ? "*" : _origin;
            res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            res.Headers["Access-Control-Allow-Headers"] = "content-type";
        }

        static void RespondJson(HttpListenerContext ctx, int status, object obj)
        {
            SetCors(ctx.Response);
            var json = _json.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        static void HandleCustomProtocol(string uri)
        {
            // Example: photonium-zemax://open?url=https%3A%2F%2Fexample.com%2FDoubleGauss.zmx
            var u = new Uri(uri.Replace("photonium-zemax://", "http://dummy/"));
            var q = System.Web.HttpUtility.ParseQueryString(u.Query);
            var action = u.AbsolutePath.Trim('/');
            
            if (!ZOSAPI_Initializer.Initialize())
                throw new Exception("ZOSAPI init failed.");

            if (action.Equals("start", StringComparison.OrdinalIgnoreCase))
            {
                EnsureApp();
                Console.WriteLine("Started OpticStudio via protocol handler");
                return;
            }
            
            if (action.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                EnsureApp();
                var url = q.Get("url");
                if (string.IsNullOrEmpty(url)) 
                    throw new Exception("Missing url parameter");
                    
                var name = q.Get("filename") ?? Path.GetFileName(url.Split('?')[0]);
                if (string.IsNullOrEmpty(name)) 
                    name = "download.zmx";
                    
                if (!name.EndsWith(".zmx", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".zos", StringComparison.OrdinalIgnoreCase)) 
                    name += ".zmx";

                var tempDir = Path.Combine(Path.GetTempPath(), "photonium_zmx");
                Directory.CreateDirectory(tempDir);
                var fpath = Path.Combine(tempDir, name);
                
                using (var wc = new WebClient()) 
                    wc.DownloadFile(url, fpath);
                    
                if (!_sys.LoadFile(fpath, false)) 
                    throw new Exception("LoadFile failed.");
                    
                Console.WriteLine("Loaded file via protocol handler: " + fpath);
                return;
            }
        }
    }
}
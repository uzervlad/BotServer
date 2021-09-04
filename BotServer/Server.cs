using System;
using System.Net;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BotServer
{
    class Server
    {
        private HttpListener listener = new HttpListener();

        public delegate Task<string> EndpointCallback(HttpListenerRequest request, HttpListenerResponse response);
        private Dictionary<string, EndpointCallback> endpoints = new Dictionary<string, EndpointCallback>();

        private App app;

        public Server(App app, int port)
        {
            this.app = app;
            Console.WriteLine($"Listening on port {port}");
            if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                listener.Prefixes.Add($"http://*:{port}/");
            } else {
                listener.Prefixes.Add($"http://localhost:{port}/");
            }
            listener.Start();
            Listen();
        }

        void Listen()
        {
            var l = new Thread(new ThreadStart(Listener));

            l.Start();
        }

        public void Close()
        {
            listener.Close();
        }

        private void Listener()
        {
            while(true)
            {
                try
                {
                    var context = listener.GetContextAsync().GetAwaiter().GetResult();
                    ProcessRequest(context);
                } catch(System.Net.HttpListenerException) {}
            }
        }

        private async void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;
                var path = request.Url.AbsolutePath;

                if(endpoints.ContainsKey(path))
                {
                    try
                    {
                        var result = await endpoints.GetValueOrDefault(path)(request, response);
                        byte[] data = Encoding.UTF8.GetBytes(result);
                        response.ContentType = "application/json";
                        response.OutputStream.Write(data);
                    }
                    catch(Exception e)
                    {
                        var query = Helpers.ParseQueryString(request.QueryString);

                        var result = JsonConvert.SerializeObject(new
                        {
                            error = e.Message,
                            data = query
                        });
                        byte[] data = Encoding.UTF8.GetBytes(result);
                        response.ContentType = "application/json";
                        response.OutputStream.Write(data);
                    }
                }

                response.Close();
            }
            catch(HttpListenerException) {}
        }

        public void AddEndpoint(string endpoint, EndpointCallback callback)
        {
            endpoints.Add(endpoint, callback);
        }
    }
}
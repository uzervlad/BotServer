using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BotServer
{
    class Server
    {
        private HttpListener listener = new HttpListener();

        public delegate string EndpointCallback(HttpListenerRequest request, HttpListenerResponse response);
        private Dictionary<string, EndpointCallback> endpoints = new Dictionary<string, EndpointCallback>();

        private App app;

        public Server(App app, int port)
        {
            this.app = app;
            Console.WriteLine($"Listening on port {port}");
            listener.Prefixes.Add($"http://*:{port}/");
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
                try {
                    var context = listener.GetContextAsync().GetAwaiter().GetResult();
                    var request = context.Request;
                    var response = context.Response;

                    var path = request.Url.AbsolutePath;

                    if(endpoints.ContainsKey(path))
                    {
                        var result = endpoints.GetValueOrDefault(path)(request, response);
                        byte[] data = Encoding.UTF8.GetBytes(result);
                        response.ContentType = "application/json";
                        response.OutputStream.Write(data);
                    }

                    response.Close();
                } catch(System.Net.HttpListenerException) {}
            }
        }

        public void AddEndpoint(string endpoint, EndpointCallback callback)
        {
            endpoints.Add(endpoint, callback);
        }
    }
}
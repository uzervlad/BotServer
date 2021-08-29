using System;

namespace BotServer
{
    class AppConsole
    {
        private App app;

        public AppConsole(App a)
        {
            app = a;
        }

        public void ThreadRunner()
        {
            while(true)
            {
                var cmd = Console.ReadLine();
                switch(cmd)
                {
                    case "":
                        break;
                    case "maps":
                        Console.WriteLine($"[*] {app.cache.Count} maps in cache");
                        break;
                    case "stop":
                    case "exit":
                    case "close":
                        Console.WriteLine("[~] Stopping...");
                        app.Stop();
                        break;
                    default:
                        Console.WriteLine("[!] Unknown command");
                        break;
                }
            }
        }
    }
}
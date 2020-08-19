using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Collections.Generic;
using BotServer.PPCalculator;

namespace BotServer
{
    class MapCache
    {
        private List<ExpirableMap> maps = new List<ExpirableMap>();

        public int Count {
            get {
                return maps.Count;
            }
        }

        public MapCache()
        {
            Thread cleaner = new Thread(new ThreadStart(CleanerThread));
            cleaner.Start();
        }

        public WorkingBeatmap GetBeatmap(int ID)
        {
            if(maps.Exists(map => map.ID == ID))
            {
                var map = maps.Find(map => map.ID == ID);
                if(map.expired) // Map has expired, redownload
                    return DownloadMap(ID);
                else // Map hasn't expired, return it
                    return map.map;
            }
            else
                return DownloadMap(ID);
        }

        private WorkingBeatmap DownloadMap(int ID)
        {
            using(var client = new WebClient())
                client.DownloadFile($"https://osu.ppy.sh/osu/{ID}", $"{ID}.osu");

            var path = Directory.GetCurrentDirectory();

            var map = new WorkingBeatmap($"{path}/{ID}.osu");
            maps.Add(new ExpirableMap { map = map, ID = ID });

            File.Delete($"{path}/{ID}.osu");

            return map;
        }

        private void CleanerThread()
        {
            while(true)
            {
                maps.RemoveAll(map => map.expired);
                Thread.Sleep(5000);
            }
        }

        private class ExpirableMap
        {
            public WorkingBeatmap map;
            public int ID;
            private DateTime ExpiresAt = DateTime.Now.AddMinutes(5);

            public bool expired {
                get {
                    return DateTime.Now >= ExpiresAt;
                }
            }
        }
    }
}
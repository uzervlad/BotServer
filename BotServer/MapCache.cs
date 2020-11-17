using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Collections.Generic;
using BotServer.PPCalculator;
using Newtonsoft.Json;

namespace BotServer
{
    class MapCache
    {
        public string Token { get; set; }

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

        public ExpirableMap GetBeatmap(int ID)
        {
            if(maps.Exists(map => map.ID == ID))
            {
                var map = maps.Find(map => map.ID == ID);
                if(map.expired) // Map has expired, redownload
                    return DownloadMap(ID);
                else // Map hasn't expired, return it
                    return map;
            }
            else
                return DownloadMap(ID);
        }

        private ExpirableMap DownloadMap(int ID)
        {
            using(var client = new WebClient())
                client.DownloadFile($"https://osu.ppy.sh/osu/{ID}", $"{ID}.osu");

            var path = Directory.GetCurrentDirectory();

            var map = new WorkingBeatmap($"{path}/{ID}.osu");
            var expirable = new ExpirableMap { 
                map = map, 
                ID = ID,
                SetID = GetBeatmapsetID(ID)
            };
            maps.Add(expirable);

            File.Delete($"{path}/{ID}.osu");

            return expirable;
        }

        private int GetBeatmapsetID(int ID)
        {
            using(var client = new WebClient())
            {
                var byte_data = client.DownloadData($"https://osu.ppy.sh/api/get_beatmaps?k={Token}&b={ID}");
                var data = JsonConvert.DeserializeObject<APIBeatmap[]>(System.Text.Encoding.Default.GetString(byte_data));
                try {
                    return Int32.Parse(data[0].SetID);
                } catch {
                    return 0;
                }
            }
        }

        private void CleanerThread()
        {
            while(true)
            {
                maps.RemoveAll(map => map.expired);
                Thread.Sleep(5000);
            }
        }

        public class ExpirableMap
        {
            public WorkingBeatmap map;
            public int ID;
            public int SetID;
            private DateTime ExpiresAt = DateTime.Now.AddMinutes(5);

            public bool expired {
                get {
                    return DateTime.Now >= ExpiresAt;
                }
            }
        }

        private class APIBeatmap
        {
            [JsonProperty("beatmapset_id")]
            public string SetID = "0";
        }
    }
}
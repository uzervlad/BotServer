using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using BotServer.PPCalculator;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Net.Http;

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

        public async Task<ExpirableMap> GetBeatmap(int ID)
        {
            if(maps.Exists(map => map.ID == ID))
            {
                var map = maps.Find(map => map.ID == ID);
                if(map.expired) // Map has expired, redownload
                    return await DownloadMap(ID);
                else // Map hasn't expired, return it
                    return map;
            }
            else
                return await DownloadMap(ID);
        }

        private async Task<ExpirableMap> DownloadMap(int ID)
        {
            using(var client = new HttpClient())
            {
                try {
                    var mapStream = await client.GetStreamAsync($"https://osu.ppy.sh/osu/{ID}");
                    var map = new WorkingBeatmap(mapStream);
                    var expirable = new ExpirableMap
                    {
                        map = map, ID = ID,
                        apiMap = await GetAPIBeatmap(ID)
                    };
                    
                    maps.Add(expirable);

                    return expirable;
                } catch(Exception) {
                    return null;
                }
            }
        }

        public async Task<APIBeatmap[]> GetAPIBeatmapset(int SetID)
        {
            using(var client = new HttpClient())
            {
                var raw_data = await client.GetStringAsync($"https://osu.ppy.sh/api/get_beatmaps?k={Token}&s={SetID}");
                var data = JsonConvert.DeserializeObject<APIBeatmap[]>(raw_data);
                return data;
            }
        }

        public async Task<APIBeatmap> GetAPIBeatmap(int ID)
        {
            using(var client = new HttpClient())
            {
                var raw_data = await client.GetStringAsync($"https://osu.ppy.sh/api/get_beatmaps?k={Token}&b={ID}");
                var data = JsonConvert.DeserializeObject<APIBeatmap[]>(raw_data);
                try {
                    return data[0];
                } catch {
                    return null;
                }
            }
        }

        public async Task<APIBeatmap> GetAPIBeatmap(string Hash)
        {
            using(var client = new HttpClient())
            {
                var raw_data = await client.GetStringAsync($"https://osu.ppy.sh/api/get_beatmaps?k={Token}&h={Hash}");
                var data = JsonConvert.DeserializeObject<APIBeatmap[]>(raw_data);
                try {
                    return data[0];
                } catch {
                    return null;
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
            public APIBeatmap apiMap;
            public int ID;
            private DateTime ExpiresAt = DateTime.Now.AddMinutes(5);

            public bool expired {
                get {
                    return DateTime.Now >= ExpiresAt;
                }
            }
        }

        public class APIBeatmap
        {
            [JsonProperty("title")]
            public string Title;

            [JsonProperty("artist")]
            public string Artist;

            [JsonProperty("version")]
            public string Version;

            [JsonProperty("creator")]
            public string Creator;

            [JsonProperty("beatmapset_id")]
            public int SetID = 0;

            [JsonProperty("beatmap_id")]
            public int ID = 0;

            [JsonProperty("approved")]
            public BeatmapStatus Status = BeatmapStatus.Graveyard;

            [JsonProperty("difficultyrating")]
            public float Stars = 0;
        }
    }

    public enum BeatmapStatus {
        Graveyard = -2,
        WIP = -1,
        Pending = 0,
        Ranked = 1,
        Approved = 2,
        Qualified = 3,
        Loved = 4
    }
}
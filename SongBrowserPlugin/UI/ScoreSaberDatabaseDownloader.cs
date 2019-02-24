using Mobcast.Coffee.AssetSystem;
using SongBrowserPlugin.DataAccess;
using SongBrowserPlugin.DataAccess.Network;
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Logger = SongBrowserPlugin.Logging.Logger;

using System.Security.Cryptography;

namespace SongBrowserPlugin.UI
{
    public class ScoreSaberDatabaseDownloader : MonoBehaviour
    {
        public const String SCRAPED_SCORE_SABER_JSON_URL = "https://wes.ams3.digitaloceanspaces.com/beatstar/bssb.json";

        public static ScoreSaberDatabaseDownloader Instance;

        public static ScoreSaberDataFile ScoreSaberDataFile = null;        

        public Action onScoreSaberDataDownloaded;

        // 4MB buffer
        private readonly byte[] _buffer = new byte[4 * 1048576];

        /// <summary>
        /// Awake.
        /// </summary>
        private void Awake()
        {
            Logger.Trace("Awake()");

            if (Instance == null)
            {
                Instance = this;
            }
        }

        /// <summary>
        /// Acquire any UI elements from Beat saber that we need.  Wait for the song list to be loaded.
        /// </summary>
        public void Start()
        {
            Logger.Trace("Start()");

            StartCoroutine(WaitForDownload());
        }

        private void DownloadScoreSaberData()
        {

        }


        private string zdate(int i)
        {
            return zdate(i.ToString());
        }
        private string zdate(string i)
        {
            return i.Length < 2 ? "0" + i : i;
        }

        /// <summary>
        /// Wait for score saber related files to download.
        /// </summary>
        /// <returns></returns>

        private IEnumerator WaitForDownload()
        {
            var cachePath = "./pp.json";
            string cacheHash = null;
            DateTime? cacheTime = null;
            try
            {
                var cacheBytes = File.ReadAllText(cachePath);
                cacheHash = HashHelper.md5(System.Text.Encoding.ASCII.GetBytes(cacheBytes));
                cacheTime = File.GetLastWriteTimeUtc(cachePath);
                Logger.Info("Found cache copy of BeatStar, {0}, {1}", cacheHash, cacheTime);
            }
            catch (FileNotFoundException e)
            {
                Logger.Info("No cached copy of BeatStar found");
            }
            catch (Exception e)
            {
                Logger.Error("Cache exception");
            }

            if (ScoreSaberDatabaseDownloader.ScoreSaberDataFile != null)
            {
                Logger.Info("Using cached copy of BeatStar...");
            }
            else
            {
                SongBrowserApplication.MainProgressBar.ShowMessage("Downloading BeatStar data...");

                Logger.Info("Attempting to download: {0}", ScoreSaberDatabaseDownloader.SCRAPED_SCORE_SABER_JSON_URL);
                using (UnityWebRequest www = UnityWebRequest.Get(ScoreSaberDatabaseDownloader.SCRAPED_SCORE_SABER_JSON_URL))
                {
                    // Use 4MB cache, large enough for this file to grow for awhile.
                    www.SetCacheable(new CacheableDownloadHandlerScoreSaberData(www, _buffer));
                    
                    if (!string.IsNullOrEmpty(cacheHash) && cacheTime.HasValue)
                    {
                        www.SetRequestHeader("If-None-Match", cacheHash.ToLower());

                        var time = cacheTime.Value;
                        var dayStrings = new string[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
                        var monthStrings = new string[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
                        var timeString = string.Format("{0}, {1} {2} {3} {4}:{5}:{6} GMT", dayStrings[(int)time.DayOfWeek], zdate(time.Day), monthStrings[time.Month], time.Year, zdate(time.Hour), zdate(time.Minute), zdate(time.Second));
                        Logger.Info(timeString);
                        www.SetRequestHeader("If-Modified-Since", timeString);
                    }

                    //www.SetRequestHeader("", "");
                    yield return www.SendWebRequest();

                    Logger.Debug("Returned from web request!...");
                    Logger.Info("Returned from BeatStar request with code {0} and {1} bytes", www.responseCode, www.downloadedBytes);
                    try
                    {
                        ScoreSaberDatabaseDownloader.ScoreSaberDataFile = (www.downloadHandler as CacheableDownloadHandlerScoreSaberData).ScoreSaberDataFile;
                        Logger.Info("Success downloading BeatStar data!");
                        Logger.Info("22Returned from BeatStar request with code {0} and {1} bytes", www.responseCode, www.downloadedBytes);
                        File.WriteAllText(cachePath, ScoreSaberDataFile.DataString.Trim());
                        SongBrowserApplication.MainProgressBar.ShowMessage("Success downloading BeatStar data...", 10.0f);
                        onScoreSaberDataDownloaded?.Invoke();
                    }
                    catch (System.InvalidOperationException)
                    {
                        Logger.Error("Failed to download BeatStar data file...");
                    }
                    catch (Exception e)
                    {
                        Logger.Exception("Exception trying to download BeatStar data file...", e);
                    }
                }
            }
        }
    }
}

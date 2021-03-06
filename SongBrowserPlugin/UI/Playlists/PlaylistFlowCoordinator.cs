﻿using CustomUI.BeatSaber;
using SimpleJSON;
using SongBrowserPlugin.DataAccess;
using SongBrowserPlugin.DataAccess.BeatSaverApi;
using SongBrowserPlugin.UI.DownloadQueue;
using SongLoaderPlugin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using VRUI;
using Logger = SongBrowserPlugin.Logging.Logger;

namespace SongBrowserPlugin.UI
{
    public class PlaylistFlowCoordinator : FlowCoordinator
    {
        public event Action<Playlist> didFinishEvent;

        public FlowCoordinator ParentFlowCoordinator;

        private BackButtonNavigationController _playlistsNavigationController;
        private PlaylistListViewController _playlistListViewController;
        private PlaylistDetailViewController _playlistDetailViewController;
        private DownloadQueueViewController _downloadQueueViewController;

        private bool _downloadingPlaylist;

        private PlaylistsReader _playlistsReader;
        private Playlist _lastPlaylist;

        public void Awake()
        {
            if (_playlistsNavigationController == null)
            {
                _playlistsNavigationController = BeatSaberUI.CreateViewController<BackButtonNavigationController>();

                GameObject _playlistDetailGameObject = Instantiate(Resources.FindObjectsOfTypeAll<StandardLevelDetailViewController>().First(), _playlistsNavigationController.rectTransform, false).gameObject;
                _playlistDetailViewController = _playlistDetailGameObject.AddComponent<PlaylistDetailViewController>();
                Destroy(_playlistDetailGameObject.GetComponent<StandardLevelDetailViewController>());
                _playlistDetailViewController.name = "PlaylistDetailViewController";
            }
        }

        protected override void DidActivate(bool firstActivation, ActivationType activationType)
        {
            try
            {
                if (firstActivation && activationType == ActivationType.AddedToHierarchy)
                {
                    title = "Playlists";

                    if (_playlistsReader == null)
                    {
                        _playlistsReader = new PlaylistsReader();
                        _playlistsReader.UpdatePlaylists();
                        Logger.Debug("Reader found {0} playlists!", _playlistsReader.Playlists.Count);

                        this.MatchSongsForAllPlaylists(true);
                    }

                    _playlistsNavigationController.didFinishEvent += _playlistsNavigationController_didFinishEvent;

                    _playlistListViewController = BeatSaberUI.CreateViewController<PlaylistListViewController>();
                    _playlistListViewController.didSelectRow += _playlistListViewController_didSelectRow;

                    _playlistDetailViewController.downloadButtonPressed += _playlistDetailViewController_downloadButtonPressed;
                    _playlistDetailViewController.selectButtonPressed += _playlistDetailViewController_selectButtonPressed;

                    _downloadQueueViewController = BeatSaberUI.CreateViewController<DownloadQueueViewController>();

                    SetViewControllersToNavigationConctroller(_playlistsNavigationController, new VRUIViewController[]
                    {
                        _playlistListViewController
                    });

                    ProvideInitialViewControllers(_playlistsNavigationController, _downloadQueueViewController, null);
                }
                _downloadingPlaylist = false;
                _playlistListViewController.SetContent(_playlistsReader.Playlists);

                _downloadQueueViewController.allSongsDownloaded += _downloadQueueViewController_allSongsDownloaded;
            }
            catch (Exception e)
            {
                Logger.Exception("Error activating playlist flow coordinator: ", e);
            }
        }

        protected override void DidDeactivate(DeactivationType deactivationType)
        {
            _downloadQueueViewController.allSongsDownloaded -= _downloadQueueViewController_allSongsDownloaded;

        }

        private void _downloadQueueViewController_allSongsDownloaded()
        {
            SongLoader.Instance.RefreshSongs(false);
            _downloadingPlaylist = false;
        }

        private void _playlistListViewController_didSelectRow(Playlist playlist)
        {
            if (!_playlistDetailViewController.isInViewControllerHierarchy)
            {
                PushViewControllerToNavigationController(_playlistsNavigationController, _playlistDetailViewController);
            }

            _lastPlaylist = playlist;
            _playlistDetailViewController.SetContent(playlist);
        }

        private void _playlistDetailViewController_selectButtonPressed(Playlist playlist)
        {
            if (!_downloadQueueViewController.queuedSongs.Any(x => x.songQueueState == SongQueueState.Downloading || x.songQueueState == SongQueueState.Queued))
            {
                if (_playlistsNavigationController.viewControllers.IndexOf(_playlistDetailViewController) >= 0)
                {
                    PopViewControllerFromNavigationController(_playlistsNavigationController, null, true);
                }

                ParentFlowCoordinator.InvokePrivateMethod("DismissFlowCoordinator", new object[] { this, null, false });
                didFinishEvent?.Invoke(playlist);
            }
        }

        private void _playlistDetailViewController_downloadButtonPressed(Playlist playlist)
        {
            if (!_downloadingPlaylist)
                StartCoroutine(DownloadPlaylist(playlist));
        }

        public IEnumerator DownloadPlaylist(Playlist playlist)
        {
            PlaylistFlowCoordinator.MatchSongsForPlaylist(playlist, true);

            List<PlaylistSong> needToDownload = playlist.Songs.Where(x => x.Level == null).ToList();
            Logger.Info($"Need to download {needToDownload.Count} songs");

            _downloadingPlaylist = true;
            foreach (var item in needToDownload)
            {
                Song beatSaverSong = null;

                if (String.IsNullOrEmpty(playlist.CustomArchiveUrl))
                {
                    Logger.Info("Obtaining hash and url for " + item.Key + ": " + item.SongName);
                    yield return GetInfoForSong(playlist, item, (Song song) => { beatSaverSong = song; });
                }
                else
                {
                    string archiveUrl = playlist.CustomArchiveUrl.Replace("[KEY]", item.Key);

                    beatSaverSong = new Song()
                    {
                        songName = item.SongName,
                        id = item.Key,
                        downloadingProgress = 0f,
                        hash = (item.LevelId == null ? "" : item.LevelId),
                        downloadUrl = archiveUrl
                    };
                }

                if (beatSaverSong != null && !SongLoader.CustomLevels.Any(x => x.levelID.Substring(0, 32) == beatSaverSong.hash.ToUpper()))
                {
                    _downloadQueueViewController.EnqueueSong(beatSaverSong, true);
                }
            }
            _downloadingPlaylist = false;
        }

        public IEnumerator GetInfoForSong(Playlist playlist, PlaylistSong song, Action<Song> songCallback)
        {
            string url = "";
            bool _usingHash = false;
            if (!string.IsNullOrEmpty(song.Key))
            {
                url = $"{PluginConfig.BeatsaverURL}/api/songs/detail/{song.Key}";
                if (!string.IsNullOrEmpty(playlist.CustomDetailUrl))
                {
                    url = playlist.CustomDetailUrl + song.Key;
                }
            }
            else if (!string.IsNullOrEmpty(song.Hash))
            {
                url = $"{PluginConfig.BeatsaverURL}/api/songs/search/hash/{song.Hash}";
                _usingHash = true;
            }
            else if (!string.IsNullOrEmpty(song.LevelId))
            {
                string hash = CustomHelpers.CheckHex(song.LevelId.Substring(0, Math.Min(32, song.LevelId.Length)));
                url = $"{PluginConfig.BeatsaverURL}/api/songs/search/hash/{hash}";
                _usingHash = true;
            }
            else
            {
                yield break;
            }

            UnityWebRequest www = UnityWebRequest.Get(url);
            www.timeout = 15;
            yield return www.SendWebRequest();

            if (www.isNetworkError || www.isHttpError)
            {
                Logger.Error($"Unable to connect to {PluginConfig.BeatsaverURL}! " + (www.isNetworkError ? $"Network error: {www.error}" : (www.isHttpError ? $"HTTP error: {www.error}" : "Unknown error")));
            }
            else
            {
                try
                {
                    JSONNode node = JSON.Parse(www.downloadHandler.text);

                    if (_usingHash)
                    {
                        if (node["songs"].Count == 0)
                        {
                            Logger.Error($"Song {song.SongName} doesn't exist on BeatSaver!");
                            songCallback?.Invoke(null);
                            yield break;
                        }
                        songCallback?.Invoke(Song.FromSearchNode(node["songs"][0]));
                    }
                    else
                    {
                        songCallback?.Invoke(new Song(node["song"]));
                    }
                }
                catch (Exception e)
                {
                    Logger.Exception("Unable to parse response! Exception: ", e);
                }
            }
        }

        private void _playlistsNavigationController_didFinishEvent()
        {
            if (!_downloadQueueViewController.queuedSongs.Any(x => x.songQueueState == SongQueueState.Downloading || x.songQueueState == SongQueueState.Queued))
            {
                if (_downloadQueueViewController.queuedSongs.Any(x => x.songQueueState == SongQueueState.Downloading || x.songQueueState == SongQueueState.Queued))
                    _downloadQueueViewController.AbortDownloads();

                if (_playlistsNavigationController.viewControllers.IndexOf(_playlistDetailViewController) >= 0)
                {
                    PopViewControllerFromNavigationController(_playlistsNavigationController, null, true);
                }

                ParentFlowCoordinator.InvokePrivateMethod("DismissFlowCoordinator", new object[] { this, null, false });
                didFinishEvent?.Invoke(null);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="playlist"></param>
        /// <param name="matchAll"></param>
        public static void MatchSongsForPlaylist(Playlist playlist, bool matchAll = false)
        {
            if (!SongLoader.AreSongsLoaded || SongLoader.AreSongsLoading || playlist.Title == "All songs" || playlist.Title == "Your favorite songs") return;
            Logger.Info("Started matching songs for playlist \"" + playlist.Title + "\"...");
            if (!playlist.Songs.All(x => x.Level != null) || matchAll)
            {
                Stopwatch execTime = new Stopwatch();
                execTime.Start();
                playlist.Songs.AsParallel().ForAll(x =>
                {
                    if (x.Level == null || matchAll)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(x.LevelId)) //check that we have levelId and if we do, try to match level
                            {
                                x.Level = SongLoader.CustomLevels.FirstOrDefault(y => y.levelID == x.LevelId);
                            }
                            if (x.Level == null && !string.IsNullOrEmpty(x.Hash)) //if level is still null, check that we have hash and if we do, try to match level
                            {
                                x.Level = SongLoader.CustomLevels.FirstOrDefault(y => y.levelID.StartsWith(x.Hash.ToUpper()));
                            }
                            if (x.Level == null && !string.IsNullOrEmpty(x.Key)) //if level is still null, check that we have key and if we do, try to match level
                            {
                                ScrappedSong song = ScrappedData.Songs.FirstOrDefault(z => z.Key == x.Key);
                                if (song != null)
                                {
                                    x.Level = SongLoader.CustomLevels.FirstOrDefault(y => y.levelID.StartsWith(song.Hash));
                                }
                                else
                                {
                                    x.Level = SongLoader.CustomLevels.FirstOrDefault(y => y.customSongInfo.path.Contains(x.Key));
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Warning($"Unable to match song with {(string.IsNullOrEmpty(x.Key) ? " unknown key!" : ("key " + x.Key + " !"))} Exception: {e}");
                        }
                    }
                });
                Logger.Info($"Matched all songs for playlist \"{playlist.Title}\"! Time: {execTime.Elapsed.TotalSeconds.ToString("0.00")}s");
                execTime.Reset();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="matchAll"></param>
        public void MatchSongsForAllPlaylists(bool matchAll = false)
        {
            Logger.Info("Matching songs for all playlists!");
            Task.Run(() =>
            {
                for (int i = 0; i < _playlistsReader.Playlists.Count; i++)
                {
                    MatchSongsForPlaylist(_playlistsReader.Playlists[i], matchAll);
                }
            });
        }

#if DEBUG
        /// <summary>
        /// Useful playlist navigation.
        /// Shift+Enter downloads.
        /// Enter selects.
        /// </summary>
        public void LateUpdate()
        {
            bool isShiftKeyDown = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (isShiftKeyDown && Input.GetKeyDown(KeyCode.Return))
            {
                _playlistDetailViewController_downloadButtonPressed(_lastPlaylist);
            }
            else if (Input.GetKeyDown(KeyCode.Return))
            {
                _playlistDetailViewController_selectButtonPressed(_lastPlaylist);
            }

            // leave
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _playlistsNavigationController_didFinishEvent();
            }
        }
#endif
    }
}

﻿using SongBrowserPlugin.DataAccess;
using SongLoaderPlugin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SongBrowserPlugin
{
    public class SongBrowserModel
    {
        public static String LastSelectedLevelId { get; set; }

        private Logger _log = new Logger("SongBrowserModel");
        
        private SongBrowserSettings _settings;

        private List<StandardLevelSO> _sortedSongs;
        private List<StandardLevelSO> _originalSongs;
        private Dictionary<String, SongLoaderPlugin.OverrideClasses.CustomLevel> _levelIdToCustomLevel;
        private SongLoaderPlugin.OverrideClasses.CustomLevelCollectionSO _gameplayModeCollection;    
        private Dictionary<String, double> _cachedLastWriteTimes;

        public bool InvertingResults { get; private set; }

        public SongBrowserSettings Settings
        {
            get
            {
                return _settings;
            }
        }

        public List<StandardLevelSO> SortedSongList
        {
            get
            {
                return _sortedSongs;
            }
        }

        public Dictionary<String, SongLoaderPlugin.OverrideClasses.CustomLevel> LevelIdToCustomSongInfos
        {
            get
            {
                return _levelIdToCustomLevel;
            }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public SongBrowserModel()
        {
            _cachedLastWriteTimes = new Dictionary<String, double>();
        }

        /// <summary>
        /// Init this model.
        /// </summary>
        /// <param name="songSelectionMasterView"></param>
        /// <param name="songListViewController"></param>
        public void Init()
        {
            _settings = SongBrowserSettings.Load();
            _log.Info("Settings loaded, sorting mode is: {0}", _settings.sortMode);
        }

        /// <summary>
        /// 
        /// </summary>
        public void ToggleInverting()
        {
            this.InvertingResults = !this.InvertingResults;
        }

        /// <summary>
        /// Get the song cache from the game.
        /// TODO: This might not even be necessary anymore.  Need to test interactions with BeatSaverDownloader.
        /// </summary>
        public void UpdateSongLists(GameplayMode gameplayMode)
        {
            String customSongsPath = Path.Combine(Environment.CurrentDirectory, "CustomSongs");
            String cachedSongsPath = Path.Combine(customSongsPath, ".cache");
            DateTime currentLastWriteTIme = File.GetLastWriteTimeUtc(customSongsPath);
            IEnumerable<string> directories = Directory.EnumerateDirectories(customSongsPath, "*.*", SearchOption.AllDirectories);

            // Get LastWriteTimes
            var Epoch = new DateTime(1970, 1, 1);
            foreach (string dir in directories)
            {
                // Flip slashes, match SongLoaderPlugin
                string slashed_dir = dir.Replace("\\", "/");

                //_log.Debug("Fetching LastWriteTime for {0}", slashed_dir);
                _cachedLastWriteTimes[slashed_dir] = (File.GetLastWriteTimeUtc(dir) - Epoch).TotalMilliseconds;
            }

            // Update song Infos
            this.UpdateSongInfos(gameplayMode);
                                
            this.ProcessSongList(gameplayMode);                       
        }

        /// <summary>
        /// Get the song infos from SongLoaderPluging
        /// </summary>
        private void UpdateSongInfos(GameplayMode gameplayMode)
        {
            _log.Trace("UpdateSongInfos for Gameplay Mode {0}", gameplayMode);

            SongLoaderPlugin.OverrideClasses.CustomLevelCollectionsForGameplayModes collections = SongLoaderPlugin.SongLoader.Instance.GetPrivateField<SongLoaderPlugin.OverrideClasses.CustomLevelCollectionsForGameplayModes>("_customLevelCollectionsForGameplayModes");
            _gameplayModeCollection = collections.GetCollection(gameplayMode) as SongLoaderPlugin.OverrideClasses.CustomLevelCollectionSO;
            _originalSongs = collections.GetLevels(gameplayMode).ToList();
            _sortedSongs = _originalSongs;
            _levelIdToCustomLevel = SongLoader.CustomLevels.ToDictionary(x => x.levelID, x => x);

            _log.Debug("Song Browser knows about {0} songs from SongLoader...", _sortedSongs.Count);
        }
        
        /// <summary>
        /// Sort the song list based on the settings.
        /// </summary>
        private void ProcessSongList(GameplayMode gameplayMode)
        {
            _log.Trace("ProcessSongList()");

            // Weights used for keeping the original songs in order
            // Invert the weights from the game so we can order by descending and make LINQ work with us...
            /*  Level4, Level2, Level9, Level5, Level10, Level6, Level7, Level1, Level3, Level8, Level11 */
            Dictionary<string, int> weights = new Dictionary<string, int>
            {
                ["Level4"] = 11,
                ["Level2"] = 10,
                ["Level9"] = 9,
                ["Level5"] = 8,
                ["Level10"] = 7,
                ["Level6"] = 6,
                ["Level7"] = 5,
                ["Level1"] = 4,
                ["Level3"] = 3,
                ["Level8"] = 2,
                ["Level11"] = 1
            };

            // This has come in handy many times for debugging issues with Newest.
            /*foreach (StandardLevelSO level in _originalSongs)
            {
                if (_levelIdToCustomLevel.ContainsKey(level.levelID))
                {
                    _log.Debug("HAS KEY {0}: {1}", _levelIdToCustomLevel[level.levelID].customSongInfo.path, level.levelID);
                }
                else
                {
                    _log.Debug("Missing KEY: {0}", level.levelID);
                }
            }*/

            PlayerDynamicData playerData = GameDataModel.instance.gameDynamicData.GetCurrentPlayerDynamicData();

            Stopwatch stopwatch = Stopwatch.StartNew();

            switch (_settings.sortMode)
            {
                case SongSortMode.Favorites:
                    _log.Info("Sorting song list as favorites");
                    _sortedSongs = _originalSongs
                        .AsQueryable()
                        .OrderBy(x => _settings.favorites.Contains(x.levelID) == false)
                        .ThenBy(x => x.songName)
                        .ThenBy(x => x.songAuthorName)
                        .ToList();
                    break;
                case SongSortMode.Original:
                    _log.Info("Sorting song list as original");
                    _sortedSongs = _originalSongs
                        .AsQueryable()
                        .OrderByDescending(x => weights.ContainsKey(x.levelID) ? weights[x.levelID] : 0)
                        .ThenBy(x => x.songName)
                        .ToList();
                    break;
                case SongSortMode.Newest:
                    _log.Info("Sorting song list as newest.");
                    _sortedSongs = _originalSongs
                        .AsQueryable()
                        .OrderBy(x => weights.ContainsKey(x.levelID) ? weights[x.levelID] : 0)
                        .ThenByDescending(x => x.levelID.StartsWith("Level") ? weights[x.levelID] : _cachedLastWriteTimes[_levelIdToCustomLevel[x.levelID].customSongInfo.path])
                        .ToList();
                    break;
                case SongSortMode.Author:
                    _log.Info("Sorting song list by author");
                    _sortedSongs = _originalSongs
                        .AsQueryable()
                        .OrderBy(x => x.songAuthorName)
                        .ThenBy(x => x.songName)
                        .ToList();
                    break;
                case SongSortMode.PlayCount:
                    _log.Info("Sorting song list by playcount");
                    // Build a map of levelId to sum of all playcounts and sort.
                    IEnumerable<LevelDifficulty> difficultyIterator = Enum.GetValues(typeof(LevelDifficulty)).Cast<LevelDifficulty>();
                    Dictionary<string, int> _levelIdToPlayCount = _originalSongs.ToDictionary(x => x.levelID, x => difficultyIterator.Sum(difficulty => playerData.GetPlayerLevelStatsData(x.levelID, difficulty, gameplayMode).playCount));
                    _sortedSongs = _originalSongs
                        .AsQueryable()
                        .OrderByDescending(x => _levelIdToPlayCount[x.levelID])
                        .ThenBy(x => x.songName)
                        .ToList();
                    break;
                case SongSortMode.Random:
                    _log.Info("Sorting song list by random");

                    System.Random rnd = new System.Random(Guid.NewGuid().GetHashCode());

                    _sortedSongs = _originalSongs
                        .AsQueryable()
                        .OrderBy(x => rnd.Next())
                        .ToList();
                    break;
                case SongSortMode.Default:
                default:
                    _log.Info("Sorting song list as default (songName)");
                    _sortedSongs = _originalSongs
                        .AsQueryable()
                        .OrderBy(x => x.songName)
                        .ThenBy(x => x.songAuthorName)
                        .ToList();
                    break;
            }

            if (this.InvertingResults && _settings.sortMode != SongSortMode.Random)
            {
                _sortedSongs.Reverse();
            }

            stopwatch.Stop();
            _log.Info("Sorting songs took {0}ms", stopwatch.ElapsedMilliseconds);
        }        
    }
}
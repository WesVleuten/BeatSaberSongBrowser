﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SongBrowserPlugin.DataAccess
{
    public class PlaylistSong
    {
        public String Key { get; set; }
        public String SongName { get; set; }
        public string Hash { get; set; }

        // Set by playlist downloading
        [NonSerialized]
        public string LevelId;
        [NonSerialized]
        public IBeatmapLevel Level;
        [NonSerialized]
        public bool OneSaber;
        [NonSerialized]
        public string Path;
    }
}

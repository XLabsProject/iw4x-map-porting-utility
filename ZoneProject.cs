﻿namespace MapPortingUtility
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.IO;
    using System.Threading.Tasks;
    using System.Diagnostics;

    public struct ZoneProject
    {
        public struct AdditionalFile
        {
            public string Path;
            public string Contents;
        }

        public string MapName => Map.Name;
        public string Source { private set; get; }

        public IReadOnlyCollection<AdditionalFile> AdditionalFiles => additionalFiles.AsReadOnly();

        public readonly IW3xportHelper.Map Map;
        public readonly string MapDataPath;

        private readonly List<AdditionalFile> additionalFiles;
        
        public ZoneProject(ref IW3xportHelper.Map map, ref Paths paths)
        {
            Map = map;
MapDataPath = IW3xportHelper.GetDumpDestinationPath(ref map, ref paths);
            Source = string.Empty;
            additionalFiles = new List<AdditionalFile>();
        }

        public void Generate()
        {
            StringBuilder sourceBuilder = new StringBuilder();

            if (HasMinigun) {
                sourceBuilder.AppendLine("require,minigun");
            }

            string baseGSCBlock = $@"
# GSC
rawfile,maps/mp/{MapName}.gsc
rawfile,maps/mp/{MapName}_fx.gsc
rawfile,maps/createfx/{MapName}_fx.gsc
rawfile,maps/createart/{MapName}_art.gsc
";

            sourceBuilder.AppendLine($@"

########################
# This source is intended to work with IW4x ZoneBuilder!
# It should build the map {MapName}.ff
# Generated with MPU Version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}
# {DateTime.Now.ToLongDateString()} {DateTime.Now.ToLongTimeString()}
########################

map_ents,maps/mp/{MapName}.d3dbsp
col_map_mp,maps/mp/{MapName}.d3dbsp
fx_map,maps/mp/{MapName}.d3dbsp
com_map,maps/mp/{MapName}.d3dbsp
game_map_mp,maps/mp/{MapName}.d3dbsp
gfx_map,maps/mp/{MapName}.d3dbsp

material,compass_map_{MapName}

{baseGSCBlock}
");

            var gscs = GetGSCs();
            if(gscs != null) {
                foreach (var gsc in gscs) {
                    string localPath = gsc.Substring(MapDataPath.Length + 1)
                        .Replace(Path.DirectorySeparatorChar, '/');

                    if (!baseGSCBlock.Contains(localPath)) {
                        sourceBuilder.AppendLine($"rawfile,{localPath}");
                    }
                }
            }

            sourceBuilder.AppendLine("\n\n# Rawfiles");

            if (HasVision) {
                sourceBuilder.AppendLine($"rawfile,{VisionPath}");
            }

            if (HasSun) {
                sourceBuilder.AppendLine($"rawfile,{SunPath}");
            }


            var fxs = GetFX();
            if (fxs != null) {
                sourceBuilder.AppendLine("\n# FX");
                foreach (var fx in fxs) {
                    string ext = Path.GetExtension(fx);
                    string localPath = fx.Substring(MapDataPath.Length + 1 + "fx/".Length);
                    localPath = localPath.Substring(0, localPath.Length - ext.Length)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    sourceBuilder.AppendLine($"fx,{localPath}");
                }
            }

            var sounds = GetSounds();
            if (sounds != null) {
                sourceBuilder.AppendLine("\n# Sounds");
                foreach (var sound in sounds) {
                    sourceBuilder.AppendLine($"sound,{Path.GetFileNameWithoutExtension(sound)}");
                }
            }


            var additionalModels = GetAdditionalModels();
            if (additionalModels != null) {
                sourceBuilder.AppendLine("\n# GSC Models & Destructibles");
                foreach (var model in additionalModels) {
                    sourceBuilder.AppendLine($"xmodel,{Path.GetFileNameWithoutExtension(model)}");
                }
            }

            GenerateLoadAssets();
            GenerateMissingGSCs();

            // and that should be it ?
            Source = sourceBuilder.ToString();
        }

        private void GenerateLoadAssets()
        {
            string materialPath = Path.Combine(MapDataPath, "materials");

            additionalFiles.Add(new AdditionalFile() {
                Path = Path.Combine(materialPath, "$levelbriefing.iw4x.json"),
                Contents = Resources.loadMaterialTemplate.Replace("MAPNAME", MapName)
            });
        }

        private void GenerateMissingGSCs()
        {
            var mainGscPath = Path.Combine(MapDataPath, "maps", "mp");
            var fxGscPath = Path.Combine(MapDataPath, "maps", "createfx");
            var artGscPath = Path.Combine(MapDataPath, "maps", "createart");

            var mainGscFile = Path.Combine(mainGscPath, MapName + ".gsc");
            var mainFxGscFile = Path.Combine(mainGscPath, MapName + "_fx.gsc");
            var fxGscFile = Path.Combine(fxGscPath, MapName + "_fx.gsc");
            var artGscFile = Path.Combine(artGscPath, MapName + "_art.gsc");

            // Template for main GSC in case it's missing
            if (!File.Exists(mainGscFile)) {

                var data = Resources.mainGscTemplate
                    .Replace("MAPNAME", MapName)
                    .Replace("AMBIENT", "ambient_mp_rural");

                // Inject map name etc
                additionalFiles.Add(new AdditionalFile() {
                    Path = mainGscFile,
                    Contents = data
                });
            }

            if (!File.Exists(fxGscFile)) {
                additionalFiles.Add(new AdditionalFile() {
                    Path = fxGscFile,
                    Contents = @"
//_createfx generated. Do not touch!
#include common_scripts\\utility;
#include common_scripts\\_createfx;

main()
{

}"
                });
            }

            var emptyGsc = @"
main()
{

}";

            if (!File.Exists(mainFxGscFile)) {
                additionalFiles.Add(new AdditionalFile() {
                    Path = mainFxGscFile,
                    Contents = emptyGsc
                });
            }

            if (!File.Exists(artGscFile)) {
                additionalFiles.Add(new AdditionalFile() {
                    Path = artGscFile,
                    Contents = emptyGsc
                });
            }
        }

        private string[] GetSounds()
        {
            string root = Path.Combine(MapDataPath, "sounds");

            if (Directory.Exists(root)) {

                List<string> soundList = new List<string>();
                var files = Directory.GetFiles(root);
                foreach (var file in files) {
                    if (file.EndsWith(".json")) {
                        soundList.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
                return soundList.ToArray();
            }

            return null;
        }

        private string[] GetFX()
        {
            string root = Path.Combine(MapDataPath, "fx");
            if (Directory.Exists(root)) {

                var files = Directory.EnumerateFiles(root, "*.iw4xFX", SearchOption.AllDirectories);
                return files.ToArray();
            }
            else {
                return null;
            }
        }

        private string[] GetGSCs()
        {
            string root = Path.Combine(MapDataPath, "maps", "mp");
            if (Directory.Exists(root)) {

                var files = Directory.EnumerateFiles(root, "*.gsc", SearchOption.AllDirectories);
                return files.ToArray();
            }
            else {
                return null;
            }
        }

        private string[] GetAdditionalModels()
        {
            string additionalModelsFile = Path.Combine(MapDataPath, "additionalModels.txt");
            if (File.Exists(additionalModelsFile)) {
                return File.ReadAllLines(additionalModelsFile);
            }
            else {
                return null;
            }
        }

        private bool HasMinigun => File.Exists(Path.Combine(MapDataPath, "HAS_MINIGUN"));

        private bool HasSun => File.Exists(Path.Combine(MapDataPath, SunPath));

        private bool HasVision => File.Exists(Path.Combine(MapDataPath, VisionPath));

        private string SunPath => Path.Combine("sun", $"{MapName}.sun");

        private string VisionPath => Path.Combine("vision", $"{MapName}.vision");

    }
}
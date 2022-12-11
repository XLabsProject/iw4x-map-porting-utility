﻿namespace MapPortingUtility
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Data;
    using System.Drawing;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Forms;

    public partial class MainForm : Form
    {
        private Paths paths;

        private readonly Action<MainForm, string> updateTextBox = (MainForm form, string txt) =>
        {
            form.outputTextBox.AppendText($"{txt}{Environment.NewLine}");
        };

        public MainForm() : base()
        {
            InitializeComponent();
            iw3MapListBox.ItemCheck += Iw3MapListBox_ItemCheck;
            iw4ZoneListBox.ItemCheck += Iw4ZoneListBox_ItemCheck;
            outputTextBox.Text = string.Empty;

            RefreshIW4Buttons(); 
            RefreshIW3Buttons();
        }

        private void Iw4ZoneListBox_ItemCheck(object _a, ItemCheckEventArgs _b)
        {
            this.BeginInvoke((MethodInvoker)RefreshIW4Buttons);
        }

        private void Iw3MapListBox_ItemCheck(object _a, ItemCheckEventArgs _b)
        {
            this.BeginInvoke((MethodInvoker)RefreshIW3Buttons);
        }

        private void RefreshIW3Buttons()
        {
            exportButton.Enabled = iw3MapListBox.CheckedItems.Count > 0;
        }

        private void RefreshIW4Buttons()
        {
            buildZoneButton.Enabled = iw4ZoneListBox.CheckedItems.Count > 0;
            generateIWDButton.Enabled = iw4ZoneListBox.CheckedItems.Count > 0;
            regenerateZoneSourceButton.Enabled = iw4ZoneListBox.CheckedItems.Count > 0;

            bool oneMapSelected = iw4ZoneListBox.CheckedItems.Count == 1;
            bool mapIsPlayable = oneMapSelected && ZoneBuilderHelper.MapFileExists(iw4ZoneListBox.CheckedItems[0].ToString(), ref paths);
            runMapButton.Enabled = mapIsPlayable;
            openMapFolderButton.Enabled = oneMapSelected;
            editArenaFileButton.Enabled = oneMapSelected;
            editCSVButton.Enabled = oneMapSelected;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (Program.ReadPaths(out paths) && paths.IsValid) {
                /// ALL good !
            }
            else {
                PromptForPaths();
            }

            if (paths.IsValid) {
                RefreshIW3MapList();
                RefreshZoneSourcesList();
                RefreshIW3Buttons();
                RefreshIW4Buttons();
            }
            else {
                Application.Exit();
            }
        }

        private void RefreshIW3MapList()
        {
            var maps = IW3xportHelper.GetMapList(ref paths);

            iw3MapListBox.SuspendLayout();

            iw3MapListBox.Items.Clear();

            for (int i = 0; i < System.Enum.GetValues(typeof(IW3xportHelper.Map.ECategory)).Length; i++) {
                foreach (var map in maps) {
                    if (map.Category == (IW3xportHelper.Map.ECategory)i) {
                        iw3MapListBox.Items.Add(map, false);
                    }
                }
            }

            iw3MapListBox.ResumeLayout();
        }

        private void RefreshZoneSourcesList()
        {
            var sources = ZoneBuilderHelper.GetZoneSources(ref paths);

            iw4ZoneListBox.SuspendLayout();

            iw4ZoneListBox.Items.Clear();

            foreach (var source in sources) {
                iw4ZoneListBox.Items.Add(source);
            }

            iw4ZoneListBox.ResumeLayout();
        }

        private void iw3RefreshButton_Click(object sender, EventArgs e)
        {
            RefreshIW3MapList();
            RefreshIW3Buttons();
        }

        private void exportButton_Click(object sender, EventArgs e)
        {
            outputTextBox.Clear();

            Enabled = false;

            bool shouldWriteSource = generateSourceCheckbox.Checked;
            bool shouldWriteArena = generateArenaCheckbox.Checked;
            bool shouldConvertGSC = convertGscCheckbox.Checked;
            bool shouldCorrectSpeculars = correctSpecularsCheckbox.Checked;
            bool shouldOverwriteGSC = replaceExistingFilesCheckbox.Checked;

            List<IW3xportHelper.Map> mapsToDump = new List<IW3xportHelper.Map>();
            Dictionary<IW3xportHelper.Map, int> indices = new Dictionary<IW3xportHelper.Map, int>();
            List<string> successfullyDumpedMaps = new List<string>();

            int i = 0;
            foreach (var item in iw3MapListBox.CheckedItems) {
                if (item is IW3xportHelper.Map map) {
                    mapsToDump.Add(map);
                    indices.Add(map, i++);
                }
            }

            bool hadError = false;

            Task.Run(() =>
            {
                foreach (var map in mapsToDump) {
                    Action untick = () =>
                    {
                        iw3MapListBox.SetItemChecked(indices[map], false);
                    };

                    try {
                        int exitCode = IW3xportHelper.DumpMap(
                            map,
                            ref paths,
                            shouldCorrectSpeculars,
                            shouldConvertGSC,
                            (txt) => outputTextBox.Invoke(updateTextBox, this, txt));

                        outputTextBox.Invoke(updateTextBox, this, $"IW3xport program terminated with output {exitCode}");

                        if (exitCode == 0) {
                            // All good!
                            iw3MapListBox.Invoke(untick);

                            IW3xportHelper.Map mapCpy = map;
                            var project = new ZoneProject(ref mapCpy, ref paths);
                            project.Generate();

                            if (shouldWriteSource) {

                                ZoneBuilderHelper.WriteSourceForProject(ref project, ref paths);
                                outputTextBox.Invoke(updateTextBox, this, $"Generated source file for {map.Name}");
                            }

                            if (shouldWriteArena) {
                                ZoneBuilderHelper.WriteArena(map.Name, ref paths);
                                outputTextBox.Invoke(updateTextBox, this, $"Generated arenafile for {map.Name}");
                            }

                            ZoneBuilderHelper.WriteAdditionalFilesForProject(ref project, replaceExistingGSC: shouldOverwriteGSC);

                            successfullyDumpedMaps.Add(map.Name);
                        }
                        else {
                            hadError = true;
                            break;
                        }
                    }
                    catch (Exception ex) {
                        hadError = true;
                        outputTextBox.Invoke(updateTextBox, this, ex.ToString());
                        break;
                    }
                }

                Action postExport = () =>
                {
                    if (hadError) {
                        MessageBox.Show($"An error occured while exporting the map (check the console). Exporting was interrupted.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else {
                        outputTextBox.AppendText($"Export successful! {Environment.NewLine}");
                    }

                    RefreshZoneSourcesList();

                    if (!hadError) {
                        // Check maps we ported successfully
                        for (int j = 0; j < iw4ZoneListBox.Items.Count; j++) {
                            string sourceName = System.IO.Path.GetFileNameWithoutExtension(iw4ZoneListBox.Items[j].ToString()).ToUpper();

                            if (successfullyDumpedMaps.Find(o => o.ToUpper() == sourceName) != null) {
                                iw4ZoneListBox.SetItemChecked(j, true);
                            }
                            else {
                                iw4ZoneListBox.SetItemChecked(j, false);
                            }
                        }
                    }

                    Enabled = true;
                };

                Invoke(postExport);
            });
        }

        private void zbRefreshButton_Click(object sender, EventArgs e)
        {
            RefreshZoneSourcesList();
            RefreshIW4Buttons();
        }

        private void optionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PromptForPaths();

            if (paths.IsValid) {
                RefreshIW3MapList();
                RefreshZoneSourcesList();
                RefreshIW3Buttons();
                RefreshIW4Buttons();
            }
            else {
                Application.Exit();
            }
        }

        private void PromptForPaths()
        {
            var pathControl = new SetPathsForm(paths);
            Enabled = false;
            pathControl.ShowDialog(this);
            paths = pathControl.computedPath;
            Enabled = paths.IsValid;
        }

        private void buildZoneButton_Click(object sender, EventArgs e)
        {
            outputTextBox.Clear();

            Enabled = false;

            bool buildTeams = buildTeamsCheckbox.Checked;

            List<string> zonesToBuild = new List<string>();
            Dictionary<string, int> indices = new Dictionary<string, int>();
            List<string> successfullyBuiltZones = new List<string>();

            int i = 0;
            foreach (var item in iw4ZoneListBox.CheckedItems) {
                zonesToBuild.Add(item.ToString());
                indices.Add(item.ToString(), i++);
            }

            bool hadError = false;
            Task.Run(() =>
            {
                foreach (var zone in zonesToBuild) {
                    if (ZoneBuilderHelper.SourceFileExists(zone, ref paths)) {
                        try {
                            Action untick = () =>
                            {
                                iw4ZoneListBox.SetItemChecked(indices[zone], false);
                            };

                            int exitCode = ZoneBuilderHelper.BuildZone(zone, ref paths, withTeams: buildTeams, (txt) => outputTextBox.Invoke(updateTextBox, this, txt));

                            outputTextBox.Invoke(updateTextBox, this, $"IW4x Zonebuilder program terminated with output {exitCode}");

                            if (exitCode == 0) {
                                // All good!
                                iw4ZoneListBox.Invoke(untick);
                                successfullyBuiltZones.Add(zone);
                            }
                            else {
                                hadError = true;
                                break;
                            }
                        }
                        catch (Exception ex) {
                            hadError = true;
                            outputTextBox.Invoke(updateTextBox, this, ex.ToString());
                            break;
                        }
                    }
                    else {
                        outputTextBox.Invoke(updateTextBox, this, $"Could not find zone source for {zone}!");
                        hadError = true;
                        break;
                    }
                }

                Action postExport = () =>
                {
                    if (hadError) {
                        MessageBox.Show($"An error occured while building the map (check the console). Building was interrupted.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else {
                        outputTextBox.AppendText($"Build successful! You should now be able to run the map! {Environment.NewLine}");
                    }

                    Enabled = true;
                };

                Invoke(postExport);
            });
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var box = new AboutBox();
            Enabled = false;
            box.Show(this);
            Enabled = true;
        }

        private void getHelpOnlineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://discord.gg/xlabs");
        }

        private void runMapButton_Click(object sender, EventArgs e)
        {
            if (iw4ZoneListBox.CheckedItems.Count == 1) {
                string mapName = iw4ZoneListBox.CheckedItems[0].ToString();
                if (ZoneBuilderHelper.MapFileExists(mapName, ref paths)) {
                    ZoneBuilderHelper.TestMap(mapName, ref paths);
                }
            }
        }

        private void generateIWDButton_Click(object sender, EventArgs e)
        {
            outputTextBox.Clear();

            Enabled = false;

            bool buildTeams = buildTeamsCheckbox.Checked;

            List<string> iwdsToGenerate = new List<string>();

            foreach (var item in iw4ZoneListBox.CheckedItems) {
                iwdsToGenerate.Add(item.ToString());
            }

            Task.Run(() =>
            {
                foreach (var mapName in iwdsToGenerate) {
                    if (ZoneBuilderHelper.MapFileExists(mapName, ref paths)) {
                        ZoneBuilderHelper.GenerateIWD(mapName, ref paths, (txt) => outputTextBox.Invoke(updateTextBox, this, txt)); ;
                    }
                    else {
                        outputTextBox.Invoke(updateTextBox, this, $"Could not find map file for {mapName}!");
                        break;
                    }
                }

                Action postExport = () =>
                {
                    Enabled = true;
                };

                Invoke(postExport);
            });
        }

        private void regenerateZoneSourceButton_Click(object sender, EventArgs e)
        {
            Enabled = false;
            outputTextBox.Clear();

            Task.Run(() =>
            {
                foreach (var item in iw4ZoneListBox.CheckedItems) {
                    var map = new IW3xportHelper.Map() {
                        Path = IW3xportHelper.GetDumpDestinationPath(item.ToString(), ref paths)
                    };

                    outputTextBox.Invoke(updateTextBox, this, $"Generating source for {item}...");

                    ZoneProject proj = new ZoneProject(ref map, ref paths);
                    proj.Generate();
                    ZoneBuilderHelper.WriteSourceForProject(ref proj, ref paths);
                }

                Action postExport = () =>
                {
                    outputTextBox.AppendText("\nDone regenerating sources!\n");
                    Enabled = true;
                };

                Invoke(postExport);
            });

        }

        private void openMapFolderButton_Click(object sender, EventArgs e)
        {
            if (iw4ZoneListBox.CheckedItems.Count == 1) {
                string mapName = iw4ZoneListBox.CheckedItems[0].ToString();
                if (ZoneBuilderHelper.MapFileExists(mapName, ref paths)) {
                    System.Diagnostics.Process.Start("explorer", IW3xportHelper.GetDumpDestinationPath(mapName, ref paths));
                    System.Diagnostics.Process.Start("explorer", ZoneBuilderHelper.GetZoneDestinationPath(mapName, ref paths));
                }
            }
        }

        private void editArenaFileButton_Click(object sender, EventArgs e)
        {
            if (iw4ZoneListBox.CheckedItems.Count == 1) {
                string mapName = iw4ZoneListBox.CheckedItems[0].ToString();
                if (ZoneBuilderHelper.MapFileExists(mapName, ref paths)) {
                    string arenaFile = ZoneBuilderHelper.GetArenaFilePath(mapName, ref paths);
                    System.Diagnostics.Process.Start("notepad", arenaFile);
                }
            }
        }

        private void editCSVButton_Click(object sender, EventArgs e)
        {
            if (iw4ZoneListBox.CheckedItems.Count == 1) {
                string mapName = iw4ZoneListBox.CheckedItems[0].ToString();
                if (ZoneBuilderHelper.MapFileExists(mapName, ref paths)) {
                    System.Diagnostics.Process.Start($"{ZoneBuilderHelper.GetZoneSourcePath(mapName, ref paths)}");
                }
            }
        }
    }
}
﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HLDS_Launcher
{
    /*
     * TODO: populate mapcycle.txt with all maps on /maps folder automatically (using a button or something).
     * TODO: add advanced settings tab.
     * TODO: ADVANCED SETTINGS = change most common cvars from server.cfg within UI.
     * TODO: option to add more command line parameters.
     * 
     * v1.2
     * - Added bots support for ReGame.dll (Only CS 1.6).
     * - Added option to set server IP address.
     * - Added button to edit mapcycle file.
     * - Show public ip directly on application instead of redirecting to website.
     * - Improved launcher log to detect if server crashed/closed unexpectedly.
     * - Now server doesn't restart automatically when the user stops it.
     * - Random mapcycle now creates a new file named mapcycle_random.txt and use it for map rotation.
     * 
     * v1.1
     * - Added option to randomize mapcycle.
     * 
     * v1.0
     * - Initial release.
     */

    public partial class MainWindow : Window
    {
        string _game;
        string _mapcyclefile;
        string _map;
        string _maxPlayers;
        string _localIP;
        string _port;
        string _vac;
        string _bots;

        bool writeLog = false;

        List<Scripts.Game> games = new List<Scripts.Game>();
        List<string> gameFolders = new List<string>();
        List<string> gameNames = new List<string>();

        Process hlds;
        ProcessPriorityClass priority;

        System.Net.WebClient webClient;

        public MainWindow()
        {
            CheckEXE();
            InitializeComponent();
            LoadGames();
            LoadUserValues();

            webClient = new System.Net.WebClient();
            GetPublicIP();
        }

        // Check if HLDS.exe exists in the same folder.
        private void CheckEXE()
        {
            if (!File.Exists("HLDS.exe"))
            {
                System.Windows.MessageBox.Show("HLDS.exe not found. Launcher must be in the same directory.", "HLDS Launcher", MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                ExitApplication();
            }
        }

        // Load user settings.
        private void LoadUserValues()
        {
            gameList.SelectedIndex = Properties.Settings.Default.gameNameIndex;
            maxPlayers.Text = Properties.Settings.Default.maxPlayers;
            localip_TextBox.Text = Properties.Settings.Default.localIP;
            port.Text = Properties.Settings.Default.port;
            priorityList.SelectedIndex = Properties.Settings.Default.priorityIndex;
            randomMapcycle.IsChecked = Properties.Settings.Default.randomMapcycle;
            secureVAC.IsChecked = Properties.Settings.Default.vac;
            enableBots.IsChecked = Properties.Settings.Default.bots;
            autoRestart.IsChecked = Properties.Settings.Default.autoRestart;
            enableLog.IsChecked = Properties.Settings.Default.enableLogging;
            mapsList.SelectedIndex = Properties.Settings.Default.gameMapIndex;
        }

        // Save user settings.
        private void SaveUserSettings()
        {
            Properties.Settings.Default.gameNameIndex = gameList.SelectedIndex;
            Properties.Settings.Default.gameMapIndex = mapsList.SelectedIndex;
            Properties.Settings.Default.maxPlayers = maxPlayers.Text;
            Properties.Settings.Default.localIP = localip_TextBox.Text;
            Properties.Settings.Default.port = port.Text;
            Properties.Settings.Default.priorityIndex = priorityList.SelectedIndex;
            Properties.Settings.Default.randomMapcycle = (bool)randomMapcycle.IsChecked;
            Properties.Settings.Default.vac = (bool)secureVAC.IsChecked;
            Properties.Settings.Default.bots = (bool)enableBots.IsChecked;
            Properties.Settings.Default.autoRestart = (bool)autoRestart.IsChecked;
            Properties.Settings.Default.enableLogging = (bool)enableLog.IsChecked;
            Properties.Settings.Default.Save();
        }

        // Load games/mods
        private void LoadGames()
        {
            string[] folders = Directory.GetDirectories(".");

            // Check each folder to find out if it's a game/mod.
            foreach (string folder in folders)
            {
                // If folder "valve" exists, add hldm to game list.
                if (folder == ".\\valve")
                {
                    AddGame("Half-Life Deathmatch", "valve", "", folders);
                }
                else if (!folder.Contains("_"))
                {
                    // If it's a game/mod, get name and check for gameplay type.
                    if (File.Exists(folder + "\\liblist.gam"))
                    {
                        StreamReader sr = new StreamReader(folder + "\\liblist.gam");
                        string line, gameName = "", fallbackdir = "";
                        bool isMultiplayer = false, isSingleplayer = false;

                        // Get game name, gameplay type and fallback directory from liblist.gam.
                        while (!sr.EndOfStream)
                        {
                            line = sr.ReadLine();

                            // Get game/mod name.
                            if (line.StartsWith("game "))
                            {
                                gameName = line.Replace("game \"", "");
                                gameName = gameName.Remove(gameName.LastIndexOf('"'));
                            }
                            // Get gameplay type.
                            else if (line.StartsWith("type "))
                            {
                                isMultiplayer = line.Contains("multiplayer");
                                isSingleplayer = !isMultiplayer;
                            }
                            // Get fallback directory if exists.
                            else if (line.StartsWith("fallback_dir "))
                            {
                                fallbackdir = line.Replace("fallback_dir \"", "");
                                fallbackdir = fallbackdir.Remove(fallbackdir.LastIndexOf('"'));
                            }
                        }
                        // If game/mod is multiplayer, add name to list.
                        if (isMultiplayer == true)
                        {
                            AddGame(gameName, folder.Remove(0, 2), fallbackdir, folders);
                        }
                        // If gameplay type is unknown, search if server.cfg exists and add the game to list if true.
                        else if (isSingleplayer == false)
                        {
                            if (File.Exists(folder + "\\server.cfg"))
                            {
                                AddGame(gameName, folder.Remove(0, 2), fallbackdir, folders);
                            }
                        }
                    }
                }
            }
            // Add games to game list.
            gameList.ItemsSource = gameNames;
            gameList.SelectedIndex = 0;
        }

        private void AddGame(string name, string folderName, string fallbackDir, string[] folders)
        {
            Scripts.Game game = new Scripts.Game
            {
                Name = name,
                ShortName = folderName,
                FallbackDir = fallbackDir
            };
            game.GetExtraFolders(folders);
            game.LoadMaps();

            games.Add(game);
            gameNames.Add(game.Name);
            gameFolders.Add(game.ShortName);
        }

        // Randomize mapcycle.
        private void RandomMapCycle()
        {
            if (gameList.Items.Count <= 1)
            {
                return;
            }
            List<string> mapList = new List<string>();
            Random random = new Random();

            mapList.AddRange(games[gameList.SelectedIndex].Maps);
            mapList.RemoveAt(0);
            StreamWriter sw = new StreamWriter(".\\" + games[gameList.SelectedIndex].ShortName + "\\mapcycle_random.txt");

            string mapName = _map.Remove(0, 6);
            sw.WriteLine(mapName);
            mapList.Remove(mapName);

            while (mapList.Count > 0)
            {
                int i = random.Next(0, mapList.Count);
                sw.WriteLine(mapList[i]);
                mapList.RemoveAt(i);
            }
            sw.Close();
            _mapcyclefile = " +mapcyclefile mapcycle_random.txt ";
        }

        // Start hlds.exe
        private void StartHLDS()
        {
            hlds = new Process();
            hlds.StartInfo.FileName = "hlds.exe";
            hlds.StartInfo.Arguments = "-console" + _game + _maxPlayers + _localIP + _port + _vac + _mapcyclefile + _map + _bots;
            hlds.EnableRaisingEvents = true;
            hlds.Exited += new EventHandler(Hlds_Exited);
            hlds.Start();
            hlds.PriorityClass = priority;
            WriteToLog("Server started with parameters: " + hlds.StartInfo.Arguments);
        }

        // Stop server and restore UI.
        private void StopServer()
        {
            gameList.IsEnabled = true;
            mapsList.IsEnabled = true;
            maxPlayers.IsEnabled = true;
            port.IsEnabled = true;
            secureVAC.IsEnabled = true;
            enableBots.IsEnabled = true;
            autoRestart.IsEnabled = true;
            enableLog.IsEnabled = true;
            randomMapcycle.IsEnabled = true;
            localip_TextBox.IsEnabled = true;
            priorityList.IsEnabled = true;
            
            buttonStart.IsEnabled = true;
            buttonStart.Visibility = Visibility.Visible;

            buttonStop.IsEnabled = false;
            buttonStop.Visibility = Visibility.Hidden;
            WriteToLog("Server stopped by user.");
        }

        private void ExitApplication()
        {
            Application.Current.Shutdown();
        }

        // HLDS exit event handler.
        private void Hlds_Exited(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (hlds.ExitCode != 0)
                {
                    WriteToLog("Server process ended unexpectedly. Game: " + games[gameList.SelectedIndex].ShortName);

                    if (autoRestart.IsChecked == true)
                    {
                        WriteToLog("Restarting server...");
                        StartHLDS();
                        return;
                    }
                }
                StopServer();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle, null);
            
        }

        private void WriteToLog(string line)
        {
            if (writeLog == true)
            {
                TextWriter wr = File.AppendText("HLDSLauncher.log");
                wr.WriteLine(DateTime.Now.ToString() + " : " + line);
                wr.Close();
            }
        }

        // Textbox allow only numbers.
        private void TextInputOnlyNumbers(object sender, TextCompositionEventArgs e) // Text Input event.
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }
        private void TextOnlyNumbers(object sender, KeyEventArgs e) // KeyDown event.
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }

        // Max players value cannot be more than 32.
        private void MaxPlayers_TextChanged(object sender, TextChangedEventArgs e)
        {
            int.TryParse(maxPlayers.Text, out int i);
            if (i > 32)
            {
                maxPlayers.Text = "32";
            }
        }

        // Select/change game in combobox.
        private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (gameList.Items.Count > 0)
            {
                mapsList.ItemsSource = games[gameList.SelectedIndex].Maps;
                mapsList.SelectedIndex = 0;

                if (games[gameList.SelectedIndex].ShortName == "cstrike")
                {
                    enableBots.IsEnabled = true;
                }
                else
                {
                    enableBots.IsEnabled = false;
                }
            }            
        }

        // Get public IP from ipify.org.
        private void GetPublicIP()
        {
            publicIP_Text.Text = "...";
            webClient.DownloadStringCompleted += new DownloadStringCompletedEventHandler(PublicIP_DownloadStringCompleted);
            webClient.DownloadStringAsync(new Uri("https://api.ipify.org"));
        }

        private void ButtonGetIP_Click(object sender, RoutedEventArgs e)
        {
            buttonGetIP.IsEnabled = false;
            GetPublicIP();
        }

        private void PublicIP_DownloadStringCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                publicIP_Text.Text = "Failed";
            }
            else
            {
                publicIP_Text.Text = e.Result;
            }
            buttonGetIP.IsEnabled = true;
        }

        // Button edit server.cfg.
        private void ButtonEditServerCFG_Click(object sender, RoutedEventArgs e)
        {
            string serverFilePath = ".\\" + games[gameList.SelectedIndex].ShortName + "\\server.cfg";
            if (File.Exists(serverFilePath))
            {
                Process.Start(serverFilePath);
            }
            else
            {
                System.Windows.MessageBox.Show("File \"server.cfg\" not found in path: " + serverFilePath, "HLDS Launcher", MessageBoxButton.OK, 
                    MessageBoxImage.Asterisk);
            }
        }

        // Button Start Server
        private void ButtonStart_Click(object sender, RoutedEventArgs e)
        {
            _game = " -game " + games[gameList.SelectedIndex].ShortName;
            _map = " +map " + mapsList.SelectionBoxItem;
            _maxPlayers = " +maxplayers " + maxPlayers.Text;
            _localIP = localip_TextBox.Text.Length > 0 ? " +ip " + localip_TextBox.Text : "";
            _port = " +port " + port.Text;
            _vac = secureVAC.IsChecked == true ? "" : " -insecure ";
            _bots = enableBots.IsEnabled && enableBots.IsChecked == true ? " -bots " : "";

            // If selected map is "Random Map", choose a map randomly.
            if (mapsList.SelectedIndex == 0)
            {
                Random rand = new Random();
                _map = " +map " + mapsList.Items[rand.Next(1, mapsList.Items.Count)].ToString();
            }
            // If map name starts with "-", create a cfg file and change command to exec. 
            // If this isn't done, the server will not read the map command properly and will not load the map.
            if (mapsList.SelectionBoxItem.ToString().StartsWith("-"))
            {
                TextWriter tw = new StreamWriter("hldslauncher_loadmap.cfg");
                tw.WriteLine("map " + mapsList.SelectionBoxItem.ToString());
                tw.Close();
                _map = "+exec hldslauncher_loadmap.cfg";
            }
            // Set process priority.
            switch (priorityList.SelectedIndex)
            {
                case 0:
                    priority = ProcessPriorityClass.Normal;
                    break;
                case 1:
                    priority = ProcessPriorityClass.AboveNormal;
                    break;
                case 2:
                    priority = ProcessPriorityClass.High;
                    break;
                case 3:
                    priority = ProcessPriorityClass.RealTime;
                    break;
            }
            // Random Mapcycle
            if (randomMapcycle.IsChecked == true)
            {
                RandomMapCycle();
            }
            else
            {
                _mapcyclefile = "";
            }
            // Check if Launcher should write to log.
            if (enableLog.IsChecked == true)
            {
                writeLog = true;
            }
            else
            {
                writeLog = false;
            }
            // Save settings and start server.
            SaveUserSettings();
            StartHLDS();

            // If auto restart is ON, block all fields and show "Stop" button. Else exit launcher.
            if (autoRestart.IsChecked == true)
            {
                gameList.IsEnabled = false;
                mapsList.IsEnabled = false;
                maxPlayers.IsEnabled = false;
                port.IsEnabled = false;
                secureVAC.IsEnabled = false;
                enableBots.IsEnabled = false;
                autoRestart.IsEnabled = false;
                enableLog.IsEnabled = false;
                randomMapcycle.IsEnabled = false;
                localip_TextBox.IsEnabled = false;
                priorityList.IsEnabled = false;

                buttonStart.IsEnabled = false;
                buttonStart.Visibility = Visibility.Hidden;

                buttonStop.IsEnabled = true;
                buttonStop.Visibility = Visibility.Visible;
            }
            else
            {
                ExitApplication();
            }
        }

        // Button stop server.
        private void ButtonStop_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Do you want to stop the server?", "Stop server", MessageBoxButton.YesNo, MessageBoxImage.Question) ==
                MessageBoxResult.Yes)
            {
                StopServer();                
                hlds.CloseMainWindow();
                hlds.Close();
                hlds.Refresh();
            }
        }

        // Button exit
        private void ButtonExit_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        // Button edit mapcycle.txt
        private void ButtonEditMapcycle_Click(object sender, RoutedEventArgs e)
        {
            string serverFilePath = ".\\" + games[gameList.SelectedIndex].ShortName + "\\mapcycle.txt";
            if (File.Exists(serverFilePath))
            {
                Process.Start(serverFilePath);
            }
            else
            {
                System.Windows.MessageBox.Show("File \"mapcycle.txt\" not found in path: " + serverFilePath, "HLDS Launcher", MessageBoxButton.OK,
                    MessageBoxImage.Asterisk);
            }
        }
    }
}
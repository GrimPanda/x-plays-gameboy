﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using XPlaysGameboy.Samples.SimulatorPlaysPokemon.Models;
using Path = System.IO.Path;
using FormsApplication = System.Windows.Forms.Application;
using FileResources = XPlaysGameboy.Samples.SimulatorPlaysPokemon.Properties.Resources;

namespace XPlaysGameboy.Samples.SimulatorPlaysPokemon
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly GameboyEngine _gameboy;
        private readonly TwitchChatEngine _twitchChatEngine;

        private int _slowmotionCountdown;
        private int _lastCommandIndex;

        private RepeatRequest _slowmotionRepeatRequest;

        public MainWindow()
        {
            InitializeComponent();

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            WindowState = WindowState.Maximized;

            _slowmotionCountdown = 300;
            if (Debugger.IsAttached)
            {
                _slowmotionCountdown = 10;
            }

            _gameboy = GameboyEngine.Instance;
            _twitchChatEngine = TwitchChatEngine.Instance;

            //initialize chat, but only if an API key and username is available. you'll need to generate these yourselves.
            var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XPlaysGameboy");
            var twitchOAuthTokenPath = Path.Combine(dataRoot, "Twitch.oauth");
            var twitchUsernamePath = Path.Combine(dataRoot, "Twitch.username");
            if (File.Exists(twitchOAuthTokenPath) && File.Exists(twitchUsernamePath))
            {
                var twitchOAuthToken = File.ReadAllText(twitchOAuthTokenPath);
                var twitchUsername = File.ReadAllText(twitchUsernamePath);
                _twitchChatEngine.Start(twitchUsername, twitchOAuthToken);
            }

            _twitchChatEngine.MessageReceived += _twitchChatEngine_MessageReceived;

            Loaded += MainWindow_Loaded;
        }

        void _twitchChatEngine_MessageReceived(string username, string message)
        {
            var messageUpper = message.ToUpper();
            if (messageUpper.Contains(" "))
            {
                var split = messageUpper.Split(' ');
                switch (split[0])
                {
                    case "REPEAT":
                        if (_slowmotionCountdown < 0 && _slowmotionRepeatRequest == null)
                        {
                            int amount;
                            if (int.TryParse(split[1], out amount))
                            {
                                //amount can be between 2 and 15.
                                amount = Math.Min(Math.Max(amount, 2), 15);

                                _slowmotionRepeatRequest = new RepeatRequest()
                                {
                                    Amount = amount,
                                    RequestAuthor = username,
                                    CommandIndex = _lastCommandIndex
                                };
                            }
                        }
                        break;
                }
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            //write the pokemon red ROM to the disk.
            var romPath = Path.Combine(Environment.CurrentDirectory, "PokemonRed.gb");
            if (!File.Exists(romPath))
            {
                File.WriteAllBytes(romPath, FileResources.PokemonRed);
            }

            //start the emulator with 5X normal speed.
            await _gameboy.Start(romPath, GameboyArea, 10);

            await Task.Delay(1000);

            //now load the game.
            _gameboy.TapStart();

            await Task.Delay(1000);

            _gameboy.LoadState();

            await Task.Delay(1000);

            //now that the game is running, start simulating random keypresses.
            StartKeyPressLoop();
            StartSlowMotionLoop();
        }

        private async void StartSlowMotionLoop()
        {
            while (true)
            {
                await Task.Delay(1000);

                _slowmotionCountdown -= 1;

                if (_slowmotionCountdown < -30)
                {
                    _slowmotionCountdown = 300;
                }
            }
        }

        private async void StartKeyPressLoop()
        {

            var random = new Random((int)DateTime.UtcNow.Ticks);
            var frame = 0;

            var lastTick = DateTime.UtcNow;

            var commandList = new LinkedList<string>();
            while (true)
            {

                var delay = 25;
                if (_slowmotionCountdown < 0)
                {
                    delay = 10000;
                    SlowMotionCountdown.Text = "Slowdown mode! Write \"repeat <number>\" in the chat to repeat the keypress that occured when you wrote it, X amount of times.";
                }
                else
                {
                    SlowMotionCountdown.Text = "Next slowdown in " + (_slowmotionCountdown) + " seconds ...";
                }

                var difference = (int)(DateTime.UtcNow - lastTick).TotalMilliseconds;
                await Task.Delay(Math.Max(delay - difference, 1));

                lastTick = DateTime.UtcNow;

                frame++;

                //every 100th frame (10 seconds), save progress.
                if (frame == 1000)
                {
                    _gameboy.SaveState();
                    frame = 0;
                }

                Action command;

                string commandName;
                _lastCommandIndex = _slowmotionRepeatRequest == null ? Math.Min(random.Next(0, 6), 5) : _slowmotionRepeatRequest.CommandIndex;
                switch (_lastCommandIndex)
                {
                    case 0:
                        commandName = "Right";
                        command = _gameboy.TapRight;
                        break;

                    case 1:
                        commandName = "Left";
                        command = _gameboy.TapLeft;
                        break;

                    case 2:
                        commandName = "Down";
                        command = _gameboy.TapDown;
                        break;

                    case 3:
                        commandName = "Up";
                        command = _gameboy.TapUp;
                        break;

                    case 4:
                        commandName = "A";
                        command = _gameboy.TapA;
                        break;

                    case 5:
                        commandName = "B";
                        command = _gameboy.TapB;
                        break;

                    default:
                        throw new InvalidOperationException();
                }

                if (_slowmotionRepeatRequest != null)
                {
                    commandName += " [X" + _slowmotionRepeatRequest.Amount + " by " + _slowmotionRepeatRequest.RequestAuthor + "]";
                }

                commandList.AddFirst(commandName);
                if (commandList.Count > 100)
                {
                    commandList.RemoveLast();
                }

                Log.ItemsSource = null;
                Log.ItemsSource = commandList;

                FormsApplication.DoEvents();

                var repeat = _slowmotionRepeatRequest == null ? 1 : _slowmotionRepeatRequest.Amount;
                for (var i = 0; i < repeat; i++)
                {
                    command();
                    await Task.Delay(100);
                }

                _slowmotionRepeatRequest = null;

            }
        }
    }
}

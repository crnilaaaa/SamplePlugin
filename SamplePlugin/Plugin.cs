﻿using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Buttplug;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using System.Linq;

namespace SamplePlugin
{
    public sealed class Plugin : IDalamudPlugin
    {
        private class Trigger : IComparable
        {
            public Trigger(int intensity, string text)
            {
                Intensity = intensity;
                ToMatch = text;
            }

            public int Intensity { get; }
            public string ToMatch { get; }

            public override string ToString()
            {
                return $"Trigger(intensity: {Intensity}, text: '{ToMatch}')";
            }
            public string ToConfigString()
            {
                return $"{Intensity} {ToMatch}";
            }
            public int CompareTo(object? obj)
            {
                Trigger? that = obj as Trigger;
                int thatintensity = that != null ? that.Intensity : 0;
                return this.Intensity.CompareTo(thatintensity);
            }
        }

        [PluginService]
        [RequiredVersion("1.0")]
        private ChatGui Chat { get; init; }
        private Buttplug.ButtplugClient buttplugClient;
        private readonly SortedSet<Trigger> Triggers = new SortedSet<Trigger>();

        public string Name => "Buttplug Triggers";
        private const string commandName = "/buttplugtriggers";

        private DalamudPluginInterface PluginInterface { get; init; }
        private CommandManager CommandManager { get; init; }
        private Configuration Configuration { get; init; }
        private PluginUI PluginUi { get; init; }
        private string AuthorizedUser { get; set; }

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;

            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            // you might normally want to embed resources and load them from the manifest stream
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var imagePath = Path.Combine(Path.GetDirectoryName(assemblyLocation)!, "goat.png");
            var goatImage = this.PluginInterface.UiBuilder.LoadImage(imagePath);
            this.PluginUi = new PluginUI(this.Configuration, goatImage);

            this.CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "A simple text triggers to buttplug.io plugin."
            });

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            Chat.ChatMessage += CheckForTriggers; // XXX: o.o
        }

        private readonly XivChatType[] allowedChatTypes = { 
            XivChatType.TellIncoming, XivChatType.Party, 
            XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4, 
            XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8, 
            XivChatType.FreeCompany, XivChatType.CrossParty, 
            XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, 
            XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
            XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, 
            XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8 };

        private void CheckForTriggers(XivChatType type, uint senderId, ref SeString _sender, ref SeString _message, ref bool isHandled)
        {
            string sender = _sender.ToString();
            if (!allowedChatTypes.Any(ct => ct == type) || (AuthorizedUser.Length > 0 && !sender.ToString().Contains(AuthorizedUser))) {
                return;
            }
            string message = _message.ToString();
            var matchingintensities = this.Triggers.Where(t => message.Contains(t.ToMatch));
            if (matchingintensities.Any())
            {
                int intensity = matchingintensities.Select(t => t.Intensity).Max();
                buttplugClient.Devices[0].SendVibrateCmd(intensity / 100.0f);
            }
        }

        public void Dispose()
        {
            this.PluginUi.Dispose();
            this.CommandManager.RemoveHandler(commandName);
            if(this.buttplugClient != null) this.buttplugClient.DisconnectAsync();
            Chat.ChatMessage -= CheckForTriggers; // XXX: ???

        }

        private void Print(string message)
        {
            Chat.Print(message);
        }

        private void PrintError(string error)
        {
            Chat.PrintError(error);
        }

        private void PrintHelp(string command)
        {
            string helpMessage =
                $@"Usage: {command} list
       {command} list
       {command} add <intensity 0-100> <trigger text>
       {command} remove <id>
       {command} connect [ip[:port]] # defaults to 'localhost:12345', the intiface default
       {command} disconnect
       {command} user [authorized user] # set/clear sender string match
       {command} save [file path]
       {command} load [file path]

Example:
       {command} connect
       {command} add 0 shh
       {command} add 20 slowly 
       {command} add 75 getting there
       {command} add 100 hey ;)
       {command} user Alice

       These commands let anyone whose name contains 'Alice' control all your connected toys with the appropriate phrases, as long as those are uttered in a tell, a party, a (cross) linkshell, or a free company chat.
";
            Chat.Print(helpMessage);
        }

        private void OnCommand(string command, string args)
        {
            if (args.Length == 0)
            {
                PrintHelp(command);
            }
            switch (args.Substring(0, 3))
            {
                case "lis":
                    ListTriggers();
                    break;
                case "add":
                    AddTrigger(args);
                    break;
                case "rem":
                    RemoveTrigger(args);
                    break;
                case "con":
                    ConnectButtplugs(args);
                    break;
                case "dis":
                    DisconnectButtplugs();
                    break;
                case "use":
                    SetAuthorizedUser(args);
                    break;
                case "sav":
                    SaveConfig(args);
                    break;
                case "loa":
                    LoadConfig(args);
                    break;
                default:
                    Print($"Unknown subcommand: {args}");
                    break;
            }

        }

        private void LoadConfig(string args)
        {
            string config = "";
            string path = "";
            try
            {
                path = args.Split(" ")[1];
                config = File.ReadAllText(path);
            }
            catch (Exception) 
            {
                PrintError($"Malformed or invalid arguments for [load]: {args}");
                return;
            }
            foreach(string line in config.Split("\n")) {
                string[] trigargs = line.Split(" ");
                int intensity;
                string toMatch = trigargs[1];
                if (int.TryParse(trigargs[0], out intensity)) {
                    Trigger trigger = new(intensity, toMatch);
                    if (!Triggers.Add(trigger))
                    {
                        Print($"Note: duplicate trigger: {trigger}");
                    };
                }
            }
        }

        private void SaveConfig(string args)
        {
            string path;
            var config = string.Join("\n", Triggers.Select(t => t.ToString()));
            try
            {
                path = args.Split(" ")[1];
                File.WriteAllText(path, config);
            }
            catch (Exception)
            {
                PrintError($"Malformed or invalid arguments for [save]: {args}");
                return;
            }
            Print($"Wrote current config to {path}");
        }

        private void SetAuthorizedUser(string args)
        {
            try
            {
                AuthorizedUser = args.Split(" ", 2)[1];
            }
            catch(IndexOutOfRangeException)
            {
                Print("Cleared authorized user.");
                return;
            }
            Print($"Authorized user set to '{AuthorizedUser}'");
        }

        private void DisconnectButtplugs()
        {
            Task task = this.buttplugClient.DisconnectAsync();
            task.Wait();
            Print("Disconnected!");
        }

        private void ConnectButtplugs(string args)
        {
            try
            {
                this.buttplugClient = new("buttplugtriggers-dalamud");
            }
            catch (Exception e)
            {
                PrintError($"Can't load buttplug.io: {e.Message}");
            }
            buttplugClient.DeviceAdded += ButtplugClient_DeviceAdded;
            string host = "localhost";
            string port = ":12345";
            string hostandport = host + port;
            if (args.Contains(" "))
            {
                hostandport = args.Split(" ", 2)[1];
                if(!hostandport.Contains(":"))
                {
                    hostandport += port;
                }
            }
            var uri = new Uri($"ws://{hostandport}/buttplug");
            var connector = new ButtplugWebsocketConnectorOptions(uri);
            Print($"Connecting to {hostandport}...");
            Task task = buttplugClient.ConnectAsync(connector);
            task.Wait();
            if(buttplugClient.Connected)
            {
                Print($"Connected!");
            }
            else
            {
                PrintError("Failed connecting (TODO: Why?");
            }
            Print("Scanning for devices...");
            buttplugClient.StartScanningAsync();
        }

        private void ButtplugClient_DeviceAdded(object? sender, DeviceAddedEventArgs e)
        {
            Print("Added device: " + e.Device.Name);
        }

        private void RemoveTrigger(string args)
        {
            int id = -1;
            try
            {
                id = int.Parse(args.Split(" ")[1]);
                if (id < 0)
                {
                    throw new FormatException(); // XXX: exceptionally exceptional control flow please somnenoee hehhehjel;;  ,.-
                }
            } 
            catch(FormatException)
            {
                PrintError("Malformed argument for [remove]");
                return; // XXX: exceptional control flow
            }
            Trigger removed = Triggers.ElementAt(id);
            Triggers.Remove(removed);
            Print($"Removed Trigger: {removed}");
        }

        private void AddTrigger(string args)
        {
            string[] blafuckcsharp;
            int intensity;
            string text;
            try
            {
                blafuckcsharp = args.Split(" ", 3);
                intensity = int.Parse(blafuckcsharp[1]);
                text = blafuckcsharp[2];
            }
            catch (Exception e) when (e is FormatException or IndexOutOfRangeException)
            {
                PrintError($"Malformed arguments for [add].");
                return; // XXX: exceptional control flow
            }
            Trigger newTrigger = new(intensity, text);
            Print($"Adding Trigger: {newTrigger}...");
            if (Triggers.Add(newTrigger))
            {
                Print("Success!");
            }
            else
            {
                PrintError($"Failed. Possible duplicate?");
            }
        }

        private void ListTriggers()
        {
            string message =
                @"Configured triggers:
ID   Intensity   Text Match
";
            for (int i = 0; i < Triggers.Count; ++i)
            {
                message += $"[{i}] | {Triggers.ElementAt(i).Intensity} | {Triggers.ElementAt(i).ToMatch}\n";
            }
            Chat.Print(message);
        }

        private void DrawUI()
        {
            this.PluginUi.Draw();
        }

        private void DrawConfigUI()
        {
            this.PluginUi.SettingsVisible = true;
        }
    }
}

﻿using GenericModConfigMenu;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;

namespace PassiveFriendship
{
    public class ModEntry : Mod
    {
        ModConfig? Config;

        GameLocation? gameLocation;

        Dictionary<string, CharacterData>? disposition;

        //Config bools
        int radius;
        bool notifyInConsole;
        int timeIntervalLength;
        int friendshipGainedPerInterval;

        int timeCounter;

        // Constants for full-heart amounts
        const int fourteenHeartPoints = 3749;
        const int tenHeartPoints = 2749;
        const int eightHeartPoints = 2249;

        public override void Entry(IModHelper helper)
        {
            //Config options being set & verified
            Config = Helper.ReadConfig<ModConfig>();
            radius = Config.FriendshipRadius;
            if (radius < 0)
            {
                radius = 2;
                Monitor.Log("Error: Config option \"FriendshipRadius\" must be an integer and at least 0. " +
                    "Option has been auto-set to 2.", LogLevel.Error);
            }
            notifyInConsole = Config.NotifyAboutFriendshipInConsole;
            timeIntervalLength = Config.TimeIntervalLengthInTenMinuteIncrements;
            if (timeIntervalLength < 1)
            {
                timeIntervalLength = 1;
                Monitor.Log("Error: Config option \"TimeIntervalLengthInTenMinuteIncrements\" must be an integer and at " +
                    "least 1 (the value of 1 corresponds to 10 in-game minutes). Option has been auto-set to 1.", LogLevel.Error);
            }
            else if (timeIntervalLength > 26)
            {
                timeIntervalLength = 26;
                Monitor.Log("Error: Config option \"TimeIntervalLengthInTenMinuteIncrements\" must be an integer and at " +
                    "most 26 (the value of 1 corresponds to 10 in-game minutes). Option has been auto-set to 26.", LogLevel.Error);
            }
            timeCounter = timeIntervalLength;

            friendshipGainedPerInterval = Config.AmountOfFriendshipGainedPerTimeInterval;
            if (friendshipGainedPerInterval < 1)
            {
                friendshipGainedPerInterval = 1;
                Monitor.Log("Error: Config option \"AmountOfFriendshipGainedPerTimeInterval\" must be an integer and at " +
                    "least 1. Option has been auto-set to 1.", LogLevel.Error);
            }

            //Event handler
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.Player.Warped += OnPlayerWarped;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null)
            {
                return;
            }

            configMenu.Register(
                mod: ModManifest,
                reset: () => Config = new ModConfig(),
                save: () => Helper.WriteConfig(Config)
                );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => "Friendship Radius",
                tooltip: () => "Friends are detected in a square centered on the player - this determines how many tiles this square will reach in one direction from the player",
                getValue: () => Config.FriendshipRadius,
                setValue: value => Config.FriendshipRadius = value,
                min: 0
                );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => "Time Interval",
                tooltip: () => "How many 10-minute increments should pass between additions of friendship. E.g. if set to 1, friendship will be detected every 10 in-game minutes (this might be different if you have a mod that changes how time passes!).",
                getValue: () => Config.TimeIntervalLengthInTenMinuteIncrements,
                setValue: value => Config.TimeIntervalLengthInTenMinuteIncrements = value,
                min: 1
                );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => "Friendship per Interval",
                tooltip: () => "How many points of friendship are added for each time interval. For reference, 1 in-game heart is equal to 250 friendship points.",
                getValue: () => Config.AmountOfFriendshipGainedPerTimeInterval,
                setValue: value => Config.AmountOfFriendshipGainedPerTimeInterval = value
                );

            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "Log Friendship in Console",
                tooltip: () => "When enabled, this will print a message in the SMAPI console everytime friendship is gained.",
                getValue: () => Config.NotifyAboutFriendshipInConsole,
                setValue: value => Config.NotifyAboutFriendshipInConsole = value
                );
        }

        //Apparently getting currentLocation can return null for non-main players when warping, so I decided on this instead
        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            gameLocation = Game1.player.currentLocation;
        }
        private void OnPlayerWarped(object? sender, WarpedEventArgs e)
        {
            gameLocation = e.NewLocation;
        }

        //Triggers every 10 in-game minutes
        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            //Stops if world isn't ready, then resets lists for next time interval
            if (!Context.IsWorldReady) return;
            timeCounter--;
            disposition = Game1.content.Load<Dictionary<string, CharacterData>>("Data\\Characters");
            SetListsForNewTime();
            Vector2 playerLocation = Game1.player.Tile;
            if (gameLocation != null)
            {
                foreach (NPC character in gameLocation.characters)
                {
                    Vector2 npcLocation = character.Tile;
                    if (!disposition.ContainsKey(character.Name)) continue;
                    if (VillagerIsWithinRadiusOfPlayer(playerLocation, npcLocation))
                    {
                        NPCList.nearbyVillagersNow.Add(character.Name.ToString().ToLower(), character.datable.Value);
                    }
                }
            }
            else
            {
                Monitor.Log("Error: Game location is null - I can't search for friends on your map, " +
                    "so I can't help you gain friendship!", LogLevel.Error);
            }

            if (timeCounter <= 0)
            {
                timeCounter = timeIntervalLength;
                //If character is present for 2 or more time changes & friendship isn't maxed, add a point of friendship
                foreach (KeyValuePair<string, Friendship> character in Game1.player.friendshipData.Pairs)
                {
                    if (NPCList.nearbyVillagersBefore.ContainsKey(character.Key.ToLower()))
                    {
                        if (NPCList.nearbyVillagersNow.ContainsKey(character.Key.ToLower()))
                        {
                            try
                            {
                                bool isDatable = NPCList.nearbyVillagersNow[character.Key.ToLower()];

                                if (!isDatable && character.Value.Points < tenHeartPoints)
                                {
                                    character.Value.Points += friendshipGainedPerInterval;
                                }
                                else if (isDatable)
                                {
                                    /* If you're married and below 15 hearts, or
                                     * if you're dating and below 11 hearts, or
                                     * if you're not dating and below 9 hearts,
                                     * add a friendship point. */
                                    if ((character.Value.Status == FriendshipStatus.Married && character.Value.Points < fourteenHeartPoints) ||
                                        (character.Value.Status == FriendshipStatus.Dating && character.Value.Points < tenHeartPoints) ||
                                        (character.Value.Points < eightHeartPoints))
                                    {
                                        character.Value.Points += friendshipGainedPerInterval;
                                    }
                                }
                                if (notifyInConsole)
                                {
                                    bool singlePoint = friendshipGainedPerInterval == 1;
                                    Monitor.Log($"Gained {(singlePoint ? "a " : "")}point{(singlePoint ? "" : "s")} of friendship with {character.Key} at time {e.NewTime}. " +
                                                $"Now at {character.Value.Points} points.", LogLevel.Debug);
                                }
                            }
                            catch
                            {
                                Monitor.Log($"Error: Something happened with {character.Key}. Please report this " +
                                    $"to the evidently ineffective mod author.", LogLevel.Error);
                            }
                        }
                    }
                }
            }
        }
        //Checks if character is within radius
        private bool VillagerIsWithinRadiusOfPlayer(Vector2 playerLocation, Vector2 npcLocation)
        {
            float xDistance = npcLocation.X - playerLocation.X;
            float yDistance = npcLocation.Y - playerLocation.Y;
            return (Math.Abs(xDistance) <= radius && Math.Abs(yDistance) <= radius);
        }

        //Just resets lists for new time
        private void SetListsForNewTime()
        {
            NPCList.nearbyVillagersBefore.Clear();
            foreach (KeyValuePair<string, bool> kvp in NPCList.nearbyVillagersNow)
            {
                NPCList.nearbyVillagersBefore.Add(kvp.Key, kvp.Value);
            }
            NPCList.nearbyVillagersNow.Clear();
        }
    }

    class NPCList
    {
        public static Dictionary<string, bool> nearbyVillagersNow = new Dictionary<string, bool>();
        public static Dictionary<string, bool> nearbyVillagersBefore = new Dictionary<string, bool>();
    }

    //Class for config.json
    class ModConfig
    {
        public int FriendshipRadius { get; set; } = 2;
        public bool NotifyAboutFriendshipInConsole { get; set; } = false;
        public int TimeIntervalLengthInTenMinuteIncrements { get; set; } = 1;
        public int AmountOfFriendshipGainedPerTimeInterval { get; set; } = 1;
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrowdControl.Common;
using JetBrains.Annotations;


// Crowdcontrol code for Pokemon Mystery Dumgeon: Red Rescue Team
// Code adapted from a base of Mega Man 2 example code
namespace CrowdControl.Games.Packs
{
    [UsedImplicitly]
    public class RedRescueTeam : GBAEffectPack
    {
        public RedRescueTeam([NotNull] IPlayer player, [NotNull] Func<CrowdControlBlock, bool> responseHandler, [NotNull] Action<object> statusUpdateHandler) : base(player, responseHandler, statusUpdateHandler) { }

        private volatile bool _quitting = false;
        protected override void Dispose(bool disposing)
        {
            _quitting = true;
            base.Dispose(disposing);
        }

        // Useful information for future effects:
        //in combined WRAM, 0043B0 is XP points
        //004199 is level
        //00419E is current hp
        //0041A0 is max HP
        //4139 is current floor
        //0042AC, 0042B4, 0042BC, 0042C4 are move PP
        //Set 0262AA to -1 for player invis
        //Set 0262BA to -1 for partner invis
        //Set 0262C6 to -1 for part. shadow invis
        //Set 0262B0 to like -60 to get player off screen
        //Set 0262C0 to -60 to get partner off screen
        //0041D6 is next move direction

        // Set up dictionary for useful offsets about leader and partner pokemon
        private static readonly Dictionary<string, (string name, byte[] vals)> leaders =
        new Dictionary<string, (string, byte[])>
        {
            {"first", ("First", new byte[] {0x10, 0x01, 0x00})},
            {"second", ("Second", new byte[] {0x84, 0x00, 0x01, })}

        };

        // Dictionary for weather offsets
        private static readonly Dictionary<string, (string name, uint vals)> weather =
        new Dictionary<string, (string, uint)>
        {
            {"sunny", ("Sunny", 0x00000000)},
            {"sandstorm", ("Sandstorm", 0x00000001)},
            {"cloudy", ("Cloudy", 0x00000002)},
            {"rain", ("Rain", 0x00000003)},
            {"hail", ("Hail", 0x00000004)},
            {"fog", ("Fog", 0x00000005)},
            {"snow", ("Snow", 0x00000006)}

        };

        // constant values for memory addresses
        private const uint ADDR_FLOOR = 0x02004139; // Floor the player is on
        private const uint ADDR_MONEY = 0x02038c08; // Amount of money the player has
        private const uint ADDR_FORCE = 0x0200415a; // Number of turns until unseen force appears
        private const uint ADDR_EXPER = 0x020041a8; // Experience the player has
        private const uint ADDR_CURHP = 0x0200419E; // Current health
        private const uint ADDR_MAXHP = 0x020041A0; // max health
        private const uint ADDR_DIREC = 0x0201737c; // Direction the player is facing
        private const uint ADDR_INPUT = 0x02025638; // input the player is making
        private const uint ADDR_HELD = 0x020041F2; // held item of the player
        private const uint ADDR_CANGO = 0x020041D4; // whether or not imputs are allowed/turn can progress
        private const uint ADDR_MENUINPUT1 = 0x0202563E; // menu open input
        private const uint ADDR_MENUINPUT2 = 0x0202563A; // menu a button and left/right movement
        private const uint ADDR_MENULOCATION = 0x0202EE28; // menu open
        // private const uint ADDR_WEATHER = 0x04A9F78;
       

        // List of effects that are possible to use
        public override List<Effect> Effects
        {
            get
            {
                List<Effect> effects = new List<Effect>
                    {
                        new Effect("Floor down", "floordown", new[] {"quantity99"}),
                        new Effect("Floor up", "floorup", new[] {"quantity99"}),
                        new Effect("Money up", "givemoney", new[] {"quantity9999" }),
                        new Effect("Money down", "stealmoney", new[] {"quantity9999"}),
                        new Effect("Raise Max HP", "raisehealth", new[] {"quantity5"}),
                        new Effect("Lower Max HP", "drophealth", new[] {"quantity5"}),
                        new Effect("Full Heal Party", "heals", new[] {"quantity5"}),
                        new Effect("Unseen Force", "unseen"),
                        new Effect("Grant Immunity", "immunity"),
                        new Effect("Level up", "levelup"),
                        new Effect("Swap leader", "lead", ItemKind.BidWar),
                        new Effect("Swap the Weather", "changeweather", ItemKind.Folder)

                    };


                // Create list of ranges for effects with multiple choices
                effects.AddRange(leaders.Select(t => new Effect($"Change Starter to {t.Value.name} Pokemon", $"leader_{t.Key}", "lead")));
                effects.AddRange(weather.Select(t => new Effect($"Change weather to {t.Value.name} (Current floor only)", $"weather_{t.Key}", "changeweather")));
                return effects;
            }
        }

        // Create quantity sliders for effects that need them
        public override List<Common.ItemType> ItemTypes => new List<Common.ItemType>(new[]
          {
                new Common.ItemType("Quantity", "quantity5", Common.ItemType.Subtype.Slider, "{\"min\":1,\"max\":5}"),
                new Common.ItemType("Quantity", "quantity99", Common.ItemType.Subtype.Slider, "{\"min\":1,\"max\":99}"),
                new Common.ItemType("Quantity", "quantity9999", Common.ItemType.Subtype.Slider, "{\"min\":1,\"max\":9999}"),
            });


        public override List<(string, Action)> MenuActions => new List<(string, Action)>();

        public override Game Game { get; } = new Game(11, "Red Rescue Team", "RedRescueTeam", "GBA", ConnectorType.GBAConnector);

        protected override bool IsReady(EffectRequest request) => Connector.Read8(0x00b1, out byte b) && (b < 0x80);

        protected override void RequestData(DataRequest request) => Respond(request, request.Key, null, false, $"Variable name \"{request.Key}\" not known");

        protected override void StartEffect(EffectRequest request)
        {
            if (!IsReady(request))
            {
                DelayEffect(request, TimeSpan.FromSeconds(5));
                return;
            }

            string[] codeParams = request.FinalCode.Split('_');
            switch (codeParams[0])
            {

                // Change the current floor, min floor of 0 max floor of 99.
                // TODO: reload the floor by spawning a staircase beneath the player.
                case "floorup":
                    {
                        if (!byte.TryParse(codeParams[1], out byte floor))
                        {
                            Respond(request, EffectStatus.FailTemporary, "Invalid floor quantity.");
                            return;
                        }
                        TryEffect(request,
                            () => Connector.RangeAdd8(ADDR_FLOOR, -floor, 0, 99, false),
                            () => true,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} sent you {floor} floors down.");
                            });
                        return;
                    }
                // Change the current floor, min floor of 0 max floor of 99.
                // TODO: reload the floor by spawning a staircase beneath the player.
                case "floordown":
                    {
                        if (!byte.TryParse(codeParams[1], out byte floor))
                        {
                            Respond(request, EffectStatus.FailTemporary, "Invalid floor quantity.");
                            return;
                        }
                        TryEffect(request,
                            () => Connector.RangeAdd8(ADDR_FLOOR, floor, 0, 99, false),
                            () => true,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} sent you {floor} floors down.");
                            });
                        return;
                    }

                // Give the player some money
                case "givemoney":
                    {
                        if (!uint.TryParse(codeParams[1], out uint money))
                        {
                            Respond(request, EffectStatus.FailTemporary, "Invalid money quantity.");
                            return;
                        }
                        TryEffect(request,
                            () => Connector.RangeAdd32(ADDR_MONEY, money, 0, 9999, false),
                            () => true,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} sent you {money} dollars.");
                            });
                        return;
                    }

                // Take some money from the player
                case "stealmoney":
                    {
                        if (!uint.TryParse(codeParams[1], out uint money))
                        {
                            Respond(request, EffectStatus.FailTemporary, "Invalid money quantity.");
                            return;
                        }
                        TryEffect(request,
                            () => Connector.RangeAdd32(ADDR_MONEY, -money, 0, 9999, false),
                            () => true,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} stole {money} dollars from you.");
                            });
                        return;
                    }

                // Raise the player's max health
                case "raisehealth":
                    {
                        if (!uint.TryParse(codeParams[1], out uint health))
                        {
                            Respond(request, EffectStatus.FailTemporary, "Invalid health quantity.");
                            return;
                        }
                        TryEffect(request,
                            () => Connector.RangeAdd32(ADDR_MAXHP, health, 0, 999, false),
                            () => true,
                            () =>
                            {
                                
                                Connector.SendMessage($"{request.DisplayViewer} raised your health by {health}.");
                                
                            });
                        return;
                    }
                // Lower the player's max health
                case "drophealth":
                    {
                        if (!uint.TryParse(codeParams[1], out uint health))
                        {
                            Respond(request, EffectStatus.FailTemporary, "Invalid health quantity.");
                            return;
                        }
                        TryEffect(request,
                            () => Connector.RangeAdd32(ADDR_MAXHP, -health, 0, 999, false),
                            () => true,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} lowered your health by {health}.");
                            });
                        return;
                    }

                // Start unseen force timer (eseentially limit number of steps allowed on current floor)
                // TODO: Prevent further uses and refund if the unseen force is already active
                case "unseen":
                    {
                        TryEffect(request,
                            () => Connector.Write16(ADDR_FORCE, 150),
                            () => true,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} summoned the unseen force.");
                            });
                        return;
                    }

                // Freeze the player's current health, preventing it from changing for 30 seconds
                // Consider: Make the immunity a number of turns instead? Maybe ten?
                case "immunity":
                    StartTimed(request,
                          () => Connector.Read8(ADDR_CURHP, out byte b),
                          () =>
                          {
                              Connector.Read8(ADDR_CURHP, out byte b);
                              bool result = Connector.Freeze8(ADDR_CURHP, b);
                              if (result) { Connector.SendMessage($"{request.DisplayViewer} gave you immunity!"); }
                              return result;
                          },
                          TimeSpan.FromSeconds(45));
                    return;

                // Swap the current leader of the party
                // Note: This will not work if the party is only one member, such as in purity forest
                case "leader":
                    Dictionary<string, Func<bool>> _bwar = new Dictionary<string, Func<bool>>
                {
                    {"first", ()=>setLeader(new byte[] {0x10, 0x01, 0x00})},
                    {"second", ()=>setLeader(new byte[] {0x84, 0x00, 0x01})}

                };


                    BidWar(request, _bwar.Where(s => s.Key == codeParams[1]).ToDictionary(dict => dict.Key, dict => dict.Value));
                    
                    break;

                // Lock the current player's money
                // Currently unused, effect would be designed to lock the money for an entire floor
                case "moneylock":
                    StartTimed(request,
                          () => Connector.Read8(ADDR_CURHP, out byte b),
                          () =>
                          {
                              Connector.Read8(ADDR_CURHP, out byte b);
                              bool result = Connector.Freeze8(ADDR_CURHP, b);
                              if (result) { Connector.SendMessage($"{request.DisplayViewer} gave you immunity!"); }
                              return result;
                          },
                          TimeSpan.FromSeconds(45));
                    return;

                // Work in progress levelup function
                // Modifying the player's level does not affect stats - need to level up naturally in order to guarantee stat increase
                // This function would take control of inputs to make the user eat a rare candy. This was inconsistent, so this is currently defunct with plans to redo later. 
                case "levelup":
                    {
                        TryEffect(request,
                            () => Connector.Read16(ADDR_HELD, out ushort b),
                            () => true,
                            () =>
                            {
                                // Save the current held item
                                Connector.Read16(ADDR_HELD, out ushort b);
                                bool result = true;
                                Connector.Freeze16(ADDR_MENUINPUT1, 0);
                                Connector.Freeze16(ADDR_MENUINPUT2, 0);
                                Connector.Freeze16(ADDR_INPUT, 0);

                                System.Threading.Thread.Sleep(500); // Replace this with a proper wait for turn function by polling current turn

                                // This way of doing this isn't great, but it works as a placeholder. Need to run some tests to see if this is actually consistent.
                                // Basically just freeze the player's inputs, then force inputs of your own. 
                                // Move away from using sleep and instead check memory values to determine if the steps have been completed
                                Connector.Freeze16(ADDR_MENUINPUT1, 2);
                                System.Threading.Thread.Sleep(30);
                                Connector.Freeze16(ADDR_MENUINPUT1, 0);
                                System.Threading.Thread.Sleep(34);
                                Connector.Freeze16(ADDR_MENULOCATION, 1);
                                System.Threading.Thread.Sleep(34);
                                Connector.Freeze16(ADDR_MENUINPUT2, 1);
                                System.Threading.Thread.Sleep(34);
                                Connector.Freeze16(ADDR_MENUINPUT2, 0);
                                System.Threading.Thread.Sleep(34);
                                Connector.Freeze16(ADDR_MENUINPUT2, 32);
                                System.Threading.Thread.Sleep(34);
                                Connector.Freeze16(ADDR_MENUINPUT2, 0);
                                System.Threading.Thread.Sleep(34);
                                Connector.Freeze16(ADDR_MENULOCATION, 0);
                                System.Threading.Thread.Sleep(34);
                                Connector.Freeze16(ADDR_MENUINPUT2, 1);
                                System.Threading.Thread.Sleep(17);
                                Connector.Freeze16(ADDR_MENUINPUT2, 0);
                                Connector.Freeze16(ADDR_MENULOCATION, 1);
                                System.Threading.Thread.Sleep(34);
                                System.Threading.Thread.Sleep(34);
                                Connector.Freeze16(ADDR_MENUINPUT2, 1);
                                System.Threading.Thread.Sleep(30);
                                Connector.Freeze16(ADDR_MENUINPUT2, 1);
                                System.Threading.Thread.Sleep(34);

                                // Unfreeze all, returning control to the user
                                Connector.Unfreeze(ADDR_MENULOCATION);
                                Connector.Unfreeze(ADDR_INPUT);
                                Connector.Unfreeze(ADDR_MENUINPUT1);
                                Connector.Unfreeze(ADDR_MENUINPUT2);
                                Connector.Unfreeze(ADDR_INPUT);

                                if (result) { Connector.SendMessage($"{request.DisplayViewer} leveled you up!"); }
                            });
                        return;
                    }

                // Change the weather to the desired value
                case "weather":
                    {
                        ForceWeather(weather[codeParams[1]].vals);
                        return;
                    }

            }
        }

        // Helper method to swap the partner and leader
        protected bool setLeader(byte[] values)
        {

            // Write certain values to memory in order to change control, camera, and other values
            Connector.Write8(0x02003BAC, values[0]);
            Connector.Write8(0x0203B450, values[0]);
            Connector.Write8(0x0201BCEC, values[0]);
            Connector.Write8(0x02004197, values[1]);
            Connector.Write8(0x0203415A, values[1]);
            Connector.Write8(0x020381FA, values[1]);
            Connector.Write8(0x0200439F, values[2]);
            Connector.Write8(0x020313A2, values[2]);
            Connector.Write8(0x0203825E, values[2]);

            return true;


        }

        // Helper method for changing the weather to whatever is wanted
        protected bool ForceWeather(uint offset)
        {
            uint weatherVal = 0x02011D64;

            // Loop through all weathers and turn them off
            for(uint i=0; i<8; i++)
            {
                Connector.Write8(weatherVal + i, 0);
            }

            // Turn on desired weather
            Connector.Write8(weatherVal + offset, 1);

            return true;


        }

        // Stop timed effects, triggered automatically after timer elapses
        protected override bool StopEffect(EffectRequest request)
        {
            switch (request.BaseCode)
            {
                case "immunity":
                    {
                        // Unfreeze current HP and display a message, return true to show that effect is ended
                        Connector.Unfreeze(ADDR_CURHP);
                        Connector.SendMessage($"{request.DisplayViewer}'s immunity wore off.");
                        return true;
                    }
                default:
                    return true;
            }
        }




    }



}



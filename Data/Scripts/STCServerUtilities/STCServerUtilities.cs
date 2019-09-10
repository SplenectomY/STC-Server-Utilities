using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Splen.ServerUtilities
{
    public class WeaponHeatWarningGrid
    {
        public Dictionary<long, int> blockWarnings = new Dictionary<long, int>();

        public WeaponHeatWarningGrid(MyCubeGrid grid)
        {
            if (grid == null)
                return;

            if (!Core.Instance.playerWeaponWarningLevels.ContainsKey(grid.EntityId))
                Core.Instance.playerWeaponWarningLevels.Add(grid.EntityId, this);
        }
    }

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Core : MySessionComponentBase
    {
        #region config

        public int debug = 0;

        /// <summary>
        /// This determines how far out from a planet asteroids can spawn. Values lower than 3f will cause little/no asteroids to spawn around moons.
        /// </summary>
        private readonly float asteroidZoneMultiplier = 3f;

        /// <summary>
        /// SteamID of the user who will receive in-game debug messages.
        /// </summary>
        public readonly ulong AdminSteamId = 76561198047667745;

        /// <summary>
        /// Maximum overclock multiplier for thrusters
        /// </summary>
        private readonly float maxThrusterOverclock = 15f;

        private const double cleanupDistance = 25000D;

        #endregion

        private bool m_initialize = false;
        public List<BoundingSphereD> AsteroidSafeZones = new List<BoundingSphereD>();
        private List<long> m_safeAsteroidList = new List<long>();
        public long AdminIdentityId = -1L;
        public static Core Instance;
        public int Timer;
        public bool DisableChatDebugLogging = false;
        public bool AsteroidEventsHooked;
        private List<long> existingGridEntities = new List<long>();

        private readonly Action<byte[]> _messageHandler = new Action<byte[]>(HandleMessage);

        public Dictionary<long, ThrusterHeatWarningGrid> playerThrusterWarningLevels = new Dictionary<long, ThrusterHeatWarningGrid>();
        public Dictionary<long, WeaponHeatWarningGrid> playerWeaponWarningLevels = new Dictionary<long, WeaponHeatWarningGrid>();
        public Dictionary<long, int> LastThrusterHeatNoteSentToPlayer = new Dictionary<long, int>();
        public Dictionary<long, int> LastWeaponHeatNoteSentToPlayer = new Dictionary<long, int>();
        public Dictionary<long, int> TimeThrusterNoteWasSentToPlayer = new Dictionary<long, int>();
        public Dictionary<long, int> TimeWeaponNoteWasSentToPlayer = new Dictionary<long, int>();
        public Dictionary<long, CubeGrid> CubeGridInfo = new Dictionary<long, CubeGrid>();

        private IMyTerminalControlOnOffSwitch SafetySwitch;
        private IMyTerminalControlSlider ThrusterOverclock;
        private IMyTerminalAction safetySwitchAction;
        private IMyTerminalAction overclockActionIncrease;
        private IMyTerminalAction overclockActionDecrease;

        #region overrides

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            Instance = this;
            base.Init(sessionComponent);
        }

        public override void BeforeStart()
        {
            MyAPIGateway.Multiplayer.RegisterMessageHandler(35243, _messageHandler);

            if (Sync.IsServer)
            {
                InitAsteroidRestrictions();
                InitSpawnShipParachuteHandling();
            }

            InitControls();

            MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter += CustomActionGetter;

            AdjustDefinitions();
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Session == null)
                return;

            if (!m_initialize)
            {
                AdminIdentityId = MyAPIGateway.Players.TryGetIdentityId(Instance.AdminSteamId);

                if (Sync.IsClient && !Sync.IsServer)
                    Messaging.SendMessageToServer(new MessageAsteroidSync());

                if (Sync.IsServer) CleanupGrids();
                m_initialize = true;
            }

            if (Sync.IsServer && Timer++ % 180 == 0)
                MyAPIGateway.Parallel.Start(() => CheckPlayerNotifications());
        }

        #endregion

        #region Parachutes

        private void InitSpawnShipParachuteHandling()
        {
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, (x) => x is IMyCubeGrid);
            foreach (var entity in entities)
                existingGridEntities.Add(entity.EntityId);

            MyAPIGateway.Entities.OnEntityAdd += OpenParachutes;
        }

        private void OpenParachutes(IMyEntity ent)
        {
            MyAPIGateway.Parallel.Start(() => 
            { 
                try
                {
                    if (ent == null || !(ent is IMyCubeGrid) || existingGridEntities.Contains(ent.EntityId))
                        return;

                    var grid = ent as IMyCubeGrid;

                    if (grid == null || !grid.DisplayName.ToLower().Contains("pod"))
                        return;

                    var blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks, (x) => x.FatBlock != null && x.FatBlock is IMyParachute);

                    if (blocks.Count < 1)
                        return;

                    MyAPIGateway.Parallel.Sleep(1000);

                    foreach (var block in blocks)
                    {
                        var chute = block.FatBlock as IMyParachute;
                        if (chute == null)
                            continue;

                        MyLog.Default.WriteLineAndConsole($"STC Server Utilities: Deploying parachute on grid {grid.DisplayName}.");
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            {
                                if (chute != null) chute.OpenDoor();

                                var steamId = MyAPIGateway.Players.TryGetSteamId(chute.OwnerId);

                                if (steamId != 0)
                                {
                                    var message = new MessageOpenParachute();
                                    message.EntityId = chute.EntityId;
                                    Messaging.SendMessageToPlayer(steamId, message);
                                }
                            }
                            catch (Exception e)
                            { Debug.HandleException(e); }
                        });
                    }
                }
                catch (Exception e)
                { Debug.HandleException(e); }
            });
            
        }

        #endregion

        private static void HandleMessage(byte[] message)
        {
            //Instance.Debug("HandleMessage", 1);
            Messaging.ProcessData(message);
        }

        #region Grid Cleanup

        private bool IsPhysicalSubgrid(IMyCubeGrid cubeGrid)
        {
            var group = MyAPIGateway.GridGroups.GetGroup(cubeGrid, GridLinkTypeEnum.Physical);
            if (group.Count > 1) return true;
            return false;
        }

        private bool IsNPCGrid(IMyCubeGrid cubeGrid)
        {
            var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(cubeGrid.BigOwners.FirstOrDefault());
            if (faction != null && faction.IsEveryoneNpc()) return true;
            return false;
        }

        private bool IsGridTooFarFromPlayers(IMyCubeGrid cubeGrid, double distance = cleanupDistance)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, (x) => !x.IsBot);
            foreach (var player in players)
            {
                var dis = Vector3D.Distance(player.GetPosition(), cubeGrid.GetPosition());
                if (dis < distance) return false;
            }
            return true;
        }

        private void DeleteGrid(IMyCubeGrid cubeGrid, string reason)
        {
            MyLog.Default.WriteLineAndConsole($"DELETED GRID: {cubeGrid.DisplayName}. Reason: {reason}.");
            cubeGrid.Delete();
            cubeGrid.Close();
        }

        private void CleanupGrids()
        {
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, (x) => x != null && x is IMyCubeGrid && (x as IMyCubeGrid) != null);

            foreach (var entity in entities.ToList())
            {
                var cubeGrid = entity as IMyCubeGrid;

                switch (cubeGrid.DisplayName)
                {
                    case "Moon Drop Pod":
                    case "Respawn Moon Pod":
                    case "Respawn Planet Pod":
                    case "Respawn Space Pod":
                        DeleteGrid(cubeGrid, "DEFAULT NAME");
                        continue;
                }

                if (IsPhysicalSubgrid(cubeGrid)) continue;

                /* currently disabled since keen apparently likes to rename grids at random
                if (cubeGrid.DisplayName.ToLower().Contains("static grid") 
                  || cubeGrid.DisplayName.ToLower().Contains("small grid")
                  || cubeGrid.DisplayName.ToLower().Contains("large grid")
                  || cubeGrid.DisplayName.ToLower().Contains("static ship")
                  || cubeGrid.DisplayName.ToLower().Contains("small ship")
                  || cubeGrid.DisplayName.ToLower().Contains("large ship"))
                {
                    MyLog.Default.WriteLineAndConsole($"DELETED GRID: {cubeGrid.DisplayName}. Reason: DEFAULT NAME.");
                    cubeGrid.Delete();
                    cubeGrid.Close();
                    continue;
                }
                */

                if (cubeGrid.BigOwners.Count < 1 && cubeGrid.SmallOwners.Count < 1)
                {
                    DeleteGrid(cubeGrid, "NO OWNER OR PCU OVERAGE");
                    continue;
                }

                if (IsNPCGrid(cubeGrid))
                {
                    DeleteGrid(cubeGrid, "OWNED BY NPC FACTION");
                    continue;
                }

                if (!((MyCubeGrid)cubeGrid).IsPowered)
                {
                    DeleteGrid(cubeGrid, "NO POWER");
                    continue;
                }
            }
        }

        #endregion

        #region Actions/Controls

        private void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
        {
            if (block == null || block.BlockDefinition.IsNull() || block.BlockDefinition.SubtypeName == null || block.BlockDefinition.TypeIdString != "MyObjectBuilder_Thrust")
                return;

            actions.Add(safetySwitchAction);
            actions.Add(overclockActionIncrease);
            actions.Add(overclockActionDecrease);
        }

        private void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            try
            {
                if (block == null || block.BlockDefinition.IsNull())
                    return;

                if (block.BlockDefinition.TypeIdString == "MyObjectBuilder_Thrust")
                {
                    controls.Add(ThrusterOverclock);
                    controls.Add(SafetySwitch);
                } 

                switch (block.BlockDefinition.SubtypeName)
                {
                    case "ConveyorHydrogenIntake":
                    case "SmallConveyorHydrogenIntake":
                    case "ConveyorHydrogenIntakeSlope":
                    case "SmallConveyorHydrogenIntakeSlope":
                    case "HydrogenIntake":
                    case "SmallHydrogenIntake":
                        foreach (var item in controls)
                        {
                            switch (item.Id)
                            {
                                case "ShowInInventory":
                                case "UseConveyor":
                                    item.Visible = (x) =>
                                    {
                                        if (x is IMyGasGenerator && x.GameLogic.GetAs<HydrogenIntake>() != null)
                                            return false;

                                        return true;
                                    };
                                    item.Enabled = (x) =>
                                    {
                                        if (x is IMyGasGenerator && x.GameLogic.GetAs<HydrogenIntake>() != null)
                                            return false;

                                        return true;
                                    };
                                    break;
                            }
                        }
                        break;
                }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void InitControls()
        {
            ThrusterOverclock = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyThrust>("Overclock");
            ThrusterOverclock.Title = MyStringId.GetOrCompute("Overclock");
            ThrusterOverclock.Tooltip = MyStringId.GetOrCompute("Multiplies thruster power at the cost of heat and degradation.");
            ThrusterOverclock.SetLimits(1, maxThrusterOverclock);
            ThrusterOverclock.SupportsMultipleBlocks = true;
            ThrusterOverclock.Getter = (x) =>
            {
                if (x == null || x.GameLogic == null)
                    return 1f;

                var logic = x.GameLogic.GetAs<ThrustOverride>();

                return logic != null ? logic.Overclock : 1f;
            };

            ThrusterOverclock.Setter = (x, y) =>
            {
                var logic = x.GameLogic.GetAs<ThrustOverride>();

                if (logic != null)
                {
                    logic.Overclock = y;

                    if (Sync.IsClient)
                    {
                        var message = new MessageThrusterVariables();
                        message.EntityId = logic.Entity.EntityId;
                        message.UpdateCustomData = true;
                        message.Overclock = logic.Overclock;
                        message.SafetySwitch = logic.SafetySwitch;
                        Messaging.SendMessageToServer(message);
                    }
                }
            };

            ThrusterOverclock.Writer = (x, y) =>
            {
                if (x == null || x.GameLogic == null)
                    return;

                var logic = x.GameLogic.GetAs<ThrustOverride>();

                if (logic != null)
                    y.Append(logic.Overclock.ToString() + "x");
            };

            SafetySwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyThrust>("SafetySwitch");
            SafetySwitch.Title = MyStringId.GetOrCompute("Safety Switch");
            SafetySwitch.Tooltip = MyStringId.GetOrCompute("When enabled, reduces thrust when necessary to prevent damage from excessive heat.\nTurning this off can allow steady thrust over longer periods of time,\nwhich can be useful in emergencies.");
            SafetySwitch.OnText = MyStringId.GetOrCompute("On");
            SafetySwitch.OffText = MyStringId.GetOrCompute("Off");
            SafetySwitch.SupportsMultipleBlocks = true;

            SafetySwitch.Getter = x =>
            {
                if (x == null || x.GameLogic == null)
                    return true;

                var logic = x.GameLogic.GetAs<ThrustOverride>();

                return logic != null ? logic.SafetySwitch : true;
            };

            SafetySwitch.Setter = (x, y) =>
            {
                Debug.Write("Attempting to set safety switch", 1);
                if (x == null || x.GameLogic == null)
                    return;

                var logic = x.GameLogic.GetAs<ThrustOverride>();

                logic.SafetySwitch = y;

                if (Sync.IsClient)
                    Debug.Write("Set safety switch on client", 1);

                if (Sync.IsServer)
                    Debug.Write("Set safety switch on server", 1);

                if (Sync.IsClient)
                {
                    var message = new MessageThrusterVariables();
                    message.UpdateCustomData = true;
                    message.EntityId = logic.Entity.EntityId;
                    message.Overclock = logic.Overclock;
                    message.SafetySwitch = logic.SafetySwitch;
                    Messaging.SendMessageToServer(message);
                }
            };

            safetySwitchAction = MyAPIGateway.TerminalControls.CreateAction<IMyThrust>("SafetySwitchAction");
            safetySwitchAction.Enabled = (x) => true;
            safetySwitchAction.Name = new StringBuilder(string.Format("Safety Toggle On/Off"));
            safetySwitchAction.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            safetySwitchAction.Action = (x) =>
            {
                if (x == null || SafetySwitch == null)
                    return;

                SafetySwitch.Setter(x, !SafetySwitch.Getter(x));
            };
            safetySwitchAction.ValidForGroups = true;
            safetySwitchAction.Writer = (x, y) =>
            {
                if (x == null || SafetySwitch == null)
                    return;

                y.Append(SafetySwitch.Getter(x) ? "On" : "Off");
            };
            safetySwitchAction.InvalidToolbarTypes = new List<MyToolbarType>();

            overclockActionIncrease = MyAPIGateway.TerminalControls.CreateAction<IMyThrust>("OverclockActionIncrease");
            overclockActionIncrease.Enabled = (x) => true;
            overclockActionIncrease.Name = new StringBuilder(string.Format("Increase Overclock"));
            overclockActionIncrease.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            overclockActionIncrease.ValidForGroups = true;
            overclockActionIncrease.Action = (x) =>
            {
                if (x == null || ThrusterOverclock == null)
                    return;

                ThrusterOverclock.Setter(x, Math.Min(ThrusterOverclock.Getter(x) + 1f, maxThrusterOverclock));
            };
            overclockActionIncrease.Writer = (x, y) =>
            {
                if (x == null || ThrusterOverclock == null)
                    return;
                y.Append(ThrusterOverclock.Getter(x).ToString() + "x");
            };
            overclockActionIncrease.InvalidToolbarTypes = new List<MyToolbarType>();

            overclockActionDecrease = MyAPIGateway.TerminalControls.CreateAction<IMyThrust>("OverclockActionDecrease");
            overclockActionDecrease.Enabled = (x) => true;
            overclockActionDecrease.Name = new StringBuilder(string.Format("Decrease Overclock"));
            overclockActionDecrease.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            overclockActionDecrease.ValidForGroups = true;
            overclockActionDecrease.Action = (x) =>
            {
                if (x == null || ThrusterOverclock == null)
                    return;

                ThrusterOverclock.Setter(x, Math.Max(ThrusterOverclock.Getter(x) - 1f, 1f));
            };
            overclockActionDecrease.Writer = (x, y) =>
            {
                if (x == null || ThrusterOverclock == null)
                    return;
                y.Append(ThrusterOverclock.Getter(x).ToString() + "x");
            };
            overclockActionDecrease.InvalidToolbarTypes = new List<MyToolbarType>();
        }

        #endregion

        private void CheckPlayerNotifications()
        {
            try
            {
                foreach (var item in new Dictionary<long, int>(TimeThrusterNoteWasSentToPlayer))
                    if (item.Value != 0 && Timer > item.Value + 180)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            {
                                MyVisualScriptLogicProvider.RemoveNotification(LastThrusterHeatNoteSentToPlayer[item.Key], item.Key);
                                TimeThrusterNoteWasSentToPlayer[item.Key] = 0;
                            }
                            catch (Exception e)
                            { Debug.HandleException(e); }
                        });
                        
                    }

                foreach (var item in new Dictionary<long, int>(TimeWeaponNoteWasSentToPlayer))
                    if (item.Value != 0 && Timer > item.Value + 180)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            {
                                MyVisualScriptLogicProvider.RemoveNotification(LastWeaponHeatNoteSentToPlayer[item.Key], item.Key);
                                TimeWeaponNoteWasSentToPlayer[item.Key] = 0;
                            }
                            catch (Exception e)
                            { Debug.HandleException(e); }
                        });

                    }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void AdjustDefinitions()
        {
            foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var spotlight = def as MyReflectorBlockDefinition;
                if (spotlight != null)
                {
                    spotlight.LightReflectorRadius.Max = 3000f;
                    spotlight.LightReflectorRadius.Default = 3000f;
                }

                var bd = def as MyCubeBlockDefinition;
                if (bd == null) continue;

                string idString = string.Format("{0}", bd.Id);

                if (idString.Contains("MyObjectBuilder_LargeGatlingTurret") && bd.PCU == 1) bd.PCU = 225;
                else if (idString.Contains("MyObjectBuilder_LargeMissileTurret") && bd.PCU == 1) bd.PCU = 275;
                else if (idString.Contains("MyObjectBuilder_InteriorTurret") && bd.PCU == 1) bd.PCU = 125;
                else if (idString.Contains("MyObjectBuilder_SmallMissileLauncher") && bd.PCU == 1) bd.PCU = 150;

                else if (idString.Contains("MyObjectBuilder_AdvancedDoor/VCZ_Elevator")) bd.PCU = 125;
                else if (idString.Contains("MyObjectBuilder_AdvancedDoor/LargeShipUsableLadderRetractable")) bd.PCU = 125;
                else if (idString.Contains("MyObjectBuilder_AdvancedDoor/SmallShipUsableLadderRetractable")) bd.PCU = 125;
                else if (idString.Contains("MyObjectBuilder_AdvancedDoor")) bd.PCU = 50;

                else if (idString.Contains("MyObjectBuilder_AirVent")) bd.PCU = 10;
                else if (idString.Contains("MyObjectBuilder_Assembler")) bd.PCU = 40;
                else if (idString.Contains("MyObjectBuilder_BatteryBlock")) bd.PCU = 15;
                else if (idString.Contains("MyObjectBuilder_ButtonPanel")) bd.PCU = 5;
                else if (idString.Contains("MyObjectBuilder_CameraBlock")) bd.PCU = 25;
                else if (idString.Contains("MyObjectBuilder_CargoContainer")) bd.PCU = 10;
                else if (idString.Contains("MyObjectBuilder_Cockpit")) bd.PCU = 15;
                else if (idString.Contains("MyObjectBuilder_ConveyorSorter")) bd.PCU = 25;
                else if (idString.Contains("MyObjectBuilder_Conveyor")) bd.PCU = 10;
                else if (idString.Contains("MyObjectBuilder_Door")) bd.PCU = 115;
                else if (idString.Contains("MyObjectBuilder_Drill")) bd.PCU = 190;
                else if (idString.Contains("MyObjectBuilder_ExtendedPistonBase")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_Gyro")) bd.PCU = 50;
                else if (idString.Contains("MyObjectBuilder_InteriorLight")) bd.PCU = 25;
                else if (idString.Contains("MyObjectBuilder_LandingGear")) bd.PCU = 35;
                else if (idString.Contains("MyObjectBuilder_MotorAdvancedRotor")) bd.PCU = 100;

                else if (idString.Contains("MyObjectBuilder_OreDetector/NaniteUltrasonicHammer")) bd.PCU = 225;
                else if (idString.Contains("MyObjectBuilder_OreDetector")) bd.PCU = 40;

                else if (idString.Contains("MyObjectBuilder_OxygenGenerator")) bd.PCU = 40;
                else if (idString.Contains("MyObjectBuilder_OxygenTank")) bd.PCU = 25;
                else if (idString.Contains("MyObjectBuilder_PistonTop")) bd.PCU = 5;
                else if (idString.Contains("MyObjectBuilder_Projector")) bd.PCU = 50;
                else if (idString.Contains("MyObjectBuilder_RadioAntenna")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_Reactor")) bd.PCU = 25;
                else if (idString.Contains("MyObjectBuilder_Refinery")) bd.PCU = 90;
                else if (idString.Contains("MyObjectBuilder_ReflectorLight")) bd.PCU = 25;
                else if (idString.Contains("MyObjectBuilder_RemoteControl")) bd.PCU = 25;
                else if (idString.Contains("MyObjectBuilder_ShipGrinder")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_ShipWelder") && bd.PCU == 1) bd.PCU = 150;
                else if (idString.Contains("MyObjectBuilder_SmallGatlingGun")) bd.PCU = 80;

                else if (idString.Contains("MyObjectBuilder_SolarPanel")) bd.PCU = 55;
                else if (idString.Contains("MyObjectBuilder_TextPanel")) bd.PCU = 5;

                else if (idString.Contains("MyObjectBuilder_Thrust") && bd.PCU == 1) bd.PCU = 15;

                else if (idString.Contains("MyObjectBuilder_UpgradeModule/Radar")) bd.PCU = 250;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/DSControl")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/Emitter")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/LargeEnhancer")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/LargeShieldModulator")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/LargeHackingBlock")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/LargeFirewallBlock")) bd.PCU = 25;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/MA_Navball")) bd.PCU = 25;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/PlanetaryEmitterLarge")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/SmallEnhancer")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/SmallHackingBlock")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_UpgradeModule/SmallShieldModulator")) bd.PCU = 100;

                else if (idString.Contains("MyObjectBuilder_Warhead/ThermoNuclearLargeWarhead")) bd.PCU = 100;
                else if (idString.Contains("MyObjectBuilder_Warhead/ThermoNuclearSmallWarhead")) bd.PCU = 50;

                else MyLog.Default.WriteLine(string.Format("{0} | PCU: {1}", bd.Id, bd.PCU));
            }
        }

        #region AsteroidHandling
        // These methods create bounding sphere zones around planets and restrict asteroids to only exist within these areas. 

        /// <summary>
        /// Call on SERVER on init (Seems to work in BeforeStart). 
        /// Call on CLIENT only AFTER client receives zone location/radius messages from server.
        /// Client must build its own BoundingSphereD zones as planets dont seem to immediately
        /// available on client init.
        /// </summary>
        public void InitAsteroidRestrictions()
        {
            if (Sync.IsServer)
                GetPlanetZones();

            if (Instance.AsteroidEventsHooked)
                return;

            if (Sync.IsClient) MyAPIGateway.Entities.OnEntityAdd += AsteroidEntityEventHandler;
            if (Sync.IsServer && Sync.IsDedicated) MyAPIGateway.Entities.OnEntityAdd += AsteroidEntityEventHandlerParallel;
            Instance.AsteroidEventsHooked = true;
            CheckExistingAsteroids();
        }

        private void GetPlanetZones()
        {
            var planets = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(planets, x => x != null && x is MyVoxelBase && ((x as MyVoxelBase) is MyPlanet) && !(x as MyVoxelBase).Closed);

            if (planets.Count < 1)
                MyLog.Default.WriteLineAndConsole("ASTEROID RESTRICTIONS WARNING: NO PLANETS WERE FOUND!");

            foreach (var item in planets)
            {
                var planet = item as MyPlanet;

                if (planet == null)
                {
                    MyLog.Default.WriteLineAndConsole("ASTEROID RESTRICTIONS WARNING: Planet was null!");
                    continue;
                }

                MyLog.Default.WriteLineAndConsole($"Adding a new asteroid safe zone at planet '{planet.Name}' " +
                  $"{planet.PositionComp.GetPosition().ToString()} with radius {planet.MaximumRadius * asteroidZoneMultiplier}");

                AsteroidSafeZones.Add(new BoundingSphereD(planet.PositionComp.GetPosition(), planet.MaximumRadius * asteroidZoneMultiplier));
            }
        }

        public void CheckExistingAsteroids()
        {
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, x => true);
            foreach (var item in entities)
                CheckAsteroidEntity(item);
        }

        public void AsteroidEntityEventHandler(IMyEntity ent)
        {
            CheckAsteroidEntity(ent);
        }

        public void AsteroidEntityEventHandlerParallel(IMyEntity ent)
        {
            MyAPIGateway.Parallel.Start(() => CheckAsteroidEntityParallel(ent));
        }

        /// <summary>
        /// Deletes an asteroid if it's outside of all defined bounding boxes.
        /// Designed to run as a parallel task
        /// </summary>
        /// <param name="ent"></param>
        private void CheckAsteroidEntityParallel(IMyEntity ent)
        {
            try
            {
                if (ent == null || !(ent is MyVoxelBase) || ((ent as MyVoxelBase) is MyPlanet) || (ent as MyVoxelBase).Closed || AsteroidSafeZones.Count < 1)
                    return;

                var roid = ent as MyVoxelBase;

                if (roid == null && Debug.Write("Asteroid was null!", 2, debug))
                    return;

                foreach (var zone in AsteroidSafeZones)
                {
                    var result = zone.Contains(roid.PositionComp.GetPosition());
                    if (result == ContainmentType.Contains && Debug.Write("Asteroid found safe in planet zone.", 1, debug))                       
                        return;
                }

                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    try
                    {
                        if (roid != null && ent != null)
                        {
                            roid.Delete();
                            roid.Close();
                        }
                    }
                    catch (Exception e)
                    {
                        MyVisualScriptLogicProvider.ShowNotificationToAll("WARNING!!! An asteroid was not properly pruned severside. Contact an administrator immediately.", 5000, "Red");
                        MyLog.Default.WriteLineAndConsole("WARNING!!! WARNING!!! WARNING!!!\nWARNING!!! WARNING!!! WARNING!!!");
                        MyLog.Default.WriteLineAndConsole("STC Server Utilities: AN ASTEROID WAS NOT PROPERLY DELETED SERVERSIDE. THIS SHOULD NEVER HAPPEN.");
                        MyLog.Default.WriteLineAndConsole("Please take the appropriate measures to fix this problem at the code level.");
                        MyLog.Default.WriteLineAndConsole("WARNING!!! WARNING!!! WARNING!!!\nWARNING!!! WARNING!!! WARNING!!!");
                        Debug.HandleException(e); 
                    }
                });
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        /// <summary>
        /// Deletes an asteroid if it's outside of all defined bounding boxes or is not in a list of previously found asteroids
        /// </summary>
        /// <param name="ent"></param>
        private void CheckAsteroidEntity(IMyEntity ent)
        {
            try
            {
                if (ent == null || !(ent is MyVoxelBase) || ((ent as MyVoxelBase) is MyPlanet) || (ent as MyVoxelBase).Closed || AsteroidSafeZones.Count < 1)
                    return;

                var roid = ent as MyVoxelBase;

                if (roid == null && Debug.Write("Asteroid was null!", 2, debug))
                    return;

                if (m_safeAsteroidList.ToList().Contains(roid.EntityId) && Debug.Write("Asteroid entityId found safe list.", 2, debug))
                    return;

                foreach (var zone in AsteroidSafeZones)
                {
                    var result = zone.Contains(roid.PositionComp.GetPosition());
                    if (result == ContainmentType.Contains && Debug.Write("Asteroid found safe in planet zone.", 1, debug))
                    {
                        m_safeAsteroidList.Add(roid.EntityId);
                        return;
                    }
                }

                Debug.Write("Deleting asteroid", 1, debug);

                try
                {
                    roid.Delete();
                    roid.Close();
                }
                catch (Exception e)
                { Debug.HandleException(e); }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }
    }

    #endregion

    public static class Extensions
    {
        public static bool TryGetPlayer(IMyPlayerCollection collection, long identityId, out IMyPlayer player)
        {
            var players = new List<IMyPlayer>();
            collection.GetPlayers(players, p => p != null && p.IdentityId == identityId);

            player = players.FirstOrDefault();
            return player != null;
        }
    }

    /*
        private void AdjustTurretControls()
        {
            List<IMyTerminalControl> m_controls;
            MyAPIGateway.TerminalControls.GetControls<IMyLargeTurretBase>(out m_controls);

            foreach (var item in m_controls)
            { //To do: Ignore tractor beams
                switch (item.Id)
                {
                    case "TargetNeutrals":
                    case "TargetStations":
                    case "TargetLargeShips":
                    case "TargetSmallShips":
                        item.Visible = (x) =>
                        {
                            if (x is IMyLargeGatlingTurret)
                                return (x.GameLogic as LargeGatlingTurretNoAI).TurretNoAILogic.Toggle;
                            else if (x is IMyLargeMissileTurret)
                                return (x.GameLogic as LargeMissileTurretNoAI).TurretNoAILogic.Toggle;
                            else if (x is IMyLargeInteriorTurret)
                                return (x.GameLogic as LargeInteriorTurretNoAI).TurretNoAILogic.Toggle;

                            return true;
                        };
                        break;
                }
            }
        }

    public class TurretNoAI
    {
        IMyLargeTurretBase m_turret;
        MyCubeBlock m_cubeBlock;
        IMyPlayer m_builtByPlayer;
        IMyPlayer m_ownerPlayer;
        List<string> m_controlButtons = new List<string> { "TargetNeutrals", "TargetStations", "TargetLargeShips", "TargetSmallShips" };
        public bool Toggle = true;

        bool m_debug = false;

        public TurretNoAI(IMyEntity entity)
        {
            m_turret = entity as IMyLargeTurretBase;
            m_cubeBlock = entity as MyCubeBlock;
        }

        public void Update100()
        {
            if (m_cubeBlock == null)
                return;

            MyAPIGateway.Parallel.Start(() =>
            {
                try
                {
                    Extensions.TryGetPlayer(MyAPIGateway.Players, m_cubeBlock.BuiltBy, out m_builtByPlayer);
                    Extensions.TryGetPlayer(MyAPIGateway.Players, m_cubeBlock.OwnerId, out m_ownerPlayer);
                }
                catch (System.Exception e)
                {
                    MyLog.Default.WriteLineAndConsole($"{e.ToString()}");
                }

            });

            if (m_debug)
            {
                MyLog.Default.WriteLineAndConsole($"BuiltByPlayer is null = {m_builtByPlayer == null} | OwnerPlayer is null = {m_ownerPlayer == null}");

                if (m_builtByPlayer != null)
                    MyLog.Default.WriteLineAndConsole($"BuiltByPlayer is bot = {m_builtByPlayer.IsBot}");

                if (m_ownerPlayer != null)
                    MyLog.Default.WriteLineAndConsole($"BuiltByPlayer is bot = {m_ownerPlayer.IsBot}");
            }
        }

        public void Update10()
        {
            if (m_cubeBlock == null)
                return;

            // If both are null, it's a bot, so dont restrict the turret.
            if (m_builtByPlayer == null && m_ownerPlayer == null)
            {
                Toggle = true;
                return;
            }

            // If the turret is owned and built by an NPC, return
            if ((m_builtByPlayer != null && m_builtByPlayer.IsBot) && (m_ownerPlayer != null && m_ownerPlayer.IsBot))
            {
                Toggle = true;
                return;
            }

            Toggle = false;

            foreach (string item in m_controlButtons)
                m_turret.SetValue(item, false);
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret), true)]
    public class LargeGatlingTurretNoAI : MyGameLogicComponent
    {
        MyObjectBuilder_EntityBase objectBuilder;
        public TurretNoAI TurretNoAILogic;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            TurretNoAILogic = new TurretNoAI(Entity);
        }

        public override void UpdateAfterSimulation100()
        {
            base.UpdateAfterSimulation100();
            TurretNoAILogic.Update100();
        }

        public override void UpdateAfterSimulation10()
        {
            base.UpdateAfterSimulation10();
            TurretNoAILogic.Update10();
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), true)]
    public class LargeMissileTurretNoAI : MyGameLogicComponent
    {
        MyObjectBuilder_EntityBase objectBuilder;
        public TurretNoAI TurretNoAILogic;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            TurretNoAILogic = new TurretNoAI(Entity);
        }

        public override void UpdateAfterSimulation100()
        {
            base.UpdateAfterSimulation100();
            TurretNoAILogic.Update100();
        }

        public override void UpdateAfterSimulation10()
        {
            base.UpdateAfterSimulation10();
            TurretNoAILogic.Update10();
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_InteriorTurret), true)]
    public class LargeInteriorTurretNoAI : MyGameLogicComponent
    {
        MyObjectBuilder_EntityBase objectBuilder;
        public TurretNoAI TurretNoAILogic;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            TurretNoAILogic = new TurretNoAI(Entity);
        }

        public override void UpdateAfterSimulation100()
        {
            base.UpdateAfterSimulation100();
            TurretNoAILogic.Update100();
        }

        public override void UpdateAfterSimulation10()
        {
            base.UpdateAfterSimulation10();
            TurretNoAILogic.Update10();
        }
    }
    */
}
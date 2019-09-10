using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using System.Text.RegularExpressions;
using GSF;

namespace Splen.ServerUtilities
{
    public class ThrusterHeatWarningGrid
    {
        public Dictionary<long, int> blockWarnings = new Dictionary<long, int>();

        public ThrusterHeatWarningGrid(MyCubeGrid grid)
        {
            if (grid == null)
                return;

            if (!Core.Instance.playerThrusterWarningLevels.ContainsKey(grid.EntityId))
                Core.Instance.playerThrusterWarningLevels.Add(grid.EntityId, this);
        }
    }

    public static class TypeHelpers
    {
        /// <summary> Checks if the given object is of given type. </summary>
        public static bool IsOfType<T>(this object Object, out T Casted) where T : class
        {
            Casted = Object as T;
            return Casted != null;
        }

        public static bool IsOfType<T>(this object Object) where T : class
        {
            return Object is T;
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false)]
    public class ThrustOverride : MyGameLogicComponent
    {
        #region config

        private readonly int debug = 0;

        #endregion

        #region controls

        /// <summary>
        /// The current user-set max overclock value, linked to a slider in the controls. This value * vanilla ThrustMultiplier = m_maxMultiplier. 
        /// </summary>
        public float Overclock = 1f;

        /// <summary>
        /// If true, maximum thrust will be reduced to "prevent" damage from heat when necessary. Linked to an on-off switch in the controls
        /// </summary>
        public bool SafetySwitch = true;

        #endregion

        private IMyThrust m_iThrust;
        private MyThrust m_thrust;
        private long adminIdentityId = -1L;
        MyObjectBuilder_EntityBase objectBuilder;
        private float m_minMultiplier;
        public float MaxMultiplier;
        public float CurrentMultiplier;
        private float m_maxHeat;
        private float m_damageHeat;
        private float m_heat;
        private Random rand = new Random();
        
        private MyCubeBlock m_cubeBlock;
        private IMySlimBlock m_slimBlock;
        private long m_thrusterWarningRemove;
        private IMyTerminalBlock m_termBlock;
        public bool UpdateCustomData;
        private float m_heatMult = 1f;
        private float m_maxIntervalThrust;
        private long m_entityId;
        private bool m_initialized;
        private long _cubeGridEntityId;

        #region overrides

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            this.objectBuilder = objectBuilder;

            MyAPIGateway.Parallel.Start(() => Init());
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (!m_initialized) return;
            Debug.Write("Thrusters: UpdateOnceBeforeFrame", 1, debug);

            RetrieveValuesFromCustomData();

            if (Sync.IsServer)
                UpdateGridWarningDictionaries();
        }

        public override void UpdateBeforeSimulation10()
        {
            if (!m_initialized) return;
            try
            {
                if (m_iThrust == null)
                    return;

                m_iThrust.ThrustMultiplier = CurrentMultiplier;

                if (Sync.IsServer && m_iThrust.CurrentThrust > m_maxIntervalThrust)
                    m_maxIntervalThrust = m_iThrust.CurrentThrust;
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        public override void UpdateAfterSimulation100()
        {
            if (!m_initialized) return;
            try
            {
                if (UpdateCustomData)
                    MyAPIGateway.Parallel.Start(() => AddValuesToCustomData());

                UpdateGridWarningDictionaries();
                MyAPIGateway.Parallel.Start(() =>
                {
                    try
                    {
                        ApplyHeat();
                        SendHeatNotifications();
                        LogicCore.Instance.MoveHeatIntoSinks(ref m_heat, m_cubeBlock);
                        CalculateThrustMultiplier();
                        m_maxIntervalThrust = 0f;
                    }
                    catch (Exception e)
                    { Debug.HandleException(e); }
                });
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        #endregion

        private void Init()
        {
            try
            {
                NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;

                m_iThrust = Entity as IMyThrust;
                m_thrust = m_iThrust as MyThrust;
                m_cubeBlock = (m_iThrust as IMyCubeBlock) as MyCubeBlock;
                m_slimBlock = (m_iThrust as IMyCubeBlock).SlimBlock;
                m_termBlock = m_iThrust as IMyTerminalBlock;
                m_entityId = m_iThrust.EntityId;
                m_minMultiplier = m_iThrust.ThrustMultiplier;
                m_maxHeat = m_slimBlock.MaxIntegrity;
                m_damageHeat = m_maxHeat * 2;
                MaxMultiplier = m_minMultiplier * Overclock;
                m_iThrust.ThrustMultiplier = MaxMultiplier;
                _cubeGridEntityId = m_cubeBlock.CubeGrid.EntityId;

                if (Sync.IsServer)
                    NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

                m_initialized = true;
            }
            catch (Exception e)
            {
                Debug.HandleException(e);
                MyAPIGateway.Parallel.Sleep(1000);
                Init();
            }
        }

        private void RetrieveValuesFromCustomData()
        {
            if (m_termBlock == null) return;
            
            Debug.Write($"m_termBlock.CustomData = {m_termBlock.CustomData}", 2, debug);

            if (m_termBlock.CustomData.Contains("Overclock") && Debug.Write("Found Overclock value in CustomData on Init. Retrieving value ...", 1, debug))
            {
                Match match = Regex.Match(m_termBlock.CustomData, @"Overclock:(\d+)");
                if (match.Value != "" && Debug.Write($"Regex match successful. Overclock value is {match.Groups[1].Captures[0].Value}", 1, debug))
                    Overclock = float.Parse(match.Groups[1].Captures[0].Value);

                match = Regex.Match(m_termBlock.CustomData, @"SafetySwitch:(\w+)");
                if (match.Value != "" && Debug.Write($"Regex match successful. Safety value is {match.Groups[1].Captures[0].Value}", 1, debug))
                    SafetySwitch = bool.Parse(match.Groups[1].Captures[0].Value);
            }
        }

        public void AddValuesToCustomData() // This is probably not performance friendly. Hooking into mod storage might come in handy here.
        {
            try
            {
                if (Sync.IsServer && Debug.Write($"m_termBlock == null ? {m_termBlock == null}", 2, debug) && m_termBlock != null)
                {
                    if (!m_termBlock.CustomData.Contains("Overclock") && Debug.Write("Did not find Overclock value in CustomData. Adding ...", 1, debug))
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => 
                        { 
                            if (m_termBlock != null)
                                m_termBlock.CustomData += $"\nOverclock:{(int)Overclock}\n"; 
                        }); 
                    else if (Debug.Write("Adjusting existing Overclock CustomData value on thruster ...", 1, debug))
                    {
                        string newString = Regex.Replace(m_termBlock.CustomData, @"Overclock:(\d+)", $"Overclock:{(int)Overclock}");
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => 
                        {
                            if (m_termBlock != null)
                                m_termBlock.CustomData = newString; 
                        });
                    }

                    if (!m_termBlock.CustomData.Contains("SafetySwitch") && Debug.Write("Did not find SafetySwitch value in CustomData . Adding ...", 1, debug))
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            if (m_termBlock != null)
                                m_termBlock.CustomData += $"\nSafetySwitch:{SafetySwitch.ToString()}\n";
                        });
                    else if (Debug.Write("Adjusting existing SafetySwitch CustomData value on thruster ...", 1, debug))
                    {
                        string newString = Regex.Replace(m_termBlock.CustomData, @"nSafetySwitch:(\w+)", $"nSafetySwitch:{SafetySwitch.ToString()}");
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            if (m_termBlock != null)
                                m_termBlock.CustomData = newString;
                        });
                    }

                    MyAPIGateway.Utilities.InvokeOnGameThread(() => Debug.Write(m_termBlock.CustomData, 2, debug));
                }

                UpdateCustomData = false;
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }
        
        public void UpdateGridWarningDictionaries()
        {
            try
            {
                if (!Core.Instance.playerThrusterWarningLevels.ContainsKey(m_cubeBlock.CubeGrid.EntityId))
                    Core.Instance.playerThrusterWarningLevels.Add(m_cubeBlock.CubeGrid.EntityId, new ThrusterHeatWarningGrid(m_cubeBlock.CubeGrid));

                var gridWarning = Core.Instance.playerThrusterWarningLevels[m_cubeBlock.CubeGrid.EntityId];

                if (gridWarning.blockWarnings.ContainsKey(m_cubeBlock.EntityId))
                    gridWarning.blockWarnings[m_cubeBlock.EntityId] = 0;
                else if (!gridWarning.blockWarnings.ContainsKey(m_cubeBlock.EntityId))
                    gridWarning.blockWarnings.Add(m_cubeBlock.EntityId, 0);
            }
            catch (Exception e)
            { MyLog.Default.WriteLine(e.ToString()); }
        }

        private void ApplyHeat()
        {
            if (m_heat > m_maxHeat) m_heatMult = m_heat / m_maxHeat;
            else m_heatMult = 1f;

            var heatToAdd = m_maxIntervalThrust / 10000 * m_heatMult;
            if (heatToAdd > 0f && heatToAdd < 100f)
                heatToAdd = Overclock > 1f ? heatToAdd + 100f : 100f;

            m_heat = Math.Max(m_heat + heatToAdd - 100, 0);
        }

        private void SendOverheatedMessageToPlayer(string message, string color, int warningLevel)
        {
            try
            {
                foreach (var item in Core.Instance.playerThrusterWarningLevels[m_cubeBlock.CubeGrid.EntityId].blockWarnings.ToList())
                    if (item.Key != m_cubeBlock.EntityId && warningLevel <= item.Value )
                        return;

                MyAPIGateway.Utilities.InvokeOnGameThread(() => Core.Instance.playerThrusterWarningLevels[m_cubeBlock.CubeGrid.EntityId].blockWarnings[m_cubeBlock.EntityId] = warningLevel);

                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players, x => x != null && x.Controller != null && x.Controller.ControlledEntity != null && x.Client != null && Debug.Write("Found a valid, online player", 2, debug));

                foreach (var player in players)
                {
                    var playerEntity = player.Controller.ControlledEntity.Entity;
                    IMyShipController SC;
                    if (Debug.Write($"playerEntity == null ? {playerEntity == null}", 2, debug) && playerEntity != null  && playerEntity.IsOfType(out SC) && SC.CubeGrid == m_cubeBlock.CubeGrid)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            {
                                if (Debug.Write($"player == null ? {player == null}", 2, debug) && player != null)
                                {
                                    /* WORK ON THIS, MAKE A CUSTOM GUI PLEASE
                                    if (!Core.Instance.LastThrusterHeatNoteSentToPlayer.ContainsKey(player.IdentityId))
                                        Core.Instance.LastThrusterHeatNoteSentToPlayer.Add(player.IdentityId, MyVisualScriptLogicProvider.AddNotification(message, color, player.IdentityId));
                                    else
                                    {
                                        MyVisualScriptLogicProvider.RemoveNotification(Core.Instance.LastThrusterHeatNoteSentToPlayer[player.IdentityId], player.IdentityId);
                                        Core.Instance.LastThrusterHeatNoteSentToPlayer[player.IdentityId] = MyVisualScriptLogicProvider.AddNotification(message, color, player.IdentityId);
                                    }

                                    if (!Core.Instance.TimeThrusterNoteWasSentToPlayer.ContainsKey(player.IdentityId))
                                        Core.Instance.TimeThrusterNoteWasSentToPlayer.Add(player.IdentityId, Core.Instance.Timer);
                                    else Core.Instance.TimeThrusterNoteWasSentToPlayer[player.IdentityId] = Core.Instance.Timer;
                                    */
                                    MyVisualScriptLogicProvider.ClearNotifications(player.IdentityId);
                                    MyVisualScriptLogicProvider.ShowNotification(message, 3000, color, player.IdentityId);
                                }
                                    
                                // TO DO: play some audio
                                // make sure the audio is on a cooldown so we dont get spam
                            }
                            catch (Exception e)
                            { Debug.HandleException(e); }
                        });
                    }
                }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }
    
        private void SendHeatNotifications()
        {
            if (m_heat > 0f)  Debug.Write($"Heat:{m_heat}/{m_maxHeat}", 2, debug);
            
            var overrideMsg = " Overriding safeties.";
            var heat = m_heat / m_maxHeat;
            var safetyMessage = SafetySwitch ? " Reducing thrust." : overrideMsg;
            var heatPercent = (int)(heat * 100);

            if (heat > 0.8f && heat <= 1f && m_maxIntervalThrust > 0f && Debug.Write("Sending thrusters overheating message ...", 1, debug))
                SendOverheatedMessageToPlayer($"WARNING: Thrusters overheating! ({heatPercent}%)", "White", heatPercent);

            else if (!SafetySwitch && m_heat > m_damageHeat && Debug.Write("Sending thrusters critical message ...", 1, debug))
            {
                int randDmg = rand.Next(0, 2);
                if (randDmg > 0)
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        try
                        { m_slimBlock.DoDamage((m_heat / m_damageHeat) * 0.01f * randDmg * m_maxHeat, MyDamageType.Deformation, true); }
                        catch (Exception e)
                        { Debug.HandleException(e); } 
                    });
                
                SendOverheatedMessageToPlayer($"WARNING: THRUSTERS CRITICAL! MELTDOWN IMMINENT! ({heatPercent}%)", "Red", heatPercent * 4);
            }
            else if (m_heat > m_maxHeat && Debug.Write("Sending thrusters overheated message ...", 1, debug))
            {
                int w = 2; if (safetyMessage == overrideMsg) w = 3;
                SendOverheatedMessageToPlayer($"WARNING: Thrusters overheated ({heatPercent}%)!{safetyMessage}", "Red", heatPercent * w);
            }
        }

        private void CalculateThrustMultiplier()
        {
            MaxMultiplier = m_minMultiplier * Overclock;

            if (!SafetySwitch)
                CurrentMultiplier = MaxMultiplier;
            else if (m_heat > m_maxHeat)
                CurrentMultiplier = Math.Max(m_minMultiplier, m_iThrust.ThrustMultiplier - (m_heat / m_maxHeat));
            else
                CurrentMultiplier = Math.Min(MaxMultiplier, m_iThrust.ThrustMultiplier + (m_maxHeat / m_heat + 1));

            var message = new MessageThrusterVariables();
            message.EntityId = m_entityId;
            message.ThrustMultiplier = CurrentMultiplier;
            message.Overclock = Overclock;
            message.SafetySwitch = SafetySwitch;

            if (!Core.Instance.CubeGridInfo.ContainsKey(_cubeGridEntityId))
                Messaging.SendMessageToAllPlayers(message);
            else
                Messaging.SendMessageToConcurrentPlayerList(message, Core.Instance.CubeGridInfo[_cubeGridEntityId].PlayersInSyncRange);
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = true)
        {
            return objectBuilder;
        }
    }
}
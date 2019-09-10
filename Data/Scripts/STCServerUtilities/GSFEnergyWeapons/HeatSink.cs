using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using VRage.ModAPI;
using Sandbox.Game.Entities;
using VRageMath;
using VRage.Game.Entity;
using GSF.Utilities;
using Splen.ServerUtilities;
using System.Collections.Generic;
using System;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using System.Text;
using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;
using Sandbox.Game.EntityComponents;
using System.Linq;

namespace GSF
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true, "GSF_Heatsink_Large", "GSF_Heatsink_Small")]
    public class HeatSink : MyGameLogicComponent
    {
        private readonly int debug = 0;
        MyObjectBuilder_EntityBase objectBuilder;
        MyDefinitionId m_electricityDefinition = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        bool addedToLogic;
        MyResourceSinkComponent m_resourceSink;

        public MyCubeBlock CubeBlock;
        public float Heat;
        public float MaxHeat;
        IMyEntity m_entity;
        private long m_cubeGridId;
        IMyFunctionalBlock m_functionalBlock;
        IMyTerminalBlock m_terminalBlock;
        Random random = new Random();
        private float m_powerConsumption;
        private bool m_powerAvailable = true;
        private float m_lastHeatSentToClients;
        private bool m_initialized;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            this.objectBuilder = objectBuilder;
            MyAPIGateway.Parallel.Start(() => Init());
        }

        private void Init()
        {
            try
            {
                m_entity = Entity;
                m_functionalBlock = Entity as IMyFunctionalBlock;
                m_terminalBlock = Entity as IMyTerminalBlock;
                CubeBlock = Entity as MyCubeBlock;
                m_cubeGridId = CubeBlock.CubeGrid.EntityId;

                MaxHeat = ((IMySlimBlock)CubeBlock.SlimBlock).MaxIntegrity;

                InitPowerSystem();

                if (Sync.IsClient)
                {
                    NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
                    m_terminalBlock.AppendingCustomInfo += AppendCustomInfo;
                }

                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

                Debug.Write($"m_resourceSink == null ? {m_resourceSink == null}", 1, debug);
                Debug.Write($"m_electricityDefinition == null ? {m_electricityDefinition == null}", 1, debug);
                MyAPIGateway.Utilities.InvokeOnGameThread(() => AddToDictionaries());
                m_initialized = true;
            }   
            catch (Exception e)
            {
                Debug.HandleException(e);
                MyAPIGateway.Parallel.Sleep(1000);
                Init();
            }
        }

        private void AddToDictionaries()
        {
            if (!LogicCore.Instance.GridHeatsinkSystems.ContainsKey(m_cubeGridId))
                LogicCore.Instance.GridHeatsinkSystems.Add(m_cubeGridId, new List<HeatSink> { this });

            if (!LogicCore.Instance.GridHeatsinkSystems[m_cubeGridId].Contains(this))
                LogicCore.Instance.GridHeatsinkSystems[m_cubeGridId].Add(this);
        }

        private float RequiredInputFunc()
        {
            
            if (!m_functionalBlock.Enabled || !CubeBlock.IsFunctional)
                return 0f;

            var powerneeded = m_powerConsumption * Heat / MaxHeat;
            if (m_resourceSink.IsPowerAvailable(m_electricityDefinition, powerneeded))
            {
                m_powerAvailable = true;
                return powerneeded;
            }   
            else
            {
                m_powerAvailable = false;
                return 0f;
            }
        }

        private void InitPowerSystem()
        {
            m_powerConsumption = MaxHeat * 0.001f;
            var powerSystem = new MyResourceSinkComponent();
            var sinkInfo = new MyResourceSinkInfo();
            sinkInfo.ResourceTypeId = m_electricityDefinition;
            sinkInfo.MaxRequiredInput = m_powerConsumption;
            sinkInfo.RequiredInputFunc = new Func<float>(RequiredInputFunc);
            powerSystem.AddType(ref sinkInfo);
            Entity.Components.Add<MyResourceSinkComponent>(powerSystem);
            m_resourceSink = Entity.Components.Get<MyResourceSinkComponent>();
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = true)
        {
            return objectBuilder;
        }

        void AppendCustomInfo(IMyTerminalBlock block, StringBuilder info)
        {
            if (!m_initialized) return;
            info.Clear();
            info.AppendLine($"Cooling mode: {(!CubeBlock.IsFunctional ? "Disabled (Needs Repair)" : m_functionalBlock.Enabled && m_powerAvailable ? "Active (60°/sec)" : "Passive (6°/sec)")}");
            info.AppendLine($"Input power: {m_resourceSink.RequiredInputByType(m_electricityDefinition).ToString("0.00")}/{m_powerConsumption.ToString("0.00")} MW");
            info.AppendLine($"Temp/Max: {Heat}°/{MaxHeat}°");
            info.AppendLine($"Integrity: {((IMySlimBlock)CubeBlock.SlimBlock).Integrity/MaxHeat * 100}%");
        }

        public override void UpdateBeforeSimulation10()
        {
            if (!m_initialized) return;
            try
            {
                if (m_entity != null)
                {
                    var emissivity = MathHelper.Clamp(Heat / MaxHeat * 0.25f, 0.0f, 0.25f);
                    MyCubeBlockEmissive.SetEmissiveParts((MyEntity)m_entity, emissivity, Color.Red, Color.White);
                }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        public override void UpdateBeforeSimulation100()
        {
            if (!m_initialized) return;
            try
            {
                if (Sync.IsClient && MyAPIGateway.Gui?.GetCurrentScreen == MyTerminalPageEnum.ControlPanel)
                { // Force terminal screen to refresh if being viewed
                    m_functionalBlock.RefreshCustomInfo();
                    LogicCore.Instance.UpdateTerminal(CubeBlock);
                }

                if (m_resourceSink != null)
                    m_resourceSink.Update();

                if (!Sync.IsServer || m_entity == null)
                    return;

                MyAPIGateway.Parallel.Start(() => DissipateHeat());
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void DoDamage()
        {
            try
            {
                if (CubeBlock != null && (IMySlimBlock)CubeBlock.SlimBlock != null)
                    ((IMySlimBlock)CubeBlock.SlimBlock).DoDamage(1, MyDamageType.Deformation, true);
            }   
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void DissipateHeat()
        {
            try
            {
                if (Heat > 0f)
                {
                    var newHeat = Heat;

                    if (!CubeBlock.IsFunctional) 
                        newHeat = Math.Max(Heat - 0.1f, 0f);
                    else if (m_functionalBlock.Enabled && m_powerAvailable) 
                        newHeat = Math.Max(Heat - 100f, 0f);
                    else 
                        newHeat = Math.Max(Heat - 10f, 0f);

                    Heat = newHeat;

                    if (Heat / MaxHeat > 0.5f && random.Next(0, 100) == 100)
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => DoDamage());     
                }
                SendHeatToClients();
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void SendHeatToClients()
        {
            try
            {
                if (m_lastHeatSentToClients == Heat) return;

                m_lastHeatSentToClients = Heat;
                var message = new MessageHeatSinkUpdate();
                message.EntityId = m_entity.EntityId;
                message.Heat = Heat;

                if (!Core.Instance.CubeGridInfo.ContainsKey(m_cubeGridId))
                    Messaging.SendMessageToAllPlayers(message);
                else
                    Messaging.SendMessageToConcurrentPlayerList(message, Core.Instance.CubeGridInfo[m_cubeGridId].PlayersInSyncRange);  
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        public override void Close()
        {
            if (LogicCore.Instance.GridHeatsinkSystems.ContainsKey(m_cubeGridId))
                LogicCore.Instance.GridHeatsinkSystems[m_cubeGridId].Remove(this);

            base.Close();
        }
    }
}
using System;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.Game;
using VRageMath;
using VRage;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using VRage.Game;
using VRage.ModAPI;
using VRage.Game.ModAPI;

namespace Splen.ServerUtilities
{
    public enum RamscoopState
    {
        Disabled,
        Idle,
        Collecting,
        Blocked,
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_OxygenGenerator), false, new string[] 
    { "ConveyorHydrogenIntake", "SmallConveyorHydrogenIntake", "ConveyorHydrogenIntakeSlope", "SmallConveyorHydrogenIntakeSlope", "HydrogenIntake", "SmallHydrogenIntake" })]
    public class HydrogenIntake : MyGameLogicComponent
    {
        MyObjectBuilder_EntityBase m_objectBuilder;
        MyDefinitionId m_definitionId = new MyDefinitionId(typeof(MyObjectBuilder_Ore), "Ice");
        MyFixedPoint m_amount = (MyFixedPoint)0.001;
        float m_sizeFactor = 0;
        double m_cosTheta = 0;
        double m_speed = 0;
        MyFixedPoint m_amountAdded;
        float m_IceToGasRatio = 0;
        RamscoopState m_state;
        MyInventory m_inventory;
        IMyGasGenerator m_gasGenerator;
        IMyTerminalBlock m_terminalBlock;
        IMyFunctionalBlock m_functionalBlock;

        #region Constants
        private readonly float SmallShipRate = 10.0f;
        private readonly float LargeShipRate = 100.0f;

        private readonly int debug = 1;
        #endregion

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            m_objectBuilder = objectBuilder;
            m_gasGenerator = Entity as IMyGasGenerator;
            m_terminalBlock = Entity as IMyTerminalBlock;
            m_functionalBlock = Container.Entity as IMyFunctionalBlock;
            m_inventory = (MyInventory)(Container.Entity as VRage.Game.Entity.MyEntity).GetInventoryBase();
            var subtype = m_functionalBlock.BlockDefinition.SubtypeId;

            if(m_functionalBlock.CubeGrid.GridSizeEnum == MyCubeSize.Small )
                m_sizeFactor = SmallShipRate;
            else m_sizeFactor = LargeShipRate;

            var ramscoopDefinition = MyDefinitionManager.Static.GetDefinition(new MyDefinitionId(typeof(MyObjectBuilder_OxygenGenerator), (Container.Entity as IMyFunctionalBlock).BlockDefinition.SubtypeId)) as MyOxygenGeneratorDefinition;
            m_IceToGasRatio = ramscoopDefinition.ProducedGases[0].IceToGasRatio;

            m_terminalBlock.AppendingCustomInfo += AppendingCustomInfo;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override void Close()
        {
            m_terminalBlock.AppendingCustomInfo -= AppendingCustomInfo;
        }

        public static void AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            StringBuilder text = new StringBuilder(100);
            var ramscoop = (arg1 as IMyTerminalBlock).GameLogic.GetAs<HydrogenIntake>();

            var percent = (ramscoop.m_cosTheta * ramscoop.m_speed) / (arg1.CubeGrid.GridSizeEnum == MyCubeSize.Large ? MyDefinitionManager.Static.EnvironmentDefinition.LargeShipMaxSpeed : MyDefinitionManager.Static.EnvironmentDefinition.SmallShipMaxSpeed);

            // For some reason, the amount of gas actually generated doesn't match what I calculated. Why?
            // It seems to be close half what I calculate below (1/2 correction included below)
            text.AppendFormat("Collection Rate: {0:P0}, {1:F3} L/s\r\n", percent, (float)(ramscoop.m_amountAdded * ramscoop.m_IceToGasRatio) * (60f / 100f) * 0.5f);  // 60 UPS but updates happen every 100 updates
            text.AppendFormat("State: {0}\r\n", ramscoop.m_state);
            arg2.Append(text);
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return (copy ? m_objectBuilder.Clone() as MyObjectBuilder_EntityBase : m_objectBuilder);
        }

        public override void UpdateBeforeSimulation100()
        {
            try
            {
                MyAPIGateway.Parallel.Start(() =>
                {
                    if (m_gasGenerator != null) m_gasGenerator.UseConveyorSystem = false;
                    if (m_terminalBlock != null) m_terminalBlock.ShowInInventory = false;

                    m_cosTheta = 0;
                    m_speed = 0;
                    m_amountAdded = 0;

                    if (m_functionalBlock != null && m_functionalBlock.IsWorking && m_functionalBlock.IsFunctional)
                    {
                        m_state = RamscoopState.Idle;

                        if (m_inventory != null && m_inventory.GetItemsCount() == 0)
                            DoWork();
                    }
                    else m_state = RamscoopState.Disabled;

                    if (m_terminalBlock != null) m_terminalBlock.RefreshCustomInfo();
                });
                
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void DoWork()
        {
            try
            {
                Vector3D velocity = Vector3D.Zero;
                var grid = (Container.Entity as IMyCubeBlock).CubeGrid;
                var entity = (Container.Entity as IMyFunctionalBlock);
                MyFixedPoint amount = 0;
                m_speed = 0;

                if (grid != null && entity != null && grid.Physics != null)
                {
                    velocity = grid.Physics.LinearVelocity;

                    var rotation = entity.WorldMatrix.GetDirectionVector(Base6Directions.Direction.Forward);
                    var start = entity.GetPosition() + (entity.WorldAABB.Size.Z / 2 * rotation);
                    var end = start + (100 * rotation);

                    if ((Container.Entity as IMyCubeBlock).CubeGrid.RayCastBlocks(start, end).HasValue)
                        m_state = RamscoopState.Blocked;
                    else if (!Vector3D.IsZero(velocity))
                    {
                        m_state = RamscoopState.Collecting;
                        m_speed = velocity.Length();

                        var rotdot = Vector3D.Dot(velocity, rotation);
                        var lens = velocity.Length() * rotation.Length();

                        var cos_theta = (rotdot / lens);

                        cos_theta += 1.0f;  // positive offset, facing reverse will be 0.

                        m_cosTheta = cos_theta / 2.0d;
                        m_amountAdded = amount = m_amount * (MyFixedPoint)m_cosTheta * (MyFixedPoint)m_speed * (MyFixedPoint)m_sizeFactor;
                    }
                }

                if (Sync.IsServer && m_inventory.CanItemsBeAdded(amount, m_definitionId))
                {
                    var content = (MyObjectBuilder_PhysicalObject)MyObjectBuilderSerializer.CreateNewObject(m_definitionId);
                    if (content != null)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            { if (content != null) m_inventory.AddItems(amount, content); }
                            catch (Exception e)
                            { Debug.HandleException(e); }
                        });
                    } 
                }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private bool IsInPlanetAtmo()
        {
            /*
            var planets = Sandbox.Game.Entities.Planet.MyPlanets.GetPlanets();

            foreach (var planet in planets)
            {
                if (planet.GetAirDensity(Container.Entity.GetPosition()) > 0)
                    return true;
            }
            */
            return false;
        }
    }
}

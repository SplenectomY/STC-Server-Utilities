using VRage.Game.Components;
using VRage.ObjectBuilders;
using Sandbox.Common.ObjectBuilders;
using GSF.Utilities;
using VRage.ModAPI;

namespace GSF.Weapons
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), false,
    "SmallBeamBaseGTF_Large",
    "Interior_Pulse_Laser_Base_Large",
    "SmallPulseLaser_Base_Large",
    "MediumQuadBeamGTFBase_Large",
    "MPulseLaserBase_Large",
    "LDualPulseLaserBase_Large",
    "XLDualPulseLaserBase_Large",
    "LargeDualBeamGTFBase_Large",
    "XLGigaBeamGTFBase_Large",
    "MPulseLaserBase_Small")]

    public class BeamTurret : MyGameLogicComponent
    {
        MyObjectBuilder_EntityBase objectBuilder;
        BeamLogic beamLogic;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            this.objectBuilder = objectBuilder;
            beamLogic = new BeamLogic(Entity);
            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = true)
        {
            return objectBuilder;
        }

        public override void UpdateBeforeSimulation100()
        {
            beamLogic.Update100();
        }

        public override void MarkForClose()
        {
            beamLogic.ClearInventory();
            base.MarkForClose();
        }

        public override void Close()
        {
            beamLogic.ClearInventory();
            base.Close();
        }
    }
}
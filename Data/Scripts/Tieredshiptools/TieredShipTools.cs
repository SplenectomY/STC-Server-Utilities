using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

[MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), useEntityUpdate: true)]
public class UpgradeableShipWelder : UpgradeableShipTool
{
    public override void Init(MyObjectBuilder_EntityBase objectBuilder)
    {
        base.Init(objectBuilder);

        ActionOnOwnGrid = true;

        if (((IMyCubeBlock)Entity).CubeGrid.GridSizeEnum == MyCubeSize.Large)
        {
            SPHERE_OFFSET = 4.0f;
            SPHERE_RADIUS = 1.6f;
        }
        else
        {
            SPHERE_OFFSET = 2.3f;
            SPHERE_RADIUS = 0.8f;
        }

    }

    protected override void Action(float multiplier, BoundingSphereD sphere, IMyCubeGrid targetGrid, IMyCubeBlock welderBlock)
    {
        var inventory = ((MyEntity)welderBlock).GetInventory();
        bool isHelping = ((IMyShipWelder)welderBlock).HelpOthers;
        var amount = MyAPIGateway.Session.WelderSpeedMultiplier * multiplier;
        var blocks = targetGrid.GetBlocksInsideSphere(ref sphere);
        foreach (var block in blocks)
            block.IncreaseMountLevel(amount, welderBlock.OwnerId, inventory, 0.6f, isHelping);
    }
}

[MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipGrinder), useEntityUpdate: true)]
public class UpgradeableShipGrinder : UpgradeableShipTool
{
    public override void Init(MyObjectBuilder_EntityBase objectBuilder)
    {
        base.Init(objectBuilder);

        ActionOnOwnGrid = false;

        if (((IMyCubeBlock)Entity).CubeGrid.GridSizeEnum == MyCubeSize.Large)
        {
            SPHERE_OFFSET = 3.5f;
            SPHERE_RADIUS = 1.6f;
        }
        else
        {
            SPHERE_OFFSET = 2.0f;
            SPHERE_RADIUS = 0.8f;
        }
    }

    protected override void Action(float multiplier, BoundingSphereD sphere, IMyCubeGrid targetGrid, IMyCubeBlock thisBlock)
    {
        var inventory = ((MyEntity)thisBlock).GetInventory();
        var amount = MyAPIGateway.Session.GrinderSpeedMultiplier * multiplier;
        var blocks = targetGrid.GetBlocksInsideSphere(ref sphere);
        foreach (var block in blocks)
            block.DecreaseMountLevel(amount, inventory);
    }
}


public abstract class UpgradeableShipTool : MyGameLogicComponent
{
    #region defaults
    protected const string UPGRADE_SPEED_KEY = "Speed";
    protected const float VANILLA_SPEED = 1;

    protected float SPHERE_OFFSET = 4f; //multiply this by forward vector to get sphere center
    protected float SPHERE_RADIUS = 2.1f;
    #endregion

    protected bool ActionOnOwnGrid = true;
    protected const bool DEBUG = false;
	
    protected float m_speedMultiplier;
    private bool m_init;
    
    public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
    {
        return Entity.GetObjectBuilder(copy);
    }

    public override void Init(MyObjectBuilder_EntityBase objectBuilder)
    {
        NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        
        //Add speed upgrade value to block
        (Entity as IMyCubeBlock).AddUpgradeValue(UPGRADE_SPEED_KEY, VANILLA_SPEED);
        m_speedMultiplier = 1;

        //Hook upgrade event
        (Entity as IMyCubeBlock).OnUpgradeValuesChanged += OnUpgradeValuesChanged;
    }

    /// <summary>
    /// When an upgrade is applied, unapplied, etc.
    /// </summary>
    private void OnUpgradeValuesChanged()
    {
        float speed;
        ((IMyCubeBlock)Entity).UpgradeValues.TryGetValue(UPGRADE_SPEED_KEY, out speed);
        m_speedMultiplier = speed / VANILLA_SPEED;
    }

    public override void UpdateBeforeSimulation()
    {
        //If welder is welding
        if ((MyAPIGateway.Multiplayer.IsServer || !MyAPIGateway.Multiplayer.MultiplayerActive) && m_speedMultiplier > 1.001f && ((Entity as IMyGunObject<MyToolBase>).IsShooting || (Entity as IMyFunctionalBlock).Enabled))
        {
            //List of grids that are in range of the welder
            List<IMyEntity> potentialGrids;
            BoundingSphereD weldSphere = new BoundingSphereD(Entity.GetPosition() + Entity.WorldMatrix.Forward * SPHERE_OFFSET, SPHERE_RADIUS);
            
            potentialGrids = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref weldSphere);

            //If not ActionOnOwnGrid, and own grid is in the list, remove it.
            if (!ActionOnOwnGrid && potentialGrids.Contains(((IMyCubeBlock)Entity).CubeGrid))
                potentialGrids.Remove(((IMyCubeBlock)Entity).CubeGrid);

            //Because of TopMostEntites, every E is a grid
            foreach (IMyEntity e in potentialGrids)
                if (e is IMyCubeGrid && e.Physics != null) {
                    Action(m_speedMultiplier - 1, weldSphere, (IMyCubeGrid)e, (IMyCubeBlock)Entity); //speedMultiplier-1 because vanilla has already taken care of the 1
                }
        }
    }

    public override void UpdateAfterSimulation()
    {
       /* if (DEBUG)
        {
            MatrixD weldTransform = MatrixD.CreateFromTransformScale(new Quaternion(), Entity.GetPosition() + Entity.WorldMatrix.Forward * SPHERE_OFFSET, Vector3.One);
            Color sphereColor = Color.White;
            MySimpleObjectDraw.DrawTransparentSphere(ref weldTransform, SPHERE_RADIUS, ref sphereColor, MySimpleObjectRasterizer.Solid, 20);
        } */
    }

    public override void Close()
    {
        //Unhook upgrade event
        (Entity as IMyCubeBlock).OnUpgradeValuesChanged -= OnUpgradeValuesChanged;
    }
    
    /// <summary>
    /// Supplemental action that accounts for the difference between vanilla speed and this upgraded tool's speed. Run this on every grid in the tool's sphere.
    /// This method by rexxar, thank you!
    /// </summary>
    /// <param name="multiplier">Speed Multiplier (Already accounts for world settings)</param>
    /// <param name="sphere">The Sphere</param>
    /// <param name="targetGrid">Seriously you can figure this one out</param>
    /// <param name="welderBlock">The welder itself</param>
    protected abstract void Action(float multiplier, BoundingSphereD sphere, IMyCubeGrid targetGrid, IMyCubeBlock thisBlock);
}



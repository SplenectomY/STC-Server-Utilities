using Sandbox.ModAPI;

namespace Splen.ServerUtilities
{
    public static class Sync
    {
        public static bool IsServer
        {
            get
            {
                if (MyAPIGateway.Session == null)
                    return false;

                if (MyAPIGateway.Session.OnlineMode == VRage.Game.MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer)
                    return true;

                return false;
            }
        }

        public static bool IsClient
        {
            get
            {
                if (MyAPIGateway.Session == null)
                    return false;

                if (MyAPIGateway.Session.OnlineMode == VRage.Game.MyOnlineModeEnum.OFFLINE)
                    return true;

                if (MyAPIGateway.Session.Player != null && MyAPIGateway.Session.Player.Client != null && MyAPIGateway.Multiplayer.IsServerPlayer(MyAPIGateway.Session.Player.Client))
                    return true;

                if (!MyAPIGateway.Multiplayer.IsServer)
                    return true;

                return false;
            }
        }

        public static bool IsDedicated
        {
            get
            {
                if (MyAPIGateway.Utilities.IsDedicated)
                    return true;

                return false;
            }
        }
    }
}
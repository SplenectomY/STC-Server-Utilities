using Sandbox.Game;
using Sandbox.ModAPI;
using System;
using VRage.Utils;

namespace Splen.ServerUtilities
{
    public static class Debug
    {
        public const int GlobalDebugLevel = 0;

        public static bool Write(string s, int level, int debugLevel = GlobalDebugLevel, bool chat = true)
        {
            try
            {
                if (debugLevel < level)
                    return true;

                MyLog.Default.WriteLineAndConsole($"STC Utilities Debug: {s}");

                if (Core.Instance == null)
                    return true;

                if (Core.Instance.AdminIdentityId <= 0L)
                    Core.Instance.AdminIdentityId = MyAPIGateway.Players.TryGetIdentityId(Core.Instance.AdminSteamId);

                if (chat && !Core.Instance.DisableChatDebugLogging && Core.Instance.AdminIdentityId > 0L)
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        try
                        { MyVisualScriptLogicProvider.SendChatMessage(s, "Debug", Core.Instance.AdminIdentityId); }
                        catch (Exception e)
                        {
                            MyLog.Default.WriteLineAndConsole(e.ToString());
                            Core.Instance.DisableChatDebugLogging = true;
                        }
                    });
            }
            catch
            {

            }

            return true;
        }

        public static void HandleException(Exception e)
        {
            Write($"{e.GetType().ToString()} caught at {e.TargetSite}. See log.", 0);
            MyLog.Default.WriteLineAndConsole(e.ToString());
        }
    }
}
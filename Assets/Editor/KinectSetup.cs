using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace ShapesKinect.EditorTools
{
    /// <summary>
    /// Turns the KINECT_V2 scripting symbol on when Microsoft's Kinect v2 Unity plugin is in the
    /// project, and off when it isn't, so the game uses a real Kinect without any manual setup and
    /// still compiles without the plugin. Lives in its own assembly so it keeps working even if the
    /// game's scripts don't compile.
    /// </summary>
    [InitializeOnLoad]
    internal static class KinectSetup
    {
        private const string Symbol = "KINECT_V2";

        static KinectSetup()
        {
            EditorApplication.delayCall += Check;
        }

        private static void Check()
        {
            var pluginPresent = AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => assembly.GetType("Windows.Kinect.KinectSensor", false) != null);
            var target = NamedBuildTarget.Standalone;
            var symbols = PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            var hasSymbol = symbols.Contains(Symbol);
            if (pluginPresent == hasSymbol) return;
            if (pluginPresent) symbols.Add(Symbol);
            else symbols.Remove(Symbol);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
            UnityEngine.Debug.Log(pluginPresent
                ? "Shapes Kinect: found the Kinect v2 plugin, turning on Kinect support."
                : "Shapes Kinect: no Kinect v2 plugin found, using the simulated Kinect.");
        }
    }
}

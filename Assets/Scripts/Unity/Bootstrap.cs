using UnityEngine;

namespace Nullroute.UnityFrontend
{
    /// <summary>
    /// Makes the game self-starting: whatever scene loads, make sure there's a black camera
    /// and exactly one terminal.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Object.FindAnyObjectByType<TerminalView>() != null) return;

            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;

            new GameObject("Terminal").AddComponent<TerminalView>();
        }
    }
}

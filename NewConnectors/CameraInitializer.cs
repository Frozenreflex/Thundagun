#region

using UnityEngine;
using UnityFrooxEngineRunner;

#endregion

namespace Thundagun.NewConnectors;

public static class CameraInitializer
{
    public static void SetupCamera(Camera c, CameraSettings settings)
    {
        c.backgroundColor = Color.black;
        c.cullingMask = ~LayerMask.GetMask("Hidden", "Overlay");
        if (!settings.SetupPostProcessing)
            return;

        SetupPostProcessing(c, settings);
    }

    public static void SetupPostProcessing(Camera c, CameraSettings settings)
    {
        c.allowHDR = true;
        c.gameObject.AddComponent<CameraPostprocessingManager>().Initialize(c, settings);
    }

    public static void SetPostProcessing(
        Camera c,
        bool enabled,
        bool motionBlur,
        bool screenspaceReflections)
    {
        c.GetComponent<CameraPostprocessingManager>()
            ?.UpdatePostProcessing(enabled, motionBlur, screenspaceReflections);
    }

    public static void RemovePostProcessing(Camera c)
    {
        var component = c.GetComponent<CameraPostprocessingManager>();
        if (component == null) return;

        component.RemovePostProcessing();
        Object.Destroy(component);
    }
}
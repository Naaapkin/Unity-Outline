using UnityEditor;
using UnityEngine;

public class Utility
{
#if UNITY_EDITOR
    // 添加到 Camera 的右键上下文菜单
    [MenuItem("CONTEXT/Camera/Create Screenshot")]
    private static void CreateScreenShot(MenuCommand command)
    {
        Camera camera = command.context as Camera;
        if (camera == null)
        {
            Debug.LogError("No Camera found!");
            return;
        }
        ScreenCapture.CaptureScreenshot(Application.dataPath + "/Screenshot.png");
    }
#endif
}

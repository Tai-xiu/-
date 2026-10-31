// 把 UGUI 画布临时切到相机渲染，渲染到 RT 并保存 PNG（用于编辑期预览截图）。
using UnityEngine;
using UnityEngine.UI;

public static class MenuCapture
{
    public static string Capture(string outPath, int w = 1920, int h = 1080)
    {
        var canvas = Object.FindObjectOfType<Canvas>();
        var cam = Camera.main;
        if (canvas == null || cam == null) return "missing canvas/camera";

        var oldMode = canvas.renderMode;
        var oldCam = canvas.worldCamera;
        var oldMask = cam.cullingMask;
        var oldTarget = cam.targetTexture;

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 5f;
        cam.cullingMask = -1;
        cam.orthographic = true;
        cam.orthographicSize = 5f;

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath));
        System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());

        // restore
        cam.targetTexture = oldTarget;
        cam.cullingMask = oldMask;
        canvas.renderMode = oldMode;
        canvas.worldCamera = oldCam;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        return "ok " + outPath;
    }
}

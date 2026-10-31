// 场景预览截图：把 Overlay 画布临时切到相机渲染，连同 3D 背景一起渲染成 PNG（之后还原）。
using System.Collections.Generic;
using UnityEngine;

public static class SceneCapture
{
    class State
    {
        public Canvas c;
        public RenderMode mode;
        public Camera cam;
        public float plane;
    }

    public static string Capture(string outPath, int w = 1920, int h = 1080)
    {
        var cam = Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();
        if (cam == null) return "no camera";

        var states = new List<State>();
        foreach (var c in Object.FindObjectsOfType<Canvas>(true))
        {
            if (!c.gameObject.activeInHierarchy) continue;
            if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            states.Add(new State { c = c, mode = c.renderMode, cam = c.worldCamera, plane = c.planeDistance });
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = 1f;
        }

        int oldMask = cam.cullingMask;
        var oldTarget = cam.targetTexture;
        cam.cullingMask = -1;

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

        cam.targetTexture = oldTarget;
        cam.cullingMask = oldMask;
        for (int i = 0; i < states.Count; i++)
        {
            states[i].c.renderMode = states[i].mode;
            states[i].c.worldCamera = states[i].cam;
            states[i].c.planeDistance = states[i].plane;
        }

        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        return "ok " + outPath;
    }
}

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

/// <summary>生成原先由运行时代码创建的小型显示模板；已有资产不覆盖。</summary>
public static class SupportingUIPrefabBuilder
{
    [MenuItem("Tools/UI/生成辅助 UI Prefab（仅首次）")]
    public static void Build()
    {
        UIKit.EnsureLoaded();
        System.IO.Directory.CreateDirectory(EditableUIPrefabBuilder.Folder);
        Save("FloatingTextLabel",()=>Text("FloatingTextLabel",22,new Color(1,.95f,.6f),new Vector2(620,40)));
        Save("SkillFallback",()=> {
            var go=Text("SkillFallback",22,Color.white,Vector2.zero);
            var rt=(RectTransform)go.transform;rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;
            return go;
        });
        Save("SceneFader",()=> {
            var go=new GameObject("SceneFader",typeof(Canvas),typeof(GraphicRaycaster),typeof(Image));
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=999;
            go.GetComponent<Image>().color=new Color(0,0,0,0);return go;
        });
        Save("EatRing",()=> {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/InteractPrompt.prefab");
            var original=source.transform.Find("HoldRing").GetComponent<Image>();
            var go=new GameObject("EatRing",typeof(RectTransform),typeof(Image));var image=go.GetComponent<Image>();
            image.sprite=original.sprite;image.color=Color.white;image.type=Image.Type.Filled;
            image.fillMethod=Image.FillMethod.Radial360;image.fillOrigin=(int)Image.Origin360.Top;image.fillClockwise=true;image.raycastTarget=false;
            image.rectTransform.sizeDelta=new Vector2(56,56);return go;
        });
        Save("AlertMark",()=> {
            var go=new GameObject("AlertMark",typeof(TextMesh));go.transform.localPosition=new Vector3(0,1.6f,0);
            var text=go.GetComponent<TextMesh>();text.text="!";text.font=UIKit.LegacyFont;text.fontSize=64;text.characterSize=.35f;text.anchor=TextAnchor.MiddleCenter;text.color=new Color(.95f,.15f,.1f);
            if(text.font!=null)go.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
            return go;
        });
        Save("EnemyHealthBar",()=> {
            var go=new GameObject("EnemyHealthBar");var view=go.AddComponent<EnemyHealthBarVisual>();
            var shader=Shader.Find("Legacy Shaders/Transparent/Diffuse");
            var bg=GameObject.CreatePrimitive(PrimitiveType.Cube);bg.name="Background";Object.DestroyImmediate(bg.GetComponent<Collider>());bg.transform.SetParent(go.transform,false);bg.transform.localScale=new Vector3(1.3f,.2f,.02f);
            var fill=GameObject.CreatePrimitive(PrimitiveType.Cube);fill.name="Fill";Object.DestroyImmediate(fill.GetComponent<Collider>());fill.transform.SetParent(go.transform,false);fill.transform.localScale=new Vector3(1.26f,.18f,.01f);
            view.background=bg.GetComponent<Renderer>();view.fill=fill.GetComponent<Renderer>();
            string folder="Assets/Resources/UI/Panels/Materials/";System.IO.Directory.CreateDirectory(folder);
            var bgMat=AssetDatabase.LoadAssetAtPath<Material>(folder+"EnemyHealthBackground.mat");if(bgMat==null){bgMat=new Material(shader);bgMat.color=new Color(.12f,.12f,.12f,1);AssetDatabase.CreateAsset(bgMat,folder+"EnemyHealthBackground.mat");}
            var fillMat=AssetDatabase.LoadAssetAtPath<Material>(folder+"EnemyHealthFill.mat");if(fillMat==null){fillMat=new Material(shader);fillMat.color=new Color(.3f,.85f,.3f,1);AssetDatabase.CreateAsset(fillMat,folder+"EnemyHealthFill.mat");}
            view.background.sharedMaterial=bgMat;view.fill.sharedMaterial=fillMat;return go;
        });
        AssetDatabase.SaveAssets();
    }

    private static GameObject Text(string name,int size,Color color,Vector2 dimensions)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Text));
        var text=go.GetComponent<Text>();text.font=UIKit.LegacyFont;text.fontSize=size;text.color=color;text.alignment=TextAnchor.MiddleCenter;
        text.text="提示文字";text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Overflow;text.verticalOverflow=VerticalWrapMode.Overflow;
        text.rectTransform.sizeDelta=dimensions;return go;
    }

    private static void Save(string name,System.Func<GameObject> create)
    {
        string path=EditableUIPrefabBuilder.Folder+name+".prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)return;
        var go=create();try{PrefabUtility.SaveAsPrefabAsset(go,path);}finally{Object.DestroyImmediate(go);}
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Prefab 内显式节点引用。Key 是代码绑定标识，物体改名和移动层级不会断开引用。</summary>
public class UINodeReferences : MonoBehaviour
{
    [Serializable]
    public class Node { public string key; public UnityEngine.Object target; }
    public List<Node> nodes = new List<Node>();
    [Header("交互状态样式")]
    public Color selectedText = new Color(.85f, .75f, .53f, 1);
    public Color inactiveText = new Color(.55f, .5f, .42f, 1);
    public Color selectedTint = Color.white;
    public Color inactiveTint = new Color(.72f, .70f, .66f, 1);
    public Sprite selectedSprite, inactiveSprite;
    [Range(0, 1)] public float rarityTint = .45f;

    public T Get<T>(string key) where T : UnityEngine.Object
    {
        foreach (var node in nodes)
            if (node.key == key && node.target is T) return (T)node.target;
        throw new InvalidOperationException(name + " 缺少 UI 节点引用：" + key + " (" + typeof(T).Name + ")");
    }

    public void Add(string key, UnityEngine.Object target) { nodes.Add(new Node { key = key, target = target }); }

    public static UINodeReferences Spawn(string name, Transform parent)
    {
        var prefab = Resources.Load<UINodeReferences>("UI/Panels/" + name);
        if (prefab == null) throw new InvalidOperationException("缺少 UI/Panels/" + name + " Prefab");
        var view = Instantiate(prefab, parent, false);
        view.name = name;
        view.gameObject.SetActive(true);
        return view;
    }

    /// <summary>列表条目从完整模板实例化，不覆盖模板中的布局与文字外观。</summary>
    public UINodeReferences SpawnRow(string key, Transform parent)
    {
        var row = Instantiate(Get<UINodeReferences>(key), parent, false);
        row.gameObject.SetActive(true);
        return row;
    }
}

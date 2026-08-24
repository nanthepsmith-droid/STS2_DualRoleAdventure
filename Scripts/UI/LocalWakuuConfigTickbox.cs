using System;
using Godot;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace LocalMultiControl.Scripts.UI;

/// <summary>
/// 瓦库托管设置面板用的勾选框。
/// 游戏原生的 NTickbox 是场景驱动控件（依赖 settings_tickbox.tscn 的 %TickboxVisuals 等唯一名节点），
/// 无法直接 new。这里参考"古明地恋"mod（KoishiConfigTickbox / EcoLib.TransferAllNodes）的做法：
/// 在构造函数里实例化游戏自带场景，把子节点整体搬进当前实例并改挂 Owner，
/// 让 % 唯一名查找在新 owner 下重新注册，从而在纯代码中获得原生外观的勾选框。
/// </summary>
internal sealed partial class LocalWakuuConfigTickbox : NSettingsTickbox
{
    private readonly Func<bool>? _getter;
    private readonly Action<bool>? _setter;

    /// <param name="getter">读取当前生效值（面板打开/刷新时同步勾选状态）。</param>
    /// <param name="setter">写回新值（内部转调 LocalWakuuAutopilotConfig.TrySetAndSave）。</param>
    public LocalWakuuConfigTickbox(Func<bool> getter, Action<bool> setter)
    {
        _getter = getter;
        _setter = setter;

        // 以下尺寸/焦点参数照抄 KoishiConfigTickbox 的实测值
        SetCustomMinimumSize(new Vector2(324f, 64f));
        SizeFlagsHorizontal = (SizeFlags)8; // ShrinkEnd：贴行尾
        SizeFlagsVertical = (SizeFlags)1;   // Fill
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Pass;

        TransferAllNodesFromScene(this, SceneHelper.GetScenePath("screens/settings_tickbox"));
    }

    /// <summary>
    /// 注意：NTickbox 基类约定子类不要调 base._Ready()，改为直接调 ConnectSignals()。
    /// </summary>
    public override void _Ready()
    {
        ConnectSignals();
        IsTicked = _getter?.Invoke() ?? false;
    }

    protected override void OnTick()
    {
        Apply(true);
    }

    protected override void OnUntick()
    {
        Apply(false);
    }

    private void Apply(bool value)
    {
        _setter?.Invoke(value);
        IsTicked = _getter?.Invoke() ?? value; // 以配置层回读为准，写失败时自动弹回
    }

    /// <summary>
    /// 把源场景实例的全部子节点搬到 target 名下（EcoLib.TransferAllNodes 的最小复刻）：
    /// 子节点脱离场景根、挂到 target，并把整棵子树的 Owner 改为 target，
    /// 场景里预置的 UniqueNameInOwner 标记随之在 target 上重新注册，% 查找保持可用。
    /// </summary>
    private static void TransferAllNodesFromScene(Node target, string scenePath)
    {
        PackedScene? packedScene = ResourceLoader.Load<PackedScene>(scenePath, null, ResourceLoader.CacheMode.Reuse);
        if (packedScene == null)
        {
            LocalMultiControlLogger.Warn($"瓦库托管勾选框加载场景失败: {scenePath}");
            return;
        }

        Node source = packedScene.Instantiate();
        target.Name = source.Name;
        foreach (Node child in source.GetChildren())
        {
            source.RemoveChild(child);
            target.AddChild(child);
            child.Owner = target;
            SetDescendantOwners(child, target);
        }

        source.QueueFree();
    }

    private static void SetDescendantOwners(Node node, Node owner)
    {
        foreach (Node child in node.GetChildren())
        {
            child.Owner = owner;
            SetDescendantOwners(child, owner);
        }
    }
}

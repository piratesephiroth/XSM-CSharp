#if TOOLS
using Godot;
using System;

[Tool]
public class Xsm : EditorPlugin
{
	public override void _EnterTree()
	{
		var stateScript = GD.Load<Script>("res://addons/xsm_csharp/State.cs");
		var stateRootScript = GD.Load<Script>("res://addons/xsm_csharp/StateRoot.cs");
		var icon = GD.Load<Texture>("res://addons/xsm_csharp/icon_statecharts.png");
		AddCustomType("State", "Node", stateScript, icon);
		AddCustomType("StateRoot", "Node", stateRootScript, icon);
	}

	public override void _ExitTree()
	{
		RemoveCustomType("StateRoot");
		RemoveCustomType("State");
	}
}
#endif

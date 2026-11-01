using Editor;
using Sandbox;

namespace FluidPlay.Waymaker.Editor;

/// <summary>
/// Standalone spline editing tool for Waymaker components.
/// </summary>
[EditorTool]
[Title("Waymaker Splines")]
[Icon("timeline")]
[Alias("waymaker_splines")]
[Group("1")]
[Order(3)]
public sealed class WaymakerSplineEditorTool : EditorTool<Waymaker>
{
	private WaymakerSplineEditorWindow m_Window;
	private Waymaker m_SelectedWaymaker;

	public override void OnEnabled()
	{
		m_Window = new WaymakerSplineEditorWindow();
		AddOverlay(m_Window, TextFlag.RightBottom, 10);
	}

	public override void OnDisabled()
	{
		m_Window?.OnDisabled();
	}

	public override void OnUpdate()
	{
		m_Window?.OnUpdate();
	}

	public override void OnSelectionChanged()
	{
		Waymaker target = GetSelectedComponent<Waymaker>();
		if (!target.IsValid())
		{
			m_SelectedWaymaker = null;
			m_Window?.OnSelectionChanged(null);
			return;
		}

		if (target == m_SelectedWaymaker)
			return;

		m_Window?.OnSelectionChanged(target);
		m_SelectedWaymaker = target;
	}
}

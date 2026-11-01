using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace FluidPlay.Waymaker.Editor;

/// <summary>
/// Waymaker-owned spline control sheet and gizmos.
/// </summary>
public sealed class WaymakerSplineEditorWindow : WidgetWindow
{
	private const float WaymakerHeaderHeight = 32.0f;
	private const float GizmoBoxSize = 2.0f;
	private const float LineThickness = 2.0f;
	private const float TangentLineThickness = 0.8f;
	private const float GeneratedWireThickness = 1.0f;

	private static bool s_IsClosed;
	private Waymaker m_Target;
	private ControlWidget m_InTangentControl;
	private ControlWidget m_OutTangentControl;
	private bool m_InTangentSelected;
	private bool m_OutTangentSelected;
	private bool m_DraggingOutNewPoint;
	private bool m_MoveInProgress;
	private List<Vector3> m_Polyline = [];
	private IDisposable m_MovementUndoScope;

	/// <summary>
	/// Editor-facing tangent mode enum with registered icons; Spline.HandleMode itself has no editor enum control.
	/// </summary>
	public enum WaymakerHandleMode
	{
		[Icon("auto_fix_high")]
		Auto,

		[Icon("show_chart")]
		Linear,

		[Icon("open_in_full")]
		Mirrored,

		[Icon("call_split")]
		Split
	}

	public WaymakerSplineEditorWindow()
	{
		ContentMargins = 0;
		Layout = Layout.Column();
		MaximumWidth = 500;
		MinimumWidth = 300;
		Rebuild();
	}

	public void OnDisabled()
	{
		m_MovementUndoScope?.Dispose();
		m_MovementUndoScope = null;
	}

	public void OnUpdate()
	{
		if (m_Target.IsValid())
			DrawGizmos();
	}

	public void OnSelectionChanged(Waymaker target)
	{
		m_Target = target;
		SelectedPointIndex = 0;
		m_InTangentSelected = false;
		m_OutTangentSelected = false;
		Rebuild();
	}

	[Title("Tangent Mode")]
	private WaymakerHandleMode _selectedPointTangentMode
	{
		get => IsSelectedPointValid() ? (WaymakerHandleMode)SelectedPoint.Mode : WaymakerHandleMode.Auto;
		set => UpdateSelectedPoint(SelectedPoint with { Mode = (Spline.HandleMode)value });
	}

	[Title("Position")]
	private Vector3 _selectedPointPosition
	{
		get => IsSelectedPointValid() ? SelectedPoint.Position : Vector3.Zero;
		set => UpdateSelectedPoint(SelectedPoint with { Position = value });
	}

	[Title("In")]
	private Vector3 _selectedPointIn
	{
		get => IsSelectedPointValid() ? SelectedPoint.In : Vector3.Zero;
		set => UpdateSelectedPoint(SelectedPoint with { In = value });
	}

	[Title("Out")]
	private Vector3 _selectedPointOut
	{
		get => IsSelectedPointValid() ? SelectedPoint.Out : Vector3.Zero;
		set => UpdateSelectedPoint(SelectedPoint with { Out = value });
	}

	[Title("Roll (Degrees)")]
	private float _selectedPointRoll
	{
		get => IsSelectedPointValid() ? SelectedPoint.Roll : 0.0f;
		set => UpdateSelectedPoint(SelectedPoint with { Roll = value });
	}

	[Title("Scale (_, Width, Height)")]
	private Vector3 _selectedPointScale
	{
		get => IsSelectedPointValid() ? SelectedPoint.Scale : Vector3.One;
		set => UpdateSelectedPoint(SelectedPoint with { Scale = value });
	}

	[Title("Up Vector")]
	private Vector3 _selectedPointUp
	{
		get => IsSelectedPointValid() ? SelectedPoint.Up : Vector3.Up;
		set => UpdateSelectedPoint(SelectedPoint with { Up = value });
	}

	private int SelectedPointIndex { get; set; }
	private Spline.Point SelectedPoint => IsSelectedPointValid() ? m_Target.Spline.GetPoint(SelectedPointIndex) : new Spline.Point();

	private bool IsSelectedPointValid() => m_Target.IsValid() && SelectedPointIndex >= 0 && SelectedPointIndex < m_Target.Spline.PointCount;

	private void UpdateSelectedPoint(Spline.Point point)
	{
		if (!IsSelectedPointValid())
			return;

		using (CreateUndoScope("Update Waymaker Spline Point"))
			m_Target.Spline.UpdatePoint(SelectedPointIndex, point);

		ToggleTangentInput();
	}

	private void Rebuild()
	{
		Layout.Clear(true);
		Layout.Margin = 0;
		Icon = s_IsClosed ? "" : "route";
		WindowTitle = s_IsClosed ? "" : $"Waymaker Spline Editor - {m_Target?.GameObject?.Name ?? ""}";
		IsGrabbable = !s_IsClosed;

		if (s_IsClosed)
		{
			var closedRow = Layout.AddRow();
			closedRow.Add(new IconButton("route", ReopenWindow)
			{
				ToolTip = "Open Waymaker Spline Editor",
				FixedHeight = WaymakerHeaderHeight,
				FixedWidth = WaymakerHeaderHeight,
				Background = Color.Transparent
			});
			MinimumWidth = 0;
			return;
		}

		MinimumWidth = 400;
		BuildHeader();
		if (m_Target.IsValid())
			BuildControlSheet();
		Layout.Margin = 4;
	}

	private void BuildHeader()
	{
		var header = Layout.AddRow();
		header.AddStretchCell();
		header.Add(new IconButton("info")
		{
			ToolTip = "Edit spline points with the control sheet or 3D gizmos. Click a segment to insert a point; Shift-drag a point to extend the spline.",
			FixedHeight = WaymakerHeaderHeight,
			FixedWidth = WaymakerHeaderHeight,
			Background = Color.Transparent
		});
		header.Add(new IconButton("close", CloseWindow)
		{
			ToolTip = "Close Editor",
			FixedHeight = WaymakerHeaderHeight,
			FixedWidth = WaymakerHeaderHeight,
			Background = Color.Transparent
		});
	}

	private void BuildControlSheet()
	{
		var serialized = this.GetSerialized();
		var controlSheet = new ControlSheet();
		controlSheet.AddRow(serialized.GetProperty(nameof(_selectedPointTangentMode)));
		controlSheet.AddRow(serialized.GetProperty(nameof(_selectedPointPosition)));
		m_InTangentControl = controlSheet.AddRow(serialized.GetProperty(nameof(_selectedPointIn)));
		m_OutTangentControl = controlSheet.AddRow(serialized.GetProperty(nameof(_selectedPointOut)));
		controlSheet.AddGroup("Advanced", [
			serialized.GetProperty(nameof(_selectedPointRoll)),
			serialized.GetProperty(nameof(_selectedPointScale)),
			serialized.GetProperty(nameof(_selectedPointUp))
		]);
		controlSheet.AddLayout(BuildControlButtons());
		Layout.Add(controlSheet);
		ToggleTangentInput();
	}

	private Layout BuildControlButtons()
	{
		var row = Layout.Row();
		row.Spacing = 16;
		row.Margin = 8;
		row.Add(new IconButton("skip_previous", SelectPreviousPoint) { ToolTip = "Go to previous point" });
		row.Add(new IconButton("skip_next", SelectNextPoint) { ToolTip = "Go to next point" });
		row.Add(new IconButton("delete", DeleteSelectedPoint) { ToolTip = "Delete point (at least two points are kept)" });
		row.Add(new IconButton("add", AddPointAfterSelection) { ToolTip = "Insert point after current point" });
		return row;
	}

	private void ReopenWindow()
	{
		s_IsClosed = false;
		Rebuild();
	}

	private void SelectPreviousPoint() => SelectPoint(Math.Max(0, SelectedPointIndex - 1));

	private void SelectNextPoint() => SelectPoint(Math.Min(m_Target.Spline.PointCount - 1, SelectedPointIndex + 1));

	private void DeleteSelectedPoint()
	{
		if (!IsSelectedPointValid() || m_Target.Spline.PointCount <= 2)
			return;

		using (CreateUndoScope("Delete Waymaker Spline Point"))
			m_Target.Spline.RemovePoint(SelectedPointIndex);

		SelectPoint(Math.Min(SelectedPointIndex, m_Target.Spline.PointCount - 1));
	}

	private void AddPointAfterSelection()
	{
		if (!IsSelectedPointValid())
			return;

		using (CreateUndoScope("Add Waymaker Spline Point"))
		{
			var spline = m_Target.Spline;
			if (SelectedPointIndex == spline.PointCount - 1)
			{
				float distance = spline.GetDistanceAtPoint(SelectedPointIndex);
				Vector3 tangent = spline.SampleAtDistance(distance).Tangent;
				var newPoint = SelectedPoint with { Position = SelectedPoint.Position + tangent * 200.0f };
				spline.InsertPoint(SelectedPointIndex + 1, newPoint);
			}
			else
			{
				float current = spline.GetDistanceAtPoint(SelectedPointIndex);
				float next = spline.GetDistanceAtPoint(SelectedPointIndex + 1);
				spline.AddPointAtDistance((current + next) * 0.5f, true);
			}
		}

		SelectPoint(Math.Min(SelectedPointIndex + 1, m_Target.Spline.PointCount - 1));
	}

	private void SelectPoint(int index)
	{
		SelectedPointIndex = Math.Clamp(index, 0, Math.Max(0, m_Target.Spline.PointCount - 1));
		m_InTangentSelected = false;
		m_OutTangentSelected = false;
		WindowTitle = $"Waymaker Spline Point [{SelectedPointIndex}] Editor - {m_Target.GameObject.Name}";
		ToggleTangentInput();
	}

	private void ToggleTangentInput()
	{
		if (!m_Target.IsValid())
			return;

		bool editable = SelectedPoint.Mode is not (Spline.HandleMode.Auto or Spline.HandleMode.Linear);
		if (m_InTangentControl is not null)
			m_InTangentControl.Enabled = editable;
		if (m_OutTangentControl is not null)
			m_OutTangentControl.Enabled = editable;
	}

	private IDisposable CreateUndoScope(string name)
	{
		return SceneEditorSession.Active.UndoScope(name).WithComponentChanges(m_Target).Push();
	}

	private void CloseWindow()
	{
		s_IsClosed = true;
		Rebuild();
		Position = Parent.Size - 32;
	}

	private void DrawGizmos()
	{
		using (Gizmo.Scope("waymaker_spline_editor", m_Target.WorldTransform))
		{
			DrawSplineSegments();
			if (IsViewportWireframeEnabled())
				DrawGeneratedMeshWireframe();
			DrawPositionGizmo();
			DrawPointControls();
		}
	}

	private bool IsViewportWireframeEnabled()
	{
		var editorCamera = m_Target.Scene.GetAllComponents<CameraComponent>()
			.FirstOrDefault(camera => camera.GameObject.Name == "editor_camera");
		return editorCamera?.WireframeMode == true;
	}

	/// <summary>
	/// Draw the baked quad topology while editing the Waymaker spline. The mesh is a generated child, so it would
	/// otherwise only show a wireframe when that child is selected directly.
	/// </summary>
	private void DrawGeneratedMeshWireframe()
	{
		foreach (var child in m_Target.GameObject.Children)
		{
			if (!child.Tags.Has("waymaker_generated_mesh"))
				continue;

			var meshComponent = child.Components.Get<MeshComponent>();
			var mesh = meshComponent?.Mesh;
			if (mesh is null)
				continue;

			Gizmo.Draw.Color = Color.Cyan;
			Gizmo.Draw.LineThickness = GeneratedWireThickness;
			Gizmo.Draw.IgnoreDepth = true;

			foreach (var face in mesh.FaceHandles)
			{
				foreach (var halfEdge in mesh.GetFaceEdges(face))
				{
					if (!halfEdge.NextEdge.IsValid)
						continue;

					Vector3 start = mesh.GetVertexPosition(halfEdge.Vertex);
					Vector3 end = mesh.GetVertexPosition(halfEdge.NextEdge.Vertex);
					start = m_Target.WorldTransform.PointToLocal(meshComponent.WorldTransform.PointToWorld(start));
					end = m_Target.WorldTransform.PointToLocal(meshComponent.WorldTransform.PointToWorld(end));
					Gizmo.Draw.Line(start, end);
				}
			}
		}
	}

	private void DrawSplineSegments()
	{
		m_Target.Spline.ConvertToPolyline(ref m_Polyline);
		for (int i = 0; i < m_Polyline.Count - 1; i++)
		{
			Vector3 start = m_Polyline[i];
			Vector3 end = m_Polyline[i + 1];
			using (Gizmo.Scope("segment" + i))
			using (Gizmo.Hitbox.LineScope())
			{
				Gizmo.Draw.LineThickness = LineThickness;
				Gizmo.Hitbox.AddPotentialLine(start, end, LineThickness * 2.0f);
				Gizmo.Draw.Line(start, end);
				if (Gizmo.IsHovered && Gizmo.HasMouseFocus && Gizmo.HasClicked && Gizmo.Pressed.This)
					InsertPointAtClosestPosition(start, end);
			}
		}
	}

	private void InsertPointAtClosestPosition(Vector3 start, Vector3 end)
	{
		if (!new Line(start, end).ClosestPoint(Gizmo.CurrentRay.ToLocal(Gizmo.Transform), out Vector3 pointOnLine, out _))
			return;

		float distance = m_Target.Spline.SampleAtClosestPosition(pointOnLine).Distance;
		using (CreateUndoScope("Add Waymaker Spline Point"))
			SelectedPointIndex = m_Target.Spline.AddPointAtDistance(distance, true);

		m_InTangentSelected = false;
		m_OutTangentSelected = false;
		ToggleTangentInput();
	}

	private void DrawPositionGizmo()
	{
		if (!Gizmo.IsShiftPressed)
			m_DraggingOutNewPoint = false;

		if (!IsSelectedPointValid() || (SelectedPointIndex == 0 && !m_InTangentSelected && !m_OutTangentSelected))
			return;

		m_MoveInProgress = false;
		Vector3 gizmoPosition = SelectedPoint.Position;
		if (m_InTangentSelected)
			gizmoPosition += SelectedPoint.In;
		else if (m_OutTangentSelected)
			gizmoPosition += SelectedPoint.Out;

		// The handle is drawn at the current scope's transform, so the point/tangent position has to go into
		// a child scope. Passing it as the control's value instead leaves the handle on the component origin,
		// where it sits under the GameObject's own move gizmo and can never be grabbed. Translation-only
		// scope, so the delta still comes back in component-local space.
		using (Gizmo.Scope("position", new Transform(gizmoPosition)))
		{
			if (Gizmo.Control.Position("waymaker_spline_control_", Vector3.Zero, out Vector3 delta, GetHandleRotation()))
			{
				m_MoveInProgress = true;
				m_MovementUndoScope ??= CreateUndoScope("Move Waymaker Spline Point");
				MoveSelectedControl(delta);
			}

			if (!m_MoveInProgress && Gizmo.WasLeftMouseReleased)
			{
				m_MovementUndoScope?.Dispose();
				m_MovementUndoScope = null;
			}
		}
	}

	private void MoveSelectedControl(Vector3 delta)
	{
		var updated = SelectedPoint;
		if (m_InTangentSelected)
		{
			updated.In += delta;
			if (updated.Mode == Spline.HandleMode.Auto)
				updated.Mode = Spline.HandleMode.Mirrored;
			if (updated.Mode == Spline.HandleMode.Mirrored)
				updated.Out = -updated.In;
		}
		else if (m_OutTangentSelected)
		{
			updated.Out += delta;
			if (updated.Mode == Spline.HandleMode.Auto)
				updated.Mode = Spline.HandleMode.Mirrored;
			if (updated.Mode == Spline.HandleMode.Mirrored)
				updated.In = -updated.Out;
		}
		else
		{
			if (Gizmo.IsShiftPressed && !m_DraggingOutNewPoint)
			{
				m_DraggingOutNewPoint = true;
				m_Target.Spline.InsertPoint(SelectedPointIndex + 1, updated);
				SelectedPointIndex++;
				updated = SelectedPoint;
			}
			updated.Position += delta;
		}

		m_Target.Spline.UpdatePoint(SelectedPointIndex, updated);
		if (!Gizmo.IsShiftPressed)
			m_DraggingOutNewPoint = false;
	}

	private Rotation GetHandleRotation()
	{
		if (Gizmo.Settings.GlobalSpace || !IsSelectedPointValid())
			return Rotation.Identity;

		float distance = m_Target.Spline.GetDistanceAtPoint(SelectedPointIndex);
		var sample = m_Target.Spline.SampleAtDistance(distance);
		if (sample.Tangent.IsNearlyZero())
			return Rotation.Identity;

		Vector3 up = sample.Up.IsNearZeroLength ? Vector3.Up : sample.Up;
		up = Rotation.FromAxis(sample.Tangent, sample.Roll) * up;
		return Rotation.LookAt(sample.Tangent, up);
	}

	private void DrawPointControls()
	{
		var spline = m_Target.Spline;
		for (int i = 0; i < spline.PointCount; i++)
		{
			if (spline.IsLoop && i == spline.SegmentCount)
				continue;

			using (Gizmo.Scope("point_controls" + i, new Transform(spline.GetPoint(i).Position)))
			{
				Gizmo.Draw.IgnoreDepth = true;
				DrawPointPositionHandle(i);
				if (SelectedPointIndex == i)
					DrawTangentHandles(spline.GetPoint(i));
			}
		}
	}

	private void DrawPointPositionHandle(int index)
	{
		using (Gizmo.Scope("position"))
		using (Gizmo.GizmoControls.PushFixedScale())
		{
			Gizmo.Hitbox.DepthBias = 0.1f;
			Gizmo.Hitbox.BBox(BBox.FromPositionAndSize(Vector3.Zero, GizmoBoxSize));
			if (Gizmo.IsHovered || (index == SelectedPointIndex && !m_InTangentSelected && !m_OutTangentSelected))
				Gizmo.Draw.Color = Color.Cyan;
			Gizmo.Draw.SolidBox(BBox.FromPositionAndSize(Vector3.Zero, GizmoBoxSize));
			if (Gizmo.HasClicked && Gizmo.Pressed.This)
				SelectPoint(index);
		}
	}

	private void DrawTangentHandles(Spline.Point point)
	{
		Gizmo.Draw.Color = Color.White;
		Gizmo.Draw.LineThickness = TangentLineThickness;
		DrawTangentHandle("in_tangent", point.In, ref m_InTangentSelected, ref m_OutTangentSelected);
		DrawTangentHandle("out_tangent", point.Out, ref m_OutTangentSelected, ref m_InTangentSelected);
	}

	private void DrawTangentHandle(string name, Vector3 offset, ref bool selected, ref bool otherSelected)
	{
		using (Gizmo.Scope(name, new Transform(offset)))
		{
			Gizmo.Draw.Line(-offset, Vector3.Zero);
			if (SelectedPoint.Mode == Spline.HandleMode.Linear)
				return;

			using (Gizmo.GizmoControls.PushFixedScale())
			{
				Gizmo.Hitbox.DepthBias = 0.1f;
				Gizmo.Hitbox.BBox(BBox.FromPositionAndSize(Vector3.Zero, GizmoBoxSize));
				if (Gizmo.IsHovered || selected)
					Gizmo.Draw.Color = Color.Cyan;
				Gizmo.Draw.SolidBox(BBox.FromPositionAndSize(Vector3.Zero, GizmoBoxSize));
				if (Gizmo.HasClicked && Gizmo.Pressed.This)
				{
					selected = true;
					otherSelected = false;
				}
			}
		}
	}
}

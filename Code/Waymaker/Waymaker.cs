using System;
using System.Linq;
using Sandbox;

namespace FluidPlay.Waymaker;

/// <summary>
/// Generates a UV-mapped surface that follows an editable spline.
/// </summary>
[Icon("route")]
public partial class Waymaker : Component, Component.ExecuteInEditor, Component.IHasBounds
{
	private const string GeneratedMeshTag = "waymaker_generated_mesh";
	private const float SampleSpacing = 40.0f;

	[Property, Feature("Waymaker"), Hide]
	public Spline Spline
	{
		get;
		set
		{
			field = value ?? new Spline();
			SubscribeToSpline();
			MarkDirty();
		}
	} = new();

	[Property(Title = "Loop"), Feature("Spline", Icon = "all_inclusive")]
	private bool Loop
	{
		get => Spline?.IsLoop ?? false;
		set
		{
			if (Spline is null || Spline.IsLoop == value || (value && Spline.PointCount < 2))
				return;

			Spline.IsLoop = value;
			MarkDirty();
		}
	}

	private Spline m_SubscribedSpline;
	private bool m_IsDirty = true;

	[Property(Title = "Source Material"), Feature("Surface", Icon = "texture"), Validate(nameof(IsSourceMaterialAssigned), "Assign a source material to generate the Waymaker surface.", LogLevel.Warn)]
	private Material SourceMaterial { get; set { field = value; MarkDirty(); } }

	[Property(Title = "Surface Width"), Feature("Surface")]
	private float SurfaceWidth { get; set { field = Math.Max(0.01f, value); MarkDirty(); } } = 500.0f;

	[Property(Title = "Dynamic V Tiling"), Feature("Texturing", Icon = "texture")]
	private bool DynamicVTiling { get; set { field = value; MarkDirty(); } } = true;

	[Property(Title = "Dynamic V Tile Length"), Feature("Texturing"), ShowIf(nameof(DynamicVTiling), true)]
	private float DynamicVTileLength { get; set { field = Math.Max(0.1f, value); MarkDirty(); } } = 500.0f;

	[Property(Title = "Vertical Tiles"), Feature("Texturing"), ShowIf(nameof(DynamicVTiling), false)]
	private int VerticalTiles { get; set { field = Math.Max(1, value); MarkDirty(); } } = 1;

	[Property(Title = "Horizontal Tiling"), Feature("Texturing")]
	private float HorizontalTiling { get; set { field = Math.Max(0.01f, value); MarkDirty(); } } = 1.0f;

	[Property(Title = "Horizontal Padding"), Feature("Texturing"), Range(0.0f, 1.0f)]
	private float HorizontalPadding { get; set { field = value.Clamp(0.0f, 1.0f); MarkDirty(); } } = 0.0f;

	[Property(Title = "Cap Texture"), Feature("Caps", Icon = "vertical_align_bottom"), ShowIf(nameof(CanUseCapSettings), true)]
	private bool CapTexture { get; set { field = value; MarkDirty(); } } = false;

	[Property(Title = "Cap Texture Start"), Feature("Caps"), ShowIf(nameof(ShowCapMaterials), true)]
	private Material CapTextureStart { get; set { field = value; MarkDirty(); } }

	[Property(Title = "Cap Texture End"), Feature("Caps"), ShowIf(nameof(ShowCapMaterials), true)]
	private Material CapTextureEnd { get; set { field = value; MarkDirty(); } }

	[Property(Title = "Cap Length"), Feature("Caps"), ShowIf(nameof(ShowCapMaterials), true)]
	private float CapLength { get; set { field = Math.Max(0.0f, value); MarkDirty(); } } = 100.0f;

	// Keep the serialized member name so existing scenes retain their value; the inspector now reflects its edge-length meaning.
	[Property(Title = "Minimum Edge Length"), Feature("Geometry"), Validate(nameof(IsMinimumEdgeLengthInRecommendedRange), "Values above 0.1 may create coarser quads and distort tight turns.", LogLevel.Warn)]
	private float MinimumEdgeWidth { get; set { field = Math.Max(0.0f, value); MarkDirty(); } } = 0.01f;

	private bool CanUseCapSettings => Spline is not null && WaymakerMeshBuilder.CanUseCaps(DynamicVTiling, Spline.IsLoop);
	private bool ShowCapMaterials => CanUseCapSettings && CapTexture;

	public BBox LocalBounds
	{
		get
		{
			var bounds = Spline?.Bounds ?? new BBox();
			var padding = new Vector3(SurfaceWidth * 0.5f);
			return new BBox(bounds.Mins - padding, bounds.Maxs + padding);
		}
	}

	public Waymaker()
	{
		Spline.InsertPoint(Spline.PointCount, new Spline.Point { Position = Vector3.Zero });
		Spline.InsertPoint(Spline.PointCount, new Spline.Point { Position = new Vector3(1000.0f, 0.0f, 0.0f) });
		Spline.InsertPoint(Spline.PointCount, new Spline.Point { Position = new Vector3(1600.0f, 1000.0f, 0.0f) });
	}

	protected override void OnEnabled()
	{
		SubscribeToSpline();
		EnsureGeneratedMeshExists();
	}

	protected override void OnDisabled()
	{
		UnsubscribeFromSpline();
		RemoveGeneratedMesh();
	}

	protected override void OnValidate()
	{
		SubscribeToSpline();
		MarkDirty();
	}

	protected override void OnUpdate()
	{
		SyncSplineSubscription();

		if (!m_IsDirty || !Scene.IsEditor)
			return;

		RebuildGeneratedMesh();
	}

	private bool IsMinimumEdgeLengthInRecommendedRange(float value) => value <= 0.1f;
	private bool IsSourceMaterialAssigned(Material material) => material is not null;

	private void MarkDirty() => m_IsDirty = true;

	private void SyncSplineSubscription()
	{
		if (ReferenceEquals(m_SubscribedSpline, Spline))
			return;

		SubscribeToSpline();
		MarkDirty();
	}

	private void SubscribeToSpline()
	{
		if (ReferenceEquals(m_SubscribedSpline, Spline))
			return;

		UnsubscribeFromSpline();
		m_SubscribedSpline = Spline;

		if (m_SubscribedSpline is not null)
			m_SubscribedSpline.SplineChanged += MarkDirty;
	}

	private void UnsubscribeFromSpline()
	{
		if (m_SubscribedSpline is null)
			return;

		m_SubscribedSpline.SplineChanged -= MarkDirty;
		m_SubscribedSpline = null;
	}

	private void EnsureGeneratedMeshExists()
	{
		if (!Scene.IsEditor || HasGeneratedMesh())
			return;

		RebuildGeneratedMesh();
	}

	private void RebuildGeneratedMesh()
	{
		if (!Scene.IsEditor)
			return;

		RemoveGeneratedMesh();

		if (Spline is { PointCount: >= 2 } && SourceMaterial is not null && Spline.Length > 0.001f)
		{
			var settings = new WaymakerMeshSettings
			{
				SourceMaterial = SourceMaterial,
				StartCapMaterial = CapTextureStart,
				EndCapMaterial = CapTextureEnd,
				SurfaceWidth = SurfaceWidth,
				DynamicVTiling = DynamicVTiling,
				DynamicVTileLength = DynamicVTileLength,
				VerticalTiles = VerticalTiles,
				HorizontalTiling = HorizontalTiling,
				HorizontalPadding = HorizontalPadding,
				UseCaps = CapTexture && CanUseCapSettings,
				CapLength = CapLength,
				MinimumEdgeLength = MinimumEdgeWidth
			};

			var mesh = WaymakerMeshBuilder.Build(Spline, settings, SampleSpacing);
			if (mesh is not null && mesh.FaceHandles.Any())
			{
				var child = new GameObject(GameObject, true, "Waymaker Surface");
				child.Tags.Add(GeneratedMeshTag);

				var meshComponent = child.AddComponent<MeshComponent>();
				meshComponent.Mesh = mesh;
				meshComponent.SmoothingAngle = 40.0f;
			}
		}

		m_IsDirty = false;
	}

	private bool HasGeneratedMesh() => GameObject.Children.Any(child => child.Tags.Has(GeneratedMeshTag));

	private void RemoveGeneratedMesh()
	{
		if (!GameObject.IsValid())
			return;

		foreach (var child in GameObject.Children.Where(child => child.Tags.Has(GeneratedMeshTag)).ToArray())
			child.Destroy();
	}
}

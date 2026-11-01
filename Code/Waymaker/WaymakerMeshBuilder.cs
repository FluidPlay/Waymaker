using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace FluidPlay.Waymaker;

internal struct WaymakerMeshSettings
{
	public Material SourceMaterial;
	public Material StartCapMaterial;
	public Material EndCapMaterial;
	public float SurfaceWidth;
	public bool DynamicVTiling;
	public float DynamicVTileLength;
	public int VerticalTiles;
	public float HorizontalTiling;
	public float HorizontalPadding;
	public bool UseCaps;
	public float CapLength;
	public float MinimumEdgeLength;
}

internal readonly record struct WaymakerFrame(Vector3 Position, Rotation Rotation, float SplineDistance)
{
	public Vector3 Right => Rotation.Right;
}

/// <summary>
/// Samples Waymaker's spline and builds the textured quad strip, including UVs and optional endpoint cap materials.
/// </summary>
internal static class WaymakerMeshBuilder
{
	private const float Epsilon = 0.001f;

	internal static (float Min, float Max) GetMainURange(float horizontalTiling, float horizontalPadding)
	{
		float tiling = Math.Max(0.01f, horizontalTiling);
		float padding = Math.Min(Math.Max(0.0f, horizontalPadding), tiling);
		return (padding * 0.5f, tiling - padding * 0.5f);
	}

	internal static (float Min, float Max) GetDynamicVRange(float startDistance, float endDistance, float tileLength)
	{
		float length = Math.Max(Epsilon, tileLength);
		return (startDistance / length, endDistance / length);
	}

	internal static (float Min, float Max) GetFixedVRange(float startDistance, float endDistance, float totalLength, int verticalTiles)
	{
		float length = Math.Max(Epsilon, totalLength);
		float tiles = Math.Max(1, verticalTiles);
		return (startDistance / length * tiles, endDistance / length * tiles);
	}

	internal static bool CanUseCaps(bool dynamicVTiling, bool isLoop) => !dynamicVTiling && !isLoop;

	internal static float GetEffectiveCapLength(float totalLength, float requestedLength, bool useCaps, bool dynamicVTiling, bool isLoop)
	{
		if (!useCaps || !CanUseCaps(dynamicVTiling, isLoop))
			return 0.0f;

		return Math.Clamp(requestedLength, 0.0f, Math.Max(0.0f, totalLength) * 0.5f);
	}

	public static PolygonMesh Build(Spline spline, WaymakerMeshSettings settings, float sampleSpacing)
	{
		if (spline is null || spline.PointCount < 2 || spline.Length <= Epsilon)
			return null;

		float totalLength = spline.Length;
		bool useCaps = settings.UseCaps && CanUseCaps(settings.DynamicVTiling, spline.IsLoop);
		float capLength = GetEffectiveCapLength(totalLength, settings.CapLength, useCaps, settings.DynamicVTiling, spline.IsLoop);

		var distances = CreateSampleDistances(spline, sampleSpacing, capLength);
		if (distances.Count < 2)
			return null;

		var sampledFrames = distances.Select(distance => SampleFrame(spline, distance)).ToArray();
		float halfWidth = Math.Max(0.0f, settings.SurfaceWidth) * 0.5f;
		var frames = MergeShortEdges(sampledFrames, halfWidth, Math.Max(0.0f, settings.MinimumEdgeLength),
			capLength > Epsilon ? capLength : -1.0f,
			capLength > Epsilon ? totalLength - capLength : -1.0f);
		if (frames.Length < 2)
			return null;

		var cumulativeCenterlineLengths = GetCumulativeCenterlineLengths(frames);

		var mesh = new PolygonMesh();
		var vertices = new HalfEdgeMesh.VertexHandle[frames.Length][];

		for (int i = 0; i < frames.Length; i++)
		{
			Vector3 halfSpan = frames[i].Right * halfWidth;
			vertices[i] = mesh.AddVertices(frames[i].Position - halfSpan, frames[i].Position + halfSpan);
		}

		var mainURange = GetMainURange(settings.HorizontalTiling, settings.HorizontalPadding);
		float mainU0 = mainURange.Min;
		float mainU1 = mainURange.Max;
		int verticalTiles = Math.Max(1, settings.VerticalTiles);
		float dynamicTileLength = Math.Max(Epsilon, settings.DynamicVTileLength);
		float interiorStart = capLength;
		float interiorEnd = Math.Max(interiorStart, totalLength - capLength);
		float interiorLength = Math.Max(0.0f, interiorEnd - interiorStart);

		for (int i = 0; i < frames.Length - 1; i++)
		{
			float distance0 = frames[i].SplineDistance;
			float distance1 = frames[i + 1].SplineDistance;
			float centerline0 = cumulativeCenterlineLengths[i];
			float centerline1 = cumulativeCenterlineLengths[i + 1];
			float segmentMidpoint = (distance0 + distance1) * 0.5f;

			bool isStartCap = capLength > Epsilon && segmentMidpoint < capLength;
			bool isEndCap = capLength > Epsilon && segmentMidpoint > totalLength - capLength;

			Material material = isStartCap
				? settings.StartCapMaterial ?? settings.SourceMaterial
				: isEndCap
					? settings.EndCapMaterial ?? settings.SourceMaterial
					: settings.SourceMaterial;

			float v0;
			float v1;
			if (isStartCap)
			{
				v0 = (distance0 / Math.Max(Epsilon, capLength)).Clamp(0.0f, 1.0f);
				v1 = (distance1 / Math.Max(Epsilon, capLength)).Clamp(0.0f, 1.0f);
			}
			else if (isEndCap)
			{
				v0 = ((distance0 - (totalLength - capLength)) / Math.Max(Epsilon, capLength)).Clamp(0.0f, 1.0f);
				v1 = ((distance1 - (totalLength - capLength)) / Math.Max(Epsilon, capLength)).Clamp(0.0f, 1.0f);
			}
			else if (settings.DynamicVTiling)
			{
				var vRange = GetDynamicVRange(centerline0, centerline1, dynamicTileLength);
				v0 = vRange.Min;
				v1 = vRange.Max;
			}
			else if (useCaps && interiorLength > Epsilon)
			{
				var vRange = GetFixedVRange(distance0 - interiorStart, distance1 - interiorStart, interiorLength, verticalTiles);
				v0 = vRange.Min;
				v1 = vRange.Max;
			}
			else
			{
				var vRange = GetFixedVRange(centerline0, centerline1, cumulativeCenterlineLengths[^1], verticalTiles);
				v0 = vRange.Min;
				v1 = vRange.Max;
			}

			WaymakerMeshUtility.AddTexturedQuad(mesh, material,
				vertices[i][0], vertices[i][1], vertices[i + 1][1], vertices[i + 1][0],
				new Vector2(mainU0, v0), new Vector2(mainU1, v0),
				new Vector2(mainU1, v1), new Vector2(mainU0, v1));
		}

		mesh.RemoveBadGeometry();

		// Translucent shaders multiply texture alpha by vertex alpha. Explicitly initialize every half-edge color;
		// leaving PolygonMesh's color stream sparse can make some translucent face corners resolve to zero alpha.
		foreach (var halfEdge in mesh.HalfEdgeHandles)
		{
			mesh.SetVertexColor(halfEdge, Color32.White);
			if (halfEdge.OppositeEdge.IsValid)
				mesh.SetVertexColor(halfEdge.OppositeEdge, Color32.White);
		}

		return mesh;
	}

	internal static List<float> CreateSampleDistances(Spline spline, float sampleSpacing, float capLength)
	{
		float totalLength = spline.Length;
		float spacing = Math.Max(1.0f, sampleSpacing);
		var samples = new List<float> { 0.0f, totalLength };

		int subdivisions = Math.Max(2, (int)MathF.Ceiling(totalLength / spacing));
		for (int i = 1; i < subdivisions; i++)
			samples.Add(totalLength * i / subdivisions);

		for (int i = 0; i < spline.PointCount; i++)
			samples.Add(spline.GetDistanceAtPoint(i).Clamp(0.0f, totalLength));

		if (capLength > Epsilon)
		{
			samples.Add(capLength);
			samples.Add(totalLength - capLength);
		}

		samples.Sort();
		var unique = new List<float>(samples.Count);
		foreach (float sample in samples)
		{
			if (unique.Count == 0 || sample - unique[^1] > Epsilon)
				unique.Add(sample);
		}

		if (unique.Count == 1)
			unique.Add(totalLength);

		return unique;
	}

	private static WaymakerFrame SampleFrame(Spline spline, float distance)
	{
		var sample = spline.SampleAtDistance(distance);
		Vector3 tangent = sample.Tangent.Normal;
		if (tangent.IsNearZeroLength)
			tangent = Vector3.Forward;

		Vector3 up = sample.Up;
		if (up.IsNearZeroLength)
			up = Vector3.Up;
		if (MathF.Abs(Vector3.Dot(tangent, up.Normal)) > 0.999f)
			up = Vector3.Right;

		up = Rotation.FromAxis(tangent, sample.Roll) * up;
		Rotation rotation = Rotation.LookAt(tangent, up);
		return new WaymakerFrame(sample.Position, rotation, distance);
	}

	internal static float[] GetCumulativeCenterlineLengths(WaymakerFrame[] frames)
	{
		var lengths = new float[frames.Length];
		for (int i = 1; i < frames.Length; i++)
			lengths[i] = lengths[i - 1] + Vector3.DistanceBetween(frames[i - 1].Position, frames[i].Position);

		return lengths;
	}

	/// <summary>
	/// Coalesces short consecutive sections instead of narrowing the surface. Each retained quad is checked using the
	/// actual left/right longitudinal edge lengths at the requested surface width. Cap boundaries remain exact.
	/// </summary>
	internal static WaymakerFrame[] MergeShortEdges(
		WaymakerFrame[] frames,
		float halfWidth,
		float minimumEdgeLength,
		float capStartDistance = -1.0f,
		float capEndDistance = -1.0f)
	{
		if (frames.Length < 3 || minimumEdgeLength <= 0.0f)
			return frames;

		var merged = new List<WaymakerFrame> { frames[0] };
		int spanStart = 0;

		for (int i = 1; i < frames.Length; i++)
		{
			bool isEnd = i == frames.Length - 1;
			bool isCapBoundary = IsAtDistance(frames[i].SplineDistance, capStartDistance)
				|| IsAtDistance(frames[i].SplineDistance, capEndDistance);
			if (!isEnd && !isCapBoundary)
				continue;

			AppendSpan(merged, frames, spanStart, i, halfWidth, minimumEdgeLength);
			spanStart = i;
		}

		return merged.ToArray();
	}

	private static void AppendSpan(
		List<WaymakerFrame> merged,
		WaymakerFrame[] frames,
		int spanStart,
		int spanEnd,
		float halfWidth,
		float minimumEdgeLength)
	{
		int currentStart = spanStart;

		for (int candidate = spanStart + 1; candidate < spanEnd; candidate++)
		{
			if (!HasMinimumLongitudinalEdgeLength(frames[currentStart], frames[candidate], halfWidth, minimumEdgeLength))
				continue;

			// Avoid accepting a short quad at the tail when the same span can be represented by one valid longer quad.
			bool leavesShortTail = !HasMinimumLongitudinalEdgeLength(frames[candidate], frames[spanEnd], halfWidth, minimumEdgeLength);
			bool fullSpanFits = HasMinimumLongitudinalEdgeLength(frames[currentStart], frames[spanEnd], halfWidth, minimumEdgeLength);
			if (leavesShortTail && fullSpanFits)
				continue;

			merged.Add(frames[candidate]);
			currentStart = candidate;
		}

		if (spanEnd > currentStart)
			merged.Add(frames[spanEnd]);
	}

	internal static bool HasMinimumLongitudinalEdgeLength(WaymakerFrame start, WaymakerFrame end, float halfWidth, float minimumEdgeLength)
	{
		Vector3 centerDelta = end.Position - start.Position;
		if (centerDelta.Length <= Epsilon)
			return false;

		Vector3 centerDirection = centerDelta.Normal;
		Vector3 leftEdge = (end.Position - end.Right * halfWidth) - (start.Position - start.Right * halfWidth);
		Vector3 rightEdge = (end.Position + end.Right * halfWidth) - (start.Position + start.Right * halfWidth);

		return IsLongitudinalEdgeLongEnough(leftEdge, centerDirection, minimumEdgeLength)
			&& IsLongitudinalEdgeLongEnough(rightEdge, centerDirection, minimumEdgeLength);
	}

	private static bool IsLongitudinalEdgeLongEnough(Vector3 edge, Vector3 centerDirection, float minimumEdgeLength)
	{
		return edge.Length >= minimumEdgeLength && Vector3.Dot(edge, centerDirection) > 0.0f;
	}

	private static bool IsAtDistance(float actual, float expected)
	{
		return expected >= 0.0f && MathF.Abs(actual - expected) <= Epsilon;
	}
}

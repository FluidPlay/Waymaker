using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluidPlay.Waymaker;
using Sandbox;
using System.Linq;

namespace FluidPlay.Waymaker.Tests;

[TestClass]
public sealed class WaymakerMeshBuilderTests
{
	[TestMethod]
	public void MainUvRangeAppliesAbsoluteUvPadding()
	{
		var singleTile = WaymakerMeshBuilder.GetMainURange(1.0f, 0.2f);
		var doubleTile = WaymakerMeshBuilder.GetMainURange(2.0f, 0.2f);

		Assert.AreEqual(0.1f, singleTile.Min, 0.0001f);
		Assert.AreEqual(0.9f, singleTile.Max, 0.0001f);
		Assert.AreEqual(0.1f, doubleTile.Min, 0.0001f);
		Assert.AreEqual(1.9f, doubleTile.Max, 0.0001f);
	}

	[TestMethod]
	public void DynamicVTilingUsesAccumulatedCenterlineDistance()
	{
		var range = WaymakerMeshBuilder.GetDynamicVRange(25.0f, 275.0f, 100.0f);

		Assert.AreEqual(0.25f, range.Min, 0.0001f);
		Assert.AreEqual(2.75f, range.Max, 0.0001f);
	}

	[TestMethod]
	public void FixedVTilingDistributesWholeRepeatsOverTheRequestedPath()
	{
		var range = WaymakerMeshBuilder.GetFixedVRange(10.0f, 60.0f, 100.0f, 3);

		Assert.AreEqual(0.3f, range.Min, 0.0001f);
		Assert.AreEqual(1.8f, range.Max, 0.0001f);
		Assert.AreEqual(1.0f, WaymakerMeshBuilder.GetFixedVRange(0.0f, 1.0f, 1.0f, 0).Max, 0.0001f);
	}

	[TestMethod]
	public void CapsAreIneligibleForDynamicOrLoopedSplines()
	{
		Assert.IsTrue(WaymakerMeshBuilder.CanUseCaps(dynamicVTiling: false, isLoop: false));
		Assert.IsFalse(WaymakerMeshBuilder.CanUseCaps(dynamicVTiling: true, isLoop: false));
		Assert.IsFalse(WaymakerMeshBuilder.CanUseCaps(dynamicVTiling: false, isLoop: true));
		Assert.AreEqual(0.0f, WaymakerMeshBuilder.GetEffectiveCapLength(100.0f, 30.0f, true, true, false), 0.0001f);
		Assert.AreEqual(0.0f, WaymakerMeshBuilder.GetEffectiveCapLength(100.0f, 30.0f, true, false, true), 0.0001f);
	}

	[TestMethod]
	public void SplineLoopCanBeSetThroughItsPublicApi()
	{
		var spline = new Spline();
		spline.InsertPoint(0, new Spline.Point { Position = Vector3.Zero });
		spline.InsertPoint(1, new Spline.Point { Position = new Vector3(100.0f, 0.0f, 0.0f) });
		spline.InsertPoint(2, new Spline.Point { Position = new Vector3(100.0f, 100.0f, 0.0f) });
		var property = typeof(Spline).GetProperty(nameof(Spline.IsLoop));
		Assert.IsNotNull(property);
		Assert.IsTrue(property.CanWrite, $"{nameof(Spline.IsLoop)} must be writable for looped Waymaker validation.");

		property.SetValue(spline, true);
		Assert.IsTrue(spline.IsLoop);
	}

	[TestMethod]
	public void ShortPathClampsEachCapToHalfItsLength()
	{
		Assert.AreEqual(50.0f, WaymakerMeshBuilder.GetEffectiveCapLength(100.0f, 80.0f, true, false, false), 0.0001f);
	}

	[TestMethod]
	public void MinimumEdgeLengthMergesShortStraightSectionsWithoutNarrowingTheSurface()
	{
		var frames = new[]
		{
			new WaymakerFrame(Vector3.Zero, Rotation.Identity, 0.0f),
			new WaymakerFrame(new Vector3(40.0f, 0.0f, 0.0f), Rotation.Identity, 40.0f),
			new WaymakerFrame(new Vector3(80.0f, 0.0f, 0.0f), Rotation.Identity, 80.0f),
			new WaymakerFrame(new Vector3(120.0f, 0.0f, 0.0f), Rotation.Identity, 120.0f),
			new WaymakerFrame(new Vector3(160.0f, 0.0f, 0.0f), Rotation.Identity, 160.0f)
		};
		const float halfWidth = 250.0f;
		var merged = WaymakerMeshBuilder.MergeShortEdges(frames, halfWidth, minimumEdgeLength: 60.0f);

		CollectionAssert.AreEqual(new[] { 0.0f, 80.0f, 160.0f }, merged.Select(frame => frame.SplineDistance).ToArray());
		Assert.AreEqual(500.0f, 2.0f * halfWidth, 0.0001f);
	}

	[TestMethod]
	public void MinimumEdgeLengthSkipsTightTurnSectionsInsteadOfShrinkingWidth()
	{
		var frames = new[]
		{
			new WaymakerFrame(Vector3.Zero, Rotation.Identity, 0.0f),
			new WaymakerFrame(new Vector3(10.0f, 0.0f, 0.0f), Rotation.FromAxis(Vector3.Up, 45.0f), 10.0f),
			new WaymakerFrame(new Vector3(10.0f, 10.0f, 0.0f), Rotation.FromAxis(Vector3.Up, 90.0f), 20.0f),
			new WaymakerFrame(new Vector3(10.0f, 20.0f, 0.0f), Rotation.FromAxis(Vector3.Up, 90.0f), 30.0f)
		};
		var merged = WaymakerMeshBuilder.MergeShortEdges(frames, halfWidth: 10.0f, minimumEdgeLength: 25.0f);

		Assert.IsTrue(merged.Length < frames.Length, "Short, collapsed inner edges should be merged into longer quads.");
		for (int i = 0; i < merged.Length - 1; i++)
			Assert.IsTrue(WaymakerMeshBuilder.HasMinimumLongitudinalEdgeLength(merged[i], merged[i + 1], 10.0f, 2.0f), $"Merged quad {i} should have both longitudinal edges at least the requested length.");
	}

	[TestMethod]
	public void CapBoundariesAreInsertedIntoTheSampledPath()
	{
		var spline = new Spline();
		spline.InsertPoint(0, new Spline.Point { Position = Vector3.Zero, Mode = Spline.HandleMode.Linear });
		spline.InsertPoint(1, new Spline.Point { Position = new Vector3(1000.0f, 0.0f, 0.0f), Mode = Spline.HandleMode.Linear });

		var distances = WaymakerMeshBuilder.CreateSampleDistances(spline, 250.0f, 100.0f);

		CollectionAssert.Contains(distances, 100.0f);
		CollectionAssert.Contains(distances, 900.0f);
		CollectionAssert.AreEqual(distances.OrderBy(distance => distance).ToArray(), distances.ToArray());
	}

	[TestMethod]
	public void DynamicVAccumulationUsesGeneratedCenterlineChordLengths()
	{
		var frames = new[]
		{
			new WaymakerFrame(Vector3.Zero, Rotation.Identity, 0.0f),
			new WaymakerFrame(new Vector3(3.0f, 4.0f, 0.0f), Rotation.Identity, 5.0f),
			new WaymakerFrame(new Vector3(3.0f, 4.0f, 12.0f), Rotation.Identity, 17.0f)
		};

		var lengths = WaymakerMeshBuilder.GetCumulativeCenterlineLengths(frames);

		Assert.AreEqual(0.0f, lengths[0], 0.0001f);
		Assert.AreEqual(5.0f, lengths[1], 0.0001f);
		Assert.AreEqual(17.0f, lengths[2], 0.0001f);
	}


}

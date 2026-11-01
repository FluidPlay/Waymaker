using System.Collections.Generic;
using Sandbox;

namespace FluidPlay.Waymaker;

/// <summary>
/// Waymaker-owned helpers for assigning UVs and materials to generated polygon faces.
/// </summary>
internal static class WaymakerMeshUtility
{
	public static void AddTexturedQuad(PolygonMesh mesh, Material material,
		HalfEdgeMesh.VertexHandle a, HalfEdgeMesh.VertexHandle b,
		HalfEdgeMesh.VertexHandle c, HalfEdgeMesh.VertexHandle d,
		Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
	{
		if (a.Equals(b) || a.Equals(c) || a.Equals(d) || b.Equals(c) || b.Equals(d) || c.Equals(d))
			return;

		var face = mesh.AddFace(a, b, c, d);
		if (!face.IsValid)
			return;

		if (material is not null)
			mesh.SetFaceMaterial(face, material);

		mesh.SetFaceTextureCoords(face, new List<Vector2> { uvA, uvB, uvC, uvD });
	}
}

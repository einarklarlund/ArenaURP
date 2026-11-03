using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// What a face's material is picked by: its tag (and, for an untagged face, its role), or the blended ground. The
    /// builder resolves a key through the theme, or the building profile its group was drawn; two keys may resolve to
    /// the same material.
    /// </summary>
    public readonly struct SurfaceKey : IEquatable<SurfaceKey>
    {
        public readonly SurfaceTag Tag;
        /// <summary>Only for <see cref="SurfaceTag.None"/>, which takes a placeholder colour per role.</summary>
        public readonly PrimitiveRole Role;
        public readonly bool Ground;

        private SurfaceKey(SurfaceTag tag, PrimitiveRole role, bool ground)
        {
            Tag = tag;
            Role = role;
            Ground = ground;
        }

        public static SurfaceKey Of(SurfaceTag tag, PrimitiveRole role) =>
            new(tag, tag == SurfaceTag.None ? role : default, false);

        public static readonly SurfaceKey BlendedGround = new(SurfaceTag.None, default, true);

        public bool Equals(SurfaceKey other) => Tag == other.Tag && Role == other.Role && Ground == other.Ground;
        public override bool Equals(object obj) => obj is SurfaceKey other && Equals(other);
        public override int GetHashCode() => ((int)Tag << 16) ^ ((int)Role << 1) ^ (Ground ? 1 : 0);
        public override string ToString() => Ground ? "BlendedGround" : Tag == SurfaceTag.None ? $"None ({Role})" : Tag.ToString();
    }

    /// <summary>
    /// One flat-shaded mesh in level space: every triangle has vertices of its own, with its face's normal and world UVs
    /// (<see cref="PrimitiveMesher.WorldUv"/>). Each triangle names the key its material comes from.
    /// </summary>
    public sealed class PlanMesh
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> Uvs = new();
        /// <summary>Three vertex indices per triangle, in the order the faces were made.</summary>
        public readonly List<int> Triangles = new();
        /// <summary>The keys in the order the faces first asked for them (the builder's submeshes follow the order of
        /// their materials' first keys here). Holds the blended ground when a split top asked for it, even with no
        /// triangle.</summary>
        public readonly List<SurfaceKey> Keys = new();
        /// <summary>Index into <see cref="Keys"/> per triangle.</summary>
        public readonly List<int> TriangleKeys = new();
        /// <summary>
        /// Per vertex, when <see cref="Keys"/> holds the blended ground (else null; see the Arena/Level Terrain
        /// shader): the three layers of its triangle, and its weights over them with w = 1. Vertices of other triangles
        /// keep zeros.
        /// </summary>
        public List<Vector3> LayerIds;
        public List<Vector4> LayerWeights;

        public int TriangleCount => TriangleKeys.Count;

        /// <summary>The index list <paramref name="key"/>'s next triangle goes to: the key is registered on first use.</summary>
        internal int KeyIndex(SurfaceKey key)
        {
            int index = Keys.IndexOf(key);
            if (index >= 0) return index;
            Keys.Add(key);
            return Keys.Count - 1;
        }
    }

    /// <summary>One owner's group: a combined mesh with a collider, or a prop drawn by its prefab.</summary>
    public sealed class PlanGroup
    {
        public OwnerKind Kind;
        public int Id;
        public string Name;
        /// <summary>The prefabbed prop that stands in this group's place, or null for a mesh.</summary>
        public PropDescription Prefab;
        public PlanMesh Mesh;
        public bool CastShadows;
        /// <summary>The building profile drawn for a building group, or -1.</summary>
        public int Profile = -1;
        /// <summary>
        /// The collider's own mesh, when the render mesh splits ground tops: collision follows the primitives alone,
        /// whatever the theme's thresholds. Null when the collider is the render mesh.
        /// </summary>
        public List<Vector3> CollisionVertices;
        public List<int> CollisionTriangles;
    }

    /// <summary>A light fixture: its prefab without colliders, or a glowing placeholder box in its light's slot.</summary>
    public sealed class PlanFixture
    {
        public PropDescription Prop;
        public bool Prefab;
        /// <summary>The placeholder's box (one key: <see cref="SurfaceTag.PropPlaceholder"/>), or null.</summary>
        public PlanMesh Placeholder;
        public LightSlot Slot;
    }

    /// <summary>
    /// Everything the level builder decides, as plain data: the meshes, their faces' material keys and blend
    /// layers, the colliders, which props are prefabs, the fixtures, which lights are real and the decal strips. The
    /// builder only turns it into GameObjects, in this order: <see cref="Groups"/>, <see cref="Fixtures"/>,
    /// <see cref="RealLights"/>, then the decals.
    /// </summary>
    public sealed class BuildPlan
    {
        public readonly List<PlanGroup> Groups = new();
        public readonly List<PlanFixture> Fixtures = new();
        /// <summary>Indices into the description's lights that get a real point light: highest priority first, then
        /// in description order, so every machine lights the same fixtures.</summary>
        public readonly List<int> RealLights = new();
        /// <summary>The decal strips (no dip or mound edges), or null when decals are off.</summary>
        public List<DecalStrip> Decals;
        /// <summary>Per strip: the building profile a building decal takes its material from, or -1.</summary>
        public List<int> DecalProfiles;

        public static BuildPlan Create(LevelDescription description, BuildOptions options)
        {
            options ??= new BuildOptions();
            var plan = new BuildPlan();

            // Group by owner in order of first appearance; the dictionary is lookup only, never iterated. A group holds
            // indices into the description's primitives.
            var groups = new List<(OwnerKind kind, int id, List<int> primitives)>();
            var lookup = new Dictionary<(OwnerKind, int), int>();
            for (int i = 0; i < description.Primitives.Count; i++)
            {
                var p = description.Primitives[i];
                var key = (p.OwnerKind, p.OwnerId);
                if (!lookup.TryGetValue(key, out int index))
                {
                    index = groups.Count;
                    lookup.Add(key, index);
                    groups.Add((p.OwnerKind, p.OwnerId, new List<int>()));
                }
                groups[index].primitives.Add(i);
            }

            StageProfiler.Begin("Terrain blend");
            var split = TerrainBlendStage.Build(description, options.Blend);
            StageProfiler.End();

            foreach (var (kind, id, primitives) in groups)
            {
                var group = new PlanGroup { Kind = kind, Id = id, Name = GroupName(kind, id) };
                if (kind == OwnerKind.Prop && id < description.Props.Count && HasPrefab(options, description.Props[id]))
                {
                    group.Prefab = description.Props[id];
                    plan.Groups.Add(group);
                    continue;
                }
                if (kind == OwnerKind.Building) group.Profile = ArtDraws.ProfileIndex(description.Seed, id, options.ProfileCount);
                // The boundary walls are 50 tall: their shadows would fall across the whole level.
                group.CastShadows = kind != OwnerKind.Boundary;
                MeshGroup(group, primitives, description, split);
                plan.Groups.Add(group);
            }

            plan.PlanFixtures(description, options);
            plan.PlanLights(description, options.MaxRealLights);
            if (options.BuildDecals) plan.PlanDecals(description, options);
            return plan;
        }

        private static bool HasPrefab(BuildOptions options, PropDescription prop) =>
            options.Props != null && options.Props.HasPrefab(prop.Kind, prop.Variant);

        private static void MeshGroup(PlanGroup group, List<int> primitives, LevelDescription description,
            Dictionary<int, List<BlendTriangle>> split)
        {
            var mesh = group.Mesh = new PlanMesh();
            var vertices = mesh.Vertices;
            var normals = mesh.Normals;
            // Every triangle goes to one index list; its key goes alongside.
            var triangles = mesh.Triangles;
            // Each blended-ground triangle's corners' layers, in triangle order.
            var groundLayers = new List<(int triangle, BlendTriangle layers)>();
            bool replacedTops = false;

            foreach (int i in primitives)
            {
                var p = description.Primitives[i];
                var role = p.Role;
                bool isSplit = split.TryGetValue(i, out var pieces);
                PrimitiveMesher.Append(p, description, vertices, normals, tag =>
                {
                    // A height-classified top is replaced by its pieces below.
                    if (isSplit && tag == p.Surfaces.Top) return null;
                    if (!TerrainLayers.TryOf(tag, out var layer))
                    {
                        mesh.TriangleKeys.Add(mesh.KeyIndex(SurfaceKey.Of(tag, role)));
                        return triangles;
                    }
                    groundLayers.Add((mesh.TriangleKeys.Count, BlendTriangle.OneHot(default, default, default, layer, layer, layer)));
                    mesh.TriangleKeys.Add(mesh.KeyIndex(SurfaceKey.BlendedGround));
                    return triangles;
                });
                if (!isSplit) continue;
                replacedTops = true;
                mesh.KeyIndex(SurfaceKey.BlendedGround);
                var up = new Vector3(-p.Slope.x, 1f, -p.Slope.y).normalized;
                foreach (var piece in pieces)
                {
                    var t = piece;
                    var normal = Vector3.Cross(t.B - t.A, t.C - t.A);
                    if (normal.sqrMagnitude < 1e-12f) continue;
                    // Wound like the mesher's faces: the cross product points out of the face. A corner keeps its weights.
                    if (Vector3.Dot(normal, up) < 0f)
                    {
                        (t.B, t.C) = (t.C, t.B);
                        (t.WeightB, t.WeightC) = (t.WeightC, t.WeightB);
                    }
                    int start = vertices.Count;
                    vertices.Add(t.A);
                    vertices.Add(t.B);
                    vertices.Add(t.C);
                    normals.Add(up);
                    normals.Add(up);
                    normals.Add(up);
                    groundLayers.Add((mesh.TriangleKeys.Count, t));
                    mesh.TriangleKeys.Add(mesh.KeyIndex(SurfaceKey.BlendedGround));
                    triangles.Add(start);
                    triangles.Add(start + 1);
                    triangles.Add(start + 2);
                }
            }

            // Every triangle has vertices of its own with the face's normal, so each vertex's UV follows from where
            // it is and which way its face looks. The level root sits at the origin: level space is world space.
            for (int i = 0; i < vertices.Count; i++) mesh.Uvs.Add(PrimitiveMesher.WorldUv(vertices[i], normals[i]));

            if (mesh.Keys.Contains(SurfaceKey.BlendedGround))
            {
                mesh.LayerIds = new List<Vector3>(new Vector3[vertices.Count]);
                mesh.LayerWeights = new List<Vector4>(new Vector4[vertices.Count]);
                foreach (var (t, layers) in groundLayers)
                {
                    var id = new Vector3((int)layers.Layer0, (int)layers.Layer1, (int)layers.Layer2);
                    for (int k = 0; k < 3; k++)
                    {
                        int v = triangles[3 * t + k];
                        var w = k == 0 ? layers.WeightA : k == 1 ? layers.WeightB : layers.WeightC;
                        mesh.LayerIds[v] = id;
                        mesh.LayerWeights[v] = new Vector4(w.x, w.y, w.z, 1f);
                    }
                }
            }

            if (!replacedTops) return;
            // Material thresholds must not change collision tessellation between server/client themes.
            group.CollisionVertices = new List<Vector3>();
            group.CollisionTriangles = new List<int>();
            var collisionNormals = new List<Vector3>();
            foreach (int i in primitives)
                PrimitiveMesher.Append(description.Primitives[i], description, group.CollisionVertices, collisionNormals,
                    group.CollisionTriangles);
        }

        private void PlanFixtures(LevelDescription description, BuildOptions options)
        {
            var slots = new Dictionary<int, LightSlot>();
            foreach (var light in description.Lights) slots[light.Prop] = light.Slot;
            foreach (var prop in description.Props)
            {
                if (!PropKinds.IsFixture(prop.Kind)) continue;
                slots.TryGetValue(prop.Id, out var slot);
                var fixture = new PlanFixture { Prop = prop, Slot = slot };
                Fixtures.Add(fixture);
                if (HasPrefab(options, prop))
                {
                    fixture.Prefab = true;
                    continue;
                }
                var variants = options.Props?.Shapes(prop.Kind);
                var shape = variants != null && prop.Variant >= 0 && prop.Variant < variants.Length
                    ? variants[prop.Variant].Sanitized(prop.Kind)
                    : PropShape.Default(prop.Kind);
                fixture.Placeholder = FixtureBox(prop, shape);
            }
        }

        /// <summary>A fixture's placeholder: its box, all <see cref="SurfaceTag.PropPlaceholder"/>.</summary>
        private static PlanMesh FixtureBox(PropDescription prop, PropShape shape)
        {
            var box = new Primitive
            {
                Role = PrimitiveRole.Ceiling, Shape = PrimitiveShape.Box, Position = prop.Position,
                Size = new Vector3(shape.Width, shape.Height, shape.Length), YawDegrees = prop.YawDegrees,
                OwnerKind = OwnerKind.Prop, OwnerId = prop.Id, Surfaces = Surfaces.All(SurfaceTag.PropPlaceholder),
            };
            var mesh = new PlanMesh();
            int key = mesh.KeyIndex(SurfaceKey.Of(SurfaceTag.PropPlaceholder, box.Role));
            PrimitiveMesher.Append(box, null, mesh.Vertices, mesh.Normals, _ =>
            {
                mesh.TriangleKeys.Add(key);
                return mesh.Triangles;
            });
            for (int i = 0; i < mesh.Vertices.Count; i++) mesh.Uvs.Add(PrimitiveMesher.WorldUv(mesh.Vertices[i], mesh.Normals[i]));
            return mesh;
        }

        private void PlanLights(LevelDescription description, int max)
        {
            var order = new List<int>();
            for (int i = 0; i < description.Lights.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int byPriority = description.Lights[b].Priority.CompareTo(description.Lights[a].Priority);
                return byPriority != 0 ? byPriority : a.CompareTo(b);
            });
            for (int n = 0; n < Mathf.Min(max, order.Count); n++) RealLights.Add(order[n]);
        }

        private void PlanDecals(LevelDescription description, BuildOptions options)
        {
            StageProfiler.Begin("Decals");
            Decals = DecalStage.Build(description, options.Decals);
            StageProfiler.End();
            DecalProfiles = new List<int>(Decals.Count);
            foreach (var strip in Decals)
                DecalProfiles.Add(DecalTypes.IsBuilding(strip.Type)
                    ? ArtDraws.ProfileIndex(description.Seed, strip.Building, options.ProfileCount)
                    : -1);
        }

        public static string GroupName(OwnerKind kind, int id) => kind switch
        {
            OwnerKind.Plot => $"Plot {id}",
            OwnerKind.Transition => $"Transition {id}",
            OwnerKind.Building => $"Building {id}",
            OwnerKind.Prop => $"Prop {id}",
            OwnerKind.Boundary => "Boundary",
            _ => "Level",
        };
    }
}

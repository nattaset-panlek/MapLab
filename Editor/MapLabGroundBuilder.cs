using System;
using MapLab.Shapes;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace MapLab.Editor
{
    public static class MapLabGroundBuilder
    {
        #region [Variables/Fields]
        public static readonly Rect GroundArea = new Rect(-26f, -18f, 52f, 36f);
        public static readonly Vector2 PlazaCenter = new Vector2(5.2f, -0.2f);

        private const float PathWidth = 1.5f;
        private const float PlateauHeight = 1.75f;
        private const float MiddleTierHeight = 1.25f;
        private const float TopTierHeight = 1f;
        private const float VoidHeight = 2f;
        private const float VoidRim = 0.35f;
        private const float RingHeight = 1.2f;
        private const float RingWidth = 0.9f;
        private const float PillarHeight = 2.2f;
        private const float PlazaBorder = 0.22f;
        private static readonly Vector2 PillarHalfSize = new Vector2(0.42f, 0.26f);
        private static readonly Color PlazaCurb = new Color(0.7f, 0.68f, 0.77f);
        private static readonly Color PillarTop = new Color(0.9f, 0.88f, 0.95f);
        private static readonly Color PillarWall = new Color(0.76f, 0.74f, 0.84f);
        private static readonly Vector2 VoidCenter = new Vector2(15f, 10.5f);
        private static readonly Vector2 VoidRadii = new Vector2(6.2f, 3.4f);
        private static readonly Vector2 RingCenter = new Vector2(-14f, -11f);
        private static readonly Vector2 RingRadii = new Vector2(5.2f, 3.4f);
        private static readonly Vector2 PlazaRadii = new Vector2(3.9f, 3.2f);
        private static readonly Vector2 JunctionCenter = new Vector2(-0.45f, -1.1f);
        private static readonly Vector2 JunctionRadii = new Vector2(1.55f, 1.3f);

        private static readonly Vector2[] PillarPositions =
        {
            new Vector2(3.1f, 1.9f), new Vector2(7.3f, 1.9f), new Vector2(3.1f, -2.3f), new Vector2(7.3f, -2.3f)
        };

        private static readonly Vector2[] RoadWest =
        {
            new Vector2(-27f, -2.2f), new Vector2(-19f, -0.6f), new Vector2(-12.5f, -2.6f),
            new Vector2(-6.5f, -1.4f), new Vector2(-0.5f, -1.1f), new Vector2(2.4f, -0.4f)
        };

        private static readonly Vector2[] RoadEast =
        {
            new Vector2(8f, 0f), new Vector2(11.5f, 1.6f), new Vector2(15.5f, 2.6f),
            new Vector2(20f, 1.6f), new Vector2(27f, 2.4f)
        };

        private static readonly Vector2[] BranchNorth =
        {
            new Vector2(-0.6f, -0.6f), new Vector2(-1.3f, 2.4f), new Vector2(0.4f, 5.6f),
            new Vector2(-0.9f, 9.4f), new Vector2(0.6f, 13f), new Vector2(0f, 19f)
        };

        private static readonly Vector2[] BranchSouth =
        {
            new Vector2(-0.3f, -1.6f), new Vector2(0.8f, -4.6f), new Vector2(-0.6f, -8f),
            new Vector2(0.7f, -12f), new Vector2(0.2f, -19f)
        };

        private static readonly Vector2[] PlateauOutline =
        {
            new Vector2(-21f, 2.4f), new Vector2(-17.5f, 3.3f), new Vector2(-14f, 2.7f),
            new Vector2(-10.8f, 3.6f), new Vector2(-8.2f, 3.2f), new Vector2(-6.6f, 4.4f),
            new Vector2(-6.2f, 7.2f), new Vector2(-7.8f, 10.6f), new Vector2(-12.5f, 12f),
            new Vector2(-18f, 11.4f), new Vector2(-22.5f, 8.6f), new Vector2(-23f, 5f)
        };

        private static readonly Color[] MossTints =
        {
            new Color(0.53f, 0.78f, 0.4f, 0.4f),
            new Color(0.66f, 0.87f, 0.5f, 0.5f),
            new Color(0.88f, 0.8f, 0.6f, 0.3f)
        };
        #endregion

        #region [Public Methods]
        public static string Build(Transform parent, Material litMaterial)
        {
            Transform ground = MapLabSceneBuilder.Child(parent, "Ground");
            BuildBase(ground, litMaterial);
            BuildMoss(ground, litMaterial);

            Transform roads = MapLabSceneBuilder.Set(parent, "Roads", 2);
            Transform plaza = MapLabSceneBuilder.Set(parent, "Plaza", 1);
            ExtrudedShapeMenu.CreatePath("Road West", roads, Vector3.zero, RoadWest, PathWidth, MapLabSceneBuilder.PathOrder);
            ExtrudedShapeMenu.CreatePath("Road East", roads, Vector3.zero, RoadEast, PathWidth, MapLabSceneBuilder.PathOrder);
            ExtrudedShapeMenu.CreatePath("Branch North", roads, Vector3.zero, BranchNorth, PathWidth, MapLabSceneBuilder.PathOrder);
            ExtrudedShapeMenu.CreatePath("Branch South", roads, Vector3.zero, BranchSouth, PathWidth, MapLabSceneBuilder.PathOrder);
            CreateFlat(plaza, "Junction", Blob(JunctionCenter, JunctionRadii, 8, 3), MapLabArtPainter.Dirt, 2f, MapLabSceneBuilder.JunctionOrder, 0f);
            CreateFlat(plaza, "Plaza", Blob(PlazaCenter, PlazaRadii, 10, 5), MapLabArtPainter.Cobble, 4f, MapLabSceneBuilder.PlazaOrder, PlazaBorder);
            return "ground: flat grass base; sets Roads (4 paths) and Plaza (plaza, junction)";
        }

        public static string BuildShapes(Transform parent)
        {
            Transform cliffs = MapLabSceneBuilder.Set(parent, "Cliffs", 0);
            Transform ruins = MapLabSceneBuilder.Set(parent, "Ruins", 3);
            Transform plaza = MapLabSceneBuilder.Set(parent, "Plaza", 1);
            int order = ExtrudedShapeMenu.CharacterOrder;
            ExtrudedShape plateau = ExtrudedShapeMenu.Create(ExtrudedShapeType.Plateau, "Plateau", cliffs, Vector3.zero, PlateauOutline, PlateauHeight, 0.4f, order);
            ExtrudedShape middle = ExtrudedShapeMenu.AddTier(plateau, 0.64f, MiddleTierHeight);
            ExtrudedShapeMenu.AddTier(middle, 0.55f, TopTierHeight);
            ExtrudedShapeMenu.Create(ExtrudedShapeType.Void, "Void Rock", cliffs, Vector3.zero, Blob(VoidCenter, VoidRadii, 9, 11), VoidHeight, VoidRim, order);
            ExtrudedShapeMenu.Create(ExtrudedShapeType.Ring, "Ring Wall", ruins, Vector3.zero, Blob(RingCenter, RingRadii, 10, 17), RingHeight, RingWidth, order);

            var corners = new[] { true, true, true, true };
            foreach (var position in PillarPositions)
            {
                var box = new[]
                {
                    new Vector2(-PillarHalfSize.x, -PillarHalfSize.y), new Vector2(PillarHalfSize.x, -PillarHalfSize.y),
                    new Vector2(PillarHalfSize.x, PillarHalfSize.y), new Vector2(-PillarHalfSize.x, PillarHalfSize.y)
                };
                ExtrudedShape pillar = ExtrudedShapeMenu.Create(ExtrudedShapeType.Plateau, "Pillar", plaza, position, box, PillarHeight, 0.4f, order, corners);
                Tune(pillar, serialized =>
                {
                    serialized.FindProperty("_topTexture").objectReferenceValue = ExtrudedShapeMenu.Art(MapLabArtPainter.TopStone);
                    serialized.FindProperty("_topColor").colorValue = PillarTop;
                    serialized.FindProperty("_wallColor").colorValue = PillarWall;
                    serialized.FindProperty("_wallTextureSize").floatValue = 1f;
                    serialized.FindProperty("_borderWidth").floatValue = 0f;
                });
            }
            return "shapes: set Cliffs (plateau with 2 tiers, void rock), set Ruins (ring wall), 4 box pillars in set Plaza";
        }
        #endregion

        #region [Private Methods]
        private static Sprite Art(string name)
        {
            return MapLabArtPainter.Get(MapLabSceneBuilder.GeneratedFolder, name);
        }

        private static void CreateFlat(Transform parent, string name, Vector2[] points, string texture, float textureSize, int order, float border)
        {
            ExtrudedShape shape = ExtrudedShapeMenu.Create(ExtrudedShapeType.Plateau, name, parent, Vector3.zero, points, 0f, 0.4f, order);
            Tune(shape, serialized =>
            {
                serialized.FindProperty("_ySort").boolValue = false;
                serialized.FindProperty("_generateCollision").boolValue = false;
                serialized.FindProperty("_topTexture").objectReferenceValue = ExtrudedShapeMenu.Art(texture);
                serialized.FindProperty("_topTextureSize").floatValue = textureSize;
                serialized.FindProperty("_topColor").colorValue = Color.white;
                serialized.FindProperty("_borderWidth").floatValue = border;
                serialized.FindProperty("_borderColor").colorValue = PlazaCurb;
            });
        }

        private static void Tune(PointShape shape, Action<SerializedObject> edit)
        {
            var serialized = new SerializedObject(shape);
            edit(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            shape.Rebuild();
        }

        private static void BuildBase(Transform parent, Material litMaterial)
        {
            var go = new GameObject("Grass Base");
            go.transform.SetParent(parent, false);
            go.transform.position = GroundArea.center;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Art(MapLabArtPainter.Grass);
            renderer.sharedMaterial = litMaterial;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.size = GroundArea.size + new Vector2(4f, 4f);
            renderer.sortingOrder = MapLabSceneBuilder.GroundOrder;
        }

        private static void BuildMoss(Transform parent, Material litMaterial)
        {
            Transform moss = MapLabSceneBuilder.Child(parent, "Ground Patches");
            Sprite sprite = Art(MapLabArtPainter.Moss);
            var rng = new Random(7);
            for (int i = 0; i < 34; i++)
            {
                var go = new GameObject($"Patch {i:00}");
                go.transform.SetParent(moss, false);
                go.transform.position = new Vector3(
                    GroundArea.xMin + (float)rng.NextDouble() * GroundArea.width,
                    GroundArea.yMin + (float)rng.NextDouble() * GroundArea.height, 0f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 360f);
                float scale = 1.2f + (float)rng.NextDouble() * 1.4f;
                go.transform.localScale = new Vector3(scale * (0.8f + (float)rng.NextDouble() * 0.5f), scale, 1f);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sharedMaterial = litMaterial;
                renderer.color = MossTints[rng.Next(MossTints.Length)];
                renderer.sortingOrder = MapLabSceneBuilder.MossOrder;
            }
        }

        private static Vector2[] Blob(Vector2 center, Vector2 radii, int count, int seed)
        {
            var rng = new Random(seed);
            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                float jitter = 1f + ((float)rng.NextDouble() - 0.5f) * 0.16f;
                points[i] = center + new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y) * jitter;
            }
            return points;
        }
        #endregion
    }
}

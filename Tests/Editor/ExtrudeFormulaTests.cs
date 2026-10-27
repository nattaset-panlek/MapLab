using System.Collections.Generic;
using MapLab.Shapes;
using NUnit.Framework;
using UnityEngine;

namespace MapLab.Tests
{
    public class ExtrudeFormulaTests
    {
        #region [Variables/Fields]
        private static readonly Vector2[] Square =
        {
            new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 2f), new Vector2(0f, 2f)
        };

        private static readonly Vector2[] NotchedBlock =
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(2f, 1f),
            new Vector2(2f, 0f), new Vector2(3f, 0f), new Vector2(3f, 2f), new Vector2(0f, 2f)
        };

        private static readonly Vector2[] ConcaveL =
        {
            new Vector2(3f, -1f), new Vector2(-1f, -1f), new Vector2(-1f, 3f),
            new Vector2(-3f, 3f), new Vector2(-3f, -3f), new Vector2(3f, -3f)
        };
        #endregion

        #region [Public Methods]
        [Test]
        public void WallRuns_SquareHasOneWallAlongTheBottom()
        {
            List<WallRun> runs = ExtrudeFormula.WallRuns(Square);

            Assert.AreEqual(1, runs.Count);
            Assert.AreEqual(new Vector2(0f, 0f), runs[0].Points[0]);
            Assert.AreEqual(new Vector2(2f, 0f), runs[0].Points[runs[0].Points.Length - 1]);
            Assert.AreEqual(2f, runs[0].Distances[runs[0].Distances.Length - 1] - runs[0].Distances[0], 0.0001f);
        }

        [Test]
        public void WallRuns_SouthFacingNotchGetsItsOwnWallDrawnFirst()
        {
            List<WallRun> runs = ExtrudeFormula.WallRuns(NotchedBlock);

            Assert.AreEqual(3, runs.Count);
            Assert.AreEqual(1f, runs[0].AverageY, 0.0001f, "the notch ceiling is furthest back, so it is drawn first");
            Assert.AreEqual(0f, runs[1].AverageY, 0.0001f);
            Assert.AreEqual(0f, runs[2].AverageY, 0.0001f);
        }

        [Test]
        public void WallRuns_EndWhereTheCurveTurnsVertical()
        {
            var ellipse = new List<Vector2>();
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2f / 64;
                ellipse.Add(new Vector2(Mathf.Cos(angle) * 4f, Mathf.Sin(angle) * 2f));
            }

            List<WallRun> runs = ExtrudeFormula.WallRuns(ellipse);

            Assert.AreEqual(1, runs.Count);
            Vector2 first = runs[0].Points[0];
            Vector2 last = runs[0].Points[runs[0].Points.Length - 1];
            Assert.AreEqual(-4f, first.x, 0.01f, "wall starts at the leftmost point");
            Assert.AreEqual(4f, last.x, 0.01f, "wall ends at the rightmost point");
            foreach (var point in runs[0].Points) Assert.LessOrEqual(point.y, 0.0001f, "only the lower half faces the camera");
        }

        [Test]
        public void Offset_InsetsASquareByTheRimWidth()
        {
            var square = new List<Vector2> { new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 4f), new Vector2(0f, 4f) };
            List<Vector2> inner = ExtrudeFormula.Offset(square, -1f);

            Assert.AreEqual(new Vector2(1f, 1f).x, inner[0].x, 0.0001f);
            Assert.AreEqual(new Vector2(1f, 1f).y, inner[0].y, 0.0001f);
            Assert.AreEqual(3f, inner[2].x, 0.0001f);
            Assert.AreEqual(3f, inner[2].y, 0.0001f);
        }

        [Test]
        public void Triangulate_CoversAConcaveOutlineExactly()
        {
            List<int> triangles = ExtrudeFormula.Triangulate(ConcaveL);

            Assert.AreEqual((ConcaveL.Length - 2) * 3, triangles.Count);
            Assert.AreEqual(ExtrudeFormula.SignedArea(ConcaveL), ExtrudeFormula.TriangleArea(ConcaveL, triangles), 0.0001f);
        }

        [Test]
        public void SampleClosed_PassesThroughEveryControlPoint()
        {
            List<Vector2> samples = ExtrudeFormula.SampleClosed(Square, 6);

            Assert.AreEqual(Square.Length * 6, samples.Count);
            for (int i = 0; i < Square.Length; i++) Assert.AreEqual(Square[i], samples[i * 6]);
        }

        [Test]
        public void Sample_SharpCornersGiveStraightEdges()
        {
            var sharp = new[] { true, true, true, true };
            List<Vector2> samples = ExtrudeFormula.Sample(Square, sharp, 8, true);
            Assert.AreEqual(Square, samples.ToArray());
        }

        [Test]
        public void Sample_OpenPathEndsOnItsLastPoint()
        {
            var line = new[] { new Vector2(0f, 0f), new Vector2(2f, 1f), new Vector2(4f, 0f) };
            List<Vector2> samples = ExtrudeFormula.Sample(line, null, 5, false);
            Assert.AreEqual(2 * 5 + 1, samples.Count);
            Assert.AreEqual(line[0], samples[0]);
            Assert.AreEqual(line[2], samples[samples.Count - 1]);
        }

        [Test]
        public void ClipBand_SlicesKeepTheWholeArea()
        {
            var triangle = new[] { new Vector2(0f, 0f), new Vector2(4f, 0.5f), new Vector2(1f, 3f) };
            float total = 0f;
            for (float y = -1f; y < 4f; y += 0.5f)
            {
                List<Vector2> piece = ExtrudeFormula.ClipBand(triangle, y, y + 0.5f);
                if (piece.Count >= 3) total += Mathf.Abs(ExtrudeFormula.SignedArea(piece));
            }
            Assert.AreEqual(Mathf.Abs(ExtrudeFormula.SignedArea(triangle)), total, 0.0001f);
        }

        [Test]
        public void WallShade_SouthWestFacingWallIsBrightest()
        {
            float lit = ExtrudeFormula.WallShade(ExtrudeFormula.WallLight.normalized);
            float side = ExtrudeFormula.WallShade(Vector2.right);
            Assert.AreEqual(1f, lit, 0.0001f);
            Assert.Less(side, lit);
        }

        [Test]
        public void CastingEdges_OnlyTheSideAwayFromTheLight()
        {
            bool[] casts = ExtrudeFormula.CastingEdges(Square, new Vector2(1.5f, 0f));

            Assert.AreEqual(new[] { false, true, false, false }, casts, "only the east edge casts a card shadow");
            Assert.AreEqual(new[] { true, true, false, false }, ExtrudeFormula.CastingEdges(Square, new Vector2(1f, -0.25f)), "a light from the back left also lets the front edge cast");
        }

        [Test]
        public void ClampInside_StopsTheShadowAtTheEdgeItStandsOn()
        {
            var tier = new[] { new Vector2(0.5f, 0.5f), new Vector2(1.5f, 0.5f), new Vector2(1.5f, 1.5f), new Vector2(0.5f, 1.5f) };
            Vector2[] reach = ExtrudeFormula.Translate(tier, new Vector2(3f, 0f));

            ExtrudeFormula.ClampInside(tier, reach, Square);

            foreach (var point in reach)
            {
                Assert.LessOrEqual(point.x, 2f + 0.0001f);
                Assert.GreaterOrEqual(point.x, -0.0001f);
                Assert.LessOrEqual(point.y, 2f + 0.0001f);
                Assert.GreaterOrEqual(point.y, -0.0001f);
            }
        }
        #endregion
    }
}

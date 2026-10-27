using MapLab.Samples.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace MapLab.Tests
{
    public class NavMesh2DFactoryTests
    {
        #region [Variables/Fields]
        private static readonly Rect Area = new Rect(-6f, -6f, 12f, 12f);
        private static readonly Rect WallRect = new Rect(-0.5f, -3f, 1f, 6f);

        private static readonly Vector2[] Wall =
        {
            new Vector2(WallRect.xMin, WallRect.yMin), new Vector2(WallRect.xMax, WallRect.yMin),
            new Vector2(WallRect.xMax, WallRect.yMax), new Vector2(WallRect.xMin, WallRect.yMax)
        };

        private static readonly Vector2[] ConcaveL =
        {
            new Vector2(3f, -1f), new Vector2(-1f, -1f), new Vector2(-1f, 3f),
            new Vector2(-3f, 3f), new Vector2(-3f, -3f), new Vector2(3f, -3f)
        };

        private NavMeshDataInstance _instance;
        #endregion

        #region [Unity Methods]
        [TearDown]
        public void TearDown()
        {
            if (_instance.valid) NavMesh.RemoveNavMeshData(_instance);
            _instance = default;
        }
        #endregion

        #region [Public Methods]
        [Test]
        public void Surface_LiesOnTheXYPlane()
        {
            Build(Wall);
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(3f, 2f, 0f), out NavMeshHit hit, 0.5f, NavMesh.AllAreas));
            Assert.AreEqual(0f, hit.position.z, 0.3f);
            Assert.AreEqual(3f, hit.position.x, 0.3f);
            Assert.AreEqual(2f, hit.position.y, 0.3f);
        }

        [Test]
        public void Obstacle_IsNotWalkable()
        {
            Build(Wall);
            Assert.IsFalse(NavMesh.SamplePosition(new Vector3(0f, 0f, 0f), out _, 0.3f, NavMesh.AllAreas));
        }

        [Test]
        public void Path_GoesAroundTheWall()
        {
            Build(Wall);
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(-3f, 0f, 0f), out NavMeshHit start, 1f, NavMesh.AllAreas));
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(3f, 0f, 0f), out NavMeshHit end, 1f, NavMesh.AllAreas));

            var path = new NavMeshPath();
            Assert.IsTrue(NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path));
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status);
            Assert.Greater(path.corners.Length, 2);

            float furthest = 0f;
            foreach (Vector3 corner in path.corners)
            {
                Assert.IsFalse(WallRect.Contains(corner), $"corner {corner} is inside the wall");
                furthest = Mathf.Max(furthest, Mathf.Abs(corner.y));
            }
            Assert.GreaterOrEqual(furthest, 3f);
        }

        [Test]
        public void ConcaveObstacle_KeepsItsNotchWalkable()
        {
            Build(ConcaveL);
            Assert.IsFalse(NavMesh.SamplePosition(new Vector3(-2f, -2f, 0f), out _, 0.3f, NavMesh.AllAreas), "corner of the L");
            Assert.IsFalse(NavMesh.SamplePosition(new Vector3(2f, -2f, 0f), out _, 0.3f, NavMesh.AllAreas), "bottom arm of the L");
            Assert.IsFalse(NavMesh.SamplePosition(new Vector3(-2f, 2f, 0f), out _, 0.3f, NavMesh.AllAreas), "left arm of the L");
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(0.8f, 0.8f, 0f), out _, 0.3f, NavMesh.AllAreas), "inside the notch");
        }
        #endregion

        #region [Private Methods]
        private void Build(Vector2[] obstacle)
        {
            NavMeshData data = NavMesh2DFactory.Build(Area, new[] { obstacle }, 0.3f);
            Assert.IsNotNull(data, "NavMesh build returned null");
            _instance = NavMesh.AddNavMeshData(data);
        }
        #endregion
    }
}

using System.Collections.Generic;
using MapLab.Samples.Navigation;
using NUnit.Framework;
using UnityEngine;

namespace MapLab.Tests
{
    public class NavigationManagerTests
    {
        #region [Unity Methods]
        [SetUp]
        public void SetUp()
        {
            Assume.That(NavigationManager.Instance, Is.Null);
            NavigationManager.GridPathfinder = null;
        }

        [TearDown]
        public void TearDown()
        {
            NavigationManager.GridPathfinder = null;
        }
        #endregion

        #region [Public Methods]
        [Test]
        public void WithoutNavMeshOrGrid_NoPathIsAvailable()
        {
            Assert.IsFalse(NavigationManager.IsAvailable);
            Assert.IsNull(NavigationManager.FindPath(Vector2.zero, Vector2.one));
        }

        [Test]
        public void WithoutNavMesh_UsesTheGridPathfinder()
        {
            var expected = new List<Vector2> { new Vector2(1f, 0f), new Vector2(2f, 3f) };
            Vector2 askedFrom = default, askedTo = default;
            NavigationManager.GridPathfinder = (from, to) =>
            {
                askedFrom = from;
                askedTo = to;
                return expected;
            };

            Assert.IsTrue(NavigationManager.IsAvailable);
            Assert.AreSame(expected, NavigationManager.FindPath(new Vector2(-1f, 0f), new Vector2(2f, 3f)));
            Assert.AreEqual(new Vector2(-1f, 0f), askedFrom);
            Assert.AreEqual(new Vector2(2f, 3f), askedTo);
        }
        #endregion
    }
}

using MapLab.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MapLab.Tests
{
    public class MapLabWindowStateTests
    {
        #region [Variables/Fields]
        private GameObject _root;
        #endregion

        #region [Unity Methods]
        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            Undo.ClearAll();
        }
        #endregion

        #region [Public Methods]
        [Test]
        public void MegaState_FollowsAMapThatUndoBroughtBack()
        {
            _root = new GameObject("Undo Map");
            var map = _root.AddComponent<MapLabMap>();
            int id = map.GetInstanceID();
            var state = new MapLabMegaState();
            state.Refresh(map);

            Undo.DestroyObjectImmediate(_root);
            Undo.PerformUndo();
            var restored = EditorUtility.InstanceIDToObject(id) as MapLabMap;
            Assert.IsNotNull(restored, "undo brings the map back");
            _root = restored.gameObject;
            Assert.IsTrue(state.Map == null, "the old reference is dead");

            state.Refresh(restored);

            Assert.IsTrue(ReferenceEquals(state.Map, restored), "the state now holds the live map, even though both share one instance id");
            Assert.DoesNotThrow(() => new SerializedObject(state.Map).FindProperty("_megaChunkPixels"));
            Assert.DoesNotThrow(() => MapLabBakeView.Settings(null, new[] { "_megaChunkPixels" }));
        }
        #endregion
    }
}

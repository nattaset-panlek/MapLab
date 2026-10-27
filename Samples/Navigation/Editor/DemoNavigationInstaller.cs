using MapLab.Editor;
using UnityEditor;
using UnityEngine;

namespace MapLab.Samples.Navigation.Editor
{
    [InitializeOnLoad]
    public static class DemoNavigationInstaller
    {
        #region [Variables/Fields]
        private const string ChildName = "Navigation";
        private const float AgentRadius = 0.3f;
        #endregion

        #region [Unity Methods]
        static DemoNavigationInstaller()
        {
            MapLabSceneBuilder.OnEnvironmentBuilt -= Install;
            MapLabSceneBuilder.OnEnvironmentBuilt += Install;
        }
        #endregion

        #region [Private Methods]
        private static void Install(Transform root, TextAsset walkableData)
        {
            var go = new GameObject(ChildName);
            go.transform.SetParent(root, false);
            var navigation = go.AddComponent<NavigationManager>();
            var serialized = new SerializedObject(navigation);
            serialized.FindProperty("_mode").enumValueIndex = (int)NavigationMode.Auto;
            serialized.FindProperty("_walkableData").objectReferenceValue = walkableData;
            serialized.FindProperty("_buildOnStart").boolValue = true;
            serialized.FindProperty("_agentRadius").floatValue = AgentRadius;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        #endregion
    }
}

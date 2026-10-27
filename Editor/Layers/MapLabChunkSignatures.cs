using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapLab.Editor
{
    public static class MapLabChunkSignatures
    {
        #region [Variables/Fields]
        public const string FileName = "Chunks.json";
        private const int Version = 1;
        private static readonly string[] TextureProperties = { "_MainTex" };

        [Serializable]
        private class Record
        {
            public int x;
            public int y;
            public string hash;
            public bool empty;
        }

        [Serializable]
        private class Store
        {
            public int version = Version;
            public string settings;
            public List<Record> chunks = new List<Record>();
        }

        public readonly struct Entry
        {
            public readonly Hash128 Hash;
            public readonly bool Empty;

            public Entry(Hash128 hash, bool empty)
            {
                Hash = hash;
                Empty = empty;
            }
        }
        #endregion

        #region [Public Methods]
        public static string Settings(float chunkSize, int pixelsPerUnit)
        {
            return $"v{Version}|{chunkSize.ToString("0.####", CultureInfo.InvariantCulture)}|{pixelsPerUnit}";
        }

        public static Dictionary<Vector2Int, Hash128> Compute(IReadOnlyList<Renderer> renderers, Dictionary<Vector2Int, List<int>> coverage, string settings)
        {
            var stamps = new string[renderers.Count];
            for (int i = 0; i < renderers.Count; i++) stamps[i] = Stamp(renderers[i]);
            var signatures = new Dictionary<Vector2Int, Hash128>(coverage.Count);
            foreach (var pair in coverage)
            {
                IEnumerable<string> parts = pair.Value.Select(index => stamps[index]).OrderBy(stamp => stamp, StringComparer.Ordinal);
                signatures[pair.Key] = Hash128.Compute($"{settings}#{pair.Key.x},{pair.Key.y}#{string.Join(";", parts)}");
            }
            return signatures;
        }

        public static string Stamp(Renderer renderer)
        {
            var text = new StringBuilder();
            Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
            for (int i = 0; i < 16; i++) text.Append(Number(matrix[i])).Append(',');
            text.Append('|').Append(renderer.GetType().Name).Append('|').Append(MapLabLayerClaims.SortKey(renderer)).Append('|').Append(renderer.sortingOrder).Append('|');
            foreach (var material in renderer.sharedMaterials) text.Append(Asset(material)).Append(';');
            if (renderer is SpriteRenderer sprite)
            {
                text.Append(Asset(sprite.sprite)).Append('|').Append(sprite.color).Append('|').Append(sprite.flipX).Append(sprite.flipY).Append('|').Append(sprite.drawMode).Append(sprite.size);
                if (sprite.sprite != null) text.Append('|').Append(sprite.sprite.rect).Append(sprite.sprite.pivot).Append(sprite.sprite.pixelsPerUnit);
            }
            else if (renderer.TryGetComponent(out MeshFilter filter))
            {
                text.Append(MeshHash(filter.sharedMesh));
            }
            if (renderer.HasPropertyBlock())
            {
                var block = new MaterialPropertyBlock();
                int count = Mathf.Max(1, renderer.sharedMaterials.Length);
                for (int submesh = -1; submesh < count; submesh++)
                {
                    if (submesh < 0) renderer.GetPropertyBlock(block);
                    else renderer.GetPropertyBlock(block, submesh);
                    foreach (var property in TextureProperties) text.Append('|').Append(Asset(block.GetTexture(property)));
                }
            }
            return Hash128.Compute(text.ToString()).ToString();
        }

        public static Dictionary<Vector2Int, Entry> Load(string folder, string settings)
        {
            string path = $"{folder}/{FileName}";
            if (!File.Exists(path)) return null;
            Store store = JsonUtility.FromJson<Store>(File.ReadAllText(path));
            if (store == null || store.version != Version || store.settings != settings) return null;
            return store.chunks.ToDictionary(record => new Vector2Int(record.x, record.y), record => new Entry(Hash128.Parse(record.hash), record.empty));
        }

        public static void Save(string folder, string settings, Dictionary<Vector2Int, Entry> chunks)
        {
            var store = new Store { settings = settings };
            foreach (var pair in chunks.OrderBy(pair => pair.Key.y).ThenBy(pair => pair.Key.x))
            {
                store.chunks.Add(new Record { x = pair.Key.x, y = pair.Key.y, hash = pair.Value.Hash.ToString(), empty = pair.Value.Empty });
            }
            Directory.CreateDirectory(folder);
            File.WriteAllText($"{folder}/{FileName}", JsonUtility.ToJson(store));
        }

        public static void Delete(string folder)
        {
            string path = $"{folder}/{FileName}";
            if (File.Exists(path)) File.Delete(path);
        }
        #endregion

        #region [Private Methods]
        private static string Asset(Object asset)
        {
            if (asset == null) return "null";
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return $"{asset.GetType().Name}:{asset.name}";
            return $"{path}:{asset.name}:{AssetDatabase.GetAssetDependencyHash(path)}";
        }

        private static string MeshHash(Mesh mesh)
        {
            if (mesh == null) return "no mesh";
            if (!mesh.isReadable) return Asset(mesh);
            var text = new StringBuilder();
            text.Append(Hash128.Compute(mesh.vertices)).Append(Hash128.Compute(mesh.colors)).Append(Hash128.Compute(mesh.uv));
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) text.Append(Hash128.Compute(mesh.GetTriangles(submesh)));
            if (mesh.uv2 != null && mesh.uv2.Length > 0) text.Append(Hash128.Compute(mesh.uv2));
            return text.ToString();
        }

        private static string Number(float value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }
        #endregion
    }
}

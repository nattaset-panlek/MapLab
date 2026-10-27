using UnityEngine;

namespace MapLab.Editor
{
    public class MapLabViewportCamera
    {
        #region [Variables/Fields]
        private Vector2 _center;
        private float _scale = 4f;
        private Rect _screen = new Rect(0f, 0f, 100f, 100f);
        #endregion

        #region [Properties]
        public Vector2 Center
        {
            get => _center;
            set => _center = value;
        }

        public float Scale
        {
            get => _scale;
            set => _scale = Mathf.Max(0.0001f, value);
        }

        public Rect Screen
        {
            get => _screen;
            set => _screen = value;
        }

        public Rect Visible
        {
            get
            {
                Vector2 size = _screen.size / _scale;
                return new Rect(_center - size * 0.5f, size);
            }
        }
        #endregion

        #region [Public Methods]
        public Vector2 WorldToScreen(Vector2 world)
        {
            return new Vector2(_screen.center.x + (world.x - _center.x) * _scale, _screen.center.y - (world.y - _center.y) * _scale);
        }

        public Vector2 ScreenToWorld(Vector2 screen)
        {
            return new Vector2(_center.x + (screen.x - _screen.center.x) / _scale, _center.y - (screen.y - _screen.center.y) / _scale);
        }

        public Rect WorldToScreen(Rect world)
        {
            Vector2 min = WorldToScreen(new Vector2(world.xMin, world.yMax));
            return new Rect(min.x, min.y, world.width * _scale, world.height * _scale);
        }

        public void ZoomAt(Vector2 screen, float factor, float minScale, float maxScale)
        {
            Vector2 anchor = ScreenToWorld(screen);
            Scale = Mathf.Clamp(_scale * factor, minScale, maxScale);
            Vector2 after = ScreenToWorld(screen);
            _center += anchor - after;
        }

        public void Pan(Vector2 screenDelta)
        {
            _center += new Vector2(-screenDelta.x, screenDelta.y) / _scale;
        }

        public void Frame(Rect world, float padding, float minScale, float maxScale)
        {
            if (world.width <= 0f || world.height <= 0f) return;
            float width = Mathf.Max(1f, _screen.width - padding * 2f);
            float height = Mathf.Max(1f, _screen.height - padding * 2f);
            Scale = Mathf.Clamp(Mathf.Min(width / world.width, height / world.height), minScale, maxScale);
            _center = world.center;
        }
        #endregion
    }
}

using System.Collections.Generic;
using MapLab.Elevation;
using UnityEngine;
#if MAPLAB_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MapLab.Samples.Elevation
{
    [RequireComponent(typeof(ElevationBody))]
    public class ElevationDemoController : MonoBehaviour
    {
        #region [Variables/Fields]
        private const float Arrive = 0.05f;
        private const float Stuck = 0.000001f;

        [SerializeField] private float _speed = 3f;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _marker;

        private readonly List<Vector2> _path = new List<Vector2>();
        private ElevationBody _body;
        private int _next;
        private bool _replanned;
        #endregion

        #region [Properties]
        public IReadOnlyList<Vector2> Path => _path;
        public bool Walking => _next < _path.Count;
        private ElevationBody Body => _body != null ? _body : _body = GetComponent<ElevationBody>();
        #endregion

        #region [Unity Methods]
        private void Update()
        {
            Vector2 input = ReadMove();
            if (input.sqrMagnitude > 0f)
            {
                Stop();
                Body.Move(input.normalized * (_speed * Time.deltaTime));
                return;
            }
            if (_camera != null && ReadClick(out Vector2 screen) && Body.Map != null && Body.Map.ScreenToGround(_camera, screen, out Vector2 ground, out _)) WalkTo(ground);
            Step(Time.deltaTime);
        }
        #endregion

        #region [Public Methods]
        public bool WalkTo(Vector2 ground)
        {
            Stop();
            if (!ElevationPathFormula.FindPath(Body.Map, Body.Ground, ground, _path)) return false;
            _next = 1;
            _replanned = false;
            if (_marker != null)
            {
                _marker.position = new Vector3(ground.x, ground.y + Body.Map.HeightAt(ground), _marker.position.z);
                _marker.gameObject.SetActive(true);
            }
            return true;
        }

        public void Step(float deltaTime)
        {
            float budget = _speed * deltaTime;
            while (budget > 0f && _next < _path.Count)
            {
                Vector2 offset = _path[_next] - Body.Ground;
                float distance = offset.magnitude;
                if (distance <= Arrive)
                {
                    _next++;
                    continue;
                }
                float step = Mathf.Min(budget, distance);
                Vector2 moved = Body.Move(offset / distance * step);
                budget -= step;
                if (moved.sqrMagnitude >= Stuck) continue;
                if (_replanned || !Replan())
                {
                    Stop();
                    break;
                }
            }
            if (_marker != null && !Walking) _marker.gameObject.SetActive(false);
        }

        public void Stop()
        {
            _path.Clear();
            _next = 0;
        }
        #endregion

        #region [Private Methods]
        private bool Replan()
        {
            Vector2 goal = _path[_path.Count - 1];
            var path = new List<Vector2>();
            if (!ElevationPathFormula.FindPath(Body.Map, Body.Ground, goal, path)) return false;
            _path.Clear();
            _path.AddRange(path);
            _next = 1;
            _replanned = true;
            return true;
        }

        private static Vector2 ReadMove()
        {
#if MAPLAB_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return Vector2.zero;
            float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        private static bool ReadClick(out Vector2 screen)
        {
#if MAPLAB_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            screen = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
            screen = Input.mousePosition;
            return Input.GetMouseButtonDown(0);
#endif
        }
        #endregion
    }
}

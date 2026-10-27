using System;
using System.Collections.Generic;
using MapLab.Shapes;
using UnityEngine;

namespace MapLab.Elevation
{
    public sealed class ElevationMap
    {
        #region [Variables/Fields]
        public const int Outside = -1;
        public const int Blocked = WalkableElevationJson.Blocked;

        private const float StepEpsilon = 0.0001f;
        private const float WalkSample = 0.25f;
        private const float SameHeight = 0.0001f;
        private const float MaxEnd = 0.5f;
        private const float EndReach = 1.5f;
        private static readonly Dictionary<int, (int Hash, ElevationMap Map)> Cache = new Dictionary<int, (int Hash, ElevationMap Map)>();

        private readonly WalkableElevationJson _data;
        private readonly byte[] _cells;
        private readonly Dictionary<int, ElevationLevel> _levels = new Dictionary<int, ElevationLevel>();
        private readonly Dictionary<int, ElevationRamp> _ramps = new Dictionary<int, ElevationRamp>();
        private readonly Dictionary<int, RampCorners> _corners = new Dictionary<int, RampCorners>();
        private readonly Dictionary<int, float> _ends = new Dictionary<int, float>();
        #endregion

        #region [Properties]
        public int Version => _data.version;
        public int Columns => _data.columns;
        public int Rows => _data.rows;
        public float CellSize => _data.cellSize;
        public float StepHeight => _data.stepHeight;
        public Rect Bounds => new Rect(_data.bounds.x, _data.bounds.y, _data.bounds.width, _data.bounds.height);
        public IEnumerable<ElevationLevel> Levels => _levels.Values;
        public IEnumerable<ElevationRamp> Ramps => _ramps.Values;
        #endregion

        #region [Public Methods]
        public ElevationMap(WalkableElevationJson data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.version != WalkableElevationJson.CurrentVersion) throw new InvalidOperationException($"Walkable_Elevation version {data.version} is not supported (expected {WalkableElevationJson.CurrentVersion}).");
            _data = data;
            _cells = Convert.FromBase64String(data.cellLevel ?? string.Empty);
            if (_cells.Length != data.columns * data.rows) throw new InvalidOperationException($"Walkable_Elevation has {_cells.Length} cells, expected {data.columns * data.rows}.");
            foreach (var level in data.levels ?? new ElevationLevel[0]) _levels[level.id] = level;
            foreach (var ramp in data.ramps ?? new ElevationRamp[0])
            {
                _ramps[ramp.id] = ramp;
                _corners[ramp.id] = Corners(ramp);
                float length = Vector2.Distance((ramp.top[0] + ramp.top[1]) * 0.5f, (ramp.bottom[0] + ramp.bottom[1]) * 0.5f);
                _ends[ramp.id] = Mathf.Min(MaxEnd, data.cellSize / Mathf.Max(data.cellSize, length));
            }
        }

        public static ElevationMap Parse(string json)
        {
            return new ElevationMap(JsonUtility.FromJson<WalkableElevationJson>(json));
        }

        public static ElevationMap Load(TextAsset file)
        {
            if (file == null) return null;
            int id = file.GetInstanceID();
            string text = file.text;
            int hash = text.GetHashCode();
            if (Cache.TryGetValue(id, out var cached) && cached.Hash == hash) return cached.Map;
            ElevationMap map = Parse(text);
            Cache[id] = (hash, map);
            return map;
        }

        public static Vector2 CellCenter(ElevationBounds bounds, float cellSize, int column, int row)
        {
            return new Vector2(bounds.x + (column + 0.5f) * cellSize, bounds.y + (row + 0.5f) * cellSize);
        }

        public Vector2 CellCenter(int column, int row)
        {
            return CellCenter(_data.bounds, _data.cellSize, column, row);
        }

        public bool TryGetCell(Vector2 ground, out int column, out int row)
        {
            column = Mathf.FloorToInt((ground.x - _data.bounds.x) / _data.cellSize);
            row = Mathf.FloorToInt((ground.y - _data.bounds.y) / _data.cellSize);
            return column >= 0 && row >= 0 && column < _data.columns && row < _data.rows;
        }

        public int RegionAt(int column, int row)
        {
            if (column < 0 || row < 0 || column >= _data.columns || row >= _data.rows) return Outside;
            return _cells[row * _data.columns + column];
        }

        public int RegionAt(Vector2 ground)
        {
            return TryGetCell(ground, out int column, out int row) ? _cells[row * _data.columns + column] : Outside;
        }

        public bool IsWalkable(int region)
        {
            return region != Outside && region != Blocked;
        }

        public bool IsRamp(int region)
        {
            return _ramps.ContainsKey(region);
        }

        public bool TryGetLevel(int region, out ElevationLevel level)
        {
            return _levels.TryGetValue(region, out level);
        }

        public bool TryGetRamp(int region, out ElevationRamp ramp)
        {
            return _ramps.TryGetValue(region, out ramp);
        }

        public float HeightAt(Vector2 ground)
        {
            return HeightAt(RegionAt(ground), ground);
        }

        public float HeightAt(int region, Vector2 ground)
        {
            if (_levels.TryGetValue(region, out ElevationLevel level)) return level.height;
            if (!_ramps.TryGetValue(region, out ElevationRamp ramp)) return 0f;
            return RampFormula.Height(_corners[region], ramp.lowHeight, ramp.highHeight, ramp.stairs ? ramp.steps : 0, ground);
        }

        public float SortBias(int region)
        {
            if (_levels.TryGetValue(region, out ElevationLevel level)) return level.sortBias;
            return _ramps.TryGetValue(region, out ElevationRamp ramp) ? ramp.sortBias : 0f;
        }

        public bool CanStep(Vector2 from, Vector2 to)
        {
            if (!TryGetCell(to, out int toColumn, out int toRow)) return false;
            int target = RegionAt(toColumn, toRow);
            if (!IsWalkable(target)) return false;
            if (!TryGetCell(from, out int fromColumn, out int fromRow)) return true;
            int origin = RegionAt(fromColumn, fromRow);
            if (!IsWalkable(origin) || origin == target) return true;

            Vector2 fromCenter = CellCenter(fromColumn, fromRow);
            Vector2 toCenter = CellCenter(toColumn, toRow);
            bool fromRamp = _ramps.ContainsKey(origin);
            bool toRamp = _ramps.ContainsKey(target);
            if (fromRamp == toRamp) return Mathf.Abs(HeightAt(origin, fromCenter) - HeightAt(target, toCenter)) <= _data.stepHeight + StepEpsilon;
            return fromRamp ? LeavesRamp(origin, target, toCenter, fromCenter) : LeavesRamp(target, origin, fromCenter, toCenter);
        }

        public bool CanWalkStraight(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / (_data.cellSize * WalkSample)));
            Vector2 current = from;
            for (int i = 1; i <= steps; i++)
            {
                Vector2 next = from + delta * (i / (float)steps);
                if (!CanStep(current, next)) return false;
                current = next;
            }
            return true;
        }

        public Vector2 Constrain(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / (_data.cellSize * WalkSample)));
            Vector2 step = delta / steps;
            Vector2 current = from;
            for (int i = 0; i < steps; i++)
            {
                Vector2 next = current + step;
                if (CanStep(current, next))
                {
                    current = next;
                    continue;
                }
                var alongX = new Vector2(current.x + step.x, current.y);
                var alongY = new Vector2(current.x, current.y + step.y);
                if (Mathf.Abs(step.x) > 0f && CanStep(current, alongX)) current = alongX;
                else if (Mathf.Abs(step.y) > 0f && CanStep(current, alongY)) current = alongY;
                else break;
            }
            return current;
        }

        public bool ScreenToGround(Vector2 visual, out Vector2 ground, out int region)
        {
            ground = visual;
            region = Outside;
            bool found = false;
            foreach (var level in _levels.Values)
            {
                var candidate = new Vector2(visual.x, visual.y - level.height);
                if (RegionAt(candidate) != level.id) continue;
                Keep(candidate, level.id, ref ground, ref region, ref found);
            }
            foreach (var ramp in _ramps.Values)
            {
                if (!SolveRamp(ramp, visual, out Vector2 candidate)) continue;
                Keep(candidate, ramp.id, ref ground, ref region, ref found);
            }
            return found;
        }

        public bool ScreenToGround(Camera camera, Vector2 screenPoint, out Vector2 ground, out int region)
        {
            Vector3 world = camera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, -camera.transform.position.z));
            return ScreenToGround(world, out ground, out region);
        }

        public Vector2 RampParameters(int ramp, Vector2 ground)
        {
            return _corners.TryGetValue(ramp, out RampCorners corners) ? RampFormula.Parameters(corners, ground) : Vector2.zero;
        }
        #endregion

        #region [Private Methods]
        private bool LeavesRamp(int ramp, int level, Vector2 levelPoint, Vector2 rampPoint)
        {
            ElevationRamp data = _ramps[ramp];
            Vector2 at = RampFormula.Parameters(_corners[ramp], levelPoint);
            float inside = RampFormula.Parameters(_corners[ramp], rampPoint).y;
            float end = _ends[ramp];
            float reach = end * EndReach;
            bool facing = at.x >= 0f && at.x <= 1f;
            if (level == data.upper) return at.y <= end && inside <= reach && (facing || at.y <= 0f);
            return level == data.lower && at.y >= 1f - end && inside >= 1f - reach && (facing || at.y >= 1f);
        }

        private bool SolveRamp(ElevationRamp ramp, Vector2 visual, out Vector2 ground)
        {
            RampCorners corners = _corners[ramp.id];
            Vector2 across = corners.TopRight - corners.TopLeft;
            Vector2 down = corners.BottomLeft - corners.TopLeft;
            float determinant = across.x * down.y - across.y * down.x;
            ground = visual;
            if (Mathf.Abs(determinant) < SameHeight) return false;
            float rise = ramp.highHeight - ramp.lowHeight;
            float k = rise * across.x / determinant;
            if (Mathf.Abs(1f - k) < SameHeight) return false;
            float y = (visual.y - ramp.highHeight + rise * (-across.x * corners.TopLeft.y - across.y * (visual.x - corners.TopLeft.x)) / determinant) / (1f - k);
            ground = new Vector2(visual.x, y);
            if (ramp.stairs) ground = new Vector2(visual.x, visual.y - HeightAt(ramp.id, ground));
            return RegionAt(ground) == ramp.id;
        }

        private static void Keep(Vector2 candidate, int id, ref Vector2 ground, ref int region, ref bool found)
        {
            if (found && candidate.y >= ground.y) return;
            ground = candidate;
            region = id;
            found = true;
        }

        private static RampCorners Corners(ElevationRamp ramp)
        {
            Vector2 topLeft = ramp.top[0];
            Vector2 topRight = ramp.top[1];
            Vector2 bottomLeft = ramp.bottom[0];
            Vector2 bottomRight = ramp.bottom[1];
            Vector2 direction = ((bottomLeft + bottomRight) - (topLeft + topRight)).normalized;
            Vector2 tangent = (topRight - topLeft).normalized;
            return new RampCorners(topLeft, topRight, bottomRight, bottomLeft, direction, new Vector2(tangent.y, -tangent.x));
        }
        #endregion
    }
}

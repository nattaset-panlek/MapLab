using System.Collections.Generic;
using UnityEngine;

namespace MapLab.Elevation
{
    public static class ElevationPathFormula
    {
        #region [Variables/Fields]
        private static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        private sealed class OpenList
        {
            private readonly List<(float Score, int Cell)> _heap = new List<(float Score, int Cell)>();

            public int Count => _heap.Count;

            public void Push(float score, int cell)
            {
                _heap.Add((score, cell));
                int child = _heap.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (_heap[parent].Score <= _heap[child].Score) break;
                    (_heap[parent], _heap[child]) = (_heap[child], _heap[parent]);
                    child = parent;
                }
            }

            public int Pop()
            {
                int cell = _heap[0].Cell;
                int last = _heap.Count - 1;
                _heap[0] = _heap[last];
                _heap.RemoveAt(last);
                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1;
                    int right = left + 1;
                    int smallest = parent;
                    if (left < _heap.Count && _heap[left].Score < _heap[smallest].Score) smallest = left;
                    if (right < _heap.Count && _heap[right].Score < _heap[smallest].Score) smallest = right;
                    if (smallest == parent) break;
                    (_heap[parent], _heap[smallest]) = (_heap[smallest], _heap[parent]);
                    parent = smallest;
                }
                return cell;
            }
        }
        #endregion

        #region [Public Methods]
        public static bool FindPath(ElevationMap map, Vector2 from, Vector2 to, List<Vector2> path)
        {
            path.Clear();
            if (map == null || !map.TryGetCell(from, out int startColumn, out int startRow) || !map.TryGetCell(to, out int goalColumn, out int goalRow)) return false;
            if (!map.IsWalkable(map.RegionAt(to))) return false;

            int columns = map.Columns;
            int start = startRow * columns + startColumn;
            int goal = goalRow * columns + goalColumn;
            if (start == goal)
            {
                if (!map.CanWalkStraight(from, to)) return false;
                path.Add(from);
                path.Add(to);
                return true;
            }

            int count = columns * map.Rows;
            var cost = new float[count];
            var came = new int[count];
            var closed = new bool[count];
            for (int i = 0; i < count; i++)
            {
                cost[i] = float.MaxValue;
                came[i] = -1;
            }
            cost[start] = 0f;
            var open = new OpenList();
            open.Push(Distance(map, start, goal), start);

            while (open.Count > 0)
            {
                int current = open.Pop();
                if (closed[current]) continue;
                if (current == goal) break;
                closed[current] = true;
                int column = current % columns;
                int row = current / columns;
                Vector2 here = map.CellCenter(column, row);
                foreach (var offset in Neighbours)
                {
                    int nextColumn = column + offset.x;
                    int nextRow = row + offset.y;
                    if (nextColumn < 0 || nextRow < 0 || nextColumn >= columns || nextRow >= map.Rows) continue;
                    int next = nextRow * columns + nextColumn;
                    if (closed[next]) continue;
                    Vector2 there = map.CellCenter(nextColumn, nextRow);
                    if (!map.CanStep(here, there)) continue;
                    if (offset.x != 0 && offset.y != 0 && (!map.CanStep(here, map.CellCenter(nextColumn, row)) || !map.CanStep(here, map.CellCenter(column, nextRow)))) continue;
                    float score = cost[current] + Vector2.Distance(here, there);
                    if (score >= cost[next]) continue;
                    cost[next] = score;
                    came[next] = current;
                    open.Push(score + Distance(map, next, goal), next);
                }
            }

            if (came[goal] < 0) return false;
            var cells = new List<Vector2>();
            for (int cell = goal; cell >= 0; cell = came[cell]) cells.Add(map.CellCenter(cell % columns, cell / columns));
            cells.Reverse();
            cells[0] = from;
            cells[cells.Count - 1] = to;
            Smooth(map, cells, path);
            return true;
        }
        #endregion

        #region [Private Methods]
        private static void Smooth(ElevationMap map, List<Vector2> cells, List<Vector2> path)
        {
            path.Add(cells[0]);
            int anchor = 0;
            while (anchor < cells.Count - 1)
            {
                int reach = cells.Count - 1;
                while (reach > anchor + 1 && !map.CanWalkStraight(cells[anchor], cells[reach])) reach--;
                path.Add(cells[reach]);
                anchor = reach;
            }
        }

        private static float Distance(ElevationMap map, int from, int to)
        {
            int columns = map.Columns;
            return Vector2.Distance(map.CellCenter(from % columns, from / columns), map.CellCenter(to % columns, to / columns));
        }
        #endregion
    }
}

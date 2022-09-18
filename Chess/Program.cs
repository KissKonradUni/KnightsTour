namespace Chess;

static class Constants
{
    public static readonly Position[] PossibleMoves = {
        new(-2,  1),
        new(-1,  2),
        new( 1, -2),
        new( 2, -1),
        new(-2, -1),
        new(-1, -2),
        new( 1,  2),
        new( 2,  1),
    };

    public static readonly float DistanceToValueWeight     = 0.5f;
    
    public static readonly float NeighbourToWholeWeight    = 0.80f;
    public static readonly float NeighbourToWholeThreshold = 0.95f;
    public static readonly int   NeighbourToWholeCycles    = 2;
}

internal class Position
{
    public int X;
    public int Y;

    public Position(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    // Add
    public static Position operator +(Position a, Position b)
    {
        return new Position(a.X + b.X, a.Y + b.Y);
    }

    // Distance
    public static float operator |(Position a, Position b)
    {
        return MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2));
    }

    public float Length()
    {
        return MathF.Sqrt(X*X + Y*Y);
    }
}

class Table
{
    private bool[,]  _travelMap;
    private int[,]   _stepMap;
    private float[,] _priorityMap;

    private Position _playerPosition;
    private readonly Position _tableSize;
    private readonly float    _maxDistance;

    public bool[,]  TravelMap      => _travelMap;
    public int[,]   StepMap        => _stepMap;
    public float[,] PriorityMap    => _priorityMap;
    public Position TableSize      => _tableSize;

    public Position PlayerPosition
    {
        get => _playerPosition;
        set
        {
            _playerPosition = value;
            UpdateMaps();
        }
    }

    public Table(Position tableSize, Position playerStart)
    {
        _travelMap      = new bool [tableSize.X, tableSize.Y];
        _priorityMap    = new float[tableSize.X, tableSize.Y];
        _stepMap        = new int  [tableSize.X, tableSize.Y];
        _tableSize      = tableSize;
        _maxDistance    = tableSize.Length();
        _playerPosition = playerStart;

        UpdateMaps();
    }

    private bool CanMoveThere(Position position)
    {
        return !(
            (position.X < 0) ||
            (position.Y < 0) ||
            (position.X >= _tableSize.X) ||
            (position.Y >= _tableSize.Y) ||
            (_travelMap[position.X, position.Y])
        );
    }

    public void UpdateMaps()
    {
        _travelMap[_playerPosition.X, _playerPosition.Y] = true;
        GenerateStepMap();
        GeneratePriorityMap();
    }
    
    private void GenerateStepMap()
    {
        for (int x = 0; x < _tableSize.X; x++)
        {
            for (int y = 0; y < _tableSize.Y; y++)
            {
                _stepMap[x, y] = 0;
                foreach (var move in Constants.PossibleMoves)
                {
                    var newPos = new Position(x, y) + move;
                    if (CanMoveThere(newPos))
                    {
                        _stepMap[x, y]++;
                    }
                }
            }
        }
    }

    private void GeneratePriorityMap()
    {
        var flattened = _stepMap.Cast<int>().ToArray();
        var minimum = flattened.Min();
        var maximum = flattened.Max();

        var minPrio = 1.0f;
        var maxPrio = 0.0f;

        for (int x = 0; x < _tableSize.X; x++)
        {
            for (int y = 0; y < _tableSize.Y; y++)
            {
                if (_travelMap[x, y])
                {
                    _priorityMap[x, y] = 0;
                    continue;
                }
                
                var valuePriority= 1.0f - ((float)_stepMap[x, y] - minimum) / maximum;
                var distancePriority = 1.0f - (new Position(x, y) | _playerPosition) / _maxDistance;

                // Lerp
                var weightedPriority = valuePriority + (distancePriority - valuePriority) * Constants.DistanceToValueWeight;

                if (weightedPriority < minPrio)
                    minPrio = weightedPriority;
                if (weightedPriority > maxPrio)
                    maxPrio = weightedPriority;
                
                _priorityMap[x, y] = weightedPriority;
            }
        }

        for (int x = 0; x < _tableSize.X; x++)
        {
            for (int y = 0; y < _tableSize.Y; y++)
            {
                if (_travelMap[x, y])
                    continue;

                _priorityMap[x, y] -= minPrio;
                _priorityMap[x, y] /= maxPrio - minPrio;
            }
        }

        for (int i = 0; i < Constants.NeighbourToWholeCycles; i++)
        {
            var copyTable = _priorityMap.Clone() as float[,];
            if (copyTable == null)
                throw new NullReferenceException("Copied array was somehow null.");
            
            for (int x = 0; x < _tableSize.X; x++)
            {
                for (int y = 0; y < _tableSize.Y; y++)
                {
                    // Increase priority of high priority cells' neighbours.
                    if (copyTable[x, y] < Constants.NeighbourToWholeThreshold)
                        continue;

                    foreach (var move in Constants.PossibleMoves)
                    {
                        var newPos = new Position(x, y) + move;

                        if (CanMoveThere(newPos))
                        {
                            var a = copyTable[newPos.X, newPos.Y];

                            _priorityMap[newPos.X, newPos.Y] = a + (1.0f - a) * Constants.NeighbourToWholeWeight;
                        }
                    }
                }
            }
        }
    }

    public Position? GetMostDesireableStep()
    {
        Position? best = null;
        float bestPrio = 0.0f;

        foreach (var move in Constants.PossibleMoves)
        {
            var newPos = _playerPosition + move;
            if (CanMoveThere(newPos))
            {
                if (bestPrio < _priorityMap[newPos.X, newPos.Y])
                {
                    best = newPos;
                    bestPrio = _priorityMap[newPos.X, newPos.Y];
                }
            }
        }

        return best;
    }

    public bool IsDone()
    {
        return _travelMap.Cast<bool>().All(val => val);
    }

    public bool IsStuck()
    {
        return _stepMap.Cast<int>().Max() == 0;
    }
}

public class Program
{
    private static void UpdateConsole(Table table)
    {
        Console.Clear();
        
        for (int x = 0; x < table.TableSize.X; x++)
        {
            for (int y = 0; y < table.TableSize.X; y++)
            {
                Console.SetCursorPosition(x * 2, y);
                
                if (table.PlayerPosition.X != x || table.PlayerPosition.Y != y)
                    Console.Write($"{(table.TravelMap[x, y] ? "X" : "-")}|");
                else
                    Console.Write("%|");
                
                Console.SetCursorPosition(table.TableSize.X * 2 + 2 + x * 3, y);
                Console.Write($"{table.StepMap[x,y].ToString().PadLeft(2, '0')}|");
                
                Console.SetCursorPosition(table.TableSize.X * 5 + 4 + x * 5, y);
                if (table.PlayerPosition.X != x || table.PlayerPosition.Y != y)
                    Console.Write($"{(table.PriorityMap[x,y] > 0.5f ? (table.PriorityMap[x,y] > 0.75f ? (table.PriorityMap[x,y] > 0.9f ? "\x1b[38;5;4m" : "\x1b[38;5;48m") : "\x1b[38;5;228m") : "\x1b[38;5;203m")}{table.PriorityMap[x,y]:0.00}\x1b[38;5;255m|");
                else
                    Console.Write("\x1b[38;5;255m [] |");
                Console.Write("\x1b[0m");
            }
        }
    }
    
    public static int Main(string[] args)
    {
        Table table = new Table(new Position(8, 8), new Position(3, 3));

        do
        {
            if (table.IsDone())
                Console.WriteLine("DONE");
            
            UpdateConsole(table);

            Console.ReadLine();

            var next = table.GetMostDesireableStep();
            if (next != null)
            {
                table.PlayerPosition = next;
            }
            else
            {
                Console.WriteLine("\nStuck or Done.");
                Console.ReadLine();
            }
        } while (true);
    }
}
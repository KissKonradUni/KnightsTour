namespace Chess;

internal static class Constants
{
    public static readonly Position[] PossibleMoves = {
        new(-2,  1),  // * *
        new(-1,  2),  //*   *
        new( 1, -2),  //  x  
        new( 2, -1),  //*   *
        new(-2, -1),  // * * 
        new(-1, -2),
        new( 1,  2),
        new( 2,  1),
    };

    public const float ValueToDistanceWeight     = 0.50f;

    public const float NeighbourToWholeWeight    = 0.80f;
    public const float NeighbourToWholeThreshold = 0.95f;
    public const int   NeighbourToWholeCycles    = 2;

    public const float VeryStuckMultiplier       = 0.25f;
}

internal class Position
{
    public readonly int X;
    public readonly int Y;

    public Position(int x, int y)
    {
        X = x;
        Y = y;
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

    public bool Equals(Position obj)
    {
        return obj.X == X && obj.Y == Y;
    }

    public Position Copy()
    {
        return new Position(X, Y);
    }
}

internal class Table : ICloneable
{
    private readonly bool[,]  _travelMap;
    private readonly int[,]   _stepMap;
    private readonly float[,] _priorityMap;

    private Position _playerPosition;
    private readonly Position _tableSize;
    private readonly float    _maxDistance;

    private readonly List<Position> _loweredPriorityPlaces;

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

    public Table(Position tableSize, Position playerStart, bool[,] travelMap,
        float[,] priorityMap, int[,] stepMap)
    {
        _travelMap      = travelMap.Clone()   as bool[,]  ?? throw new InvalidOperationException();
        _priorityMap    = priorityMap.Clone() as float[,] ?? throw new InvalidOperationException();
        _stepMap        = stepMap.Clone()     as int[,]   ?? throw new InvalidOperationException();

        _tableSize      = tableSize;
        _maxDistance    = tableSize.Length();
        _playerPosition = playerStart;
        
        _loweredPriorityPlaces = new List<Position>();
    }
    
    public Table(Position tableSize, Position playerStart)
    {
        _travelMap      = new bool [tableSize.X, tableSize.Y];
        _priorityMap    = new float[tableSize.X, tableSize.Y];
        _stepMap        = new int  [tableSize.X, tableSize.Y];
        _tableSize      = tableSize;
        _maxDistance    = tableSize.Length();
        _playerPosition = playerStart;
        
        _loweredPriorityPlaces = new List<Position>();

        UpdateMaps();
    }

    public void ResetPriority()
    {
        _loweredPriorityPlaces.Clear();
        GeneratePriorityMap();
    }
    
    public void LowerPriority(Position value)
    {
        _loweredPriorityPlaces.Add(value);
        GeneratePriorityMap();
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

    private void UpdateMaps()
    {
        _travelMap[_playerPosition.X, _playerPosition.Y] = true;
        GenerateStepMap();
        GeneratePriorityMap();
    }
    
    private void GenerateStepMap()
    {
        for (var x = 0; x < _tableSize.X; x++)
        {
            for (var y = 0; y < _tableSize.Y; y++)
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

        var minPriority = 1.0f;
        var maxPriority = 0.0f;

        for (var x = 0; x < _tableSize.X; x++)
        {
            for (var y = 0; y < _tableSize.Y; y++)
            {
                if (_travelMap[x, y])
                {
                    _priorityMap[x, y] = 0;
                    continue;
                }
                
                var valuePriority = 1.0f - ((float)_stepMap[x, y] - minimum) / maximum;
                var distancePriority = 1.0f - (new Position(x, y) | _playerPosition) / _maxDistance;

                // Lerp
                var weightedPriority = valuePriority + (distancePriority - valuePriority) * Constants.ValueToDistanceWeight;

                if (weightedPriority < minPriority)
                    minPriority = weightedPriority;
                if (weightedPriority > maxPriority)
                    maxPriority = weightedPriority;
                
                _priorityMap[x, y] = weightedPriority;
            }
        }

        for (var x = 0; x < _tableSize.X; x++)
        {
            for (var y = 0; y < _tableSize.Y; y++)
            {
                if (_travelMap[x, y])
                    continue;

                _priorityMap[x, y] -= minPriority;
                _priorityMap[x, y] /= maxPriority - minPriority;
            }
        }

        for (var i = 0; i < Constants.NeighbourToWholeCycles; i++)
        {
            var copyTable = _priorityMap.Clone() as float[,];
            if (copyTable == null)
                throw new NullReferenceException("Copied array was somehow null.");
            
            for (var x = 0; x < _tableSize.X; x++)
            { 
                for (var y = 0; y < _tableSize.Y; y++)
                {
                    if (float.IsNaN(_priorityMap[x, y]))
                    {
                        _priorityMap[x, y] = 1;
                        return;
                    }
                    
                    // Increase priority of high priority cells' neighbours.
                    if (copyTable[x, y] < Constants.NeighbourToWholeThreshold)
                        continue;

                    foreach (var move in Constants.PossibleMoves)
                    {
                        var newPos = new Position(x, y) + move;

                        if (!CanMoveThere(newPos)) continue;
                        
                        var a = copyTable[newPos.X, newPos.Y];
                        _priorityMap[newPos.X, newPos.Y] = a + (1.0f - a) * Constants.NeighbourToWholeWeight;
                    }
                }
            }
        }
        
        foreach (var lowered in _loweredPriorityPlaces)
        {
            _priorityMap[lowered.X, lowered.Y] *= Constants.VeryStuckMultiplier;
        }
    }

    public Position? GetMostDesirableStep()
    {
        Position? best = null;
        var bestPriority = -1.0f;

        foreach (var move in Constants.PossibleMoves)
        {
            var newPos = _playerPosition + move;
            
            if (!CanMoveThere(newPos)) continue;

            if (!(bestPriority < _priorityMap[newPos.X, newPos.Y])) continue;
            
            best = newPos;
            bestPriority = _priorityMap[newPos.X, newPos.Y];
        }

        return best;
    }

    public bool IsDone()
    {
        return _travelMap.Cast<bool>().All(val => val);
    }

    public bool IsStuck()
    {
        return _stepMap.Cast<int>().Max() != 0;
    }

    public object Clone()
    {
        return MemberwiseClone();
    }
}

public static class Program
{
    private static void UpdateConsole(Table table)
    {
        Console.Clear();

        Console.WriteLine($"{_runCount}. Try");
        
        if (_hidden)
            return;
        
        for (var x = 0; x < table.TableSize.X; x++)
        {
            for (var y = 0; y < table.TableSize.Y; y++)
            {
                Console.SetCursorPosition(x * 2, y + 1);
                
                if (table.PlayerPosition.X != x || table.PlayerPosition.Y != y)
                    Console.Write($"\x1b[0m{(table.TravelMap[x, y] ? "\x1b[38;5;203mX" : "\x1b[38;5;4m-")}\x1b[38;5;255m|");
                else
                    Console.Write("\x1b[38;5;50m%\x1b[38;5;255m|");
                
                Console.SetCursorPosition(table.TableSize.X * 2 + 2 + x * 3, y + 1);
                Console.Write($"\x1b[38;5;3m{table.StepMap[x,y].ToString().PadLeft(2, '0')}\x1b[38;5;255m|");
                
                Console.SetCursorPosition(table.TableSize.X * 5 + 4 + x * 5, y + 1);
                if (table.PlayerPosition.X != x || table.PlayerPosition.Y != y)
                    Console.Write($"{(table.PriorityMap[x,y] > 0.5f ? (table.PriorityMap[x,y] > 0.75f ? (table.PriorityMap[x,y] > 0.9f ? "\x1b[38;5;4m" : "\x1b[38;5;48m") : "\x1b[38;5;228m") : "\x1b[38;5;203m")}{table.PriorityMap[x,y]:0.00}\x1b[38;5;255m|");
                else
                    Console.Write("\x1b[38;5;255m [] |");
                Console.Write("\x1b[0m");
            }
        }
    }

    private static Position _tableSize = new(8, 8);
    private static Position _playerStart = new(4, 3);
        
    private static Table _table = new(_tableSize, _playerStart);
    private static Position _lastStep = new(0,0);
    
    private static int _runCount = 1;
    private static bool _hidden;
    
    private static readonly List<Table> History = new();
    private static readonly List<Position> StepHistory = new();

    public static int Main(string[] args)
    {
        Console.Write("\n\nChess ");
        foreach (var s in args)
        {
            Console.Write($"{s} ");
            if (s.StartsWith("-t"))
            {
                var format = s.Replace("-t", "").Split(",");
                _tableSize = new Position(int.Parse(format[0]), int.Parse(format[1]));
            } else if (s.StartsWith("-p"))
            {
                var format = s.Replace("-p", "").Split(",");
                _playerStart = new Position(int.Parse(format[0]), int.Parse(format[1]));
            } else if (s.StartsWith("-h"))
            {
                _hidden = true;
            }
        }
        _table = new Table(_tableSize, _playerStart);
        Console.WriteLine();
        Console.ReadLine();

        do
        {
            UpdateConsole(_table);

            if (!_hidden)
                Console.ReadLine();

            var next = _table.GetMostDesirableStep();
            if (next != null)
            {
                History.Add(new Table(_table.TableSize, _table.PlayerPosition, _table.TravelMap, _table.PriorityMap, _table.StepMap));

                if (_lastStep.Equals(next))
                {
                    RevertStep();
                    Console.WriteLine("Very stuck, sorry!");
                    if (!_hidden)
                        Console.ReadLine();
                    var thisStep = _table.PlayerPosition.Copy();
                    _table = History[0];
                    _runCount++;

                    for (var i = StepHistory.Count / 2; i < StepHistory.Count; i++)
                    {
                        _table.LowerPriority(StepHistory[i]);    
                    }
                    
                    _table.LowerPriority(thisStep);
                    History.Clear();
                }
                else
                {
                    _table.PlayerPosition = next;
                    StepHistory.Add(next);
                    _lastStep = next;   
                }
            }
            else
            {
                if (_table.IsStuck())
                {   
                    Console.WriteLine("Stuck!");
                    if (!_hidden)
                        Console.ReadLine();

                    RevertStep();
                }
                else
                {
                    Console.WriteLine("Done!");
                    Console.ReadLine();
                    
                    return 0;
                }
            }
        } while (true);
    }

    private static void RevertStep()
    {
        var lastIndex = History.Count - 1;
        _table = History[lastIndex];
        History.RemoveAt(lastIndex);
        
        var lastStepIndex = StepHistory.Count - 1;
        _table.LowerPriority(StepHistory[lastStepIndex]);
        StepHistory.RemoveAt(lastStepIndex);
    }
}
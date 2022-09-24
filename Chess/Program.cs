// ReSharper disable CommentTypo
namespace Chess;

/// <summary>
/// Fő konstansokat tartalmazó statikus class
/// </summary>
internal static class Constants
{
    /// <summary>
    /// Az összes relatív pozíciót tartalmazó tömb, amelyre a ló rá tud lépni egy adott helyről.
    /// </summary>
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

    /// Meghatározza a lépésszámból és a mező távolságából meghatározott prioritás súlyozását.
    public const float ValueToDistanceWeight     = 0.50f;

    /// Meghatározza azt, hogy a magas prioritású mezők hány százalékkal növelik az arról elérhető mezők prioritását.
    public const float NeighbourToWholeWeight    = 0.80f;
    /// A minimum, amelytől egy mezőt magas prioritásúnak tekintünk.
    public const float NeighbourToWholeThreshold = 0.95f;
    /// A szomszéd alapú prioritásnövelések száma.
    public const int   NeighbourToWholeCycles    = 2;

    /// Meghatározza, hogy hány szoros csökkenéssel jár egy sikertelen útvonal a mezőkön.
    public const float VeryStuckMultiplier       = 0.25f;
}

/// <summary>
/// Egy egyszerű 2D-s vektor objektum.
/// </summary>
internal class Position
{
    public readonly int X;
    public readonly int Y;

    public Position(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// Összeadás operator
    public static Position operator +(Position a, Position b)
    {
        return new Position(a.X + b.X, a.Y + b.Y);
    }

    /// Kettő vektort pontként használva megadja a köztük lévő távolságot
    public static float operator |(Position a, Position b)
    {
        return MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2));
    }

    /// <summary>
    /// Megadja a vektor hosszát.
    /// </summary>
    /// <returns>A vektor hossza floatban.</returns>
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

/// <summary>
/// A fő tábla class, amelyben az útvonal számításához szükséges műveletek többsége történik.
/// </summary>
internal class Table
{
    /// Eltárolja azokat a mezőket, amelyeken már jártunk (true értékkel).
    private readonly bool[,]  _travelMap;
    /// Eltárolja azt, hogy melyik mezőről hány eltérő helyre lehet lépni.
    private readonly int[,]   _stepMap;
    /// Eltárolja a különböző mezők prioritását.
    private readonly float[,] _priorityMap;

    /// A játékos jelenlegi helye.
    private Position _playerPosition;
    /// A tábla mérete.
    private readonly Position _tableSize;
    /// A táblán megtehető legnagyobb távolság. (Keresztbe, légvonalba)
    private readonly float    _maxDistance;

    /// Eltárolja azokat a mezőket, amelyeknek csökkentve lett a prioritása.
    private readonly List<Position> _loweredPriorityPlaces;

    // Csak olvasható külső fieldeket biztosítok a legtöbb változóhoz.
    public bool[,]  TravelMap      => _travelMap;
    public int[,]   StepMap        => _stepMap;
    public float[,] PriorityMap    => _priorityMap;
    public Position TableSize      => _tableSize;

    /// Amennyiben átállításra kerül a játékos helye a táblán kívülről, frissítjük a különböző térképeket.
    public Position PlayerPosition
    {
        get => _playerPosition;
        set
        {
            _playerPosition = value;
            UpdateMaps();
        }
    }

    /// <summary>
    /// Egyszerű konstruktor, amely figyel arra hogy az előző állásból átvett értékeket másolja, és ne referenciaként tárolja.
    /// </summary>
    /// <param name="tableSize">A tábla mérete</param>
    /// <param name="playerStart">A játékos helye</param>
    /// <param name="travelMap">Az előző utazási térkép</param>
    /// <param name="priorityMap">Az előző prioritás térkép</param>
    /// <param name="stepMap">Az előző lépés térkép</param>
    /// <exception cref="InvalidOperationException">Lehetséges hiba abban az esetben, ha valamelyik tömb nem lett továbbadva.</exception>
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
    
    /// <summary>
    /// Egyszerűbb konstruktor arra az esetre, ha a legelső lépéshez hoznánk létre a classunkat.
    /// </summary>
    /// <param name="tableSize">A tábla mérete</param>
    /// <param name="playerStart">A játékos helye</param>
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

    /// <summary>
    /// Visszaállítja a csökkentet prioritású mezők értékét.
    /// </summary>
    public void ResetPriority()
    {
        _loweredPriorityPlaces.Clear();
        GeneratePriorityMap();
    }
    
    /// <summary>
    /// Csökkenti egy mező prioritását.
    /// </summary>
    /// <param name="value">A kijelölt mező helye.</param>
    public void LowerPriority(Position value)
    {
        _loweredPriorityPlaces.Add(value);
        GeneratePriorityMap();
    }
    
    /// <summary>
    /// Leellenőri, amennyiben a kijelölt helyre tud lépni a játákos. Csak magát a mezőt validálja, nem nézi meg hogy szabályosan történő lépésről van-e szó.
    /// </summary>
    /// <param name="position">A tesztelendő hely</param>
    /// <returns>TRUE - ha oda lehetne lépni</returns>
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

    /// <summary>
    /// Frissíti az összes térképet.
    /// </summary>
    private void UpdateMaps()
    {
        _travelMap[_playerPosition.X, _playerPosition.Y] = true;
        GenerateStepMap();
        GeneratePriorityMap();
    }
    
    /// <summary>
    /// Legenerálja a lépéstérképet a jelenlegi állapotból.
    /// </summary>
    private void GenerateStepMap()
    {
        // Az összes x és y ponton.
        for (var x = 0; x < _tableSize.X; x++)
        {
            for (var y = 0; y < _tableSize.Y; y++)
            {
                _stepMap[x, y] = 0;
                // Az összes lehetséges lépést nézve.
                foreach (var move in Constants.PossibleMoves)
                {
                    var newPos = new Position(x, y) + move;
                    // Ha oda lehet lépni.
                    if (CanMoveThere(newPos))
                    {
                        // Számoljuk.
                        _stepMap[x, y]++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Legenerálja a prioritás térképet.
    /// </summary>
    /// <exception cref="NullReferenceException">Egy szükségtelen error check eredménye, amiért az IDE nem volt hajlandó békén hagyni.</exception>
    private void GeneratePriorityMap()
    {
        // Megkeressük a minimum és maximum lépésszámot a másik térképen.
        var flattened = _stepMap.Cast<int>().ToArray();
        var minimum = flattened.Min();
        var maximum = flattened.Max();

        var minPriority = 1.0f;
        var maxPriority = 0.0f;

        // Az összes x és y mezőn.
        for (var x = 0; x < _tableSize.X; x++)
        {
            for (var y = 0; y < _tableSize.Y; y++)
            {
                // Amennyiben még lehet rá lépni.
                if (_travelMap[x, y])
                {
                    _priorityMap[x, y] = 0;
                    continue;
                }
                
                // Kiszámoljuk a prioritást ezekkel a formulákkal:
                // Lépésszám szerint: prio1 = 1.0f - ((lépésszám - minum) / maximum)
                // Távolság szerint:  prio2 = 1.0f - (mező és játékos távolsága) / legnagyobb távolság
                var valuePriority = 1.0f - ((float)_stepMap[x, y] - minimum) / maximum;
                var distancePriority = 1.0f - (new Position(x, y) | _playerPosition) / _maxDistance;

                // Súlyozott átlag a kettő prioritás között egy konstans által.
                var weightedPriority = valuePriority + (distancePriority - valuePriority) * Constants.ValueToDistanceWeight;

                // Lementjük a jelenlegi minum és maximum értékét a prioritásoknak.
                if (weightedPriority < minPriority)
                    minPriority = weightedPriority;
                if (weightedPriority > maxPriority)
                    maxPriority = weightedPriority;
                
                _priorityMap[x, y] = weightedPriority;
            }
        }

        // Minden x és y mezőn
        for (var x = 0; x < _tableSize.X; x++)
        {
            for (var y = 0; y < _tableSize.Y; y++)
            {
                // Ha rá lehet lépni
                if (_travelMap[x, y])
                    continue;

                // 0.0 - 1.0 skálára méretezzük a prioritási értékeket.
                // Nem szükséges lépés, de praktikus.
                _priorityMap[x, y] -= minPriority;
                _priorityMap[x, y] /= maxPriority - minPriority;
            }
        }

        // A szomszéd alapú prioritásnövelések száma szerint.
        for (var i = 0; i < Constants.NeighbourToWholeCycles; i++)
        {
            var copyTable = _priorityMap.Clone() as float[,];
            if (copyTable == null)
                throw new NullReferenceException("Copied array was somehow null.");
            
            // Minden x és y mezőn.
            for (var x = 0; x < _tableSize.X; x++)
            { 
                for (var y = 0; y < _tableSize.Y; y++)
                {
                    // Egyedi esetre vonatkozó hibaellenőrzés, ha 1 darab mező maradt hátra.
                    if (float.IsNaN(_priorityMap[x, y]))
                    {
                        _priorityMap[x, y] = 1;
                        return;
                    }
                    
                    // Kihagyjuk a mezőt ha nem éri el a határértéket
                    if (copyTable[x, y] < Constants.NeighbourToWholeThreshold)
                        continue;

                    // Megkeressük az összes szomszédját
                    foreach (var move in Constants.PossibleMoves)
                    {
                        var newPos = new Position(x, y) + move;

                        // Ha nem valid hely, kihagyjuk
                        if (!CanMoveThere(newPos)) continue;
                        
                        // És növeljük a megadott konstans százalékkal.
                        var a = copyTable[newPos.X, newPos.Y];
                        _priorityMap[newPos.X, newPos.Y] = a + (1.0f - a) * Constants.NeighbourToWholeWeight;
                    }
                }
            }
        }
        
        // A csökkentett mezők értékét korrektáljuk.
        foreach (var lowered in _loweredPriorityPlaces)
        {
            _priorityMap[lowered.X, lowered.Y] *= Constants.VeryStuckMultiplier;
        }
    }

    /// <summary>
    /// Megkeresi a legoptimálisabb lépést.
    /// </summary>
    /// <returns>A legjobb hely mezője</returns>
    public Position? GetMostDesirableStep()
    {
        Position? best = null;
        var bestPriority = -1.0f;

        // Az összes lehetséges szomszédon a játékos helye szerint
        foreach (var move in Constants.PossibleMoves)
        {
            var newPos = _playerPosition + move;
            
            // Amennyiben valid hely
            if (!CanMoveThere(newPos)) continue;

            // És jobb a prioritása mint amit eddig találtunk
            if (!(bestPriority < _priorityMap[newPos.X, newPos.Y])) continue;
            
            // Akkor azt visszaadjuk
            best = newPos;
            bestPriority = _priorityMap[newPos.X, newPos.Y];
        }

        return best;
    }
    
    /// <summary>
    /// Megadja ha kész van a tábla. 
    /// </summary>
    /// <returns>TRUE - amennyiben minden mező TRUE.</returns>
    public bool IsDone()
    {
        return _travelMap.Cast<bool>().All(val => val);
    }

    /// <summary>
    /// Megadja, ha elakadt a játékos.
    /// </summary>
    /// <returns>TRUE - ha a legnagyobb lépésszám nem 0.</returns>
    public bool IsStuck()
    {
        return _stepMap.Cast<int>().Max() != 0;
    }
}

/// <summary>
/// A programunkat tartalmazó class.
/// </summary>
public static class Program
{
    /// <summary>
    /// Frissíti a konzol kimenetet
    /// </summary>
    /// <param name="table">A tábla, amit megkéne jeleníteni</param>
    private static void UpdateConsole(Table table)
    {
        // Kiürítjük a konzolt
        Console.Clear();

        // Kiírjuk hanyadik körnél járunk
        Console.WriteLine($"{_runCount}. Try");
        
        // Rejtett módban visszatérünk innen
        if (_hidden)
            return;
        
        // Minden x és y mezőre
        for (var x = 0; x < table.TableSize.X; x++)
        {
            for (var y = 0; y < table.TableSize.Y; y++)
            {
                Console.SetCursorPosition(x * 2, y + 1);
                
                // Kiírjuk hogyha már itt járt egyszer a játékos
                if (table.PlayerPosition.X != x || table.PlayerPosition.Y != y)
                    Console.Write($"\x1b[0m{(table.TravelMap[x, y] ? "\x1b[38;5;203mX" : "\x1b[38;5;4m-")}\x1b[38;5;255m|");
                else
                    Console.Write("\x1b[38;5;50m%\x1b[38;5;255m|");
                
                // Hogy mennyi helyről lehet a mezőre lépni
                Console.SetCursorPosition(table.TableSize.X * 2 + 2 + x * 3, y + 1);
                Console.Write($"\x1b[38;5;3m{table.StepMap[x,y].ToString().PadLeft(2, '0')}\x1b[38;5;255m|");
                
                // Illetve hogy mennyi a prioritása
                Console.SetCursorPosition(table.TableSize.X * 5 + 4 + x * 5, y + 1);
                if (table.PlayerPosition.X != x || table.PlayerPosition.Y != y)
                    Console.Write($"{(table.PriorityMap[x,y] > 0.5f ? (table.PriorityMap[x,y] > 0.75f ? (table.PriorityMap[x,y] > 0.9f ? "\x1b[38;5;4m" : "\x1b[38;5;48m") : "\x1b[38;5;228m") : "\x1b[38;5;203m")}{table.PriorityMap[x,y]:0.00}\x1b[38;5;255m|");
                else
                    Console.Write("\x1b[38;5;255m [] |");
                Console.Write("\x1b[0m");
            }
        }
    }

    /// A kezdeti táblaméret.
    private static Position _tableSize = new(8, 8);
    /// A játékos kiindulási helye.
    private static Position _playerStart = new(4, 3);
     
    /// A fő tábla amit használunk.
    private static Table _table = new(_tableSize, _playerStart);
    /// Az előző lépés helye.
    private static Position _lastStep = new(0,0);
    
    /// A lefutptt körök száma. 
    private static int _runCount = 1;
    /// Rejtett módban fut-e.
    private static bool _hidden;
    
    /// Az összes táblaállapot visszamenőleg.
    private static readonly List<Table> History = new();
    /// Az összes lépés visszamenőleg.
    private static readonly List<Position> StepHistory = new();

    /// <summary>
    /// A fő futási utasítás
    /// </summary>
    /// <param name="args">A konzolparaméterek</param>
    /// <returns>0, amennyiben sikeresen lefutott.</returns>
    public static int Main(string[] args)
    {
        // Feldolgozzuk a konzolparamétereket.
        Console.Write("\n\nChess ");
        foreach (var s in args)
        {
            Console.Write($"{s} ");
            // -tx,y a tábla mérete
            if (s.StartsWith("-t"))
            {
                var format = s.Replace("-t", "").Split(",");
                _tableSize = new Position(int.Parse(format[0]), int.Parse(format[1]));
                
            // -px,y a játékos helye 0,0-tól
            } else if (s.StartsWith("-p"))
            {
                var format = s.Replace("-p", "").Split(",");
                _playerStart = new Position(int.Parse(format[0]), int.Parse(format[1]));
                
            // ha jelen van a -h, rejtett módban fut
            } else if (s.StartsWith("-h"))
            {
                _hidden = true;
            }
        }
        _table = new Table(_tableSize, _playerStart);
        Console.WriteLine();
        Console.ReadLine();

        // Addig fut amíg nem sikerül.
        do
        {
            UpdateConsole(_table);

            // Nem állítjuk meg a program futását rejtett módban.
            if (!_hidden)
                Console.ReadLine();

            var next = _table.GetMostDesirableStep();
            if (next != null)
            {
                History.Add(new Table(_table.TableSize, _table.PlayerPosition, _table.TravelMap, _table.PriorityMap, _table.StepMap));

                // Hogyha ugyanazt a hibás lépést ismételtük meg, akkor előröl kell kezdeni a kört.
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
                    // Egyébként a megszokottak szerint lépünk
                    _table.PlayerPosition = next;
                    StepHistory.Add(next);
                    _lastStep = next;   
                }
            }
            else
            {
                // Jelezzük a játékosnak az állásunkat.
                if (_table.IsStuck())
                {   
                    Console.WriteLine("Stuck!");
                    if (!_hidden)
                        Console.ReadLine();

                    RevertStep();
                }
                else if (_table.IsDone())
                {
                    Console.WriteLine("Done!");
                    Console.ReadLine();
                    
                    return 0;
                }
            }
        } while (true);
    }

    /// Visszaállítja a tábla állását egy lépéssel.
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
using System.Globalization;

namespace Antiphon.SessionRunner;

/// <summary>The runner's durable launch ceiling. A successful write is visible to the next launch.</summary>
public sealed class RunnerCapacityState
{
    private readonly object _gate = new();
    private int _capacity;

    public RunnerCapacityState(PhoneHomeSettings settings)
    {
        StatePath = settings.CapacityStatePath;
        _capacity = settings.Capacity;
        if (File.Exists(StatePath))
        {
            var text = File.ReadAllText(StatePath).Trim();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var saved)
                || saved < 1)
                throw new InvalidDataException($"Runner capacity state at '{StatePath}' is invalid.");
            _capacity = saved;
        }
    }

    public string StatePath { get; }
    public int Capacity => Volatile.Read(ref _capacity);

    public void Apply(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(StatePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var temporary = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, capacity.ToString(CultureInfo.InvariantCulture));
                File.Move(temporary, StatePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            Volatile.Write(ref _capacity, capacity);
        }
    }
}

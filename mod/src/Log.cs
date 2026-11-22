// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The mod's log (Meitou's ModContext.Log) and the rule counters: every rule counts when it fires, and a line every 5 s lists the counts
// that changed (adversarial method: a rule that never logs is a failed test).
using System.Diagnostics;
using System.Text;

namespace PZA.WASDControl.Meitou;

public static class Counters
{
    static readonly Dictionary<string, long> s_counts = new(StringComparer.Ordinal);
    static readonly Dictionary<string, long> s_reported = new(StringComparer.Ordinal);

    /// <summary>The counters are shared with the tick (TumbleSystem counts on the simulation's thread, 0.10.2 ledge fall).</summary>
    static readonly object s_lock = new();

    /// <summary>One firing of a rule.</summary>
    public static void Add(string rule) { lock (s_lock) s_counts[rule] = s_counts.GetValueOrDefault(rule) + 1; }

    /// <summary>A rule's count so far.</summary>
    public static long Get(string rule) { lock (s_lock) return s_counts.GetValueOrDefault(rule); }

    /// <summary>"rule=total" for every rule starting with <paramref name="prefix"/> (the headless tests' read-out).</summary>
    public static string Prefixed(string prefix)
    {
        lock (s_lock)
            return string.Join(" ", s_counts.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => p.Key + "=" + p.Value));
    }

    /// <summary>"rule=total(+new)" for each rule that fired since the last call; null when none did.</summary>
    public static string? Changes()
    {
        lock (s_lock) return ChangesLocked();
    }

    static string? ChangesLocked()
    {
        var sb = new StringBuilder();
        foreach (var (rule, n) in s_counts.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            long before = s_reported.GetValueOrDefault(rule);
            if (n == before) continue;
            sb.Append(sb.Length == 0 ? "" : " ").Append(rule).Append('=').Append(n).Append("(+").Append(n - before).Append(')');
            s_reported[rule] = n;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}

public static class Log
{
    /// <summary>Where lines go (ModContext.Log); dropped before Initialize sets it.</summary>
    public static Action<string> Sink = _ => { };

    static long s_lastReport = Stopwatch.GetTimestamp();
    static long s_lastException;

    public static void Line(string text) => Sink(text);

    /// <summary>Once a frame: the 5 s counter line, when anything fired.</summary>
    public static void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(s_lastReport, now).TotalSeconds < 5) return;

        s_lastReport = now;
        if (Counters.Changes() is { } changes) Line(Strings.Format("log.counters", Driver.On ? "on" : "off", changes));
    }

    /// <summary>A throw from a patch: counted always, logged at most once a second.</summary>
    public static void Exception(string where, Exception e)
    {
        Counters.Add("error." + where.Replace(' ', '_'));
        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(s_lastException, now).TotalSeconds < 1) return;

        s_lastException = now;
        Line(Strings.Format("log.exception", where, e.GetType().Name + ": " + e.Message));
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

// ScheduleLib 12e601e A6/A7/A8. Framework-only domain doubles preserve the
// reviewed call shapes; reporting/interpolation redesign belongs to slice 15.
public static class C
{
    private sealed class CliOutputStreamingException(string message, Exception inner) : Exception(message, inner);
    private sealed class CalendarStreamingException(Result result, Exception inner) : Exception("stream", inner)
    {
        public int Exit => result.Exit;
    }
    private sealed record Result(int Exit, string[] Errors, bool ReportRemaining);
    private sealed record Key(string Value);
    private sealed record NodeDataKey<T>(Key Key);
    private sealed class Node { public Key GetOrAdd<T>(NodeDataKey<T> key) => key.Key; }
    private interface IKeyEqualityComparer<T> { }
    private sealed class Provider : IServiceProvider { public object? GetService(Type serviceType) => null; }
    private static Exception StreamingFailure(Exception inner) => inner;
    private static string ResolvePath(string path) => Path.GetFullPath(path);

    private static void Fail(Exception ex)
    {
        throw StreamingFailure(new CliOutputStreamingException("Calendar output streaming failed.", ex)); // Accepted: one simple constructed argument.
    }

    private static Exception Failure(string[] outcomes, Exception inner) => new CalendarStreamingException(new Result(outcomes.Any(x => x == "completed") ? 6 : 1, ["Output streaming failed. Completed actions remain applied; inspect the destination before another apply."], false), inner); // CR0301: new Result

    private static string DirectoryPath(string? selected)
    {
        var path = selected is { } cache ? ResolvePath(cache) : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), // CR0300: Environment.GetFolderPath
            "ScheduleLib", "cache");
        return path;
    }

    private static Key Lookup<T>(Node node, JsonObject action) =>
        node.GetOrAdd(new NodeDataKey<T>(new(action["key"]!.GetValue<string>().Split('.')[0]))); // CR0300: new NodeDataKey<T>

    private static string[] SettingsPath(string? profile, string[] parts)
    {
        var path = profile is null ? new[] { "defaults" }.Concat(parts).ToArray() : new[] { "profiles", profile }.Concat(parts).ToArray(); // CR0301: profile is null
        return path;
    }

    private static bool HasComparer(IServiceProvider provider, Type value, int length)
    {
        if (length != 0 && provider.GetService(typeof(IKeyEqualityComparer<>).MakeGenericType(value.GetGenericArguments()[0])) is null) // CR0203: length != 0 | CR0300: value.GetGenericArguments()[0]
            return false;
        return true;
    }

    public static string Run()
    {
        var trace = "";
        var inner = new Exception();
        try { Fail(inner); }
        catch (CliOutputStreamingException error) { trace += ReferenceEquals(error.InnerException, inner) + ";"; }
        var completed = Failure(["completed"], inner);
        var pending = Failure([], inner);
        trace += ((CalendarStreamingException)completed).Exit + ":" + ((CalendarStreamingException)pending).Exit + ";";
        var suppliedDirectory = DirectoryPath(".");
        var defaultDirectory = DirectoryPath(null);
        if (suppliedDirectory.Length == 0) throw new InvalidOperationException();
        trace += defaultDirectory.EndsWith("cache") ? "paths;" : "wrong;";
        var action = new JsonObject { ["key"] = "section.value" };
        var node = new Node();
        var key = Lookup<object>(node, action);
        trace += key.Value + ";";
        var defaults = SettingsPath(null, ["key"]);
        var profile = SettingsPath("user", ["key"]);
        trace += string.Join("/", defaults) + ":" + string.Join("/", profile) + ";";
        var provider = new Provider();
        var skipped = HasComparer(provider, typeof(int), 0);
        var missing = HasComparer(provider, typeof(List<int>), 1);
        return trace + skipped + ":" + missing;
    }
}

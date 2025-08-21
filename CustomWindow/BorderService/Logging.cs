using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace BorderServiceApp;

internal static class BsLog
{
    private static string Loc(string file, int line, string member)
        => $"{Path.GetFileName(file)}:{line} {member}()";

    public static void Info(string tag, string msg,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
        => Console.WriteLine($"[BS] [{tag}] {Loc(file,line,member)} {msg}");

    public static void Warn(string tag, string msg, Exception? ex = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
    {
        var hrPart = ex != null ? $" HR=0x{ex.HResult:X8}" : string.Empty;
        Console.WriteLine($"[BS] [{tag}-WARN] {Loc(file,line,member)} {msg}{hrPart}");
        if (ex != null)
            Console.WriteLine($"[BS] [{tag}-WARN] {Loc(file,line,member)} EX: {ex.GetType().Name}: {ex.Message}");
    }

    public static void Err(string tag, string msg, Exception? ex = null, int hr = 0,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
    {
        string hrPart = hr != 0 ? $" HR=0x{hr:X8}" : ex != null ? $" HR=0x{ex.HResult:X8}" : string.Empty;
        Console.WriteLine($"[BS] [{tag}-ERR] {Loc(file,line,member)} {msg}{hrPart}");
        if (ex != null)
            Console.WriteLine($"[BS] [{tag}-ERR] {Loc(file,line,member)} EX: {ex.GetType().Name}: {ex.Message}");
    }
}

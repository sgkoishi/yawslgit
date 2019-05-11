using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace yawslgit
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private static readonly object _writeLock = new object();

        [Conditional("DEBUG")]
        private static void Log(string str)
        {
            lock (_writeLock)
            {
                File.AppendAllText("C:\\Arch\\yawslgit.log", str);
            }
        }

        private static string ToLinuxPath(string path)
        {
            if (char.IsLetter(path[0]) && path[1] == ':')
            {
                return $"/mnt/{char.ToLower(path[0])}{path.Substring(2)}".Replace("\\", "/");
            }
            return path;
        }

        private static string ToCommandLine(string[] args)
        {
            var result = new List<string>();
            var needQuote = false;
            foreach (var item in args)
            {
                var backslashes = new List<string>();
                if (result.Count > 0)
                {
                    result.Add(" ");
                }
                needQuote = item.Contains(" ") || item.Contains("\t") || string.IsNullOrWhiteSpace(item);
                if (needQuote)
                {
                    result.Add("\"");
                }
                foreach (var c in item)
                {
                    switch (c)
                    {
                        case '\\':
                        {
                            backslashes.Add("\\");
                            break;
                        }
                        case '"':
                        {
                            result.Add(new string('\\', backslashes.Count * 2));
                            result.Add("\\\"");
                            backslashes = new List<string>();
                            break;
                        }
                        default:
                        {
                            if (backslashes.Count > 0)
                            {
                                result.AddRange(backslashes);
                                backslashes = new List<string>();
                            }
                            result.Add(c.ToString());
                            break;
                        }
                    }
                }
                if (needQuote)
                {
                    result.AddRange(backslashes);
                    result.Add("\"");
                }
            }
            return string.Concat(result);
        }

        private static void Main(string[] args)
        {
            var argsOnly = ToCommandLine(args.Select(ToLinuxPath).ToArray());
            var token = new Random().Next();
            Log($"CL ({token}):\r\n\t{Environment.CommandLine}\r\n");
            Log($"Args ({token}):\r\n\t{string.Join("\r\n\t", args)}\r\n");
            var psi = new ProcessStartInfo(@"C:\Windows\Sysnative\wsl.exe")
            {
                UseShellExecute = false,
                Arguments = "git " + argsOnly,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            Log($"Invoke ({token}):\r\n\t{psi.Arguments}\r\n");
            Console.Out.Write($"Git command: {psi.Arguments}\n");
            var git = new Process() { StartInfo = psi };
            git.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                var str = e?.Data?.TrimEnd('\0');
                if (!string.IsNullOrEmpty(str))
                {
                    Log($"Output ({token}):\r\n\t{str}\r\n");
                    Console.Out.Write(str + "\n");
                }
            };
            git.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                var str = e?.Data?.TrimEnd('\0');
                if (!string.IsNullOrEmpty(str))
                {
                    Log($"Error ({token}):\r\n\t{str}\r\n");
                    Console.Error.WriteLine(str + "\n");
                }
            };
            git.Start();
            git.BeginOutputReadLine();
            git.BeginErrorReadLine();
            new Thread(() =>
            {
                while (true)
                {
                    git.StandardInput.Write((char) Console.In.Read());
                }
            }).Start();
            git.WaitForExit();
            Environment.Exit(git.ExitCode);
        }
    }
}

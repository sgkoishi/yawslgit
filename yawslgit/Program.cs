using System;
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

        private static string ToLinuxPath(string path)
        {
            return char.IsLetter(path[0]) && path[1] == ':' ? $"/mnt/{char.ToLower(path[0])}{path.Substring(2)}".Replace("\\", "/") : path;
        }

        private static string ToWindowsPath(string path)
        {
            throw new NotImplementedException();
        }

        private static string ToCommandLine(string[] args)
        {
            var result = new List<string>();
            foreach (var item in args)
            {
                var backslashes = 0;
                if (result.Count > 0)
                {
                    result.Add(" ");
                }
                var needQuote = item.Contains(" ") || item.Contains("\t") || string.IsNullOrWhiteSpace(item);
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
                            backslashes++;
                            break;
                        }
                        case '"':
                        {
                            result.Add(new string('\\', backslashes * 2));
                            result.Add("\\\"");
                            backslashes = 0;
                            break;
                        }
                        default:
                        {
                            if (backslashes > 0)
                            {
                                result.Add(new string('\\', backslashes));
                                backslashes = 0;
                            }
                            result.Add(c.ToString());
                            break;
                        }
                    }
                }
                if (needQuote)
                {
                    result.Add(new string('\\', backslashes));
                    result.Add("\"");
                }
            }
            return string.Concat(result);
        }

        [Conditional("DEBUG")]
        private static void Log(string str)
        {
            lock (_writeLock)
            {
                if (File.Exists("C:\\Arch\\yawslgit.log"))
                {
                    File.AppendAllText("C:\\Arch\\yawslgit.log", str);
                }
            }
        }

        private static void Main(string[] args)
        {
            var argsOnly = ToCommandLine(args.Select(ToLinuxPath).ToArray());
            if (argsOnly == "diff-index --raw HEAD --numstat -C50% -M50% -z --")
            {
                // TortoiseGit workaround
                // See https://gitlab.com/tortoisegit/tortoisegit/issues/3380
                // Run "git status" will fix this problem - but why?
                Process.Start(new ProcessStartInfo(@"C:\Windows\Sysnative\wsl.exe")
                {
                    UseShellExecute = false,
                    Arguments = "git status",
                    CreateNoWindow = true
                }).WaitForExit();
            }
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
            Console.Error.Write($"Git command: {psi.Arguments}\n");
            var git = new Process() { StartInfo = psi };
            git.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                var str = e?.Data; // ?.TrimEnd('\0');
                if (!string.IsNullOrEmpty(str))
                {
                    Log($"Output ({token}):\r\n\t{str}\r\n");
                    Console.Out.Write(str + "\n");
                }
            };
            git.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                var str = e?.Data; // ?.TrimEnd('\0');
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
                    // There's no interact with TortoiseGit, so it's probably not gonna be used
                    git.StandardInput.Write((char) Console.In.Read());
                }
            }).Start();
            git.WaitForExit();
            Environment.Exit(git.ExitCode);
        }
    }
}

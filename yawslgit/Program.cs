using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace yawslgit
{
    internal static class Program
    {
        private static readonly object _writeLock = new object();
        private static readonly RegistryKey lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
        public static readonly string SystemDrive = Path.GetPathRoot(Environment.SystemDirectory);

        // Get all mount points of WSL as a Dict<Drive, MountPath>
        public static Dictionary<string, string> Mounts = Process.Start(Start("cat /proc/mounts | grep :")).StandardOutput.ReadToEnd().Trim().Split('\n').ToDictionary(n => n.Split()[0], n => n.Split()[1]);

        // Absolute path to WSL's file system (default distro), in Windows
        public static string linuxBasePath = (string) lxss.OpenSubKey(lxss.GetValue("DefaultDistribution").ToString()).GetValue("BasePath");

        /// <summary>
        /// Convert a Windows absolute path to WSL (mounted) path
        /// </summary>
        /// <param name="path">String to convert</param>
        /// <returns>Converted result</returns>
        private static string ToLinuxPath(string path)
        {
            return char.IsLetter(path[0]) && path[1] == ':' ? (Mounts[path.Substring(0, 2)] + path.Substring(2)).Replace("\\", "/") : path;
        }

        /// <summary>
        /// Convert all WSL path in mounted drive into Windows path.
        /// </summary>
        /// <param name="log">String to convert</param>
        /// <returns>Converted result, with "/" as path separator</returns>
        private static string ToWindowsPath(string log)
        {
            foreach (var item in Mounts)
            {
                log = log.Replace(item.Value, item.Key);
            }
            return log;
        }

        /// <summary>
        /// Convert a list of arguments to a string of command line.
        /// https://docs.microsoft.com/en-us/cpp/cpp/parsing-cpp-command-line-arguments
        /// </summary>
        /// <param name="args">List of arguments passed to this program</param>
        /// <returns>A string of command line with proper quotes and escape.</returns>
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

        /// <summary>
        /// A helper function to execute command in WSL.
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        private static ProcessStartInfo Start(string command)
        {
            return new ProcessStartInfo(SystemDrive + "Windows\\Sysnative\\wsl.exe")
            {
                UseShellExecute = false,
                Arguments = command,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
        }

        private static void Main(string[] args)
        {
            var argsOnly = ToCommandLine(args.Select(ToLinuxPath).ToArray());
            if (argsOnly == "diff-index --raw HEAD --numstat -C50% -M50% -z --")
            {
                // TortoiseGit workaround
                // See https://gitlab.com/tortoisegit/tortoisegit/issues/3380
                // Run "git status" will fix this problem - but why?
                Process.Start(Start("git status")).WaitForExit();
            }
            var token = new Random().Next();
            Log($"CL ({token}):\r\n\t{Environment.CommandLine}\r\n");
            Log($"Args ({token}):\r\n\t{string.Join("\r\n\t", args)}\r\n");
            var psi = Start("git " + argsOnly);
            Log($"Invoke ({token}):\r\n\t{psi.Arguments}\r\n");
            var git = new Process() { StartInfo = psi };
            //git.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
            //{
            //    var str = e?.Data; // ?.TrimEnd('\0');
            //    if (!string.IsNullOrEmpty(str))
            //    {
            //        str = ToWindowsPath(str);
            //        Log($"Output ({token}):\r\n\t{str}\r\n");
            //        Console.Out.Write(str + "\n");
            //    }
            //};
            //git.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
            //{
            //    var str = e?.Data; // ?.TrimEnd('\0');
            //    if (!string.IsNullOrEmpty(str))
            //    {
            //        str = ToWindowsPath(str);
            //        Log($"Error ({token}):\r\n\t{str}\r\n");
            //        Console.Error.WriteLine(str + "\n");
            //    }
            //};
            var outputBuffer = new byte[1];
            var outputStream = Console.OpenStandardOutput();
            void ReadNextOutput() => git.StandardOutput.BaseStream.BeginRead(outputBuffer, 0, 1, (ar) =>
            {
                git.StandardOutput.BaseStream.EndRead(ar);
                outputStream.Write(outputBuffer, 0, 1);
                ReadNextOutput();
            }, null);
            var errorBuffer = new byte[1];
            var errorStream = Console.OpenStandardError();
            void ReadNextError() => git.StandardError.BaseStream.BeginRead(errorBuffer, 0, 1, (ar) =>
            {
                git.StandardError.BaseStream.EndRead(ar);
                errorStream.Write(errorBuffer, 0, 1);
                ReadNextError();
            }, null);
            git.Start();
            ReadNextOutput();
            ReadNextError();
            //git.BeginOutputReadLine();
            //git.BeginErrorReadLine();
            git.WaitForExit();
            Environment.Exit(git.ExitCode);
        }
    }
}

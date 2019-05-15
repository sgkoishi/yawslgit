using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

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
                // Run "git status" will fix this problem
                Process.Start(Start("git status")).WaitForExit();
            }
            var git = new Process()
            {
                StartInfo = Start("git " + argsOnly)
            };

            var outputBuffer = new byte[1];
            var outputStream = Console.OpenStandardOutput();
            var outputEOS = false;
            void ReadNextOutput() => git.StandardOutput.BaseStream.ReadAsync(outputBuffer, 0, 1).ContinueWith(i =>
            {
                if (i.Result > 0)
                {
                    outputStream.Write(outputBuffer, 0, 1);
                    outputEOS = false;
                }
                else
                {
                    if (git.StandardOutput.EndOfStream)
                    {
                        outputEOS = true;
                    }
                }
                ReadNextOutput();
            });

            var errorBuffer = new byte[1];
            var errorStream = Console.OpenStandardError();
            var errorEOS = false;
            void ReadNextError() => git.StandardError.BaseStream.ReadAsync(errorBuffer, 0, 1).ContinueWith(i =>
            {
                if (i.Result > 0)
                {
                    errorStream.Write(errorBuffer, 0, 1);
                    errorEOS = false;
                }
                else
                {
                    if (git.StandardError.EndOfStream)
                    {
                        errorEOS = true;
                    }
                }
                ReadNextError();
            });

            git.Start();
            ReadNextOutput();
            ReadNextError();

            git.WaitForExit();

            while (true)
            {
                // while (!errorEOS && !outputEOS) doesn't work, I had to separate them
                // ¯\_(ツ)_/¯
                if (!errorEOS)
                {
                    Thread.Sleep(1);
                }
                else if (!outputEOS)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Environment.Exit(git.ExitCode);
                }
            }
        }
    }
}

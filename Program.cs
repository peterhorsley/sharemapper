using CredentialManagement;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShareMapper
{
    public class ShareMapping
    {
        public string RootFolder { get; set; }
        public string UncPath { get; set; }
        public string Username { get; set; }
        public string Tag { get; set; }

        public override string ToString()
        {
            return string.Format("{0}\\{1} -> {2}@{3}", RootFolder, GetLocalFolderName(), Username, UncPath);
        }

        public string GetLocalFolderName()
        {
            return Tag + UncPath.Replace("\\\\", "-").Replace("\\", "-");
        }

        public string GetLocalPath()
        {
            return Path.Combine(RootFolder, GetLocalFolderName());
        }
    }

    public class CmdOutput
    {
        public bool Success { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
    }

    public class Program
    {
        private static void ShowHelp(List<ShareMapping> mappings)
        {
            Console.WriteLine("Usage: ShareMapper.exe [map] [add] [delete] [update-password] [set-root-folder]");
            ListMappings(mappings);
        }

        static void Main(string[] args)
        {
            // Administrator rights required to create symlinks on Windows :(
            if (!IsRunningAsAdministrator())
            {
                Console.WriteLine("ERROR: This must be run as Administrator.");
                return;
            }

            var vaultResourcePrefix = "ShareMapper-";
            var exePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            var configFileName = Path.Combine(exePath, "ShareMapper.json");
            var mappings = new List<ShareMapping>();
            if (File.Exists(configFileName))
            {
                mappings = JsonConvert.DeserializeObject<List<ShareMapping>>(File.ReadAllText(configFileName));
            }
            if (args.Length == 0)
            {
                ShowHelp(mappings);
                return;
            }

            if (args[0] == "add")
            {
                var rootFolder = @"c:\shares";
                Console.Write("Enter local root folder in which to create the symlink (or accept {0} by pressing ENTER): ", rootFolder);
                var enteredRootFolder = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(enteredRootFolder))
                {
                    rootFolder = enteredRootFolder.ToLower().TrimEnd(new char[] { '\\' });
                }

                var uncPath = "";
                while (GetServer(uncPath).Length == 0)
                {
                    Console.Write("Enter unc path: ");
                    uncPath = Console.ReadLine();
                }

                var username = "";
                while (string.IsNullOrWhiteSpace(username))
                {
                    Console.Write("Enter username: ");
                    username = Console.ReadLine();
                }

                var password = "";
                while (string.IsNullOrWhiteSpace(password))
                {
                    Console.Write("Enter password: ");
                    password = ReadPassword();
                }

                Console.Write("{0}Enter tag (optional): ", Environment.NewLine);
                var tag = Console.ReadLine();

                var mapping = new ShareMapping()
                {
                    RootFolder = rootFolder,
                    UncPath = uncPath.ToLower(),
                    Username = username.ToLower(),
                    Tag = tag,
                };
                mappings.Add(mapping);
                File.WriteAllText(configFileName, JsonConvert.SerializeObject(mappings, Formatting.Indented));
                StoreCredential(vaultResourcePrefix + mapping.GetLocalFolderName(), username, password);
                Console.WriteLine();
                Console.WriteLine("Share mapping added to config.");
            }
            else if (args[0] == "update-password")
            {
                if (mappings.Count == 0)
                {
                    Console.WriteLine("No mappings configured.");
                    return;
                }
                ListMappings(mappings);
                var number = -1;
                while (number < 1 || number > mappings.Count)
                {
                    Console.Write("Enter mapping number: ");
                    number = Convert.ToInt32(Console.ReadLine());
                }
                var mappingToUpdate = mappings[number - 1];

                var password = "";
                while (string.IsNullOrWhiteSpace(password))
                {
                    Console.Write("Enter new password: ");
                    password = ReadPassword();
                }

                StoreCredential(vaultResourcePrefix + mappingToUpdate.GetLocalFolderName(), mappingToUpdate.Username, password);
                Console.WriteLine(Environment.NewLine + "Share mapping password updated.");
            }
            else if (args[0] == "delete")
            {
                if (mappings.Count == 0)
                {
                    Console.WriteLine("No mappings configured.");
                    return;
                }
                ListMappings(mappings);
                var number = -1;
                while (number < 1 || number > mappings.Count)
                {
                    Console.Write("Enter mapping number: ");
                    number = Convert.ToInt32(Console.ReadLine());
                }
                var mappingToRemove = mappings[number - 1];
                mappings.Remove(mappingToRemove);
                File.WriteAllText(configFileName, JsonConvert.SerializeObject(mappings, Formatting.Indented));
                DeleteCredential(vaultResourcePrefix + mappingToRemove.GetLocalFolderName());
                Console.WriteLine("Share mapping removed from config.");
            }
            else if (args[0] == "map")
            {
                if (mappings.Count == 0)
                {
                    Console.WriteLine("No mapping configured.");
                    return;
                }

                Console.OutputEncoding = Encoding.UTF8;
                // Object for console output synchronization
                object consoleLock = new object();
                // Track line numbers for each mapping
                var lineNumbers = new ConcurrentDictionary<ShareMapping, int>();

                var startLine = Console.CursorTop;
                var lastLine = startLine;
                for (int i = 0; i < mappings.Count; i++)
                {
                    Console.WriteLine(""); // Reserve a line
                    if (Console.CursorTop != lastLine)
                    {
                        lastLine = Console.CursorTop;
                        lineNumbers[mappings[i]] = Console.CursorTop - 1;
                    }
                    else
                    {
                        for (var j = 0; j < i; j++)
                        {
                            lineNumbers[mappings[j]]--;
                        }
                        lineNumbers[mappings[i]] = Console.CursorTop - 1;
                    }
                }
                var endLine = Console.CursorTop;

                Parallel.ForEach(mappings, mapping =>
                {
                    int line = lineNumbers[mapping];

                    var error = "";
                    if (Directory.Exists(mapping.GetLocalPath()))
                    {
                        try
                        {
                            Directory.Delete(mapping.GetLocalPath());
                        }
                        catch (Exception e)
                        {
                            error = e.Message;
                        }
                    }
                    if (error != "")
                    {
                        lock (consoleLock)
                        {
                            Console.SetCursorPosition(0, line);
                            Console.Write($"Mapping {mapping.GetLocalPath()}: -> {mapping.UncPath}: ");
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Write("✗ ({0})", error);
                            Console.ResetColor();
                        }
                    }
                    else
                    {
                        var charsWritten = 0;
                        lock (consoleLock)
                        {
                            Console.SetCursorPosition(0, line);
                            var str = $"Mapping {mapping.GetLocalPath()}: -> {mapping.UncPath}...";
                            Console.Write(str);
                            charsWritten += str.Length - 3;
                        }
                        if (!Directory.Exists(mapping.RootFolder))
                        {
                            Directory.CreateDirectory(mapping.RootFolder);
                        }
                        var password = RetrieveCredential(vaultResourcePrefix + mapping.GetLocalFolderName());
                        var success = CreateSymLink(mapping.UncPath, mapping.Username, password, mapping.GetLocalPath());
                        lock (consoleLock)
                        {
                            Console.SetCursorPosition(charsWritten, line);
                            Console.Write(": ");
                            if (success)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.Write("✓");
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.Write("✗" + (password.Length == 0 ? " (no password)" : ""));
                            }
                            Console.ResetColor();
                        }
                    }
                });
                // Move cursor to the end after all mappings are done
                lock (consoleLock)
                {
                    Console.SetCursorPosition(0, endLine);
                }
            }
            else
            {
                ShowHelp(mappings);
            }
        }

        public static void ListMappings(List<ShareMapping> mappings)
        {
            if (mappings.Count > 0)
            {
                Console.WriteLine("Configured mappings:");
                for (var i = 0; i < mappings.Count; i++)
                {
                    Console.WriteLine(string.Format("[{0}] {1}", i + 1, mappings[i].ToString()));
                }
            }
        }

        public static string GetServer(string uncPath)
        {
            var parts = uncPath.Split('\\');
            if (parts.Length > 2)
            {
                return parts[2];
            }
            return "";
        }

        public static bool CreateSymLink(string uncPath, string username, string password, string localLinkPath)
        {
            var server = GetServer(uncPath);
            var result = RunCommand($"cmdkey /add:{server} /user:{username} /pass:\"{password}\"");
            if (!result.Success)
            {
                result = RunCommand($"net use {uncPath} \"{password}\" /user:{username}");
                if (!result.Success)
                {
                    return false;
                }
            }
            Thread.Sleep(500);
            result = RunCommand($"mklink /D \"{localLinkPath}\" \"{uncPath}\"");
            return result.Success;
        }

        private static CmdOutput RunCommand(string arguments, bool ignoreErrors = false)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var proc = Process.Start(psi);
            proc.WaitForExit();

            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();

            return new CmdOutput
            {
                Success = proc.ExitCode == 0,
                Output = output,
                Error = error
            };
        }

        private static bool IsRunningAsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void StoreCredential(string resource, string username, string password)
        {
            var credential = new Credential
            {
                Target = resource,
                Username = username,
                Password = password,
                PersistanceType = PersistanceType.LocalComputer
            };
            credential.Save();
        }

        private static string RetrieveCredential(string resource)
        {
            var credential = new Credential { Target = resource };
            if (credential.Load())
            {
                return credential.Password;
            }
            else
            {
                return "";
            }
        }

        private static void DeleteCredential(string resource)
        {
            var credential = new Credential
            {
                Target = resource
            };

            if (credential.Exists())
            {
                credential.Delete();
            }
        }

        private static string ReadPassword()
        {
            string password = string.Empty;
            ConsoleKeyInfo keyInfo;

            do
            {
                keyInfo = Console.ReadKey(intercept: true); // Read key without displaying it
                if (keyInfo.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    // Handle backspace
                    password = password.Substring(0, password.Length - 1); // Remove the last character
                    Console.Write("\b \b"); // Erase the last character in the console
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    // Append valid characters to password
                    password += keyInfo.KeyChar;
                    Console.Write("*"); // Display a masking character
                }
            } while (keyInfo.Key != ConsoleKey.Enter);

            return password;
        }
    }
}

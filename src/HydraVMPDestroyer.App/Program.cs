using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MegaDumper;
using dnlib.DotNet;
using dnlib.DotNet.Writer;

namespace HydraVMPDestroyer.App
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("        HydraVMPDestroyer - Unified Tool");
            Console.WriteLine("   VMP Unpacker + MegaDumper + de4dotEx");
            Console.WriteLine("=================================================");

            Console.WriteLine($"[INFO] Runtime Architecture: {(IntPtr.Size == 8 ? "x64" : "x86")}");
            Console.WriteLine("[!] Note: Specifically for .NET VMProtect executables.");

            // Hardcoded dump directory - matches MegaDumper GUI default output
            string dumpsDir = @"C:\Dumps";

            bool success = false;
            string currentTarget = "";

            // -----------------------------------------------
            // MODE 1: No argument → GUI Mode
            // MegaDumper GUI already ran and dumped to C:\Dumps\dumps
            // We just pick up the files and run de4dotEx on them.
            // -----------------------------------------------
            if (args.Length < 1)
            {
                Console.WriteLine("\n[INFO] No argument given. Switching to GUI-Assist Mode.");
                Console.WriteLine($"[INFO] Scanning for dumps in: {dumpsDir}");

                if (!Directory.Exists(dumpsDir))
                {
                    Console.WriteLine($"[ERROR] Dump directory not found: {dumpsDir}");
                    Console.WriteLine("[USAGE] Run MegaDumper GUI first, or provide the target file as an argument.");
                    Console.ReadKey();
                    return 1;
                }

                var files = Directory.GetFiles(dumpsDir, "*.exe", SearchOption.AllDirectories);
                string found = files.FirstOrDefault(f => f.ToLower().Contains("rawdump")) ??
                               files.FirstOrDefault(f => f.ToLower().Contains("vdump")) ??
                               files.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();

                if (!string.IsNullOrEmpty(found))
                {
                    currentTarget = found;
                    success = true;
                    Console.WriteLine($"[INFO] Found dump: {currentTarget}");
                }
                else
                {
                    Console.WriteLine("[ERROR] No .exe dumps found in C:\\Dumps\\dumps. Run MegaDumper GUI first.");
                    Console.ReadKey();
                    return 1;
                }
            }
            // -----------------------------------------------
            // MODE 2: Argument given → Full CLI Pipeline
            // Launch target, dump to C:\Dumps, run de4dotEx.
            // -----------------------------------------------
            else
            {
                string inputFilePath = Path.GetFullPath(args[0]);
                if (!File.Exists(inputFilePath))
                {
                    Console.WriteLine($"[ERROR] File not found: {inputFilePath}");
                    return 1;
                }

                string outputDir = Path.GetDirectoryName(inputFilePath);
                string dumpRoot = @"C:\Dumps";

                Console.WriteLine("\n[INFO] Starting Dynamic Analysis Mode...");

                ProcessStartInfo startInfo = new ProcessStartInfo(inputFilePath);
                startInfo.WorkingDirectory = outputDir;

                Process process = Process.Start(startInfo);
                Console.WriteLine($"[INFO] Process started (PID: {process.Id}). Waiting 5 seconds for initialization...");
                await Task.Delay(5000);

                try
                {
                    Mega_Dumper.MainForm megaDumper = new Mega_Dumper.MainForm();
                    string dumpResult = await megaDumper.DumpProcessByIdCli((uint)process.Id, dumpRoot);
                    Console.WriteLine($"[INFO] MegaDumper finished: {dumpResult}");

                    if (Directory.Exists(dumpsDir))
                    {
                        var files = Directory.GetFiles(dumpsDir, "*.exe", SearchOption.AllDirectories);

                        string found = files.FirstOrDefault(f => f.ToLower().Contains("rawdump")) ??
                                       files.FirstOrDefault(f => f.ToLower().Contains("vdump")) ??
                                       files.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();

                        if (!string.IsNullOrEmpty(found))
                        {
                            currentTarget = found;
                            success = true;
                        }
                    }

                    if (!process.HasExited) { try { process.Kill(); } catch { } }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Dumping failed: {ex.Message}");
                }
            }

            if (success && File.Exists(currentTarget))
            {
                // Step 2: Restore Original Filename (CRITICAL for VMP)
                string originalName = Path.GetFileName(args.Length > 0 ? Path.GetFullPath(args[0]) : currentTarget);
                string renamedTarget = Path.Combine(Path.GetDirectoryName(currentTarget), originalName);

                if (!currentTarget.Equals(renamedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(renamedTarget)) File.Delete(renamedTarget);
                    File.Copy(currentTarget, renamedTarget);
                    Console.WriteLine($"[INFO] Copied dump as: {renamedTarget}");
                    currentTarget = renamedTarget;
                }

                // VMP ANTI-TAMPER FIX: Do NOT normalize the assembly with dnlib here!
                // VMP's .cctor hashes the PE headers. Any structural changes (even fixing alignments)
                // triggers the Anti-Tamper, causing the dictionary to remain empty (KeyNotFoundException).
                // We pass the raw dump directly to de4dotEx.

                // Step 3: Deobfuscate
                Console.WriteLine("\n[INFO] Running de4dotEx...");

                // de4dot.cui.dll is copied to the output directory during build
                string de4dotDllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "de4dot.cui.dll");

                if (!File.Exists(de4dotDllPath))
                {
                    // Fallback to the explicit project build path
                    de4dotDllPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\de4dotEx\Debug\net8.0\de4dot.cui.dll"));
                }

                if (!File.Exists(de4dotDllPath))
                {
                    Console.WriteLine($"[ERROR] Could not find de4dotEx at: {de4dotDllPath}");
                }
                else
                {
                    string de4dotArgs = $"\"{de4dotDllPath}\" --un-name true --strtyp emulate --strtok true --delegate-to-method true --proxy-calls true --file \"{currentTarget}\"";

                    try
                    {
                        var de4dotProc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = de4dotArgs,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        });

                        string output = de4dotProc.StandardOutput.ReadToEnd();
                        string error = de4dotProc.StandardError.ReadToEnd();

                        de4dotProc.WaitForExit();

                        if (!string.IsNullOrWhiteSpace(output))
                            Console.WriteLine(output);

                        if (!string.IsNullOrWhiteSpace(error))
                            Console.WriteLine($"[DE4DOT ERROR]:\n{error}");

                        if (de4dotProc.ExitCode != 0)
                        {
                            Console.WriteLine($"[ERROR] de4dotEx crashed with Exit Code: {de4dotProc.ExitCode}");
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"[ERROR] de4dotEx execution error: {ex.Message}"); }
                }
            }
            else
            {
                Console.WriteLine("[ERROR] Pipeline aborted: No valid dump found.");
            }

            Console.WriteLine("\n[SUCCESS] Pipeline finished. Press any key to exit...");
            Console.ReadKey();
            Environment.Exit(0);
            return 0;
        }

        private static void NormalizeAssembly(string filePath)
        {
            Console.WriteLine($"[INFO] Normalizing with dnlib: {Path.GetFileName(filePath)}");
            try
            {
                using (var module = ModuleDefMD.Load(filePath))
                {
                    var options = new ModuleWriterOptions(module);
                    options.MetadataOptions.Flags |= MetadataFlags.KeepOldMaxStack;
                    options.Logger = DummyLogger.NoThrowInstance;
                    
                    string temp = filePath + ".tmp";
                    module.Write(temp, options);
                    File.Delete(filePath);
                    File.Move(temp, filePath);
                }
                Console.WriteLine("[SUCCESS] Normalization complete.");
            }
            catch (Exception ex) { Console.WriteLine($"[WARNING] Normalization skipped: {ex.Message}"); }
        }
    }
}
